using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;

namespace Lisan.Services;

public sealed class ChatLine
{
    public DateTime Utc { get; init; } = DateTime.UtcNow;
    public XivChatType Type { get; init; }
    public string Channel { get; init; } = "";
    public string Sender { get; init; } = "";
    public string English { get; init; } = "";
    public string Translation { get; init; } = "";
    public bool Busy { get; init; }
}

/// <summary>
/// Translate incoming chat off-thread. Original line stays in the game log (links intact).
/// Arabic / CJK go to the Lisan chat window — the game font cannot draw them.
/// </summary>
public sealed class ChatTranslator : IDisposable
{
    private const int MaxLines = 150;
    private const int MaxRecent = 80;
    private const int MaxQueue = 24;
    private const string NativeTag = "[" + Brand.Name + "]";

    private readonly LocalTranslator translator;
    private readonly object gate = new();
    private readonly List<ChatLine> lines = [];
    private readonly ConcurrentQueue<Pending> queue = new();
    private readonly HashSet<string> recent = new(StringComparer.Ordinal);
    private readonly Queue<string> recentOrder = new();
    private int draining;
    private bool disposed;

    public ChatTranslator(LocalTranslator translator)
    {
        this.translator = translator;
    }

    public int PendingCount => queue.Count;
    public bool Paused { get; set; }

    public void Register() => Plugin.ChatGui.ChatMessage += OnChat;

    public void Dispose()
    {
        disposed = true;
        Plugin.ChatGui.ChatMessage -= OnChat;
        while (queue.TryDequeue(out _)) { }
    }

    public List<ChatLine> Snapshot()
    {
        lock (gate)
            return lines.ToList();
    }

    public ChatLine? Last
    {
        get
        {
            lock (gate)
                return lines.Count == 0 ? null : lines[^1];
        }
    }

    public void Clear()
    {
        lock (gate)
            lines.Clear();
    }

    private void OnChat(IHandleableChatMessage message)
    {
        try
        {
            if (disposed || Paused)
                return;
            var cfg = Plugin.Configuration;
            if (!cfg.Enabled || !cfg.TranslateChat)
                return;

            var type = message.LogKind;
            if (!ChannelWanted(type, cfg))
                return;

            var sender = Sanitize(message.Sender.TextValue);
            var english = Sanitize(message.Message.TextValue);
            if (!LooksTranslatable(english))
                return;
            if (IsOwnPrint(english, sender))
                return;
            if (!cfg.TranslateOwnChat && IsLocalPlayer(sender))
                return;

            var key = (ushort)type + "|" + sender + "|" + english;
            lock (gate)
            {
                if (!recent.Add(key))
                    return;
                recentOrder.Enqueue(key);
                while (recentOrder.Count > MaxRecent)
                    recent.Remove(recentOrder.Dequeue());
            }

            if (queue.Count >= MaxQueue)
                return;

            if (translator.TryGetCached(english, out var cached)
                && !string.IsNullOrWhiteSpace(cached)
                && cached != english)
            {
                Finish(type, sender, english, cached);
                return;
            }

            queue.Enqueue(new Pending(type, sender, english));
            KickDrain();
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Chat translate hook failed");
        }
    }

    private void KickDrain()
    {
        if (Interlocked.CompareExchange(ref draining, 1, 0) != 0)
            return;
        Task.Run(Drain);
    }

    private void Drain()
    {
        try
        {
            while (!disposed && queue.TryDequeue(out var job))
            {
                string translated;
                try
                {
                    translated = translator.Translate(job.English);
                }
                catch (Exception ex)
                {
                    Plugin.Log.Warning(ex, "Chat translate failed");
                    translated = job.English;
                }

                if (string.IsNullOrWhiteSpace(translated))
                    translated = job.English;

                var type = job.Type;
                var sender = job.Sender;
                var english = job.English;
                _ = Plugin.Framework.RunOnFrameworkThread(() => Finish(type, sender, english, translated));
            }
        }
        finally
        {
            Interlocked.Exchange(ref draining, 0);
            if (!disposed && !queue.IsEmpty && Interlocked.CompareExchange(ref draining, 1, 0) == 0)
                Task.Run(Drain);
        }
    }

    private void Finish(XivChatType type, string sender, string english, string translated)
    {
        if (disposed)
            return;
        if (TargetLanguages.IsBusyPlaceholder(translated))
            return;

        var line = new ChatLine
        {
            Utc = DateTime.UtcNow,
            Type = type,
            Channel = ChannelLabel(type),
            Sender = sender,
            English = english,
            Translation = translated,
        };

        lock (gate)
        {
            lines.Add(line);
            while (lines.Count > MaxLines)
                lines.RemoveAt(0);
        }

        Plugin.History.Add("Chat", string.IsNullOrWhiteSpace(sender) ? line.Channel : sender, english, translated);

        if (Plugin.Configuration.TtsSpeakChat)
            Plugin.Tts.SpeakDialogue(translated);

        if (Plugin.Configuration.ChatPrintNative && !AddonDialogueWatcher.NeedsGameFontOverlay())
        {
            try
            {
                Plugin.ChatGui.Print(translated, Brand.Name, 45);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "Chat native print failed");
            }
        }
    }

    private static bool ChannelWanted(XivChatType type, Configuration cfg) => GroupOf(type) switch
    {
        ChatGroup.Player => cfg.TranslateChatPlayers,
        ChatGroup.Party => cfg.TranslateChatParty,
        ChatGroup.Linkshell => cfg.TranslateChatLinkshell,
        ChatGroup.Npc => cfg.TranslateChatNpc,
        ChatGroup.System => cfg.TranslateChatSystem,
        ChatGroup.Emote => cfg.TranslateChatEmotes,
        _ => false,
    };

    private static ChatGroup GroupOf(XivChatType type) => type switch
    {
        XivChatType.Say or XivChatType.Shout or XivChatType.Yell
            or XivChatType.TellIncoming or XivChatType.TellOutgoing
            or XivChatType.GmSay or XivChatType.GmShout or XivChatType.GmYell
            or XivChatType.GmTell => ChatGroup.Player,
        XivChatType.Party or XivChatType.Alliance or XivChatType.CrossParty
            or XivChatType.GmParty => ChatGroup.Party,
        XivChatType.FreeCompany or XivChatType.NoviceNetwork or XivChatType.PvPTeam
            or XivChatType.Ls1 or XivChatType.Ls2 or XivChatType.Ls3 or XivChatType.Ls4
            or XivChatType.Ls5 or XivChatType.Ls6 or XivChatType.Ls7 or XivChatType.Ls8
            or XivChatType.CrossLinkShell1 or XivChatType.CrossLinkShell2
            or XivChatType.CrossLinkShell3 or XivChatType.CrossLinkShell4
            or XivChatType.CrossLinkShell5 or XivChatType.CrossLinkShell6
            or XivChatType.CrossLinkShell7 or XivChatType.CrossLinkShell8
            or XivChatType.GmFreeCompany or XivChatType.GmNoviceNetwork
            or XivChatType.GmLinkshell1 or XivChatType.GmLinkshell2
            or XivChatType.GmLinkshell3 or XivChatType.GmLinkshell4
            or XivChatType.GmLinkshell5 or XivChatType.GmLinkshell6
            or XivChatType.GmLinkshell7 or XivChatType.GmLinkshell8 => ChatGroup.Linkshell,
        XivChatType.NPCDialogue or XivChatType.NPCDialogueAnnouncements => ChatGroup.Npc,
        XivChatType.CustomEmote or XivChatType.StandardEmote => ChatGroup.Emote,
        XivChatType.SystemMessage or XivChatType.SystemError or XivChatType.ErrorMessage
            or XivChatType.Urgent or XivChatType.Notice
            or XivChatType.FreeCompanyAnnouncement
            or XivChatType.NoviceNetworkSystem => ChatGroup.System,
        _ => ChatGroup.Skip,
    };

    internal static string ChannelLabel(XivChatType type) => type switch
    {
        XivChatType.Say => "Say",
        XivChatType.Shout => "Shout",
        XivChatType.Yell => "Yell",
        XivChatType.TellIncoming or XivChatType.TellOutgoing or XivChatType.GmTell => "Tell",
        XivChatType.Party or XivChatType.CrossParty or XivChatType.GmParty => "Party",
        XivChatType.Alliance => "Alliance",
        XivChatType.FreeCompany or XivChatType.GmFreeCompany => "FC",
        XivChatType.NoviceNetwork or XivChatType.GmNoviceNetwork => "Novice",
        XivChatType.NPCDialogue or XivChatType.NPCDialogueAnnouncements => "NPC",
        XivChatType.CustomEmote or XivChatType.StandardEmote => "Emote",
        XivChatType.SystemMessage or XivChatType.SystemError or XivChatType.ErrorMessage
            or XivChatType.Urgent or XivChatType.Notice => "System",
        _ when IsLs(type) => "LS",
        _ => type.ToString(),
    };

    internal static Vector4 ChannelColor(XivChatType type) => type switch
    {
        XivChatType.Say => new(0.90f, 0.90f, 0.90f, 1f),
        XivChatType.Shout => new(1.00f, 0.55f, 0.25f, 1f),
        XivChatType.Yell => new(1.00f, 0.85f, 0.20f, 1f),
        XivChatType.TellIncoming or XivChatType.TellOutgoing or XivChatType.GmTell => new(1.00f, 0.55f, 0.85f, 1f),
        XivChatType.Party or XivChatType.CrossParty or XivChatType.GmParty => new(0.35f, 0.70f, 1.00f, 1f),
        XivChatType.Alliance => new(0.35f, 0.90f, 0.55f, 1f),
        XivChatType.FreeCompany or XivChatType.GmFreeCompany => new(0.75f, 0.55f, 1.00f, 1f),
        XivChatType.NoviceNetwork or XivChatType.GmNoviceNetwork => new(0.55f, 0.85f, 0.45f, 1f),
        XivChatType.NPCDialogue or XivChatType.NPCDialogueAnnouncements => new(0.92f, 0.82f, 0.55f, 1f),
        XivChatType.CustomEmote or XivChatType.StandardEmote => new(0.70f, 0.70f, 0.70f, 1f),
        _ when IsLs(type) => new(0.45f, 0.85f, 0.55f, 1f),
        _ => new(0.85f, 0.80f, 0.55f, 1f),
    };

    private static bool IsLs(XivChatType type) => type
        is XivChatType.Ls1 or XivChatType.Ls2 or XivChatType.Ls3 or XivChatType.Ls4
        or XivChatType.Ls5 or XivChatType.Ls6 or XivChatType.Ls7 or XivChatType.Ls8
        or XivChatType.CrossLinkShell1 or XivChatType.CrossLinkShell2
        or XivChatType.CrossLinkShell3 or XivChatType.CrossLinkShell4
        or XivChatType.CrossLinkShell5 or XivChatType.CrossLinkShell6
        or XivChatType.CrossLinkShell7 or XivChatType.CrossLinkShell8
        or XivChatType.GmLinkshell1 or XivChatType.GmLinkshell2
        or XivChatType.GmLinkshell3 or XivChatType.GmLinkshell4
        or XivChatType.GmLinkshell5 or XivChatType.GmLinkshell6
        or XivChatType.GmLinkshell7 or XivChatType.GmLinkshell8
        or XivChatType.PvPTeam;

    private static bool IsOwnPrint(string text, string sender) =>
        text.StartsWith(NativeTag, StringComparison.Ordinal)
        || text.StartsWith("[" + Brand.Name + "]", StringComparison.OrdinalIgnoreCase)
        || sender.Equals(Brand.Name, StringComparison.OrdinalIgnoreCase);

    private static bool IsLocalPlayer(string sender)
    {
        var me = Plugin.ObjectTable.LocalPlayer?.Name.ToString();
        if (string.IsNullOrWhiteSpace(me) || string.IsNullOrWhiteSpace(sender))
            return false;
        return sender.StartsWith(me, StringComparison.Ordinal);
    }

    private static bool LooksTranslatable(string text)
    {
        if (text.Length < 4 || text.Length > 400)
            return false;
        if (TargetLanguages.IsBusyPlaceholder(text))
            return false;

        var lang = TargetLanguages.Current();
        var latin = 0;
        var arabic = 0;
        var cjk = 0;
        foreach (var c in text)
        {
            if (c is >= 'A' and <= 'Z' or >= 'a' and <= 'z')
                latin++;
            else if (c is >= '\u0600' and <= '\u06FF')
                arabic++;
            else if (c is >= '\u3040' and <= '\u30FF' or >= '\u4E00' and <= '\u9FFF')
                cjk++;
        }

        if (latin < 3)
            return false;
        if (lang.ArabicScript && arabic >= 3)
            return false;
        if ((lang.Code is "ja" or "zh" or "ko") && cjk >= 4 && cjk > latin)
            return false;
        return true;
    }

    private static string Sanitize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var sb = new StringBuilder(text.Length);
        var space = false;
        foreach (var c in text)
        {
            if (c is >= '\uE000' and <= '\uF8FF' or '\u0002' or '\u0003' or '\u00A0' or '\u200B')
            {
                if (!space)
                {
                    sb.Append(' ');
                    space = true;
                }

                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (!space)
                {
                    sb.Append(' ');
                    space = true;
                }

                continue;
            }

            space = false;
            sb.Append(c);
        }

        return sb.ToString().Trim();
    }

    private enum ChatGroup
    {
        Skip,
        Player,
        Party,
        Linkshell,
        Npc,
        System,
        Emote,
    }

    private readonly record struct Pending(XivChatType Type, string Sender, string English);
}
