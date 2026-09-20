using System;
using System.Numerics;
using Dalamud.Configuration;

namespace Lisan;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 15;

    /// <summary>ISO-ish code from <see cref="TargetLanguages"/>, or "custom". Default Arabic (existing installs).</summary>
    public string TargetLanguage { get; set; } = "ar";
    public bool CustomRightToLeft { get; set; }
    /// <summary>Optional Opus-MT token for custom models, e.g. &gt;&gt;cmn_Hans&lt;&lt;.</summary>
    public string CustomTargetToken { get; set; } = "";

    public bool Enabled { get; set; } = true;
    public bool TranslateTalk { get; set; } = true;
    public bool TranslateBattleTalk { get; set; } = true;
    public bool TranslateTalkSubtitle { get; set; } = true;
    public bool TranslateChoices { get; set; } = true;
    public bool TranslateYesNo { get; set; } = true;

    /// <summary>Plugin-font balloon size in pixels (Arabic, CJK, …).</summary>
    public float BalloonFontSize { get; set; } = 26f;
    public float BalloonTextR { get; set; } = 0f;
    public float BalloonTextG { get; set; } = 0f;
    public float BalloonTextB { get; set; } = 0f;
    public float BalloonTextA { get; set; } = 1f;

    public Vector4 BalloonTextColor
    {
        get => new(BalloonTextR, BalloonTextG, BalloonTextB, BalloonTextA);
        set
        {
            BalloonTextR = value.X;
            BalloonTextG = value.Y;
            BalloonTextB = value.Z;
            BalloonTextA = value.W;
        }
    }

    public string ModelDirectory { get; set; } = string.Empty;
    /// <summary>Greedy decode (faster). Off = 2 beams (slightly better quality, slower).</summary>
    public bool FastTranslation { get; set; } = true;
    public bool WarmupTranslatorOnLoad { get; set; } = true;
    public bool LogTranslations { get; set; } = false;

    /// <summary>Speak Arabic translations with a Windows TTS voice. Off by default.</summary>
    public bool EnableTts { get; set; } = false;
    public int TtsVolume { get; set; } = 80;
    public int TtsRate { get; set; } = 0;
    public bool TtsOnlyWhenFocused { get; set; } = true;
    public bool TtsSpeakChoices { get; set; } = false;
    public bool SaveHistory { get; set; } = true;
    public bool AutoCopyTranslation { get; set; } = false;
    public bool EnableHotkeys { get; set; } = true;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);

    public void Migrate()
    {
        if (string.Equals(TargetLanguage, "he", StringComparison.OrdinalIgnoreCase)
            || string.Equals(TargetLanguage, "fr", StringComparison.OrdinalIgnoreCase))
            TargetLanguage = "ar";

        if (Version < 10)
            Version = 10;

        // Old default was near-black parchment ink; overlay text was hard to see — switch to white.
        if (Version < 11)
        {
            const float eps = 0.02f;
            if (Math.Abs(BalloonTextR - 0.18f) < eps
                && Math.Abs(BalloonTextG - 0.15f) < eps
                && Math.Abs(BalloonTextB - 0.12f) < eps)
            {
                BalloonTextR = 1f;
                BalloonTextG = 1f;
                BalloonTextB = 1f;
                BalloonTextA = 1f;
            }

            Version = 11;
        }

        // Stronger default readability: larger font + force light text if still dark.
        if (Version < 12)
        {
            var lum = (0.2126f * BalloonTextR) + (0.7152f * BalloonTextG) + (0.0722f * BalloonTextB);
            if (lum < 0.55f)
            {
                BalloonTextR = 1f;
                BalloonTextG = 1f;
                BalloonTextB = 1f;
                BalloonTextA = 1f;
            }

            if (BalloonFontSize < 28f)
                BalloonFontSize = 30f;

            Version = 12;
        }

        // Default balloon text: soft grey (was white).
        if (Version < 13)
        {
            const float eps = 0.02f;
            if (Math.Abs(BalloonTextR - 1f) < eps
                && Math.Abs(BalloonTextG - 1f) < eps
                && Math.Abs(BalloonTextB - 1f) < eps)
            {
                BalloonTextR = 0.75f;
                BalloonTextG = 0.75f;
                BalloonTextB = 0.75f;
                BalloonTextA = 1f;
            }

            Version = 13;
        }

        // Default balloon text: black.
        if (Version < 14)
        {
            BalloonTextR = 0f;
            BalloonTextG = 0f;
            BalloonTextB = 0f;
            BalloonTextA = 1f;
            Version = 14;
        }

        // Slightly smaller balloon font.
        if (Version < 15)
        {
            if (BalloonFontSize >= 29f)
                BalloonFontSize = 26f;
            Version = 15;
            Save();
        }
    }
}
