using System;
using System.Collections.Generic;
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

namespace DialogueAr.Services;

public sealed class DialogueLine
{
    public string Speaker { get; init; } = string.Empty;
    public string English { get; init; } = string.Empty;
    public string Translation { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public uint BodyNodeId { get; init; }
    public bool Busy { get; init; }
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
    private uint lastBodyNodeId;
    private long overlayPaintedAt;

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

    public void MarkOverlayPainted() => overlayPaintedAt = Environment.TickCount64;

    private bool OverlayRecentlyPainted() => Environment.TickCount64 - overlayPaintedAt < 250;

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
        foreach (var pin in textPins.Values)
        {
            if (pin.IsAllocated)
                pin.Free();
        }

        textPins.Clear();
    }

    public void OnFrameworkUpdate(IFramework framework)
    {
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

        var painted = OverlayRecentlyPainted();

        if (Plugin.Configuration.ReplaceNativeDialogue)
        {
            if (lastSource == source && !string.IsNullOrEmpty(lastDisplay))
            {
                if (english == lastDisplay || SameLoose(english, lastDisplay))
                    return;
                if (ContainsArabic(english) && TargetLanguages.Current().ArabicScript)
                {
                    SyncNativeVisibility(addonName, usedNodeId, painted);
                    return;
                }
            }

            if (english == lastOriginal && lastSource == source && !string.IsNullOrEmpty(lastDisplay))
            {
                ApplyNativeBody(addonName, usedNodeId, lastDisplay, painted);
                return;
            }
        }

        var speaker = speakerNodeId == 0 ? string.Empty : ReadNodeText(addon, speakerNodeId);
        var key = source + "|" + speaker + "|" + english;
        if (key == lastKey)
        {
            if (Plugin.Configuration.ReplaceNativeDialogue && !string.IsNullOrEmpty(lastDisplay))
                ApplyNativeBody(addonName, usedNodeId, lastDisplay, painted);
            return;
        }
        lastKey = key;
        lastOriginal = english;
        lastSource = source;
        lastDisplay = string.Empty;
        lastBodyNodeId = usedNodeId;

        PushBusy(speaker, english, source, usedNodeId);
        if (Plugin.Configuration.ReplaceNativeDialogue && NeedsGameFontOverlay())
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

            if (Plugin.Configuration.ReplaceNativeDialogue)
                ApplyNativeBody(capturedAddon, capturedNode, display, overlayPainted: true);

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
                    catch (Exception ex) { Plugin.Log.Warning(ex, "DialogueAr native write failed"); }
                });
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "DialogueAr translate failed");
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
                lastBodyNodeId = 0;
                unseenFrames = 0;
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
        lastBodyNodeId = 0;
        unseenFrames = 0;
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
        if (!Plugin.Configuration.ReplaceNativeDialogue || !NeedsGameFontOverlay())
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
                BodyNodeId = MapSource(source)?.BodyNodeId ?? 3u,
                Busy = false,
            };
            lastOriginal = english;
            lastSource = source;
            lastDisplay = FormatForAddon(translation);
            lastKey = source + "||" + english;
            lastBodyNodeId = current.BodyNodeId;
        }

        if (!Plugin.Configuration.ReplaceNativeDialogue)
            return;
        if (TargetLanguages.IsBusyPlaceholder(translation))
            return;

        var map = MapSource(source);
        if (map is null)
            return;
        ApplyNativeBody(map.Value.Addon, map.Value.BodyNodeId, lastDisplay, overlayPainted: false);
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
        var minW = vpW > 200f ? Math.Min(560f, vpW * 0.48f) : 480f;
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

        if (vpW > 200f && pixelW > vpW * 0.62f)
            pixelW = vpW * 0.52f;
        if (pixelH > 260f)
            pixelH = 260f;

        // Dalamud Window.Size is multiplied by GlobalScale; game ScreenX/Width are already pixels.
        var gs = ImGuiHelpers.GlobalScale;
        if (gs < 0.01f)
            gs = 1f;

        size = new Vector2(pixelW, pixelH) / gs;
        return size.X >= 40f && size.Y >= 24f;
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

        var wrap = Plugin.Configuration.OverlayWrapChars > 0
            ? Plugin.Configuration.OverlayWrapChars
            : 42;
        var lines = DialogueDisplay.LinesForImGui(translation, wrap, true, lang.ArabicScript);
        return lines.Length == 0 ? translation : string.Join("\n", lines);
    }

    private unsafe void ApplyNativeBody(string addonName, uint bodyNodeId, string text, bool overlayPainted)
    {
        if (NeedsGameFontOverlay())
        {
            SyncNativeVisibility(addonName, bodyNodeId, overlayPainted);
            return;
        }

        WriteBodyText(addonName, bodyNodeId, text);
    }

    private static void SyncNativeVisibility(string addonName, uint bodyNodeId, bool overlayPainted)
    {
        HideBodyText(addonName, bodyNodeId);
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

    private unsafe void WriteBodyText(string addonName, uint bodyNodeId, string text)
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
        if (textPins.TryGetValue(addonName, out var old) && old.IsAllocated)
            old.Free();
        var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        textPins[addonName] = pin;
        node->SetText((byte*)pin.AddrOfPinnedObject());

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
