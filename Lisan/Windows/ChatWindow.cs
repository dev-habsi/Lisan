using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Lisan.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Windowing;

namespace Lisan.Windows;

public sealed class ChatWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private IFontHandle? chatFont;
    private float builtFontPx = 18f;
    private string filter = "";
    private bool stickBottom = true;

    public ChatWindow(Plugin plugin)
        : base("Lisan chat###LisanChat")
    {
        this.plugin = plugin;
        Size = new Vector2(440, 320);
        SizeCondition = ImGuiCond.FirstUseEver;
        TryBuildFont();
    }

    public void Dispose()
    {
        chatFont?.Dispose();
        chatFont = null;
    }

    public void ReloadFont()
    {
        chatFont?.Dispose();
        chatFont = null;
        TryBuildFont();
    }

    public override void Draw()
    {
        var cfg = Plugin.Configuration;
        var pending = plugin.Chat.PendingCount;

        Toggle("Translate chat", () => cfg.TranslateChat, v => cfg.TranslateChat = v);
        ImGui.SameLine();
        var paused = plugin.Chat.Paused;
        if (ImGui.Checkbox("Pause", ref paused))
            plugin.Chat.Paused = paused;
        ImGui.SameLine();
        if (ImGui.Button("Copy last"))
        {
            var last = plugin.Chat.Last?.Translation;
            if (!string.IsNullOrWhiteSpace(last))
                Plugin.CopyText(last);
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear"))
            plugin.Chat.Clear();

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##chatfilter", "Filter channel / sender / text…", ref filter, 160);

        if (AddonDialogueWatcher.NeedsGameFontOverlay())
            ImGui.TextDisabled("Game chat cannot draw this script. Translations live here, original English stays in the game log.");
        else
            ImGui.TextDisabled("Original stays in the game log. Translation here (optional native Echo in settings).");

        if (pending > 0)
            ImGui.TextDisabled($"Translating… {pending} waiting");

        ImGui.Separator();

        var lang = TargetLanguages.Current();
        var needle = filter.Trim();
        var rows = plugin.Chat.Snapshot();

        if (ImGui.BeginChild("LisanChatLog", new Vector2(0, 0), true))
        {
            var atBottom = ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 12f;

            void DrawRows()
            {
                foreach (var e in rows)
                {
                    if (needle.Length > 0
                        && e.Channel.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0
                        && e.Sender.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0
                        && e.English.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0
                        && e.Translation.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    var when = e.Utc.ToLocalTime().ToString("HH:mm:ss");
                    var who = string.IsNullOrWhiteSpace(e.Sender) ? e.Channel : $"{e.Channel}  {e.Sender}";
                    ImGui.TextColored(ChatTranslator.ChannelColor(e.Type), $"{when}  {who}");

                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.65f, 0.65f, 0.65f, 1f));
                    ImGui.TextWrapped(e.English);
                    ImGui.PopStyleColor();

                    if (lang.RightToLeft && !string.IsNullOrWhiteSpace(e.Translation))
                    {
                        var wrapW = Math.Max(80f, ImGui.GetContentRegionAvail().X);
                        var maxChars = DialogueDisplay.EstimateMaxChars(wrapW, 18f);
                        var baked = DialogueDisplay.LinesForImGui(e.Translation, maxChars, true, lang.ArabicScript);
                        var originX = ImGui.GetCursorPosX();
                        foreach (var row in baked)
                        {
                            var size = ImGui.CalcTextSize(row);
                            var pad = Math.Max(0f, wrapW - size.X);
                            ImGui.SetCursorPosX(originX + pad);
                            ImGui.TextUnformatted(row);
                        }
                    }
                    else
                    {
                        ImGui.TextWrapped(e.Translation);
                    }

                    if (ImGui.IsItemClicked())
                        Plugin.CopyText(e.Translation);

                    ImGui.Spacing();
                    ImGui.Separator();
                }

                if (rows.Count == 0)
                    ImGui.TextDisabled("No chat yet. Say / party / tell while this is on.");
            }

            if (chatFont is not null)
            {
                using (chatFont.Push())
                    DrawRows();
            }
            else
            {
                DrawRows();
            }

            if (stickBottom && atBottom)
                ImGui.SetScrollHereY(1f);
        }

        ImGui.EndChild();
    }

    private static void Toggle(string label, Func<bool> get, Action<bool> set)
    {
        var value = get();
        if (ImGui.Checkbox(label, ref value))
        {
            set(value);
            Plugin.Configuration.Save();
        }
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
                return;

            var ranges = lang.GlyphRanges ?? TargetLanguages.LatinGlyphs;
            builtFontPx = 18f;
            chatFont = Plugin.PluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(e =>
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
            Plugin.Log.Warning(ex, "Chat font load failed");
        }
    }
}
