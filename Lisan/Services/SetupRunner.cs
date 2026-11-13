using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Lisan.Services;

/// <summary>
/// One-time setup from inside the game (pip packages, Hugging Face pack, overlay font), streamed to the Setup tab.
/// Plain .NET plus <c>python -m pip</c>: no shell scripts, so it behaves the same on Windows and under Wine.
/// Packs land in the plugin config folder so plugin updates keep them.
/// </summary>
public sealed class SetupRunner : IDisposable
{
    private const int MaxLines = 400;
    private const string HfBase = "https://huggingface.co/";
    private const string Noto = "https://github.com/googlefonts/noto-fonts/raw/main/hinted/ttf/";
    private const string NotoCjk = "https://github.com/notofonts/noto-cjk/raw/main/Sans/OTF/";

    /// <summary>Download URL for each <see cref="TargetLanguageInfo.FontFile"/>.</summary>
    private static readonly Dictionary<string, string> FontUrls = new()
    {
        ["NotoNaskhArabic-Regular.ttf"] = Noto + "NotoNaskhArabic/NotoNaskhArabic-Regular.ttf",
        ["NotoSansDevanagari-Regular.ttf"] = Noto + "NotoSansDevanagari/NotoSansDevanagari-Regular.ttf",
        ["NotoSansThai-Regular.ttf"] = Noto + "NotoSansThai/NotoSansThai-Regular.ttf",
        ["NotoSansCJKjp-Regular.otf"] = NotoCjk + "Japanese/NotoSansCJKjp-Regular.otf",
        ["NotoSansCJKsc-Regular.otf"] = NotoCjk + "SimplifiedChinese/NotoSansCJKsc-Regular.otf",
        ["NotoSansCJKkr-Regular.otf"] = NotoCjk + "Korean/NotoSansCJKkr-Regular.otf",
    };

    // Weights for other frameworks (rust/tf/flax/onnx mirrors, hundreds of MB each) that the worker never loads.
    private static readonly string[] SkipExtensions = [".ot", ".h5", ".msgpack", ".onnx", ".tflite"];

    // owner/name; both parts start alphanumeric so "." / ".." can never become a folder name.
    private static readonly Regex RepoId = new(@"^[A-Za-z0-9][A-Za-z0-9_.-]*/[A-Za-z0-9][A-Za-z0-9_.-]*$");

    private readonly string pluginDir;
    private readonly string modelRoot;
    private readonly string fontDir;
    private readonly HttpClient http = new();
    private readonly object gate = new();
    private readonly List<string> lines = [];
    private CancellationTokenSource? cts;
    private Process? pip;
    private bool finishedOkPending;

    public SetupRunner(string pluginDir, string configDir)
    {
        this.pluginDir = pluginDir;
        modelRoot = LocalTranslator.ModelRoot(configDir);
        fontDir = Path.Combine(configDir, "Fonts");
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Lisan-Setup");
        RecheckPython();
    }

    public string ModelRoot => modelRoot;
    public string FontDir => fontDir;
    public bool Running { get; private set; }
    public int? ExitCode { get; private set; }
    public string Status { get; private set; } = "Idle";
    /// <summary>"python" / "py" when found, "" while checking, null when missing.</summary>
    public string? Python { get; private set; } = "";

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (gate)
                return lines.ToArray();
        }
    }

    public void RecheckPython()
    {
        Python = "";
        Task.Run(() => Python = LocalTranslator.TryFindPython());
    }

    /// <summary>Start setup for a language code (or a custom Hugging Face repo id). False when already running or the repo id is invalid.</summary>
    public bool Start(string language, string repo = "", bool force = false)
    {
        var lang = TargetLanguages.Get(language);
        repo = string.IsNullOrWhiteSpace(repo) ? lang.HuggingFaceRepo : repo.Trim();
        CancellationToken token;
        lock (gate)
        {
            if (Running)
                return false;
            if (!RepoId.IsMatch(repo))
            {
                Status = $"Not a Hugging Face repo id: '{repo}' (expected owner/name)";
                return false;
            }

            lines.Clear();
            ExitCode = null;
            Running = true;
            Status = $"Running setup for {language}…";
            cts = new CancellationTokenSource();
            token = cts.Token;
        }

        var folder = repo[(repo.LastIndexOf('/') + 1)..];
        var fontFile = lang.Code == TargetLanguages.CustomCode ? null : lang.FontFile;
        Task.Run(() => RunAsync(language, repo, Path.Combine(modelRoot, folder), fontFile, force, token));
        return true;
    }

    /// <summary>True once after a successful run; the caller applies the language on the framework thread.</summary>
    public bool ConsumeFinishedOk()
    {
        lock (gate)
        {
            if (!finishedOkPending)
                return false;
            finishedOkPending = false;
            return true;
        }
    }

    public void Cancel()
    {
        Process? p;
        lock (gate)
        {
            cts?.Cancel();
            p = pip;
        }

        try
        {
            if (p is { HasExited: false })
                p.Kill(entireProcessTree: true);
        }
        catch { /* already gone */ }
    }

    public void OpenModelFolder()
    {
        try
        {
            Directory.CreateDirectory(modelRoot);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{modelRoot}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Could not open model folder");
        }
    }

    public void Dispose()
    {
        Cancel();
        http.Dispose();
    }

    private async Task RunAsync(string language, string repo, string modelDir, string? fontFile, bool force, CancellationToken ct)
    {
        int exit;
        try
        {
            var python = string.IsNullOrEmpty(Python) ? LocalTranslator.TryFindPython() : Python;
            Append("=== Lisan setup ===");
            Append($"Language : {language}");
            Append($"Repo     : {repo}");
            Append($"Model dir: {modelDir}");
            Append($"Font dir : {fontDir}");
            Append($"Python   : {python ?? "not found"}");
            if (python is null)
                throw new StepFailed(3, "Python not found on PATH. Install Python 3.10+, restart the game, then Recheck.");

            Append("[1/3] Python packages (torch CPU, transformers, sentencepiece)...");
            if (!await PipInstall(python, ct, "torch", "--index-url", "https://download.pytorch.org/whl/cpu"))
                throw new StepFailed(3, "torch install failed.");
            if (!await PipInstall(python, ct, "transformers>=4.40", "sentencepiece", "sacremoses"))
                throw new StepFailed(3, "transformers install failed.");

            try
            {
                await DownloadModel(repo, modelDir, force, ct);
            }
            catch (Exception ex) when (ex is not StepFailed && !ct.IsCancellationRequested)
            {
                throw new StepFailed(2, $"model download failed (check the repo id and your connection): {ex.Message}");
            }

            await DownloadFont(fontFile, ct);

            Append($"Done. In-game: /lisan lang {language}   or pick the language in config, then 'Apply language / restart worker'.");
            exit = 0;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Append("Cancelled.");
            exit = -1;
        }
        catch (StepFailed ex)
        {
            Append("ERROR: " + ex.Message);
            exit = ex.Code;
        }
        catch (Exception ex)
        {
            Append("ERROR: " + ex.Message);
            exit = 1;
        }

        lock (gate)
        {
            ExitCode = exit;
            Running = false;
            finishedOkPending = exit == 0;
            Status = exit switch
            {
                0 => "Done. Translator restarting with the new pack.",
                -1 => "Setup cancelled.",
                _ => $"Setup failed (exit {exit}). See the log below.",
            };
        }

        Plugin.Log.Information($"{Brand.Name} setup exited with {exit}");
    }

    /// <summary>System-wide Pythons (C:\Python3xx, Program Files) need admin for site-packages: fall back to --user.</summary>
    private async Task<bool> PipInstall(string python, CancellationToken ct, params string[] args)
    {
        if (await RunPython(python, ["-m", "pip", "install", .. args], ct) == 0)
            return true;
        Append("pip install failed; retrying with --user");
        return await RunPython(python, ["-m", "pip", "install", "--user", .. args], ct) == 0;
    }

    private async Task<int> RunPython(string python, string[] args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = python,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (python == "py")
            psi.ArgumentList.Add("-3");
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        // Plain output for the in-game log.
        psi.Environment["PIP_PROGRESS_BAR"] = "off";
        psi.Environment["PIP_DISABLE_PIP_VERSION_CHECK"] = "1";
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["PYTHONUTF8"] = "1";

        using var p = new Process { StartInfo = psi };
        p.OutputDataReceived += (_, e) => Append(e.Data);
        p.ErrorDataReceived += (_, e) => Append(e.Data);
        p.Start();
        lock (gate)
            pip = p;
        try
        {
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            await p.WaitForExitAsync(ct);
            return p.ExitCode;
        }
        finally
        {
            lock (gate)
                pip = null;
        }
    }

    private async Task DownloadModel(string repo, string modelDir, bool force, CancellationToken ct)
    {
        if (!force && LocalTranslator.HasModel(modelDir))
        {
            Append("[2/3] Model already on disk; skip download.");
            return;
        }

        Append($"[2/3] Download {repo} (internet once; local files only after this)...");
        using var doc = JsonDocument.Parse(await http.GetStringAsync(HfBase + "api/models/" + repo, ct));
        var files = doc.RootElement.GetProperty("siblings").EnumerateArray()
            .Select(s => s.GetProperty("rfilename").GetString())
            .OfType<string>()
            .ToList();
        // Tokenizer + one set of weights only.
        if (files.Contains("model.safetensors"))
            files.Remove("pytorch_model.bin");

        // File names come from the network: never write outside the pack folder.
        var root = Path.GetFullPath(modelDir) + Path.DirectorySeparatorChar;
        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith(".git", StringComparison.Ordinal) ||
                SkipExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase))
                continue;

            var dest = Path.GetFullPath(Path.Combine(modelDir, file));
            if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new StepFailed(2, $"refusing unsafe file name from Hugging Face: {file}");

            var url = HfBase + repo + "/resolve/main/" + string.Join('/', file.Split('/').Select(Uri.EscapeDataString));
            await Download(url, dest, ct);
        }

        Append($"OK {repo} -> {modelDir}");
    }

    private async Task DownloadFont(string? fontFile, CancellationToken ct)
    {
        Directory.CreateDirectory(fontDir);
        if (string.IsNullOrEmpty(fontFile))
        {
            Append("[3/3] No extra font required for this language (Dalamud default is enough).");
            return;
        }

        var dest = Path.Combine(fontDir, fontFile);
        if (File.Exists(dest) || File.Exists(Path.Combine(pluginDir, "Fonts", fontFile)))
        {
            Append("[3/3] Font already present.");
            return;
        }

        if (!FontUrls.TryGetValue(fontFile, out var url))
            throw new StepFailed(4, $"no download URL for {fontFile}; place it in {fontDir} yourself.");

        Append($"[3/3] Download overlay font {fontFile}...");
        try
        {
            await Download(url, dest, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            throw new StepFailed(4, $"font download failed (place {fontFile} in {fontDir} yourself): {ex.Message}");
        }

        Append("Font: " + dest);
    }

    /// <summary>Streams to a .part file first so an interrupted download never looks like a finished one.</summary>
    private async Task Download(string url, string dest, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        var part = dest + ".part";
        using (var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            var size = resp.Content.Headers.ContentLength;
            Append($"  {Path.GetFileName(dest)}" + (size is { } n ? $" ({n / 1048576.0:0.0} MB)" : ""));
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(part);
            await src.CopyToAsync(dst, ct);
        }

        File.Move(part, dest, overwrite: true);
    }

    private void Append(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        lock (gate)
        {
            lines.Add(text.TrimEnd());
            if (lines.Count > MaxLines)
                lines.RemoveRange(0, lines.Count - MaxLines);
        }
    }

    private sealed class StepFailed(int code, string message) : Exception(message)
    {
        public int Code { get; } = code;
    }
}
