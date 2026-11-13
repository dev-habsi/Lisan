using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Interface.Utility;
using Dalamud.Memory;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Text.Payloads;
using Lumina.Text.ReadOnly;

namespace Lisan.Services;

public sealed class DialogueLine
{
    public string Speaker { get; init; } = string.Empty;
    public string English { get; init; } = string.Empty;
    public string Translation { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public uint BodyNodeId { get; init; }
    public bool Busy { get; init; }
}

public sealed class DialogueChoice
{
    public uint NodeId { get; init; }
    public string English { get; init; } = string.Empty;
    public string Translation { get; init; } = string.Empty;
    public bool Busy { get; init; }
    public Vector2 Pos { get; init; }
    public Vector2 Size { get; init; }
}

public sealed class AddonDialogueWatcher : IDisposable
{
    private readonly LocalTranslator translator;
    private readonly object gate = new();
    private readonly Dictionary<string, GCHandle> textPins = new();
    private DialogueLine? current;
    private int seq;
    private string lastKey = string.Empty;
    private string lastOriginal = string.Empty;
    private string lastDisplay = string.Empty;
    private string lastSource = string.Empty;
    private string lastSeenEnglish = string.Empty;
    private string lastSeenSource = string.Empty;
    private long lastSeenTick;
    private int unseenFrames;
    private readonly List<DialogueChoice> choices = [];
    private readonly Dictionary<string, string> choiceOriginal = new();
    private readonly Dictionary<string, string> choiceTranslated = new();
    private readonly HashSet<string> choiceInflight = [];
    private readonly HashSet<string> choiceLogged = [];
    private string choiceAddon = string.Empty;

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

    public IReadOnlyList<DialogueChoice> Choices
    {
        get
        {
            lock (gate)
                return choices.ToArray();
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

        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostDraw, "SelectString", OnSelectString);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostRefresh, "SelectString", OnSelectString);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "SelectString", OnSelectStringClosed);

        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostDraw, "SelectIconString", OnSelectIconString);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostRefresh, "SelectIconString", OnSelectIconString);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "SelectIconString", OnSelectIconStringClosed);

        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostDraw, "CutSceneSelectString", OnCutSceneSelectString);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostRefresh, "CutSceneSelectString", OnCutSceneSelectString);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "CutSceneSelectString", OnCutSceneSelectStringClosed);

        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostDraw, "SelectYesno", OnSelectYesNo);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostRefresh, "SelectYesno", OnSelectYesNo);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "SelectYesno", OnSelectYesNoClosed);
    }

    public void Dispose()
    {
        Plugin.AddonLifecycle.UnregisterListener(OnTalk);
        Plugin.AddonLifecycle.UnregisterListener(OnTalkClosed);
        Plugin.AddonLifecycle.UnregisterListener(OnBattleTalk);
        Plugin.AddonLifecycle.UnregisterListener(OnBattleTalkClosed);
        Plugin.AddonLifecycle.UnregisterListener(OnTalkSubtitle);
        Plugin.AddonLifecycle.UnregisterListener(OnTalkSubtitleClosed);
        Plugin.AddonLifecycle.UnregisterListener(OnSelectString);
        Plugin.AddonLifecycle.UnregisterListener(OnSelectStringClosed);
        Plugin.AddonLifecycle.UnregisterListener(OnSelectIconString);
        Plugin.AddonLifecycle.UnregisterListener(OnSelectIconStringClosed);
        Plugin.AddonLifecycle.UnregisterListener(OnCutSceneSelectString);
        Plugin.AddonLifecycle.UnregisterListener(OnCutSceneSelectStringClosed);
        Plugin.AddonLifecycle.UnregisterListener(OnSelectYesNo);
        Plugin.AddonLifecycle.UnregisterListener(OnSelectYesNoClosed);
        foreach (var pin in textPins.Values)
        {
            if (pin.IsAllocated)
                pin.Free();
        }

        textPins.Clear();
    }

    public void OnFrameworkUpdate(IFramework framework)
    {
        if (choices.Count > 0 && (string.IsNullOrEmpty(choiceAddon) || !IsNamedAddonVisible(choiceAddon)))
            ClearChoices();

        var line = Current;
        if (line is null)
            return;

        if (line.Source is "Test")
            return;

        if (!IsAddonVisible(line.Source))
        {
            unseenFrames++;
            if (unseenFrames >= 8)
                ClearIfSource(line.Source);
            return;
        }

        unseenFrames = 0;
    }

    public static bool IsAddonVisible(string source)
    {
        var addonName = source switch
        {
            "BattleTalk" => "_BattleTalk",
            "TalkSubtitle" => "TalkSubtitle",
            "SelectString" => "SelectString",
            "SelectIconString" => "SelectIconString",
            "CutSceneSelectString" => "CutSceneSelectString",
            "SelectYesno" => "SelectYesno",
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
    private void OnSelectStringClosed(AddonEvent type, AddonArgs args) => ClearChoices();
    private void OnSelectIconStringClosed(AddonEvent type, AddonArgs args) => ClearChoices();
    private void OnCutSceneSelectStringClosed(AddonEvent type, AddonArgs args) => ClearChoices();

    private void OnSelectString(AddonEvent type, AddonArgs args) => TranslateChoiceAddon("SelectString");
    private void OnSelectIconString(AddonEvent type, AddonArgs args) => TranslateChoiceAddon("SelectIconString");
    private void OnCutSceneSelectString(AddonEvent type, AddonArgs args) => TranslateChoiceAddon("CutSceneSelectString");
    private void OnSelectYesNo(AddonEvent type, AddonArgs args) => TranslateChoiceAddon("SelectYesno");
    private void OnSelectYesNoClosed(AddonEvent type, AddonArgs args) => ClearChoices();

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

    private void TranslateChoiceAddon(string addonName)
    {
        var allowed = addonName switch
        {
            "SelectYesno" => Plugin.Configuration.TranslateYesNo,
            _ => Plugin.Configuration.TranslateChoices,
        };
        if (!Plugin.Configuration.Enabled || !allowed)
            return;

        var scanned = ScanChoiceNodes(addonName);
        if (scanned.Count == 0)
        {
            if (choiceAddon == addonName)
                ClearChoices();
            return;
        }

        choiceAddon = addonName;
        var overlay = NeedsGameFontOverlay();
        var busy = TargetLanguages.Current().BusyText;
        var next = new List<DialogueChoice>(scanned.Count);
        var live = new HashSet<string>();

        foreach (var row in scanned)
        {
            var key = addonName + ":" + row.NodeId;
            live.Add(key);

            var english = row.English;
            if (choiceOriginal.TryGetValue(key, out var orig))
            {
                if (choiceTranslated.TryGetValue(key, out var prev)
                    && (english == prev || SameLoose(english, prev) || ContainsArabic(english)))
                    english = orig;
                else if (ContainsArabic(english))
                    english = orig;
                else if (english != orig && !SameLoose(english, orig))
                {
                    choiceOriginal[key] = english;
                }
            }
            else if (ContainsArabic(english))
            {
                continue;
            }
            else
            {
                choiceOriginal[key] = english;
            }

            string translation;
            var isBusy = false;
            if (translator.TryGetCached(english, out var cached))
            {
                translation = cached;
                choiceTranslated[key] = cached;
                RecordChoice(addonName, english, cached);
            }
            else if (choiceTranslated.TryGetValue(key, out var done))
            {
                translation = done;
            }
            else
            {
                translation = busy;
                isBusy = true;
                if (choiceInflight.Add(key))
                    StartChoiceTranslation(addonName, row.NodeId, english);
            }

            if (overlay)
                HideBodyText(addonName, row.NodeId);
            else if (!isBusy)
                WriteBodyText(addonName, row.NodeId, FormatForAddon(translation), styleBalloon: false);

            next.Add(new DialogueChoice
            {
                NodeId = row.NodeId,
                English = english,
                Translation = translation,
                Busy = isBusy,
                Pos = row.Pos,
                Size = row.Size,
            });
        }

        foreach (var stale in choiceOriginal.Keys.Where(k => k.StartsWith(addonName + ":", StringComparison.Ordinal) && !live.Contains(k)).ToArray())
        {
            choiceOriginal.Remove(stale);
            choiceTranslated.Remove(stale);
            choiceInflight.Remove(stale);
        }

        lock (gate)
        {
            choices.Clear();
            choices.AddRange(next);
        }
    }

    private void StartChoiceTranslation(string addonName, uint nodeId, string english)
    {
        var key = addonName + ":" + nodeId;
        var capturedAddon = addonName;
        var capturedNode = nodeId;
        var captured = english;
        Task.Run(() =>
        {
            var translated = translator.Translate(captured);
            _ = Plugin.Framework.RunOnFrameworkThread(() =>
            {
                choiceInflight.Remove(key);
                if (string.IsNullOrWhiteSpace(translated))
                    translated = captured;
                choiceTranslated[key] = translated;
                RecordChoice(capturedAddon, captured, translated);
                if (Plugin.Configuration.LogTranslations)
                    Plugin.Log.Information($"[{capturedAddon} #{capturedNode}] {captured} => {translated}");
            });
        });
    }

    private void ClearChoices()
    {
        lock (gate)
            choices.Clear();
        choiceOriginal.Clear();
        choiceTranslated.Clear();
        choiceInflight.Clear();
        choiceLogged.Clear();
        choiceAddon = string.Empty;
    }

    private void RecordChoice(string source, string english, string translation)
    {
        if (string.IsNullOrWhiteSpace(translation) || TargetLanguages.IsBusyPlaceholder(translation))
            return;
        var sig = source + "|" + english + "|" + translation;
        if (!choiceLogged.Add(sig))
            return;
        Plugin.History.Add(source, "", english, translation);
        if (Plugin.Configuration.TtsSpeakChoices)
            Plugin.Tts.SpeakDialogue(translation);
    }

    private sealed class ScannedChoice
    {
        public uint NodeId;
        public string English = string.Empty;
        public Vector2 Pos;
        public Vector2 Size;
    }

    private unsafe List<ScannedChoice> ScanChoiceNodes(string addonName)
    {
        var found = new List<ScannedChoice>();
        var addonPtr = Plugin.GameGui.GetAddonByName(addonName);
        if (addonPtr.Address == nint.Zero)
            return found;

        var addon = (AtkUnitBase*)addonPtr.Address;
        if (addon is null || !addon->IsVisible)
            return found;

        var count = addon->UldManager.NodeListCount;
        var list = addon->UldManager.NodeList;
        if (list is null)
            return found;

        for (var i = 0; i < count; i++)
        {
            var node = list[i];
            if (node is null || node->Type != NodeType.Text)
                continue;
            if (!node->IsVisible())
                continue;

            var tn = (AtkTextNode*)node;
            var text = ReadTextNode(tn);
            if (string.IsNullOrWhiteSpace(text) || text.Length < 2)
                continue;
            if (TargetLanguages.IsBusyPlaceholder(text))
                continue;

            var scale = GetNodeScale((AtkResNode*)tn);
            // ScreenX/Y and scaled Width/Height are already pixels. Dalamud only
            // multiplies Window.Size by GlobalScale — dividing here shifts the
            // overlay toward the origin (onto whatever UI is behind the dialog).
            found.Add(new ScannedChoice
            {
                NodeId = tn->NodeId,
                English = text.Trim(),
                Pos = new Vector2(tn->ScreenX, tn->ScreenY),
                Size = new Vector2(
                    Math.Max(48f, tn->Width * scale.X),
                    Math.Max(20f, tn->Height * scale.Y)),
            });
        }

        if (found.Count > 12)
            found.RemoveRange(12, found.Count - 12);

        return found;
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

        var english = ReadBodyText(addon, bodyNodeId, speakerNodeId, out var usedNodeId);
        if (string.IsNullOrWhiteSpace(english))
            return;

        // Cutscene Talk types the line out. Wait until it stops growing.
        // Same-length SeString flicker must not reset the timer or we never translate.
        var now = Environment.TickCount64;
        var grew = english.StartsWith(lastSeenEnglish, StringComparison.Ordinal)
                   && english.Length > lastSeenEnglish.Length;
        var shrank = lastSeenEnglish.StartsWith(english, StringComparison.Ordinal)
                     && lastSeenEnglish.Length > english.Length;
        var sameLine = source == lastSeenSource
                       && (english == lastSeenEnglish || SameLoose(english, lastSeenEnglish) || grew || shrank);

        if (!sameLine)
        {
            lastSeenSource = source;
            lastSeenEnglish = english;
            lastSeenTick = now;
            Interlocked.Increment(ref seq);
            ShowTranslatingPlaceholder(source, addonName, speakerNodeId, usedNodeId, english);
            return;
        }

        if (grew)
        {
            lastSeenEnglish = english;
            lastSeenTick = now;
            Interlocked.Increment(ref seq);
            ShowTranslatingPlaceholder(source, addonName, speakerNodeId, usedNodeId, english);
            return;
        }

        if (now - lastSeenTick < 140)
            return;

        if (lastSource == source && !string.IsNullOrEmpty(lastDisplay))
        {
            if (english == lastDisplay || SameLoose(english, lastDisplay))
                return;
            if (ContainsArabic(english) && TargetLanguages.Current().ArabicScript)
            {
                HideBodyText(addonName, usedNodeId);
                return;
            }
        }

        if (english == lastOriginal && lastSource == source && !string.IsNullOrEmpty(lastDisplay))
        {
            ApplyNativeBody(addonName, usedNodeId, lastDisplay);
            return;
        }

        var speaker = speakerNodeId == 0 ? string.Empty : ReadNodeText(addon, speakerNodeId);
        var key = source + "|" + speaker + "|" + english;
        if (key == lastKey)
        {
            if (!string.IsNullOrEmpty(lastDisplay))
                ApplyNativeBody(addonName, usedNodeId, lastDisplay);
            return;
        }
        lastKey = key;
        lastOriginal = english;
        lastSource = source;
        lastDisplay = string.Empty;

        PushBusy(speaker, english, source, usedNodeId);
        if (NeedsGameFontOverlay())
            HideBodyText(addonName, usedNodeId);
        var captured = english;
        var capturedSpeaker = speaker;
        var capturedSource = source;
        var capturedAddon = addonName;
        var capturedNode = usedNodeId;
        var id = Interlocked.Increment(ref seq);

        void Finish(string translated)
        {
            var display = FormatForAddon(translated);
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
                    BodyNodeId = capturedNode,
                    Busy = false,
                };
                lastDisplay = display;
            }

            ApplyNativeBody(capturedAddon, capturedNode, display);

            Plugin.History.Add(capturedSource, capturedSpeaker, captured, translated);
            Plugin.Tts.SpeakDialogue(translated);

            if (Plugin.Configuration.LogTranslations)
                Plugin.Log.Information($"[{capturedSource}] {captured} => {translated}");
        }

        if (translator.TryGetCached(captured, out var cached))
        {
            Finish(cached);
            return;
        }

        Task.Run(() =>
        {
            try
            {
                var translated = translator.Translate(captured);
                _ = Plugin.Framework.RunOnFrameworkThread(() =>
                {
                    try { Finish(translated); }
                    catch (Exception ex) { Plugin.Log.Warning(ex, "Lisan native write failed"); }
                });
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "Lisan translate failed");
                _ = Plugin.Framework.RunOnFrameworkThread(() =>
                {
                    RestoreBodyText(capturedAddon, capturedNode);
                    lock (gate)
                    {
                        if (id != seq)
                            return;
                        current = new DialogueLine
                        {
                            Speaker = capturedSpeaker,
                            English = captured,
                            Translation = captured,
                            Source = capturedSource,
                            BodyNodeId = capturedNode,
                            Busy = false,
                        };
                        lastDisplay = string.Empty;
                    }
                });
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
                lastOriginal = string.Empty;
                lastDisplay = string.Empty;
                lastSource = string.Empty;
                lastSeenEnglish = string.Empty;
                lastSeenSource = string.Empty;
                unseenFrames = 0;
                Plugin.Tts.Stop();
            }
        }
    }

    public void Clear()
    {
        lock (gate)
            current = null;
        lastKey = string.Empty;
        lastOriginal = string.Empty;
        lastDisplay = string.Empty;
        lastSource = string.Empty;
        lastSeenEnglish = string.Empty;
        lastSeenSource = string.Empty;
        unseenFrames = 0;
        Plugin.Tts.Stop();
        ClearChoices();
    }

    private static unsafe string ReadNodeText(AtkUnitBase* addon, uint nodeId)
    {
        try
        {
            return ReadTextNode(addon->GetTextNodeById(nodeId));
        }
        catch
        {
            return string.Empty;
        }
    }

    private static unsafe string ReadTextNode(AtkTextNode* node)
    {
        if (node is null)
            return string.Empty;

        var ptr = node->NodeText.StringPtr;
        if (ptr.Value == null)
            return string.Empty;

        return MemoryHelper.ReadSeStringAsString(out _, (nint)ptr.Value);
    }

    private static unsafe string ReadBodyText(AtkUnitBase* addon, uint bodyNodeId, uint speakerNodeId, out uint usedNodeId)
    {
        usedNodeId = bodyNodeId;
        var primary = ReadNodeText(addon, bodyNodeId);
        if (!string.IsNullOrWhiteSpace(primary))
            return primary;

        var speaker = speakerNodeId == 0 ? string.Empty : ReadNodeText(addon, speakerNodeId);
        var best = string.Empty;
        var bestId = bodyNodeId;

        var count = addon->UldManager.NodeListCount;
        var list = addon->UldManager.NodeList;
        if (list is null)
            return string.Empty;

        for (var i = 0; i < count; i++)
        {
            var node = list[i];
            if (node is null || node->Type != NodeType.Text)
                continue;
            if (node->NodeId == speakerNodeId || node->NodeId == bodyNodeId)
                continue;

            var text = ReadTextNode((AtkTextNode*)node);
            if (string.IsNullOrWhiteSpace(text) || text == speaker)
                continue;
            if (text.Length > best.Length)
            {
                best = text;
                bestId = node->NodeId;
            }
        }

        if (string.IsNullOrWhiteSpace(best))
            return string.Empty;

        usedNodeId = bestId;
        return best;
    }

    private unsafe void ShowTranslatingPlaceholder(string source, string addonName, uint speakerNodeId, uint bodyNodeId, string english)
    {
        if (!NeedsGameFontOverlay())
            return;

        var speaker = string.Empty;
        if (speakerNodeId != 0)
        {
            var addonPtr = Plugin.GameGui.GetAddonByName(addonName);
            if (addonPtr.Address != nint.Zero)
                speaker = ReadNodeText((AtkUnitBase*)addonPtr.Address, speakerNodeId);
        }

        PushBusy(speaker, english, source, bodyNodeId);
        HideBodyText(addonName, bodyNodeId);
    }

    private void PushBusy(string speaker, string english, string source, uint bodyNodeId)
    {
        lock (gate)
        {
            current = new DialogueLine
            {
                Speaker = speaker,
                English = english,
                Translation = TargetLanguages.Current().BusyText,
                Source = source,
                BodyNodeId = bodyNodeId,
                Busy = true,
            };
        }
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
                BodyNodeId = MapSource(source)?.BodyNodeId ?? 3u,
                Busy = false,
            };
            lastOriginal = english;
            lastSource = source;
            lastDisplay = FormatForAddon(translation);
            lastKey = source + "||" + english;
        }

        if (TargetLanguages.IsBusyPlaceholder(translation))
            return;

        var map = MapSource(source);
        if (map is null)
            return;
        ApplyNativeBody(map.Value.Addon, map.Value.BodyNodeId, lastDisplay);
    }

    public static bool NeedsGameFontOverlay() =>
        !string.IsNullOrWhiteSpace(TargetLanguages.Current().FontFile);

    public static (string Addon, uint BodyNodeId)? MapSource(string source) => source switch
    {
        "BattleTalk" => ("_BattleTalk", 6u),
        "TalkSubtitle" => ("TalkSubtitle", 2u),
        "Talk" => ("Talk", 3u),
        _ => null,
    };

    public static unsafe bool TryGetBodyTextRect(string source, out Vector2 pos, out Vector2 size, uint bodyNodeId = 0)
    {
        pos = default;
        size = default;
        var map = MapSource(source);
        if (map is null)
            return false;

        var addonPtr = Plugin.GameGui.GetAddonByName(map.Value.Addon);
        if (addonPtr.Address == nint.Zero)
            return false;

        var addon = (AtkUnitBase*)addonPtr.Address;
        if (addon is null || !addon->IsVisible)
            return false;

        var nodeId = bodyNodeId != 0 ? bodyNodeId : map.Value.BodyNodeId;
        var node = addon->GetTextNodeById(nodeId);
        var vpW = ImGuiHelpers.MainViewport.WorkSize.X;
        var minW = vpW > 200f ? Math.Min(720f, vpW * 0.58f) : 520f;
        var minH = source == "TalkSubtitle" ? 56f : 100f;

        if (node is not null && (node->ScreenX > 8f || node->ScreenY > 8f))
        {
            pos = new Vector2(node->ScreenX, node->ScreenY);
        }
        else
        {
            var speakerId = source == "BattleTalk" ? 4u : source == "Talk" ? 2u : 0u;
            var name = speakerId == 0 ? null : addon->GetTextNodeById(speakerId);
            if (name is null)
                return false;

            var ns = GetNodeScale((AtkResNode*)name);
            pos = new Vector2(name->ScreenX, name->ScreenY + (name->Height * ns.Y) + 6f);
        }

        var pixelW = minW;
        var pixelH = minH;
        if (node is not null)
        {
            var scale = GetNodeScale((AtkResNode*)node);
            pixelW = Math.Max(minW, node->Width * scale.X);
            pixelH = Math.Max(minH, node->Height * scale.Y);
        }

        if (vpW > 200f && pixelW > vpW * 0.72f)
            pixelW = vpW * 0.68f;
        if (pixelH > 260f)
            pixelH = 260f;

        // Dalamud Window.Size is multiplied by GlobalScale; game ScreenX/Width are already pixels.
        var gs = ImGuiHelpers.GlobalScale;
        if (gs < 0.01f)
            gs = 1f;

        size = new Vector2(pixelW, pixelH) / gs;
        return size.X >= 40f && size.Y >= 24f;
    }

    /// <summary>The game's own color for the body text: dark on cream balloons, light on dark ones.</summary>
    public static unsafe bool TryGetBodyTextColor(string source, uint bodyNodeId, out Vector4 color)
    {
        color = default;
        var map = MapSource(source);
        if (map is null)
            return false;

        var addonPtr = Plugin.GameGui.GetAddonByName(map.Value.Addon);
        if (addonPtr.Address == nint.Zero)
            return false;

        var node = ((AtkUnitBase*)addonPtr.Address)->GetTextNodeById(bodyNodeId != 0 ? bodyNodeId : map.Value.BodyNodeId);
        if (node is null)
            return false;

        // TextColor, not Color: Color.A is what HideBodyText zeroes.
        var c = node->TextColor;
        color = new Vector4(c.R / 255f, c.G / 255f, c.B / 255f, 1f);

        // Some balloon styles (e.g. the blue machine one) color the line with a <color> macro; TextColor is then the dark default.
        var macro = TryGetMacroColor(node, out var macroColor);
        if (macro)
            color = macroColor;

        var sig = $"{source} text={c.R:X2}{c.G:X2}{c.B:X2} edge={node->EdgeColor.R:X2}{node->EdgeColor.G:X2}{node->EdgeColor.B:X2} macro={(macro ? macroColor.ToString() : "none")}";
        if (loggedColorStyles.Add(sig))
            Plugin.Log.Information($"Lisan balloon color: {sig}");
        return true;
    }

    private static readonly HashSet<string> loggedColorStyles = [];

    private static unsafe bool TryGetMacroColor(AtkTextNode* node, out Vector4 color)
    {
        color = default;
        var ptr = node->NodeText.StringPtr.Value;
        if (ptr == null)
            return false;

        foreach (var p in new ReadOnlySeStringSpan(ptr))
        {
            // Value is 0xAARRGGBB; <color(stackcolor)> has no uint and is skipped.
            if (p.Type == ReadOnlySePayloadType.Macro && p.MacroCode == MacroCode.Color
                && p.TryGetExpression(out var e) && e.TryGetUInt(out var argb) && argb != 0)
            {
                color = new Vector4(((argb >> 16) & 0xFF) / 255f, ((argb >> 8) & 0xFF) / 255f, (argb & 0xFF) / 255f, 1f);
                return true;
            }
        }

        return false;
    }

    private static unsafe Vector2 GetNodeScale(AtkResNode* node)
    {
        var scale = new Vector2(node->ScaleX, node->ScaleY);
        for (var parent = node->ParentNode; parent is not null; parent = parent->ParentNode)
            scale *= new Vector2(parent->ScaleX, parent->ScaleY);
        return scale;
    }

    private static string FormatForAddon(string translation)
    {
        if (string.IsNullOrWhiteSpace(translation))
            return translation;

        var lang = TargetLanguages.Current();
        if (!lang.RightToLeft)
            return translation;

        const int wrap = 42;
        var lines = DialogueDisplay.LinesForImGui(translation, wrap, true, lang.ArabicScript);
        return lines.Length == 0 ? translation : string.Join("\n", lines);
    }

    private unsafe void ApplyNativeBody(string addonName, uint bodyNodeId, string text)
    {
        if (NeedsGameFontOverlay())
        {
            HideBodyText(addonName, bodyNodeId);
            return;
        }

        WriteBodyText(addonName, bodyNodeId, text);
    }

    private static unsafe void HideBodyText(string addonName, uint bodyNodeId)
    {
        SetBodyAlpha(addonName, bodyNodeId, 0);
    }

    private static unsafe void RestoreBodyText(string addonName, uint bodyNodeId)
    {
        SetBodyAlpha(addonName, bodyNodeId, 255);
    }

    private static unsafe void SetBodyAlpha(string addonName, uint bodyNodeId, byte alpha)
    {
        var addonPtr = Plugin.GameGui.GetAddonByName(addonName);
        if (addonPtr.Address == nint.Zero)
            return;

        var addon = (AtkUnitBase*)addonPtr.Address;
        if (addon is null || !addon->IsVisible)
            return;

        var node = addon->GetTextNodeById(bodyNodeId);
        if (node is null)
            return;

        node->Color.A = alpha;
    }

    private unsafe void WriteBodyText(string addonName, uint bodyNodeId, string text, bool styleBalloon = true)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var addonPtr = Plugin.GameGui.GetAddonByName(addonName);
        if (addonPtr.Address == nint.Zero)
            return;

        var addon = (AtkUnitBase*)addonPtr.Address;
        if (addon is null || !addon->IsVisible)
            return;

        var node = addon->GetTextNodeById(bodyNodeId);
        if (node is null)
            return;

        var bytes = Encoding.UTF8.GetBytes(text + "\0");
        var pinKey = addonName + "#" + bodyNodeId;
        if (textPins.TryGetValue(pinKey, out var old) && old.IsAllocated)
            old.Free();
        var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        textPins[pinKey] = pin;
        node->SetText((byte*)pin.AddrOfPinnedObject());

        if (styleBalloon)
        {
            var cfg = Plugin.Configuration;
            // Keep the game's own text color: balloon themes differ (dark or cream), a fixed color is unreadable on one of them.
            node->FontSize = (byte)Math.Clamp(cfg.BalloonFontSize * 0.55f, 12f, 28f);
        }

        var lang = TargetLanguages.Current();
        if (lang.RightToLeft)
            node->AlignmentFontType = (byte)((node->AlignmentFontType & 0xF0) | 0x02);
    }

    private static bool SameLoose(string a, string b) =>
        a.Replace("\r", "", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Equals(
                b.Replace("\r", "", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal),
                StringComparison.Ordinal);

    private static bool ContainsArabic(string text)
    {
        foreach (var c in text)
        {
            if (c is >= '\u0600' and <= '\u06FF')
                return true;
        }

        return false;
    }

}
