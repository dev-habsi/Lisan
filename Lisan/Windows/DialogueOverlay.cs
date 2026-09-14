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
    private bool dockedToTalk;

    public DialogueOverlay(Plugin plugin)
        : base("Lisan Overlay###LisanOverlay",
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
        if (!cfg.Enabled)
            return false;
        if (!Plugin.ClientState.IsLoggedIn)
            return false;

        var line = plugin.Watcher.Current;
        if (line is null)
            return false;

        var docked = cfg.ReplaceNativeDialogue && AddonDialogueWatcher.NeedsGameFontOverlay();
        if (!cfg.ShowOverlay && !docked)
            return false;

        if (line.Busy && !cfg.ShowBusyPlaceholder && !docked)
            return false;

        return AddonDialogueWatcher.IsAddonVisible(line.Source);
    }

    public override void PreDraw()
    {
        var cfg = Plugin.Configuration;
        var line = plugin.Watcher.Current;
        dockedToTalk = false;
        if (cfg.ReplaceNativeDialogue
            && AddonDialogueWatcher.NeedsGameFontOverlay()
            && line is not null
            && AddonDialogueWatcher.TryGetBodyTextRect(line.Source, out var talkPos, out var talkSize, line.BodyNodeId))
        {
            dockedToTalk = true;
            ForceMainWindow = true;
            Flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoSavedSettings;
            Flags |= ImGuiWindowFlags.AlwaysAutoResize;
            Position = talkPos;
            PositionCondition = ImGuiCond.Always;
            Size = new Vector2(talkSize.X, 0);
            SizeCondition = ImGuiCond.Always;
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(talkSize.X, 72f),
                MaximumSize = new Vector2(talkSize.X, 220f),
            };
            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(4f, 2f));
            return;
        }

        ForceMainWindow = false;
        Flags |= ImGuiWindowFlags.AlwaysAutoResize;
        Flags &= ~(ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoSavedSettings);
        Size = null;
        SizeConstraints = null;
        SizeCondition = ImGuiCond.FirstUseEver;

        if (cfg.LockOverlay)
            Flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoInputs;
        else
            Flags &= ~(ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoInputs);

        if (PositionCondition == ImGuiCond.Always && cfg.OverlayPosSaved)
            PositionCondition = ImGuiCond.FirstUseEver;

        var alpha = Math.Clamp(cfg.OverlayBgAlpha, 0f, 1f);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.945f, 0.922f, 0.855f, alpha));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.78f, 0.62f, 0.28f, Math.Min(1f, alpha + 0.08f)));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 2.25f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 5f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(18f, 14f));
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(2);
    }

    public override void Draw()
    {
        var cfg = Plugin.Configuration;
        var line = plugin.Watcher.Current;
        if (line is null)
            return;

        var lang = TargetLanguages.Current();
        var docked = dockedToTalk;

        if (!docked)
        {
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
        }

        var scale = docked ? 1.08f : Math.Clamp(cfg.OverlayScale, 0.7f, 3.0f);
        var wrapW = docked
            ? Math.Max(80f, ImGui.GetContentRegionAvail().X)
            : Math.Clamp(cfg.OverlayWidth, 200f, 1200f) * scale;
        ImGui.SetWindowFontScale(scale);

        if (!docked && cfg.ShowSpeaker && !string.IsNullOrWhiteSpace(line.Speaker))
        {
            ImGui.TextColored(new Vector4(0.48f, 0.34f, 0.14f, 0.95f), line.Speaker);
            var p0 = ImGui.GetCursorScreenPos();
            var x = p0.X;
            var y = p0.Y - 2f;
            ImGui.GetWindowDrawList().AddLine(
                new Vector2(x, y),
                new Vector2(x + wrapW, y),
                ImGui.GetColorU32(new Vector4(0.72f, 0.56f, 0.26f, 0.55f)),
                1.25f);
            ImGui.Dummy(new Vector2(1, 6));
        }

        if (!docked && cfg.ShowOriginalEnglish && !string.IsNullOrWhiteSpace(line.English))
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + wrapW);
            ImGui.TextColored(new Vector4(0.42f, 0.37f, 0.30f, 0.88f), line.English);
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
        }

        if (line.Busy && !cfg.ShowBusyPlaceholder && !docked)
        {
            DrawXivFrame();
            ImGui.SetWindowFontScale(1f);
            return;
        }

        var fontPx = 22f * scale;
        var maxChars = cfg.OverlayWrapChars > 0
            ? cfg.OverlayWrapChars
            : DialogueDisplay.EstimateMaxChars(wrapW, docked ? fontPx * 1.2f : fontPx);

        var textCol = line.Busy
            ? new Vector4(0.42f, 0.36f, 0.28f, 0.88f)
            : docked
                ? new Vector4(0.18f, 0.15f, 0.12f, 1f)
                : cfg.OverlayTextColor;

        void DrawTranslation()
        {
            if (string.IsNullOrWhiteSpace(line.Translation))
                return;

            if (!lang.RightToLeft)
            {
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + wrapW);
                ImGui.TextColored(textCol, line.Translation);
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

            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 3));
            var originX = ImGui.GetCursorPosX();
            foreach (var row in lines)
            {
                var size = ImGui.CalcTextSize(row);
                if (size.X <= 2f)
                    continue;
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

        if (!docked)
            DrawXivFrame();
        ImGui.SetWindowFontScale(1f);

        if (!docked && cfg.RightClickOpensConfig && ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            plugin.ToggleConfigUi();
    }

    /// <summary>Gold double-line + corner ticks, journal-adjacent (no filigree texture).</summary>
    private static void DrawXivFrame()
    {
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetWindowPos();
        var max = min + ImGui.GetWindowSize();
        const float radius = 5f;

        var goldHi = ImGui.GetColorU32(new Vector4(0.93f, 0.80f, 0.42f, 0.88f));
        var gold = ImGui.GetColorU32(new Vector4(0.76f, 0.58f, 0.24f, 0.92f));
        var goldDim = ImGui.GetColorU32(new Vector4(0.55f, 0.42f, 0.18f, 0.55f));

        dl.AddRect(min + new Vector2(1.5f, 1.5f), max - new Vector2(1.5f, 1.5f), goldHi, radius, ImDrawFlags.None, 1.6f);
        dl.AddRect(min + new Vector2(5f, 5f), max - new Vector2(5f, 5f), goldDim, radius - 1f, ImDrawFlags.None, 1.15f);

        const float inset = 3.5f;
        const float tick = 13f;
        void Corner(Vector2 c, float dx, float dy)
        {
            dl.AddLine(c, c + new Vector2(dx * tick, 0), gold, 2.1f);
            dl.AddLine(c, c + new Vector2(0, dy * tick), gold, 2.1f);
        }

        Corner(min + new Vector2(inset, inset), 1, 1);
        Corner(new Vector2(max.X - inset, min.Y + inset), -1, 1);
        Corner(new Vector2(min.X + inset, max.Y - inset), 1, -1);
        Corner(max - new Vector2(inset, inset), -1, -1);
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
