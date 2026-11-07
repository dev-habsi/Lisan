using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Lisan.Services;

/// <summary>
/// Runs Models\setup-models.ps1 from inside the game (pip packages, Hugging Face pack, overlay font)
/// and streams its output to the Setup tab. Packs land in the plugin config folder so plugin updates keep them.
/// </summary>
public sealed class SetupRunner : IDisposable
{
    private const int MaxLines = 400;

    private readonly string script;
    private readonly string modelRoot;
    private readonly string fontDir;
    private readonly object gate = new();
    private readonly List<string> lines = [];
    private Process? proc;
    private bool finishedOkPending;

    public SetupRunner(string pluginDir, string configDir)
    {
        script = Path.Combine(pluginDir, "Models", "setup-models.ps1");
        if (!File.Exists(script))
            script = Path.GetFullPath(Path.Combine(pluginDir, "..", "..", "..", "Models", "setup-models.ps1"));
        modelRoot = LocalTranslator.ModelRoot(configDir);
        fontDir = Path.Combine(configDir, "Fonts");
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

    /// <summary>Start setup for a language code (or a custom Hugging Face repo id). False when already running or script missing.</summary>
    public bool Start(string languageCode, string repo = "", bool force = false)
    {
        lock (gate)
        {
            if (Running)
                return false;
            if (!File.Exists(script))
            {
                Status = "setup-models.ps1 missing next to the plugin";
                return false;
            }

            lines.Clear();
            ExitCode = null;
            Running = true;
            Status = $"Running setup for {languageCode}…";
        }

        var args = new StringBuilder("-NoProfile -ExecutionPolicy Bypass -File \"")
            .Append(script).Append("\" -Language ").Append(languageCode)
            .Append(" -ModelRoot \"").Append(modelRoot).Append('"')
            .Append(" -FontDir \"").Append(fontDir).Append('"');
        if (!string.IsNullOrWhiteSpace(repo))
            args.Append(" -Repo \"").Append(repo.Trim()).Append('"');
        if (force)
            args.Append(" -Force");

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = args.ToString(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (!string.IsNullOrEmpty(Python))
            psi.Environment["LISAN_PYTHON"] = Python;

        try
        {
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.OutputDataReceived += (_, e) => Append(e.Data);
            p.ErrorDataReceived += (_, e) => Append(e.Data);
            p.Exited += (_, _) =>
            {
                int code;
                try { code = p.ExitCode; } catch { code = -1; }
                lock (gate)
                {
                    ExitCode = code;
                    Running = false;
                    finishedOkPending = code == 0;
                    Status = code == 0
                        ? "Done. Translator restarting with the new pack."
                        : $"Setup failed (exit {code}). See the log below.";
                }

                Plugin.Log.Information($"{Brand.Name} setup exited with {code}");
            };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            proc = p;
            Plugin.Log.Information($"{Brand.Name} setup started: powershell {psi.Arguments}");
            return true;
        }
        catch (Exception ex)
        {
            lock (gate)
            {
                Running = false;
                ExitCode = -1;
                Status = "Could not start PowerShell: " + ex.Message;
            }

            Plugin.Log.Warning(ex, "Setup start failed");
            return false;
        }
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
        var p = proc;
        if (p is null)
            return;
        try
        {
            if (!p.HasExited)
                p.Kill(entireProcessTree: true);
        }
        catch { /* ignore */ }
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
        proc?.Dispose();
        proc = null;
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
}
