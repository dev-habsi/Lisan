using System;
using Dalamud.Game.ClientState.Objects.Types;

namespace DialogueAr.Services;

public enum ResolvedVoiceGender
{
    Female,
    Male,
}

/// <summary>
/// Resolve speaker gender from nearby characters (CustomizeData.Sex: 0=male, 1=female).
/// </summary>
public static class SpeakerGender
{
    public static ResolvedVoiceGender Resolve(string? speakerName, TtsVoiceGender mode)
    {
        if (mode == TtsVoiceGender.Female)
            return ResolvedVoiceGender.Female;
        if (mode == TtsVoiceGender.Male)
            return ResolvedVoiceGender.Male;

        // Auto
        if (!string.IsNullOrWhiteSpace(speakerName))
        {
            var fromWorld = TryFromObjectTable(speakerName);
            if (fromWorld is not null)
                return fromWorld.Value;
        }

        // Fallback: local player gender, else female (Ava) as default multilingual.
        try
        {
            var me = Plugin.ObjectTable.LocalPlayer;
            if (me is not null)
                return SexToGender(me.CustomizeData.Sex);
        }
        catch
        {
            /* ignore */
        }

        return ResolvedVoiceGender.Female;
    }

    private static ResolvedVoiceGender? TryFromObjectTable(string speakerName)
    {
        try
        {
            var target = Plugin.TargetManager.Target as ICharacter;
            if (target is not null && NameMatches(target, speakerName))
                return SexToGender(target.CustomizeData.Sex);

            var focus = Plugin.TargetManager.FocusTarget as ICharacter;
            if (focus is not null && NameMatches(focus, speakerName))
                return SexToGender(focus.CustomizeData.Sex);

            foreach (var obj in Plugin.ObjectTable)
            {
                if (obj is not ICharacter ch)
                    continue;
                if (!NameMatches(ch, speakerName))
                    continue;
                return SexToGender(ch.CustomizeData.Sex);
            }
        }
        catch
        {
            /* ignore */
        }

        return null;
    }

    private static bool NameMatches(ICharacter ch, string speakerName)
    {
        var name = ch.Name.TextValue;
        if (string.IsNullOrEmpty(name))
            return false;

        if (name.Equals(speakerName, StringComparison.OrdinalIgnoreCase))
            return true;

        // Talk sometimes shows first name only for players.
        var first = name.Split(' ', 2)[0];
        return first.Equals(speakerName, StringComparison.OrdinalIgnoreCase);
    }

    private static ResolvedVoiceGender SexToGender(byte sex) =>
        sex == 1 ? ResolvedVoiceGender.Female : ResolvedVoiceGender.Male;
}
