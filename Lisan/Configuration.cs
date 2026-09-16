using System;
using System.Numerics;
using Dalamud.Configuration;

namespace Lisan;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 8;

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

    /// <summary>Plugin-font balloon size in pixels (Arabic, CJK, …).</summary>
    public float BalloonFontSize { get; set; } = 26f;
    public float BalloonTextR { get; set; } = 0.18f;
    public float BalloonTextG { get; set; } = 0.15f;
    public float BalloonTextB { get; set; } = 0.12f;
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

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);

    public void Migrate()
    {
        if (string.Equals(TargetLanguage, "he", StringComparison.OrdinalIgnoreCase)
            || string.Equals(TargetLanguage, "fr", StringComparison.OrdinalIgnoreCase))
            TargetLanguage = "ar";

        if (Version < 8)
        {
            Version = 8;
            Save();
        }
    }
}
