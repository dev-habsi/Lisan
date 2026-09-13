using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Speech.Synthesis;
using System.Threading;

namespace DialogueAr.Services;

public sealed class TtsVoiceInfo
{
    public required string Name { get; init; }
    public required string Label { get; init; }
    public VoiceGender Gender { get; init; }
    public string Culture { get; init; } = "";
    public string TwoLetter { get; init; } = "";
    public bool IsMultilingual { get; init; }
}

/// <summary>
/// Windows TTS on a dedicated STA thread (System.Speech is not safe on pool threads in-game).
/// Voice pick: character override → manual → target-language → multilingual → any matching gender.
/// </summary>
public sealed class DialogueTtsService : IDisposable
{
    public const string AutoVoiceName = "";

    private readonly BlockingCollection<Action> queue = new();
    private readonly Thread worker;
    private SpeechSynthesizer? synth;
    private volatile string status = "Not started";
    private string? lastSpoken;
    private ResolvedVoiceGender? lastGender;
    private string? lastVoiceKey;
    private string? selectedVoiceName;
    private bool usePromptCulture;
    private bool disposed;
    private TtsVoiceInfo[] voiceCache = [];

    public DialogueTtsService()
    {
        worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "DialogueAr.TTS",
        };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        RefreshVoiceList();
    }

    public string Status => status;

    public TtsVoiceInfo[] GetInstalledVoices() => voiceCache;

    public void RefreshVoiceList()
    {
        if (disposed)
            return;

        queue.Add(() =>
        {
            try
            {
                EnsureSynthUnlocked(ResolvedVoiceGender.Female);
                if (synth is null)
                    return;

                voiceCache = synth.GetInstalledVoices()
                    .Where(v => v.Enabled)
                    .Select(v => v.VoiceInfo)
                    .OrderBy(v => v.Gender)
                    .ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(v => new TtsVoiceInfo
                    {
                        Name = v.Name,
                        Gender = v.Gender,
                        Culture = v.Culture.Name,
                        TwoLetter = v.Culture.TwoLetterISOLanguageName,
                        IsMultilingual = v.Name.Contains("Multilingual", StringComparison.OrdinalIgnoreCase),
                        Label = $"{v.Name}  ·  {v.Gender}  ·  {v.Culture.Name}",
                    })
                    .ToArray();

                status = $"TTS: {voiceCache.Length} voices loaded";
            }
            catch (Exception ex)
            {
                status = "Voice list failed: " + ex.Message;
            }
        });
    }

    public void EnsureReady()
    {
        if (disposed || !Plugin.Configuration.EnableTts)
            return;

        queue.Add(() =>
        {
            try
            {
                EnsureSynthUnlocked(ResolvedVoiceGender.Female);
            }
            catch (Exception ex)
            {
                status = "TTS failed: " + ex.Message;
                DisposeSynthUnlocked();
            }
        });
    }

    public void Speak(string? text, string? speakerName = null)
    {
        if (disposed || !Plugin.Configuration.EnableTts)
            return;

        if (string.IsNullOrWhiteSpace(text))
            return;

        if (TargetLanguages.IsBusyPlaceholder(text))
            return;

        var spoken = text;
        var gender = SpeakerGender.Resolve(speakerName, Plugin.Configuration.TtsVoiceGender);
        var characterVoice = Plugin.Configuration.FindCharacterVoice(speakerName);
        var voiceKey = !string.IsNullOrEmpty(characterVoice)
            ? "char:" + characterVoice
            : (Plugin.Configuration.TtsVoiceName ?? AutoVoiceName);

        queue.Add(() =>
        {
            try
            {
                EnsureSynthUnlocked(gender);
                if (synth is null)
                    return;

                if (Plugin.Configuration.TtsSkipDuplicates
                    && string.Equals(lastSpoken, spoken, StringComparison.Ordinal)
                    && lastGender == gender
                    && string.Equals(lastVoiceKey, voiceKey, StringComparison.Ordinal))
                    return;

                lastSpoken = spoken;
                lastGender = gender;
                lastVoiceKey = voiceKey;
                PickVoiceUnlocked(gender, characterVoice);
                ApplyRateVolumeUnlocked();

                if (!Plugin.Configuration.TtsQueueLines)
                    synth.SpeakAsyncCancelAll();

                var rate = Math.Clamp(Plugin.Configuration.TtsRate, -10, 10);
                synth.Rate = rate;

                if (usePromptCulture)
                {
                    synth.SpeakSsmlAsync(BuildSsml(spoken, rate, TargetCultureName()));
                }
                else
                {
                    var prompt = new PromptBuilder();
                    prompt.StartStyle(new PromptStyle { Rate = MapPromptRate(rate) });
                    prompt.AppendText(spoken);
                    prompt.EndStyle();
                    synth.SpeakAsync(prompt);
                }

                status = DescribeVoiceUnlocked(gender) + $" · speaking (rate {rate})";
            }
            catch (Exception ex)
            {
                status = "Speak error: " + ex.Message;
                try { DisposeSynthUnlocked(); } catch { /* ignore */ }
            }
        });
    }

    public void Stop()
    {
        if (disposed)
            return;

        queue.Add(() =>
        {
            try { synth?.SpeakAsyncCancelAll(); } catch { /* ignore */ }
            lastSpoken = null;
            lastGender = null;
            lastVoiceKey = null;
        });
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        try
        {
            queue.Add(() =>
            {
                try { synth?.SpeakAsyncCancelAll(); } catch { /* ignore */ }
                DisposeSynthUnlocked();
            });
        }
        catch
        {
            /* queue may be completed */
        }

        queue.CompleteAdding();
        try { worker.Join(2000); } catch { /* ignore */ }
        queue.Dispose();
    }

    private void WorkerLoop()
    {
        foreach (var action in queue.GetConsumingEnumerable())
        {
            try { action(); }
            catch (Exception ex)
            {
                status = "TTS worker error: " + ex.Message;
            }
        }
    }

    private void EnsureSynthUnlocked(ResolvedVoiceGender gender)
    {
        if (synth is not null)
            return;

        synth = new SpeechSynthesizer();
        synth.SetOutputToDefaultAudioDevice();
        PickVoiceUnlocked(gender);
        status = DescribeVoiceUnlocked(gender);
    }

    private void DisposeSynthUnlocked()
    {
        try { synth?.Dispose(); } catch { /* ignore */ }
        synth = null;
        selectedVoiceName = null;
    }

    private void PickVoiceUnlocked(ResolvedVoiceGender gender, string? characterVoice = null)
    {
        if (synth is null)
            return;

        var voices = synth.GetInstalledVoices()
            .Where(v => v.Enabled)
            .Select(v => v.VoiceInfo)
            .ToList();

        if (!string.IsNullOrWhiteSpace(characterVoice))
        {
            var forced = voices.FirstOrDefault(v =>
                v.Name.Equals(characterVoice, StringComparison.OrdinalIgnoreCase));
            if (forced is not null)
            {
                ApplySelected(forced, gender);
                return;
            }
        }

        var manual = Plugin.Configuration.TtsVoiceName?.Trim() ?? AutoVoiceName;
        if (!string.IsNullOrEmpty(manual))
        {
            var chosen = voices.FirstOrDefault(v => v.Name.Equals(manual, StringComparison.OrdinalIgnoreCase));
            if (chosen is not null)
            {
                ApplySelected(chosen, gender);
                return;
            }

            status = $"TTS: voice '{manual}' not found — falling back to Auto";
        }

        var want = gender == ResolvedVoiceGender.Female ? VoiceGender.Female : VoiceGender.Male;
        var lang = TargetLanguages.Current();

        var native = voices
            .Where(v => MatchesTarget(v, lang))
            .Where(v => v.Gender == want || v.Gender == VoiceGender.Neutral)
            .OrderByDescending(v => v.Gender == want)
            .ThenByDescending(v => PreferCulture(v.Culture, lang))
            .ThenBy(v => v.Name.Contains("Online", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .FirstOrDefault()
            ?? voices.Where(v => MatchesTarget(v, lang))
                .OrderByDescending(v => PreferCulture(v.Culture, lang))
                .FirstOrDefault();

        if (native is not null)
        {
            ApplySelected(native, gender);
            return;
        }

        var multi = voices
            .Where(v => v.Name.Contains("Multilingual", StringComparison.OrdinalIgnoreCase))
            .Where(v => v.Gender == want)
            .OrderByDescending(v => PreferMultiName(v.Name, gender))
            .FirstOrDefault()
            ?? voices
                .Where(v => v.Name.Contains("Multilingual", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(v => PreferMultiName(v.Name, gender))
                .FirstOrDefault();

        if (multi is not null)
        {
            ApplySelected(multi, gender);
            return;
        }

        var anyGender = voices
            .Where(v => v.Gender == want)
            .OrderBy(v => v.Name.Contains("Online", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .FirstOrDefault()
            ?? voices.FirstOrDefault();

        if (anyGender is not null)
        {
            ApplySelected(anyGender, gender);
            return;
        }

        selectedVoiceName = null;
        usePromptCulture = false;
        status = "TTS: no voices installed";
        DisposeSynthUnlocked();
    }

    private void ApplySelected(VoiceInfo voice, ResolvedVoiceGender gender)
    {
        if (synth is null)
            return;

        synth.SelectVoice(voice.Name);
        selectedVoiceName = voice.Name;
        usePromptCulture = !MatchesTarget(voice, TargetLanguages.Current());
        status = DescribeVoiceUnlocked(gender);
    }

    private static bool MatchesTarget(VoiceInfo v, TargetLanguageInfo lang)
    {
        var iso = lang.TtsCulture.Split('-')[0];
        if (v.Culture.TwoLetterISOLanguageName.Equals(iso, StringComparison.OrdinalIgnoreCase))
            return true;
        if (v.Culture.Name.Equals(lang.TtsCulture, StringComparison.OrdinalIgnoreCase))
            return true;
        if (v.Name.Contains(lang.Name, StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private void ApplyRateVolumeUnlocked()
    {
        if (synth is null)
            return;

        var cfg = Plugin.Configuration;
        synth.Rate = Math.Clamp(cfg.TtsRate, -10, 10);
        synth.Volume = Math.Clamp(cfg.TtsVolume, 0, 100);

        if (!string.IsNullOrEmpty(selectedVoiceName))
        {
            try { synth.SelectVoice(selectedVoiceName); }
            catch { selectedVoiceName = null; }
        }
    }

    private string DescribeVoiceUnlocked(ResolvedVoiceGender gender)
    {
        if (synth is null)
            return status;

        try
        {
            var mode = usePromptCulture ? TargetCultureName() + " SSML" : synth.Voice.Culture.Name;
            var auto = string.IsNullOrEmpty(Plugin.Configuration.TtsVoiceName) ? "Auto" : "Manual";
            return $"TTS: {synth.Voice.Name} ({mode}, {gender}, {auto})";
        }
        catch
        {
            return status;
        }
    }

    private static string TargetCultureName() => TargetLanguages.Current().TtsCulture;

    private static int PreferCulture(CultureInfo culture, TargetLanguageInfo lang)
    {
        var n = culture.Name;
        if (n.Equals(lang.TtsCulture, StringComparison.OrdinalIgnoreCase))
            return 3;
        var iso = lang.TtsCulture.Split('-')[0];
        if (n.StartsWith(iso, StringComparison.OrdinalIgnoreCase))
            return 2;
        return 0;
    }

    private static int PreferMultiName(string name, ResolvedVoiceGender gender)
    {
        if (gender == ResolvedVoiceGender.Female)
        {
            if (name.Contains("Ava", StringComparison.OrdinalIgnoreCase)) return 3;
            if (name.Contains("Emma", StringComparison.OrdinalIgnoreCase)) return 2;
            return 0;
        }

        if (name.Contains("Andrew", StringComparison.OrdinalIgnoreCase)) return 3;
        if (name.Contains("Brian", StringComparison.OrdinalIgnoreCase)) return 2;
        return 0;
    }

    private static PromptRate MapPromptRate(int rate) => rate switch
    {
        <= -7 => PromptRate.ExtraSlow,
        <= -3 => PromptRate.Slow,
        <= 2 => PromptRate.Medium,
        <= 6 => PromptRate.Fast,
        _ => PromptRate.ExtraFast,
    };

    private static int RateToPercent(int rate) =>
        Math.Clamp(100 + (rate * 10), 50, 200);

    private static string BuildSsml(string text, int rate, string culture)
    {
        var pct = RateToPercent(rate);
        var escaped = System.Security.SecurityElement.Escape(text) ?? text;
        var lang = string.IsNullOrWhiteSpace(culture) ? "en-US" : culture;
        return
            $"<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            $"<speak version=\"1.0\" xmlns=\"http://www.w3.org/2001/10/synthesis\" xml:lang=\"{lang}\">" +
            $"<prosody rate=\"{pct}%\">{escaped}</prosody>" +
            $"</speak>";
    }
}
