using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace DialogueAr.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private string modelDirEdit = string.Empty;
    private bool modelDirLoaded;
    private string newCharName = string.Empty;
    private int newCharVoiceIndex;
    private string customCultureEdit = string.Empty;
    private string customTokenEdit = string.Empty;
    private bool customFieldsLoaded;

    public ConfigWindow(Plugin plugin)
        : base("DialogueAr Settings###DialogueArConfig")
    {
        this.plugin = plugin;
        Size = new Vector2(500, 640);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var cfg = Plugin.Configuration;
        if (!modelDirLoaded)
        {
            modelDirEdit = cfg.ModelDirectory;
            modelDirLoaded = true;
        }

        if (!customFieldsLoaded)
        {
            customCultureEdit = cfg.CustomTtsCulture;
            customTokenEdit = cfg.CustomTargetToken;
            customFieldsLoaded = true;
        }

        var lang = TargetLanguages.Current();
        ImGui.TextWrapped($"Offline English → {lang.Name} dialogue (Talk / BattleTalk / TalkSubtitle). No cloud translation API.");
        ImGui.Spacing();

        if (ImGui.BeginTabBar("DialogueArTabs"))
        {
            if (ImGui.BeginTabItem("General"))
            {
                DrawGeneral(cfg);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Overlay"))
            {
                DrawOverlay(cfg);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Speech"))
            {
                DrawSpeech(cfg);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Characters"))
            {
                DrawCharacters(cfg);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Translator"))
            {
                DrawTranslator(cfg);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Debug"))
            {
                DrawDebug(cfg);
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        ImGui.Separator();
        ImGui.TextDisabled("/dialogar  |  /dialogar config  |  /dialogar lang fr  |  /dialogar test  |  /dialogar stop");
    }

    private void DrawGeneral(Configuration cfg)
    {
        Toggle("Enable plugin", () => cfg.Enabled, v => cfg.Enabled = v);
        ImGui.Spacing();

        ImGui.Text("Target language");
        var labels = TargetLanguages.ComboLabels();
        var selected = TargetLanguages.ComboIndex(cfg.TargetLanguage);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("##targetlang", ref selected, labels, labels.Length))
        {
            cfg.TargetLanguage = TargetLanguages.CodeFromCombo(selected);
            cfg.Save();
            plugin.ApplyLanguage();
        }

        var lang = TargetLanguages.Current();
        ImGui.TextDisabled($"Model: {lang.HuggingFaceRepo}");
        if (!string.IsNullOrEmpty(lang.TargetToken))
            ImGui.TextDisabled($"Target token: {lang.TargetToken}");
        ImGui.TextDisabled($"Then run:  Models\\setup-models.ps1 -Language {lang.Code}");

        if (cfg.TargetLanguage == TargetLanguages.CustomCode)
        {
            ImGui.Spacing();
            ImGui.Text("Custom pack");
            ImGui.SetNextItemWidth(160);
            if (ImGui.InputText("TTS culture (e.g. ja-JP)", ref customCultureEdit, 16))
            {
                cfg.CustomTtsCulture = customCultureEdit.Trim();
                cfg.Save();
            }

            ImGui.SetNextItemWidth(160);
            if (ImGui.InputText("Opus token (optional)", ref customTokenEdit, 32))
            {
                cfg.CustomTargetToken = customTokenEdit.Trim();
                cfg.Save();
            }

            Toggle("Right-to-left overlay", () => cfg.CustomRightToLeft, v =>
            {
                cfg.CustomRightToLeft = v;
                plugin.Overlay.ReloadFont();
            });
            ImGui.TextDisabled("Point Translator tab at your model folder, then Apply language.");
            if (ImGui.Button("Apply custom language"))
                plugin.ApplyLanguage();
        }

        ImGui.Spacing();
        ImGui.Text("Translate sources");
        Toggle("Talk (NPC dialogue)", () => cfg.TranslateTalk, v => cfg.TranslateTalk = v);
        Toggle("BattleTalk", () => cfg.TranslateBattleTalk, v => cfg.TranslateBattleTalk = v);
        Toggle("TalkSubtitle (cutscenes)", () => cfg.TranslateTalkSubtitle, v => cfg.TranslateTalkSubtitle = v);
    }

    private void DrawOverlay(Configuration cfg)
    {
        Toggle("Show overlay", () => cfg.ShowOverlay, v =>
        {
            cfg.ShowOverlay = v;
            plugin.Overlay.IsOpen = v;
        });
        Toggle("Lock overlay (click-through)", () => cfg.LockOverlay, v => cfg.LockOverlay = v);
        Toggle("Show speaker name", () => cfg.ShowSpeaker, v => cfg.ShowSpeaker = v);
        Toggle("Show original English", () => cfg.ShowOriginalEnglish, v => cfg.ShowOriginalEnglish = v);
        Toggle("Show “translating…” placeholder", () => cfg.ShowBusyPlaceholder, v => cfg.ShowBusyPlaceholder = v);
        Toggle("Right-click overlay opens config", () => cfg.RightClickOpensConfig, v => cfg.RightClickOpensConfig = v);

        var scale = cfg.OverlayScale;
        if (ImGui.SliderFloat("Scale", ref scale, 0.7f, 3.0f, "%.2f"))
        {
            cfg.OverlayScale = scale;
            cfg.Save();
        }

        var width = cfg.OverlayWidth;
        if (ImGui.SliderFloat("Wrap width", ref width, 280f, 900f, "%.0f"))
        {
            cfg.OverlayWidth = width;
            cfg.Save();
        }

        var wrapChars = cfg.OverlayWrapChars;
        if (ImGui.SliderInt("Wrap chars (0 = auto)", ref wrapChars, 0, 60))
        {
            cfg.OverlayWrapChars = wrapChars;
            cfg.Save();
        }

        ImGui.TextDisabled("RTL languages (Arabic / Hebrew / Persian) are pre-wrapped and right-aligned. LTR uses ImGui wrap.");

        var bg = cfg.OverlayBgAlpha;
        if (ImGui.SliderFloat("Background opacity", ref bg, 0f, 1f, "%.2f"))
        {
            cfg.OverlayBgAlpha = bg;
            cfg.Save();
        }

        var col = cfg.OverlayTextColor;
        if (ImGui.ColorEdit4("Translation text color", ref col))
        {
            cfg.OverlayTextColor = col;
            cfg.Save();
        }

        if (ImGui.Button("Reset overlay position"))
        {
            cfg.OverlayPosX = 420f;
            cfg.OverlayPosY = 620f;
            cfg.OverlayPosSaved = true;
            cfg.Save();
            plugin.Overlay.ApplySavedPosition(force: true);
        }
    }

    private void DrawSpeech(Configuration cfg)
    {
        Toggle("Speak translation (Windows TTS)", () => cfg.EnableTts, v =>
        {
            cfg.EnableTts = v;
            if (!v)
                Plugin.Tts.Stop();
            else
            {
                Plugin.Tts.EnsureReady();
                Plugin.Tts.RefreshVoiceList();
            }
        });

        var voices = Plugin.Tts.GetInstalledVoices();
        // Index 0 = Auto; then every installed voice.
        var labels = new string[voices.Length + 1];
        labels[0] = "★ Auto (Male / Female by speaker)";
        for (var i = 0; i < voices.Length; i++)
            labels[i + 1] = voices[i].Label;

        var selected = 0;
        if (!string.IsNullOrWhiteSpace(cfg.TtsVoiceName))
        {
            for (var i = 0; i < voices.Length; i++)
            {
                if (voices[i].Name.Equals(cfg.TtsVoiceName, StringComparison.OrdinalIgnoreCase))
                {
                    selected = i + 1;
                    break;
                }
            }
        }

        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("Voice", ref selected, labels, labels.Length))
        {
            cfg.TtsVoiceName = selected == 0 ? "" : voices[selected - 1].Name;
            cfg.Save();
        }

        if (ImGui.Button("Refresh voice list"))
            Plugin.Tts.RefreshVoiceList();

        ImGui.SameLine();
        ImGui.TextDisabled($"{voices.Length} installed");

        var isAuto = string.IsNullOrWhiteSpace(cfg.TtsVoiceName);
        if (isAuto)
        {
            var gender = (int)cfg.TtsVoiceGender;
            var genders = new[] { "Auto (detect speaker sex)", "Prefer Female", "Prefer Male" };
            if (ImGui.Combo("Auto gender", ref gender, genders, genders.Length))
            {
                cfg.TtsVoiceGender = (TtsVoiceGender)gender;
                cfg.Save();
            }

            ImGui.TextDisabled("Auto picks target-language voice → Multilingual → any voice matching that gender.");
        }
        else
        {
            ImGui.TextDisabled("Manual voice locked. Switch to ★ Auto to use male/female selection.");
        }

        var vol = cfg.TtsVolume;
        if (ImGui.SliderInt("Volume", ref vol, 0, 100))
        {
            cfg.TtsVolume = vol;
            cfg.Save();
        }

        var rate = cfg.TtsRate;
        if (ImGui.SliderInt("Speak rate (−10 slow … +10 fast)", ref rate, -10, 10))
        {
            cfg.TtsRate = rate;
            cfg.Save();
        }

        if (ImGui.Button("Slower"))
        {
            cfg.TtsRate = Math.Clamp(cfg.TtsRate - 2, -10, 10);
            cfg.Save();
        }

        ImGui.SameLine();
        if (ImGui.Button("Faster"))
        {
            cfg.TtsRate = Math.Clamp(cfg.TtsRate + 2, -10, 10);
            cfg.Save();
        }

        ImGui.SameLine();
        if (ImGui.Button("Max rate"))
        {
            cfg.TtsRate = 10;
            cfg.Save();
        }

        ImGui.Spacing();
        Toggle("Queue lines (don’t cut previous)", () => cfg.TtsQueueLines, v => cfg.TtsQueueLines = v);
        Toggle("Stop TTS when dialogue closes", () => cfg.TtsStopWhenDialogueCloses, v => cfg.TtsStopWhenDialogueCloses = v);
        Toggle("Skip duplicate lines", () => cfg.TtsSkipDuplicates, v => cfg.TtsSkipDuplicates = v);
        Toggle("Warm up TTS on plugin load", () => cfg.TtsWarmupOnLoad, v => cfg.TtsWarmupOnLoad = v);

        if (ImGui.Button("Stop speech now"))
            Plugin.Tts.Stop();

        ImGui.TextDisabled(Plugin.Tts.Status);
        var ttsLang = TargetLanguages.Current();
        ImGui.TextDisabled($"Non-native voices use {ttsLang.TtsCulture} SSML. Install a matching Windows TTS pack for best quality.");
        if (!string.IsNullOrWhiteSpace(ttsLang.WindowsTtsCapability))
            ImGui.TextDisabled($"Admin PowerShell: Add-WindowsCapability -Online -Name {ttsLang.WindowsTtsCapability}");
        ImGui.TextDisabled("Per-character voices: see Characters tab (they override this global voice).");
    }

    private void DrawCharacters(Configuration cfg)
    {
        ImGui.TextWrapped("Assign a Windows TTS voice to a Talk speaker name. Overrides global Auto/manual voice.");
        ImGui.Spacing();

        var line = plugin.Watcher.Current;
        if (line is not null && !string.IsNullOrWhiteSpace(line.Speaker))
        {
            ImGui.Text($"Current speaker: {line.Speaker}");
            if (ImGui.Button("Use current speaker name"))
                newCharName = line.Speaker;
            ImGui.SameLine();
            var mapped = cfg.FindCharacterVoice(line.Speaker);
            ImGui.TextDisabled(mapped is null ? "(no override)" : $"→ {mapped}");
        }
        else
        {
            ImGui.TextDisabled("No active dialogue speaker (open Talk to quick-fill).");
        }

        ImGui.Separator();
        ImGui.Text("Add override");
        ImGui.SetNextItemWidth(220);
        ImGui.InputText("Character name", ref newCharName, 128);

        var voices = Plugin.Tts.GetInstalledVoices();
        if (voices.Length == 0)
        {
            ImGui.TextDisabled("No voices loaded yet — open Speech tab and Refresh.");
            if (ImGui.Button("Refresh voices"))
                Plugin.Tts.RefreshVoiceList();
            return;
        }

        if (newCharVoiceIndex >= voices.Length)
            newCharVoiceIndex = 0;

        var voiceLabels = new string[voices.Length];
        for (var i = 0; i < voices.Length; i++)
            voiceLabels[i] = voices[i].Label;

        ImGui.SetNextItemWidth(-1);
        ImGui.Combo("Voice##charvoice", ref newCharVoiceIndex, voiceLabels, voiceLabels.Length);

        if (ImGui.Button("Add / update"))
        {
            var name = newCharName.Trim();
            if (!string.IsNullOrEmpty(name))
            {
                var voiceName = voices[newCharVoiceIndex].Name;
                var existing = cfg.CharacterVoices.Find(r =>
                    r.SpeakerName.Trim().Equals(name, StringComparison.OrdinalIgnoreCase));
                if (existing is not null)
                    existing.VoiceName = voiceName;
                else
                    cfg.CharacterVoices.Add(new CharacterVoiceOverride
                    {
                        SpeakerName = name,
                        VoiceName = voiceName,
                    });
                cfg.Save();
                newCharName = string.Empty;
            }
        }

        ImGui.Separator();
        ImGui.Text($"Overrides ({cfg.CharacterVoices.Count})");

        CharacterVoiceOverride? remove = null;
        for (var i = 0; i < cfg.CharacterVoices.Count; i++)
        {
            var row = cfg.CharacterVoices[i];
            ImGui.PushID(i);

            var speaker = row.SpeakerName;
            ImGui.SetNextItemWidth(160);
            if (ImGui.InputText("##spk", ref speaker, 128))
                row.SpeakerName = speaker;

            ImGui.SameLine();
            var vIdx = 0;
            for (var v = 0; v < voices.Length; v++)
            {
                if (voices[v].Name.Equals(row.VoiceName, StringComparison.OrdinalIgnoreCase))
                {
                    vIdx = v;
                    break;
                }
            }

            ImGui.SetNextItemWidth(280);
            if (ImGui.Combo("##voz", ref vIdx, voiceLabels, voiceLabels.Length))
            {
                row.VoiceName = voices[vIdx].Name;
                cfg.Save();
            }

            ImGui.SameLine();
            if (ImGui.Button("X"))
                remove = row;

            ImGui.PopID();
        }

        if (remove is not null)
        {
            cfg.CharacterVoices.Remove(remove);
            cfg.Save();
        }

        if (cfg.CharacterVoices.Count > 0 && ImGui.Button("Save name edits"))
            cfg.Save();

        ImGui.TextDisabled("Matches exact name, or first name (e.g. “Alphinaud” ↔ “Alphinaud Leveilleur”).");
    }

    private void DrawTranslator(Configuration cfg)
    {
        ImGui.Text($"Status: {plugin.Translator.Status}");
        ImGui.TextWrapped($"Active model: {plugin.Translator.ModelDirectory}");
        ImGui.Text($"Cache entries: {plugin.Cache.Count}");

        Toggle("Fast translation (greedy, recommended)", () => cfg.FastTranslation, v => cfg.FastTranslation = v);
        Toggle("Warm up translator on load", () => cfg.WarmupTranslatorOnLoad, v => cfg.WarmupTranslatorOnLoad = v);

        ImGui.Spacing();
        ImGui.Text("Custom model folder (optional)");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##modeldir", ref modelDirEdit, 512);
        if (ImGui.Button("Apply model folder"))
        {
            cfg.ModelDirectory = modelDirEdit.Trim();
            cfg.Save();
            plugin.ApplyLanguage();
        }

        ImGui.Spacing();
        if (ImGui.Button("Clear translation cache"))
            plugin.Cache.Clear();

        ImGui.SameLine();
        if (ImGui.Button("Test translate"))
            plugin.RunTestTranslation();

        var setupLang = TargetLanguages.Current();
        ImGui.TextDisabled($"First-time setup: run Models\\setup-models.ps1 -Language {setupLang.Code} then Apply language / reload.");
        if (ImGui.Button("Apply language / restart worker"))
            plugin.ApplyLanguage();
    }

    private void DrawDebug(Configuration cfg)
    {
        Toggle("Log each translation to Dalamud log", () => cfg.LogTranslations, v => cfg.LogTranslations = v);

        var line = plugin.Watcher.Current;
        if (line is null)
        {
            ImGui.TextDisabled("No active dialogue line.");
        }
        else
        {
            ImGui.Text($"Language: {TargetLanguages.Current().Label}");
            ImGui.Text($"Source: {line.Source}");
            ImGui.Text($"Busy: {line.Busy}");
            ImGui.TextWrapped($"Speaker: {line.Speaker}");
            ImGui.TextWrapped($"EN: {line.English}");
            ImGui.TextWrapped($"{TargetLanguages.Current().Code.ToUpperInvariant()}: {line.Translation}");
        }

        ImGui.Spacing();
        ImGui.TextDisabled($"Plugin version: {typeof(Plugin).Assembly.GetName().Version}");
    }

    private void Toggle(string label, Func<bool> get, Action<bool> set)
    {
        var value = get();
        if (ImGui.Checkbox(label, ref value))
        {
            set(value);
            Plugin.Configuration.Save();
        }
    }
}
