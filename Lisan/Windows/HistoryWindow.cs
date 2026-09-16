using System;
using System.Numerics;
using Lisan.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Lisan.Windows;

public sealed class HistoryWindow : Window
{
    private readonly Plugin plugin;
    private string filter = "";

    public HistoryWindow(Plugin plugin)
        : base("Lisan history###LisanHistory")
    {
        this.plugin = plugin;
        Size = new Vector2(560, 480);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        if (ImGui.Button("Copy last"))
            plugin.CopyLastTranslation();
        ImGui.SameLine();
        if (ImGui.Button("Speak last"))
            Plugin.Tts.Replay();
        ImGui.SameLine();
        if (ImGui.Button("Stop TTS"))
            Plugin.Tts.Stop();
        ImGui.SameLine();
        if (ImGui.Button("Export .txt"))
        {
            var file = Plugin.History.WriteExportFile();
            if (file is not null)
                Plugin.Log.Information($"{Brand.Name} history exported: {file}");
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear"))
            Plugin.History.Clear();

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##histfilter", "Filter speaker / English / translation…", ref filter, 200);

        ImGui.TextDisabled($"{Plugin.History.Count} lines  ·  {Brand.Command} history  ·  {Brand.Command} copy  ·  {Brand.Command} speak");
        ImGui.Separator();

        var needle = filter.Trim();
        var rows = Plugin.History.Snapshot();
        rows.Reverse();

        if (ImGui.BeginChild("LisanHistoryList", new Vector2(0, 0), true))
        {
            foreach (var e in rows)
            {
                if (needle.Length > 0
                    && (e.Speaker.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
                    && (e.English.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
                    && (e.Translation.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
                    && (e.Source.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0))
                    continue;

                var when = e.Utc.ToLocalTime().ToString("HH:mm:ss");
                var title = string.IsNullOrWhiteSpace(e.Speaker)
                    ? $"{when}  {e.Source}"
                    : $"{when}  {e.Speaker}";

                if (ImGui.TreeNodeEx($"{title}##{e.Utc.Ticks}{e.English.Length}"))
                {
                    ImGui.TextWrapped(e.English);
                    ImGui.Spacing();
                    ImGui.TextWrapped(e.Translation);

                    if (ImGui.SmallButton($"Copy##{e.Utc.Ticks}c"))
                        ImGui.SetClipboardText(e.Translation);
                    ImGui.SameLine();
                    if (ImGui.SmallButton($"Copy EN##{e.Utc.Ticks}e"))
                        ImGui.SetClipboardText(e.English);
                    ImGui.SameLine();
                    if (ImGui.SmallButton($"Speak##{e.Utc.Ticks}s"))
                    {
                        Plugin.Tts.Stop();
                        Plugin.Tts.SpeakDialogue(e.Translation, ignoreFocus: true);
                    }

                    ImGui.TreePop();
                }

                ImGui.Separator();
            }

            if (rows.Count == 0)
                ImGui.TextDisabled("No lines yet. Talk to an NPC.");
        }

        ImGui.EndChild();
    }
}
