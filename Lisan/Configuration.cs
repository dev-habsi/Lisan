using System;
using System.Numerics;
using Dalamud.Configuration;

namespace DialogueAr;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 6;

    /// <summary>ISO-ish code from <see cref="TargetLanguages"/>, or "custom". Default Arabic (existing installs).</summary>
    public string TargetLanguage { get; set; } = "ar";
    public bool CustomRightToLeft { get; set; }
    /// <summary>Optional Opus-MT token for custom models, e.g. &gt;&gt;cmn_Hans&lt;&lt;.</summary>
    public string CustomTargetToken { get; set; } = "";

    // ── General ──────────────────────────────────────────────
    public bool Enabled { get; set; } = true;
    public bool TranslateTalk { get; set; } = true;
    public bool TranslateBattleTalk { get; set; } = true;
    public bool TranslateTalkSubtitle { get; set; } = true;
    /// <summary>Write the translation into the game Talk / BattleTalk / TalkSubtitle text node.</summary>
    public bool ReplaceNativeDialogue { get; set; } = true;

    // ── Overlay ──────────────────────────────────────────────
    public bool ShowOverlay { get; set; } = false;
    public bool LockOverlay { get; set; }
    public bool ShowSpeaker { get; set; } = false;
    public bool ShowOriginalEnglish { get; set; } = false;
    public bool ShowBusyPlaceholder { get; set; } = true;
    public bool RightClickOpensConfig { get; set; } = true;
    public float OverlayScale { get; set; } = 1.0f;
    public float OverlayWidth { get; set; } = 400f;
    /// <summary>0 = auto from wrap width. Otherwise force ArabicRt MaxLineChars.</summary>
    public int OverlayWrapChars { get; set; } = 0;
    public float OverlayBgAlpha { get; set; } = 0.92f;
    public float OverlayTextR { get; set; } = 0.28f;
    public float OverlayTextG { get; set; } = 0.22f;
    public float OverlayTextB { get; set; } = 0.16f;
    public float OverlayTextA { get; set; } = 1.0f;
    public float OverlayPosX { get; set; } = 420f;
    public float OverlayPosY { get; set; } = 620f;
    public bool OverlayPosSaved { get; set; }

    // ── Translator ───────────────────────────────────────────
    public string ModelDirectory { get; set; } = string.Empty;
    /// <summary>Greedy decode (faster). Off = 2 beams (slightly better quality, slower).</summary>
    public bool FastTranslation { get; set; } = true;
    public bool WarmupTranslatorOnLoad { get; set; } = true;

    // ── Debug ────────────────────────────────────────────────
    public bool LogTranslations { get; set; } = false;

    public Vector4 OverlayTextColor
    {
        get => new(OverlayTextR, OverlayTextG, OverlayTextB, OverlayTextA);
        set
        {
            OverlayTextR = value.X;
            OverlayTextG = value.Y;
            OverlayTextB = value.Z;
            OverlayTextA = value.W;
        }
    }

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);

    /// <summary>v5 parchment colors. v6 native Talk replace, extra overlay off.</summary>
    public void MigrateOverlayTheme()
    {
        if (Version < 5)
        {
            var oldMint =
                Math.Abs(OverlayTextR - 0.85f) < 0.02f
                && Math.Abs(OverlayTextG - 1.0f) < 0.02f
                && Math.Abs(OverlayTextB - 0.85f) < 0.02f;
            if (oldMint)
            {
                OverlayTextR = 0.28f;
                OverlayTextG = 0.22f;
                OverlayTextB = 0.16f;
                OverlayTextA = 1.0f;
            }

            if (Math.Abs(OverlayBgAlpha - 0.88f) < 0.02f)
                OverlayBgAlpha = 0.92f;
        }

        if (Version < 6)
        {
            ReplaceNativeDialogue = true;
            ShowOverlay = false;
            if (OverlayScale > 1.05f)
                OverlayScale = 1.0f;
            if (OverlayWidth > 450f)
                OverlayWidth = 400f;
        }

        if (Version < 6)
        {
            Version = 6;
            Save();
        }
    }
}
