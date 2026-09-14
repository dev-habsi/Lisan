using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;

namespace DialogueAr.Windows;

public sealed class ConfigWindow : Window
{
    private readonly Plugin plugin;
    private string modelDirEdit = string.Empty;
    private bool modelDirLoaded;
    private string customTokenEdit = string.Empty;
    private bool customFieldsLoaded;

    public ConfigWindow(Plugin plugin)
        : base("Lisan###LisanConfig")
    {
        this.plugin = plugin;
        Size = new Vector2(500, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

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
            customTokenEdit = cfg.CustomTargetToken;
            customFieldsLoaded = true;
        }

        var lang = TargetLanguages.Current();
        ImGui.TextWrapped($"Hear this world in {lang.Name}. Offline, on your machine — a little gift from {Brand.SiteLabel}.");
        ImGui.Spacing();

        if (ImGui.BeginTabBar("LisanTabs"))
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
        ImGui.TextDisabled($"{Brand.Command}   ·   {Brand.Command} config   ·   {Brand.Command} lang fr   ·   {Brand.Command} test");
        ImGui.Spacing();
        ImGui.TextDisabled("Made with love at");
        ImGui.SameLine();
        if (ImGui.SmallButton(Brand.SiteLabel))
            Util.OpenLink(Brand.Site);
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
        ImGui.Spacing();
        Toggle("Replace in-game dialogue box", () => cfg.ReplaceNativeDialogue, v => cfg.ReplaceNativeDialogue = v);
        ImGui.TextDisabled("Arabic is drawn on the Talk balloon with Noto (game font has no Arabic glyphs — that was the ====).");
    }

    private void DrawOverlay(Configuration cfg)
    {
        Toggle("Show extra overlay (off = use the game balloon only)", () => cfg.ShowOverlay, v =>
        {
            cfg.ShowOverlay = v;
            plugin.Overlay.IsOpen = true;
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

        ImGui.TextDisabled("Overlay uses FFXIV-style parchment + gold frame (not a black box).");
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
        ImGui.TextDisabled($"{Brand.Name} {typeof(Plugin).Assembly.GetName().Version}  ·  {Brand.SiteLabel}");
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
