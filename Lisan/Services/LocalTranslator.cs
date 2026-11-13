using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Lisan.Services;

/// <summary>
/// Offline EN→X via local Python worker + Helsinki-NLP Opus-MT on disk (no cloud translation API).
/// Model folder comes from the selected language, or Configuration.ModelDirectory.
/// Worker speaks JSON lines over stdin/stdout.
/// </summary>
public sealed class LocalTranslator : IDisposable
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(240);

    private readonly TranslationCache cache;
    private readonly string pluginDir;
    private readonly string configDir;
    private readonly string workerScript;
    private readonly object gate = new();
    private readonly Queue<string> stderrTail = new();
    private string modelDir;
    private string targetToken = "";
    private Process? worker;
    private StreamWriter? stdin;
    private StreamReader? stdout;
    private bool ready;
    private string status = "Not started";

    public LocalTranslator(TranslationCache cache, string pluginDir, string configDir, string? configuredModelDir)
    {
        this.cache = cache;
        this.pluginDir = pluginDir;
        this.configDir = configDir;
        modelDir = ResolveModelDir(pluginDir, configDir, configuredModelDir, Plugin.Configuration.TargetLanguage);
        targetToken = TargetLanguages.Get(Plugin.Configuration.TargetLanguage).TargetToken ?? "";
        workerScript = ResolveWorkerScript(pluginDir);
    }

    /// <summary>Where the in-game setup downloads packs: &lt;plugin config dir&gt;\Models.</summary>
    public static string ModelRoot(string configDir) => Path.Combine(configDir, "Models");

    private static string ResolveModelDir(string pluginDir, string configDir, string? configured, string? language)
    {
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
            return configured;

        var folder = TargetLanguages.Get(language).ModelFolder;
        if (string.IsNullOrWhiteSpace(folder))
            folder = "opus-mt-en-ar";

        var candidates = new[]
        {
            Path.Combine(ModelRoot(configDir), folder),
            Path.Combine(pluginDir, "Models", folder),
            Path.GetFullPath(Path.Combine(pluginDir, "..", "..", "..", "Models", folder)),
        };

        foreach (var c in candidates)
        {
            if (Directory.Exists(c) && File.Exists(Path.Combine(c, "config.json")))
                return c;
        }

        return candidates[0];
    }

    private static string ResolveWorkerScript(string pluginDir)
    {
        var candidates = new[]
        {
            Path.Combine(pluginDir, "Models", "translate_worker.py"),
            Path.GetFullPath(Path.Combine(pluginDir, "..", "..", "..", "Models", "translate_worker.py")),
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c))
                return c;
        }

        return candidates[0];
    }

    public string Status => status;
    public string ModelDirectory => modelDir;

    public bool ModelPresent => HasModel(modelDir);

    /// <summary>config.json plus one set of weights.</summary>
    public static bool HasModel(string dir) =>
        Directory.Exists(dir) &&
        File.Exists(Path.Combine(dir, "config.json")) &&
        (File.Exists(Path.Combine(dir, "pytorch_model.bin")) ||
         File.Exists(Path.Combine(dir, "model.safetensors")) ||
         Directory.GetFiles(dir, "*.bin").Length > 0 ||
         Directory.GetFiles(dir, "*.safetensors").Length > 0);

    public void EnsureStarted()
    {
        lock (gate)
        {
            if (ready)
                return;

            if (!File.Exists(workerScript))
            {
                status = "Missing translate_worker.py";
                return;
            }

            if (!ModelPresent)
            {
                var lang = TargetLanguages.Current();
                status = lang.Code == TargetLanguages.CustomCode
                    ? "Model missing — set a custom folder, or download a pack on the Setup tab"
                    : $"Model missing — open Setup and download the {lang.Name} pack";
                return;
            }

            try
            {
                StartWorker();
                ready = true;
                status = "Ready (" + Path.GetFileName(modelDir.TrimEnd(Path.DirectorySeparatorChar)) + ")";
            }
            catch (Exception ex)
            {
                status = "Worker failed: " + ex.Message;
                ready = false;
                KillWorker();
            }
        }
    }

    public bool TryGetCached(string english, out string translation) =>
        cache.TryGet(english, out translation!);

    public string Translate(string english)
    {
        if (string.IsNullOrWhiteSpace(english))
            return english;

        if (cache.TryGet(english, out var hit))
            return hit;

        EnsureStarted();
        if (!ready)
            return english;

        try
        {
            string translated;
            lock (gate)
            {
                translated = RequestTranslation(english);
            }

            if (!string.IsNullOrWhiteSpace(translated) && translated != english)
                cache.Set(english, translated);

            return string.IsNullOrWhiteSpace(translated) ? english : translated;
        }
        catch (Exception ex)
        {
            status = "Translate error: " + ex.Message;
            return english;
        }
    }

    /// <summary>Reload model path + target token after the user changes language.</summary>
    public void Restart()
    {
        lock (gate)
        {
            KillWorker();
            modelDir = ResolveModelDir(pluginDir, configDir, Plugin.Configuration.ModelDirectory, Plugin.Configuration.TargetLanguage);
            targetToken = TargetLanguages.Get(Plugin.Configuration.TargetLanguage).TargetToken ?? "";
            ready = false;
            status = "Restarting…";
        }
    }

    public void Dispose()
    {
        lock (gate)
            KillWorker();
    }

    private void StartWorker()
    {
        KillWorker();

        var python = TryFindPython()
                     ?? throw new InvalidOperationException("Python not found. Install Python 3.10+ (tick 'Add to PATH'), then use the Setup tab.");
        var args = new StringBuilder();
        if (python == "py")
            args.Append("-3 ");
        args.Append('"').Append(workerScript).Append("\" \"").Append(modelDir).Append('"');
        if (!string.IsNullOrWhiteSpace(targetToken))
            args.Append(" \"").Append(targetToken).Append('"');

        var psi = new ProcessStartInfo
        {
            FileName = python,
            Arguments = args.ToString(),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            // No BOM: Encoding.UTF8 writes one on the first line and the worker's json.loads rejects it.
            StandardInputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["PYTHONUTF8"] = "1";

        worker = Process.Start(psi) ?? throw new InvalidOperationException("Could not start Python.");
        stdin = worker.StandardInput;
        stdout = worker.StandardOutput;

        lock (stderrTail)
            stderrTail.Clear();
        worker.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data))
                return;
            lock (stderrTail)
            {
                stderrTail.Enqueue(e.Data);
                while (stderrTail.Count > 12)
                    stderrTail.Dequeue();
            }
        };
        worker.BeginErrorReadLine();

        // Worker emits one ready {"ok":true,"pong":true} after model load.
        var readyTask = stdout.ReadLineAsync();
        if (!readyTask.Wait(StartTimeout))
            throw new TimeoutException($"worker did not become ready within {StartTimeout.TotalSeconds:0}s");

        var line = readyTask.Result;
        if (line is null)
            throw new InvalidOperationException("worker exited: " + StderrSummary());

        var reply = ParseReply(line);
        if (!reply.Ok)
            throw new InvalidOperationException(reply.Error ?? StderrSummary());
    }

    private string StderrSummary()
    {
        lock (stderrTail)
        {
            // Last real line of stderr is the Python exception; tracebacks are noise for the status line.
            var last = stderrTail.LastOrDefault(l => !l.StartsWith(' ') && !l.StartsWith("Traceback", StringComparison.Ordinal));
            return last ?? "no error output";
        }
    }

    private string RequestTranslation(string english)
    {
        if (stdin is null || stdout is null || worker is null || worker.HasExited)
        {
            StartWorker();
            ready = true;
        }

        var payload = JsonSerializer.Serialize(new { cmd = "tr", fast = Plugin.Configuration.FastTranslation, text = english });
        stdin!.WriteLine(payload);
        stdin.Flush();

        var line = stdout!.ReadLine()
                   ?? throw new InvalidOperationException("worker closed: " + StderrSummary());

        var reply = ParseReply(line);
        if (!reply.Ok)
            throw new InvalidOperationException(reply.Error ?? "worker error");
        return string.IsNullOrEmpty(reply.Text) ? english : reply.Text;
    }

    private static (bool Ok, string? Text, string? Error) ParseReply(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
            var text = root.TryGetProperty("t", out var t) ? t.GetString() : null;
            var error = root.TryGetProperty("error", out var e) ? e.GetString() : null;
            return (ok, text, error);
        }
        catch (JsonException)
        {
            return (false, null, "unexpected worker output: " + (line.Length > 200 ? line[..200] + "…" : line));
        }
    }

    private void KillWorker()
    {
        try { stdin?.Dispose(); } catch { /* ignore */ }
        try { stdout?.Dispose(); } catch { /* ignore */ }
        stdin = null;
        stdout = null;
        if (worker is { HasExited: false })
        {
            try { worker.Kill(entireProcessTree: true); } catch { /* ignore */ }
        }

        worker?.Dispose();
        worker = null;
        ready = false;
    }

    /// <summary>"python" or "py" (Windows launcher) when a working Python 3 is on PATH, else null.</summary>
    public static string? TryFindPython()
    {
        foreach (var name in new[] { "python", "py" })
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo
                {
                    FileName = name,
                    Arguments = name == "py" ? "-3 --version" : "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                });
                if (p is null)
                    continue;
                p.WaitForExit(3000);
                if (p.ExitCode == 0)
                    return name;
            }
            catch
            {
                // try next
            }
        }

        return null;
    }
}
