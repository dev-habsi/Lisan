using System;
using System.Linq;
using System.Speech.Synthesis;
using System.Text;
using System.Threading.Tasks;

namespace Lisan.Services;

/// <summary>
/// Optional Windows SAPI TTS for Arabic translations. Off by default.
/// Needs an Arabic voice in Windows Settings → Time &amp; language → Speech.
/// </summary>
public sealed class SpeechService : IDisposable
{
    private readonly object gate = new();
    private SpeechSynthesizer? synth;
    private bool missingVoiceLogged;
    private string lastSpoken = string.Empty;

    public string Status { get; private set; } = "Off";
    public string VoiceName { get; private set; } = "";
    public bool MissingArabicVoice { get; private set; }

    public static void OpenWindowsSpeechSettings()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:speech")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Could not open Windows speech settings");
        }
    }

    public static void OpenWindowsLanguageSettings()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:regionlanguage")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Could not open Windows language settings");
        }
    }

    public void RecheckVoice()
    {
        lock (gate)
        {
            synth?.Dispose();
            synth = null;
            missingVoiceLogged = false;
            VoiceName = "";
            MissingArabicVoice = false;
            Status = Plugin.Configuration.EnableTts ? "Checking…" : "Off";
        }

        if (!Plugin.Configuration.EnableTts)
        {
            Status = "Off";
            return;
        }

        Task.Run(() =>
        {
            lock (gate)
            {
                // SAPI is Windows-only; under Wine the synthesizer can fail to construct.
                try
                {
                    EnsureSynth();
                }
                catch (Exception ex)
                {
                    Status = "TTS unavailable";
                    Plugin.Log.Warning(ex, "TTS init failed");
                }
            }
        });
    }

    public void SpeakDialogue(string? text, bool ignoreFocus = false)
    {
        if (!Plugin.Configuration.EnableTts)
            return;
        if (!ignoreFocus && Plugin.Configuration.TtsOnlyWhenFocused && !GameIsForeground())
            return;
        if (!string.Equals(Plugin.Configuration.TargetLanguage, "ar", StringComparison.OrdinalIgnoreCase))
            return;
        if (string.IsNullOrWhiteSpace(text))
            return;
        if (TargetLanguages.IsBusyPlaceholder(text))
            return;
        if (!ContainsArabic(text))
            return;

        var spoken = Flatten(text);
        if (spoken.Length < 2)
            return;
        if (string.Equals(spoken, lastSpoken, StringComparison.Ordinal))
            return;

        lastSpoken = spoken;
        Task.Run(() => SpeakCore(spoken));
    }

    public void Replay()
    {
        var text = lastSpoken;
        if (string.IsNullOrWhiteSpace(text))
            text = Plugin.History.Last?.Translation ?? "";
        if (string.IsNullOrWhiteSpace(text))
            return;

        lastSpoken = string.Empty;
        SpeakDialogue(text, ignoreFocus: true);
    }

    public void Test()
    {
        lastSpoken = string.Empty;
        SpeakDialogue("مرحبا. هذا اختبار للصوت العربي.", ignoreFocus: true);
    }

    public void Stop()
    {
        lastSpoken = string.Empty;
        lock (gate)
        {
            try { synth?.SpeakAsyncCancelAll(); }
            catch { /* ignore */ }
        }
    }

    public void Dispose()
    {
        Stop();
        lock (gate)
        {
            synth?.Dispose();
            synth = null;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    private static bool GameIsForeground()
    {
        try
        {
            var fg = GetForegroundWindow();
            if (fg == nint.Zero)
                return false;
            GetWindowThreadProcessId(fg, out var pid);
            return pid == (uint)Environment.ProcessId;
        }
        catch
        {
            return true;
        }
    }

    private void SpeakCore(string text)
    {
        lock (gate)
        {
            try
            {
                if (!EnsureSynth())
                    return;

                var cfg = Plugin.Configuration;
                synth!.Volume = Math.Clamp(cfg.TtsVolume, 0, 100);
                synth.Rate = Math.Clamp(cfg.TtsRate, -10, 10);
                synth.SpeakAsyncCancelAll();
                synth.SpeakAsync(text);
                Status = "Speaking (" + VoiceName + ")";
            }
            catch (Exception ex)
            {
                Status = "TTS failed";
                Plugin.Log.Warning(ex, "TTS speak failed");
            }
        }
    }

    private bool EnsureSynth()
    {
        if (synth is not null)
            return true;

        synth = new SpeechSynthesizer();
        var voice = PickArabicVoice(synth);
        if (voice is null)
        {
            Status = "No Arabic Windows voice — see steps below";
            MissingArabicVoice = true;
            if (!missingVoiceLogged)
            {
                missingVoiceLogged = true;
                Plugin.Log.Warning("TTS: no Arabic voice. Windows Settings > Time & language > Speech > add Arabic.");
            }

            synth.Dispose();
            synth = null;
            return false;
        }

        synth.SelectVoice(voice);
        VoiceName = voice;
        MissingArabicVoice = false;
        Status = "Ready (" + voice + ")";
        Plugin.Log.Information($"TTS voice: {voice}");
        return true;
    }

    private static string? PickArabicVoice(SpeechSynthesizer synth)
    {
        try
        {
            var voices = synth.GetInstalledVoices()
                .Where(v => v.Enabled)
                .Select(v => v.VoiceInfo)
                .ToArray();

            var ar = voices.FirstOrDefault(v =>
                v.Culture.TwoLetterISOLanguageName.Equals("ar", StringComparison.OrdinalIgnoreCase)
                || v.Culture.Name.StartsWith("ar", StringComparison.OrdinalIgnoreCase));
            if (ar is not null)
                return ar.Name;

            ar = voices.FirstOrDefault(v =>
                v.Name.Contains("Arabic", StringComparison.OrdinalIgnoreCase)
                || v.Name.Contains("Huma", StringComparison.OrdinalIgnoreCase)
                || v.Name.Contains("Naayf", StringComparison.OrdinalIgnoreCase)
                || v.Name.Contains("Fahd", StringComparison.OrdinalIgnoreCase));
            return ar?.Name;
        }
        catch
        {
            return null;
        }
    }

    private static string Flatten(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is '\r' or '\n' or '\t')
                sb.Append(' ');
            else
                sb.Append(c);
        }

        return sb.ToString().Trim();
    }

    private static bool ContainsArabic(string text)
    {
        foreach (var c in text)
        {
            if (c is >= '\u0600' and <= '\u06FF')
                return true;
        }

        return false;
    }
}
