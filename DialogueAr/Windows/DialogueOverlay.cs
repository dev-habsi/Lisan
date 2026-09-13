using System;
using System.IO;
using System.Linq;
using System.Numerics;
using DialogueAr.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Windowing;

namespace DialogueAr.Windows;

public sealed class DialogueOverlay : Window, IDisposable
{
    private readonly Plugin plugin;
    private IFontHandle? overlayFont;
    private bool draggingPos;
    private Vector2 pendingPos;

    public DialogueOverlay(Plugin plugin)
        : base("DialogueAr Overlay###DialogueArOverlay",
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.AlwaysAutoResize |
            ImGuiWindowFlags.NoFocusOnAppearing |
            ImGuiWindowFlags.NoNav)
    {
        this.plugin = plugin;
        SizeCondition = ImGuiCond.FirstUseEver;
        PositionCondition = ImGuiCond.FirstUseEver;
        RespectCloseHotkey = false;
        DisableWindowSounds = true;
        ApplySavedPosition(force: true);
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

    public void ApplySavedPosition(bool force = false)
    {
        var cfg = Plugin.Configuration;
        if (!cfg.OverlayPosSaved && !force)
            return;

        Position = new Vector2(cfg.OverlayPosX, cfg.OverlayPosY);
        PositionCondition = ImGuiCond.Always;
    }

    public override bool DrawConditions()
    {
        var cfg = Plugin.Configuration;
        if (!cfg.Enabled || !cfg.ShowOverlay)
            return false;
        if (!Plugin.ClientState.IsLoggedIn)
            return false;

        var line = plugin.Watcher.Current;
        if (line is null)
            return false;

        if (line.Busy && !cfg.ShowBusyPlaceholder)
            return false;

        return AddonDialogueWatcher.IsAddonVisible(line.Source);
    }

    public override void PreDraw()
    {
        var cfg = Plugin.Configuration;
        if (cfg.LockOverlay)
            Flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoInputs;
        else
            Flags &= ~(ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoInputs);

        if (PositionCondition == ImGuiCond.Always && cfg.OverlayPosSaved)
            PositionCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var cfg = Plugin.Configuration;
        var line = plugin.Watcher.Current;
        if (line is null)
            return;

        var lang = TargetLanguages.Current();

        var pos = ImGui.GetWindowPos();
        if (ImGui.IsWindowHovered() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            draggingPos = true;
            pendingPos = pos;
        }
        else if (draggingPos)
        {
            draggingPos = false;
            cfg.OverlayPosX = pendingPos.X;
            cfg.OverlayPosY = pendingPos.Y;
            cfg.OverlayPosSaved = true;
            cfg.Save();
        }

        var scale = Math.Clamp(cfg.OverlayScale, 0.7f, 3.0f);
        var wrapW = Math.Clamp(cfg.OverlayWidth, 200f, 1200f) * scale;
        ImGui.SetWindowFontScale(scale);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.02f, 0.03f, 0.06f, Math.Clamp(cfg.OverlayBgAlpha, 0f, 1f)));

        if (cfg.ShowSpeaker && !string.IsNullOrWhiteSpace(line.Speaker))
        {
            ImGui.TextColored(new Vector4(0.75f, 0.85f, 1f, 0.95f), line.Speaker);
        }

        if (cfg.ShowOriginalEnglish && !string.IsNullOrWhiteSpace(line.English))
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + wrapW);
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 0.85f), line.English);
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
        }

        if (line.Busy && !cfg.ShowBusyPlaceholder)
        {
            ImGui.PopStyleColor();
            ImGui.SetWindowFontScale(1f);
            return;
        }

        var fontPx = 22f * scale;
        var maxChars = cfg.OverlayWrapChars > 0
            ? cfg.OverlayWrapChars
            : DialogueDisplay.EstimateMaxChars(wrapW, fontPx);

        var textCol = cfg.OverlayTextColor;

        void DrawTranslation()
        {
            if (!lang.RightToLeft)
            {
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + wrapW);
                ImGui.TextColored(textCol, line.Translation);
                ImGui.PopTextWrapPos();
                return;
            }

            var lines = DialogueDisplay.LinesForImGui(line.Translation, maxChars, true, lang.ArabicScript);
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 2));
            var originX = ImGui.GetCursorPosX();
            foreach (var row in lines)
            {
                var size = ImGui.CalcTextSize(row);
                var pad = Math.Max(0f, wrapW - size.X);
                ImGui.SetCursorPosX(originX + pad);
                ImGui.TextColored(textCol, row);
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

        ImGui.PopStyleColor();
        ImGui.SetWindowFontScale(1f);

        if (cfg.RightClickOpensConfig && ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            plugin.ToggleConfigUi();
    }

    private void TryBuildFont()
    {
        try
        {
            var lang = TargetLanguages.Current();
            if (string.IsNullOrWhiteSpace(lang.FontFile))
                return;

            var dir = Plugin.PluginInterface.AssemblyLocation.Directory?.FullName ?? string.Empty;
            var candidates = new[]
            {
                Path.Combine(dir, "Fonts", lang.FontFile),
                Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "Fonts", lang.FontFile)),
            };

            var fontPath = candidates.FirstOrDefault(File.Exists);
            if (fontPath is null)
            {
                Plugin.Log.Warning($"Overlay font missing: {lang.FontFile}. Run Models\\setup-models.ps1 -Language {lang.Code}");
                return;
            }

            var ranges = lang.GlyphRanges ?? TargetLanguages.LatinGlyphs;
            overlayFont = Plugin.PluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(e =>
            {
                e.OnPreBuild(tk =>
                {
                    tk.AddFontFromFile(fontPath, new SafeFontConfig
                    {
                        SizePx = 22,
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
