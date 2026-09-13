using System;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Memory;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace DialogueAr.Services;

public sealed class DialogueLine
{
    public string Speaker { get; init; } = string.Empty;
    public string English { get; init; } = string.Empty;
    public string Translation { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public bool Busy { get; init; }
}

public sealed class AddonDialogueWatcher : IDisposable
{
    private readonly LocalTranslator translator;
    private readonly object gate = new();
    private DialogueLine? current;
    private int seq;
    private string lastKey = string.Empty;

    public AddonDialogueWatcher(LocalTranslator translator)
    {
        this.translator = translator;
    }

    public DialogueLine? Current
    {
        get
        {
            lock (gate)
                return current;
        }
    }

    public void Register()
    {
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostDraw, "Talk", OnTalk);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostRefresh, "Talk", OnTalk);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "Talk", OnTalkClosed);

        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostDraw, "_BattleTalk", OnBattleTalk);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostRefresh, "_BattleTalk", OnBattleTalk);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "_BattleTalk", OnBattleTalkClosed);

        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostDraw, "TalkSubtitle", OnTalkSubtitle);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostRefresh, "TalkSubtitle", OnTalkSubtitle);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "TalkSubtitle", OnTalkSubtitleClosed);
    }

    public void Dispose()
    {
        Plugin.AddonLifecycle.UnregisterListener(OnTalk);
        Plugin.AddonLifecycle.UnregisterListener(OnTalkClosed);
        Plugin.AddonLifecycle.UnregisterListener(OnBattleTalk);
        Plugin.AddonLifecycle.UnregisterListener(OnBattleTalkClosed);
        Plugin.AddonLifecycle.UnregisterListener(OnTalkSubtitle);
        Plugin.AddonLifecycle.UnregisterListener(OnTalkSubtitleClosed);
    }

    public void OnFrameworkUpdate(IFramework framework)
    {
        var line = Current;
        if (line is null)
            return;

        if (line.Source is "Test")
            return;

        if (!IsAddonVisible(line.Source))
            ClearIfSource(line.Source);
    }

    public static bool IsAddonVisible(string source)
    {
        var addonName = source switch
        {
            "BattleTalk" => "_BattleTalk",
            "TalkSubtitle" => "TalkSubtitle",
            "Talk" => "Talk",
            _ => null,
        };

        if (addonName is null)
            return false;

        return IsNamedAddonVisible(addonName);
    }

    public static unsafe bool IsNamedAddonVisible(string addonName)
    {
        var addonPtr = Plugin.GameGui.GetAddonByName(addonName);
        if (addonPtr.Address == nint.Zero)
            return false;

        var addon = (AtkUnitBase*)addonPtr.Address;
        return addon is not null && addon->IsVisible;
    }

    private void OnTalkClosed(AddonEvent type, AddonArgs args) => ClearIfSource("Talk");
    private void OnBattleTalkClosed(AddonEvent type, AddonArgs args) => ClearIfSource("BattleTalk");
    private void OnTalkSubtitleClosed(AddonEvent type, AddonArgs args) => ClearIfSource("TalkSubtitle");

    private void OnTalk(AddonEvent type, AddonArgs args)
    {
        if (!Plugin.Configuration.Enabled || !Plugin.Configuration.TranslateTalk)
            return;

        ReadNodesAndTranslate("Talk", "Talk", speakerNodeId: 2, bodyNodeId: 3);
    }

    private void OnBattleTalk(AddonEvent type, AddonArgs args)
    {
        if (!Plugin.Configuration.Enabled || !Plugin.Configuration.TranslateBattleTalk)
            return;

        ReadNodesAndTranslate("BattleTalk", "_BattleTalk", speakerNodeId: 4, bodyNodeId: 6);
    }

    private void OnTalkSubtitle(AddonEvent type, AddonArgs args)
    {
        if (!Plugin.Configuration.Enabled || !Plugin.Configuration.TranslateTalkSubtitle)
            return;

        ReadNodesAndTranslate("TalkSubtitle", "TalkSubtitle", speakerNodeId: 0, bodyNodeId: 2);
    }

    private unsafe void ReadNodesAndTranslate(string source, string addonName, uint speakerNodeId, uint bodyNodeId)
    {
        var addonPtr = Plugin.GameGui.GetAddonByName(addonName);
        if (addonPtr.Address == nint.Zero)
            return;

        var addon = (AtkUnitBase*)addonPtr.Address;
        if (addon is null || !addon->IsVisible)
        {
            ClearIfSource(source);
            return;
        }

        var english = ReadNodeText(addon, bodyNodeId);
        if (string.IsNullOrWhiteSpace(english))
            return;

        var speaker = speakerNodeId == 0 ? string.Empty : ReadNodeText(addon, speakerNodeId);
        var key = source + "|" + speaker + "|" + english;
        if (key == lastKey)
            return;
        lastKey = key;

        PushBusy(speaker, english, source);
        var captured = english;
        var capturedSpeaker = speaker;
        var capturedSource = source;
        var id = Interlocked.Increment(ref seq);

        // Cache hit: show + speak immediately (no Python round-trip).
        if (translator.TryGetCached(captured, out var cached))
        {
            lock (gate)
            {
                if (id != seq)
                    return;
                current = new DialogueLine
                {
                    Speaker = capturedSpeaker,
                    English = captured,
                    Translation = cached,
                    Source = capturedSource,
                    Busy = false,
                };
            }

            Plugin.Tts.Speak(cached, capturedSpeaker);
            if (Plugin.Configuration.LogTranslations)
                Plugin.Log.Information($"[{capturedSource}] (cache) {captured} => {cached}");
            return;
        }

        Task.Run(() =>
        {
            try
            {
                var translated = translator.Translate(captured);
                lock (gate)
                {
                    if (id != seq)
                        return;
                    current = new DialogueLine
                    {
                        Speaker = capturedSpeaker,
                        English = captured,
                        Translation = translated,
                        Source = capturedSource,
                        Busy = false,
                    };
                }

                if (Plugin.Configuration.LogTranslations)
                    Plugin.Log.Information($"[{capturedSource}] {captured} => {translated}");

                Plugin.Tts.Speak(translated, capturedSpeaker);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "DialogueAr translate/TTS failed");
            }
        });
    }

    private void ClearIfSource(string source)
    {
        lock (gate)
        {
            if (current?.Source == source)
            {
                current = null;
                lastKey = string.Empty;
                if (Plugin.Configuration.TtsStopWhenDialogueCloses)
                    Plugin.Tts.Stop();
            }
        }
    }

    public void Clear()
    {
        lock (gate)
            current = null;
        lastKey = string.Empty;
    }

    private static unsafe string ReadNodeText(AtkUnitBase* addon, uint nodeId)
    {
        try
        {
            var node = addon->GetTextNodeById(nodeId);
            if (node is null)
                return string.Empty;

            var ptr = node->NodeText.StringPtr;
            if (ptr.Value == null)
                return string.Empty;

            return MemoryHelper.ReadSeStringAsString(out _, (nint)ptr.Value);
        }
        catch
        {
            return string.Empty;
        }
    }

    private void PushBusy(string speaker, string english, string source)
    {
        lock (gate)
        {
            current = new DialogueLine
            {
                Speaker = speaker,
                English = english,
                Translation = TargetLanguages.Current().BusyText,
                Source = source,
                Busy = true,
            };
        }
    }

    public void SetTestLine(string speaker, string english, string translation)
    {
        SetTestLineForSource("Test", english, translation);
    }

    public void SetTestLineForSource(string source, string english, string translation)
    {
        lock (gate)
        {
            current = new DialogueLine
            {
                Speaker = string.Empty,
                English = english,
                Translation = translation,
                Source = source,
                Busy = false,
            };
        }
    }

}
