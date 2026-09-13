using System;

namespace DialogueAr;

[Serializable]
public sealed class CharacterVoiceOverride
{
    public string SpeakerName { get; set; } = string.Empty;
    /// <summary>Exact Windows TTS voice name.</summary>
    public string VoiceName { get; set; } = string.Empty;
}
