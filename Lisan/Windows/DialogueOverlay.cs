using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Lisan.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace Lisan.Windows;

/// <summary>
/// Draws translated text on the Talk balloon when the game font cannot (Arabic, CJK, …).
/// LTR languages are written into the addon directly and do not use this window.
/// </summary>
public sealed class DialogueOverlay : Window, IDisposable
{
    private readonly Plugin plugin;
    private IFontHandle? overlayFont;
    private bool dockedToTalk;
    private bool drawingChoices;
    private float builtFontPx = 26f;

    /// <summary>Resolved overlay font file; null when the language needs one and it is missing.</summary>
    public string? FontPath { get; private set; }

    public DialogueOverlay(Plugin plugin)
        : base("Lisan Overlay###LisanOverlay",
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.AlwaysAutoResize |
            ImGuiWindowFlags.NoFocusOnAppearing |
            ImGuiWindowFlags.NoNav |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoInputs |
            ImGuiWindowFlags.NoBackground |
            ImGuiWindowFlags.NoSavedSettings)
    {
        this.plugin = plugin;
        RespectCloseHotkey = false;
        DisableWindowSounds = true;
        IsOpen = true;
        TryBuildFont();
    }

    public void Dispose()
    {
        overlayFont?.Dispose();
        overlayFont = null;
    }

    public void ReloadFont()
    {
        overlayFont?.Dispose();
        overlayFont = null;
        TryBuildFont();
    }

    public override bool DrawConditions()
    {
        var cfg = Plugin.Configuration;
        if (!cfg.Enabled)
            return false;
        if (!Plugin.ClientState.IsLoggedIn)
            return false;
        if (!AddonDialogueWatcher.NeedsGameFontOverlay())
            return false;

        if (plugin.Watcher.Choices.Count > 0)
            return true;

        var line = plugin.Watcher.Current;
        if (line is null)
            return false;

        return AddonDialogueWatcher.IsAddonVisible(line.Source);
    }

    public override void PreDraw()
    {
        dockedToTalk = false;
        drawingChoices = false;
        var choices = plugin.Watcher.Choices;
        if (choices.Count > 0)
        {
            drawingChoices = true;
            ForceMainWindow = true;
            Position = Vector2.Zero;
            PositionCondition = ImGuiCond.Always;
            var vp = ImGuiHelpers.MainViewport.WorkSize;
            var gs = ImGuiHelpers.GlobalScale;
            if (gs < 0.01f)
                gs = 1f;
            Size = vp / gs;
            SizeCondition = ImGuiCond.Always;
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = vp / gs,
                MaximumSize = vp / gs,
            };
        }
        else
        {
            var line = plugin.Watcher.Current;
            if (line is not null
                && AddonDialogueWatcher.TryGetBodyTextRect(line.Source, out var talkPos, out var talkSize, line.BodyNodeId))
            {
                dockedToTalk = true;
                ForceMainWindow = true;
                Position = talkPos;
                PositionCondition = ImGuiCond.Always;
                Size = new Vector2(talkSize.X, 0);
                SizeCondition = ImGuiCond.Always;
                var font = Math.Clamp(Plugin.Configuration.BalloonFontSize, 16f, 42f);
                SizeConstraints = new WindowSizeConstraints
                {
                    MinimumSize = new Vector2(talkSize.X, Math.Max(72f, font * 2.6f)),
                    MaximumSize = new Vector2(talkSize.X, Math.Max(220f, font * 10f)),
                };
            }
            else
            {
                ForceMainWindow = false;
                Position = new Vector2(-10000f, -10000f);
                PositionCondition = ImGuiCond.Always;
                Size = null;
                SizeConstraints = null;
            }
        }

        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0, 0, 0, 0));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0, 0, 0, 0));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(4f, 2f));
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(2);
    }

    public override void Draw()
    {
        if (drawingChoices)
        {
            DrawChoices();
            return;
        }

        if (!dockedToTalk)
            return;

        var line = plugin.Watcher.Current;
        if (line is null)
            return;

        var lang = TargetLanguages.Current();
        var cfg = Plugin.Configuration;
        var fontPx = Math.Clamp(cfg.BalloonFontSize, 16f, 42f);
        var scale = fontPx / Math.Max(8f, builtFontPx);
        var wrapW = Math.Max(80f, ImGui.GetContentRegionAvail().X);
        ImGui.SetWindowFontScale(scale);

        var maxChars = DialogueDisplay.EstimateMaxChars(wrapW, fontPx);
        // Talk and BattleTalk sit on the light parchment bubble. Dark panels keep the configured color.
        var textCol = line.Source is "Talk" or "BattleTalk"
            ? new Vector4(0f, 0f, 0f, 1f)
            : cfg.BalloonTextColor;
        if (line.Busy)
            textCol = new Vector4(textCol.X * 0.72f, textCol.Y * 0.72f, textCol.Z * 0.72f, textCol.W * 0.88f);

        void DrawTranslation()
        {
            if (string.IsNullOrWhiteSpace(line.Translation))
                return;

            if (!lang.RightToLeft)
            {
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + wrapW);
                DrawClearText(textCol, line.Translation);
                ImGui.PopTextWrapPos();
                return;
            }

            var lines = DialogueDisplay.LinesForImGui(line.Translation, maxChars, true, lang.ArabicScript);
            for (var i = 0; i < 8; i++)
            {
                var tooWide = false;
                foreach (var row in lines)
                {
                    if (ImGui.CalcTextSize(row).X > wrapW)
                    {
                        tooWide = true;
                        break;
                    }
                }

                if (!tooWide || maxChars <= 12)
                    break;
                maxChars -= 2;
                lines = DialogueDisplay.LinesForImGui(line.Translation, maxChars, true, lang.ArabicScript);
            }

            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 5));
            var originX = ImGui.GetCursorPosX();
            foreach (var row in lines)
            {
                var size = ImGui.CalcTextSize(row);
                if (size.X <= 2f)
                    continue;
                var pad = Math.Max(0f, wrapW - size.X);
                ImGui.SetCursorPosX(originX + pad);
                DrawClearText(textCol, row);
            }

            ImGui.PopStyleVar();
        }

        if (overlayFont is not null)
        {
            using (overlayFont.Push())
                DrawTranslation();
        }
        else
        {
            DrawTranslation();
        }

        ImGui.SetWindowFontScale(1f);
    }

    private void DrawChoices()
    {
        var lang = TargetLanguages.Current();
        var cfg = Plugin.Configuration;
        var fontPx = Math.Clamp(cfg.BalloonFontSize, 16f, 42f);
        var scale = fontPx / Math.Max(8f, builtFontPx);
        ImGui.SetWindowFontScale(scale);

        void DrawOne(DialogueChoice choice)
        {
            if (string.IsNullOrWhiteSpace(choice.Translation))
                return;

            var wrapW = Math.Max(48f, choice.Size.X);
            var textCol = cfg.BalloonTextColor;
            var fontPx = Math.Clamp(cfg.BalloonFontSize, 16f, 42f);
            ImGui.SetWindowFontScale(fontPx / Math.Max(8f, builtFontPx));
            if (choice.Busy)
                textCol = new Vector4(textCol.X * 0.72f, textCol.Y * 0.72f, textCol.Z * 0.72f, textCol.W * 0.88f);

            // Same space as Window.Position: game pixels plus the main viewport origin.
            var pos = choice.Pos + ImGuiHelpers.MainViewport.Pos;
            ImGui.PushClipRect(pos, pos + new Vector2(wrapW, Math.Max(20f, choice.Size.Y)), true);
            ImGui.SetCursorScreenPos(pos);

            if (!lang.RightToLeft)
            {
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + wrapW);
                DrawClearText(textCol, choice.Translation);
                ImGui.PopTextWrapPos();
                ImGui.PopClipRect();
                return;
            }

            var maxChars = DialogueDisplay.EstimateMaxChars(wrapW, fontPx);
            var lines = DialogueDisplay.LinesForImGui(choice.Translation, maxChars, true, lang.ArabicScript);
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 3));
            var originX = ImGui.GetCursorPosX();
            foreach (var row in lines)
            {
                var size = ImGui.CalcTextSize(row);
                if (size.X <= 2f)
                    continue;
                var pad = Math.Max(0f, wrapW - size.X);
                ImGui.SetCursorPosX(originX + pad);
                DrawClearText(textCol, row);
            }

            ImGui.PopStyleVar();
            ImGui.PopClipRect();
        }

        if (overlayFont is not null)
        {
            using (overlayFont.Push())
            {
                foreach (var choice in plugin.Watcher.Choices)
                    DrawOne(choice);
            }
        }
        else
        {
            foreach (var choice in plugin.Watcher.Choices)
                DrawOne(choice);
        }

        ImGui.SetWindowFontScale(1f);
    }

    /// <summary>Draw colored overlay text on the game balloon.</summary>
    private static void DrawClearText(Vector4 color, string text)
    {
        ImGui.TextColored(color, text);
    }

    private void TryBuildFont()
    {
        try
        {
            var lang = TargetLanguages.Current();
            FontPath = null;
            if (string.IsNullOrWhiteSpace(lang.FontFile))
                return;

            var dir = Plugin.PluginInterface.AssemblyLocation.Directory?.FullName ?? string.Empty;
            var candidates = new[]
            {
                Path.Combine(dir, "Fonts", lang.FontFile),
                Path.Combine(plugin.Setup.FontDir, lang.FontFile),
                Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "Fonts", lang.FontFile)),
            };

            var fontPath = candidates.FirstOrDefault(File.Exists);
            FontPath = fontPath;
            if (fontPath is null)
            {
                Plugin.Log.Warning($"Overlay font missing: {lang.FontFile}. Download it on the Setup tab.");
                return;
            }

            var ranges = lang.GlyphRanges ?? TargetLanguages.LatinGlyphs;
            builtFontPx = Math.Clamp(Plugin.Configuration.BalloonFontSize, 16f, 42f);
            overlayFont = Plugin.PluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(e =>
            {
                e.OnPreBuild(tk =>
                {
                    tk.AddFontFromFile(fontPath, new SafeFontConfig
                    {
                        SizePx = builtFontPx,
                        GlyphRanges = ranges,
                    });
                });
            });
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Overlay font load failed");
        }
    }
}
