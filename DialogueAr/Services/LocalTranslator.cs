using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace DialogueAr.Services;

/// <summary>
/// Offline EN→X via local Python worker + Helsinki-NLP Opus-MT on disk (no cloud translation API).
/// Model folder comes from the selected language, or Configuration.ModelDirectory.
/// Worker speaks JSON lines over stdin/stdout.
/// </summary>
public sealed class LocalTranslator : IDisposable
{
    private readonly TranslationCache cache;
    private readonly string pluginDir;
    private readonly string workerScript;
    private readonly object gate = new();
    private string modelDir;
    private string targetToken = "";
    private Process? worker;
    private StreamWriter? stdin;
    private StreamReader? stdout;
    private bool ready;
    private string status = "Not started";

    public LocalTranslator(TranslationCache cache, string pluginDir, string? configuredModelDir)
    {
        this.cache = cache;
        this.pluginDir = pluginDir;
        modelDir = ResolveModelDir(pluginDir, configuredModelDir, Plugin.Configuration.TargetLanguage);
        targetToken = TargetLanguages.Get(Plugin.Configuration.TargetLanguage).TargetToken ?? "";
        workerScript = ResolveWorkerScript(pluginDir);
    }

    private static string ResolveModelDir(string pluginDir, string? configured, string? language)
    {
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
            return configured;

        var folder = TargetLanguages.Get(language).ModelFolder;
        if (string.IsNullOrWhiteSpace(folder))
            folder = "opus-mt-en-ar";

        var candidates = new[]
        {
            Path.Combine(pluginDir, "Models", folder),
            Path.GetFullPath(Path.Combine(pluginDir, "..", "..", "..", "Models", folder)),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "XIVLauncher", "pluginConfigs", "DialogueAr", "Models", folder),
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

    public bool ModelPresent =>
        Directory.Exists(modelDir) &&
        File.Exists(Path.Combine(modelDir, "config.json")) &&
        (File.Exists(Path.Combine(modelDir, "pytorch_model.bin")) ||
         File.Exists(Path.Combine(modelDir, "model.safetensors")) ||
         Directory.GetFiles(modelDir, "*.bin").Length > 0 ||
         Directory.GetFiles(modelDir, "*.safetensors").Length > 0);

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
                status = string.IsNullOrEmpty(lang.Code) || lang.Code == TargetLanguages.CustomCode
                    ? "Model missing — set a custom folder or run Models\\setup-models.ps1"
                    : $"Model missing — run Models\\setup-models.ps1 -Language {lang.Code}";
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
            modelDir = ResolveModelDir(pluginDir, Plugin.Configuration.ModelDirectory, Plugin.Configuration.TargetLanguage);
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

        var psi = new ProcessStartInfo
        {
            FileName = FindPython(),
            Arguments = string.IsNullOrWhiteSpace(targetToken)
                ? $"\"{workerScript}\" \"{modelDir}\""
                : $"\"{workerScript}\" \"{modelDir}\" \"{targetToken}\"",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardInputEncoding = Encoding.UTF8,
        };

        worker = Process.Start(psi) ?? throw new InvalidOperationException("Could not start Python.");
        stdin = worker.StandardInput;
        stdout = worker.StandardOutput;

        worker.ErrorDataReceived += (_, _) => { };
        worker.BeginErrorReadLine();

        // Worker emits one ready {"ok":true,"pong":true} after model load.
        var line = stdout.ReadLine() ?? string.Empty;
        if (!line.Contains("\"ok\": true", StringComparison.OrdinalIgnoreCase) &&
            !line.Contains("\"ok\":true", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Worker ready failed: " + line);
        }
    }

    private string RequestTranslation(string english)
    {
        if (stdin is null || stdout is null || worker is null || worker.HasExited)
        {
            StartWorker();
            ready = true;
        }

        var fast = Plugin.Configuration.FastTranslation ? "true" : "false";
        var payload = "{\"cmd\":\"tr\",\"fast\":" + fast + ",\"text\":" + JsonEscape(english) + "}";
        stdin!.WriteLine(payload);
        stdin.Flush();

        var line = stdout!.ReadLine();
        if (line is null)
            throw new InvalidOperationException("Worker closed.");

        // {"ok":true,"t":"..."} — older workers used "ar"
        foreach (var marker in new[] { "\"t\":", "\"ar\":" })
        {
            var idx = line.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0)
                continue;

            var rest = line[(idx + marker.Length)..].Trim();
            if (rest.StartsWith('"'))
            {
                var end = FindClosingQuote(rest, 1);
                if (end > 1)
                    return UnescapeJson(rest[1..end]);
            }
        }

        return english;
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

    private static string FindPython()
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
                    return name == "py" ? "py" : name;
            }
            catch
            {
                // try next
            }
        }

        throw new InvalidOperationException("Python not found. Install Python 3 and re-run setup-models.ps1.");
    }

    private static string JsonEscape(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 32)
                        sb.Append("\\u").Append(((int)ch).ToString("x4"));
                    else
                        sb.Append(ch);
                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }

    private static int FindClosingQuote(string s, int start)
    {
        var esc = false;
        for (var i = start; i < s.Length; i++)
        {
            var c = s[i];
            if (esc)
            {
                esc = false;
                continue;
            }

            if (c == '\\')
            {
                esc = true;
                continue;
            }

            if (c == '"')
                return i;
        }

        return -1;
    }

    private static string UnescapeJson(string s) =>
        s.Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\r", "\r", StringComparison.Ordinal)
            .Replace("\\t", "\t", StringComparison.Ordinal)
            .Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);
}
