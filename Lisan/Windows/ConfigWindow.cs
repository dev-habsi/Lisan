using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;

namespace Lisan.Windows;

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
        Size = new Vector2(480, 520);
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
        ImGui.TextDisabled($"{Brand.Command}   ·   {Brand.Command} config   ·   {Brand.Command} lang ar   ·   {Brand.Command} test");
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

            Toggle("Right-to-left script", () => cfg.CustomRightToLeft, v =>
            {
                cfg.CustomRightToLeft = v;
                plugin.Overlay.ReloadFont();
            });
            ImGui.TextDisabled("Set the model folder on Translator, then Apply language.");
            if (ImGui.Button("Apply custom language"))
                plugin.ApplyLanguage();
        }

        ImGui.Spacing();
        ImGui.Text("Translate");
        Toggle("Talk (NPC dialogue)", () => cfg.TranslateTalk, v => cfg.TranslateTalk = v);
        Toggle("BattleTalk", () => cfg.TranslateBattleTalk, v => cfg.TranslateBattleTalk = v);
        Toggle("TalkSubtitle (cutscenes)", () => cfg.TranslateTalkSubtitle, v => cfg.TranslateTalkSubtitle = v);
        Toggle("Dialogue choices", () => cfg.TranslateChoices, v => cfg.TranslateChoices = v);
        ImGui.TextDisabled("Translation is written onto the game balloon. Scripts the game font cannot draw (Arabic, CJK, …) use a plugin font on the same balloon.");

        ImGui.Spacing();
        ImGui.Text("Balloon text");
        var fontSize = cfg.BalloonFontSize;
        if (ImGui.SliderFloat("Font size", ref fontSize, 16f, 42f, "%.0f"))
        {
            cfg.BalloonFontSize = fontSize;
            cfg.Save();
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
            plugin.Overlay.ReloadFont();

        var col = cfg.BalloonTextColor;
        if (ImGui.ColorEdit4("Text color", ref col))
        {
            cfg.BalloonTextColor = col;
            cfg.Save();
        }

        if (ImGui.Button("Reset text look"))
        {
            cfg.BalloonFontSize = 26f;
            cfg.BalloonTextColor = new Vector4(0.18f, 0.15f, 0.12f, 1f);
            cfg.Save();
            plugin.Overlay.ReloadFont();
        }

        ImGui.TextDisabled("Applies to balloon text. Arabic / CJK use the plugin font; other languages use the game font.");
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
