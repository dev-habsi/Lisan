using System;

namespace Lisan;

/// <summary>
/// Built-in English→X language packs (Helsinki-NLP Opus-MT on disk).
/// Pick one in config, then run Models\setup-models.ps1 -Language &lt;code&gt;.
/// </summary>
public sealed class TargetLanguageInfo
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required string HuggingFaceRepo { get; init; }
    public required string ModelFolder { get; init; }
    public bool RightToLeft { get; init; }
    public bool ArabicScript { get; init; }
    public string BusyText { get; init; } = "Translating…";
    /// <summary>Optional Opus-MT / TC target token prepended to source (e.g. &gt;&gt;cmn_Hans&lt;&lt;).</summary>
    public string TargetToken { get; init; } = "";
    public string? FontFile { get; init; }
    public ushort[]? GlyphRanges { get; init; }

    public string Label => $"{Name}  ({Code})";
}

public static class TargetLanguages
{
    public const string CustomCode = "custom";

    public static readonly ushort[] LatinGlyphs =
    [
        0x0020, 0x00FF,
        0x0100, 0x017F,
        0x0180, 0x024F,
        0x1E00, 0x1EFF,
        0x0400, 0x04FF,
        0x0370, 0x03FF,
        0,
    ];

    public static readonly ushort[] ArabicGlyphs =
    [
        0x0020, 0x00FF,
        0x0600, 0x06FF,
        0x0750, 0x077F,
        0x08A0, 0x08FF,
        0xFB50, 0xFDFF,
        0xFE70, 0xFEFF,
        0,
    ];

    public static readonly ushort[] CjkGlyphs =
    [
        0x0020, 0x00FF,
        0x3000, 0x30FF,
        0x31F0, 0x31FF,
        0x3400, 0x4DBF,
        0x4E00, 0x9FFF,
        0xF900, 0xFAFF,
        0xFF00, 0xFFEF,
        0,
    ];

    public static readonly ushort[] DevanagariGlyphs =
    [
        0x0020, 0x00FF,
        0x0900, 0x097F,
        0xA8E0, 0xA8FF,
        0,
    ];

    public static readonly ushort[] ThaiGlyphs =
    [
        0x0020, 0x00FF,
        0x0E00, 0x0E7F,
        0,
    ];

    public static readonly TargetLanguageInfo[] All =
    [
        Lang("ar", "Arabic", "Helsinki-NLP/opus-mt-en-ar", "opus-mt-en-ar",
            rtl: true, arabic: true, busy: "جاري الترجمة…",
            font: "NotoNaskhArabic-Regular.ttf", glyphs: ArabicGlyphs),
        Lang("fr", "French", "Helsinki-NLP/opus-mt-en-fr", "opus-mt-en-fr",
            busy: "Traduction…"),
        Lang("de", "German", "Helsinki-NLP/opus-mt-en-de", "opus-mt-en-de",
            busy: "Übersetzung…"),
        Lang("es", "Spanish", "Helsinki-NLP/opus-mt-en-es", "opus-mt-en-es",
            busy: "Traduciendo…"),
        Lang("it", "Italian", "Helsinki-NLP/opus-mt-en-it", "opus-mt-en-it",
            busy: "Traduzione…"),
        Lang("pt", "Portuguese", "Helsinki-NLP/opus-mt-tc-big-en-pt", "opus-mt-tc-big-en-pt",
            busy: "Traduzindo…", token: ">>por<<"),
        Lang("nl", "Dutch", "Helsinki-NLP/opus-mt-en-nl", "opus-mt-en-nl",
            busy: "Vertalen…"),
        Lang("pl", "Polish", "Helsinki-NLP/opus-mt-en-pl", "opus-mt-en-pl",
            busy: "Tłumaczenie…"),
        Lang("ru", "Russian", "Helsinki-NLP/opus-mt-en-ru", "opus-mt-en-ru",
            busy: "Перевод…"),
        Lang("uk", "Ukrainian", "Helsinki-NLP/opus-mt-en-uk", "opus-mt-en-uk",
            busy: "Переклад…"),
        Lang("tr", "Turkish", "Helsinki-NLP/opus-mt-en-tr", "opus-mt-en-tr",
            busy: "Çeviriliyor…"),
        Lang("sv", "Swedish", "Helsinki-NLP/opus-mt-en-sv", "opus-mt-en-sv",
            busy: "Översätter…"),
        Lang("da", "Danish", "Helsinki-NLP/opus-mt-en-da", "opus-mt-en-da",
            busy: "Oversætter…"),
        Lang("fi", "Finnish", "Helsinki-NLP/opus-mt-en-fi", "opus-mt-en-fi",
            busy: "Käännetään…"),
        Lang("cs", "Czech", "Helsinki-NLP/opus-mt-en-cs", "opus-mt-en-cs",
            busy: "Překládání…"),
        Lang("ro", "Romanian", "Helsinki-NLP/opus-mt-en-ro", "opus-mt-en-ro",
            busy: "Se traduce…"),
        Lang("hu", "Hungarian", "Helsinki-NLP/opus-mt-en-hu", "opus-mt-en-hu",
            busy: "Fordítás…"),
        Lang("el", "Greek", "Helsinki-NLP/opus-mt-en-el", "opus-mt-en-el",
            busy: "Μετάφραση…"),
        Lang("bg", "Bulgarian", "Helsinki-NLP/opus-mt-en-bg", "opus-mt-en-bg",
            busy: "Превод…"),
        Lang("ja", "Japanese", "Helsinki-NLP/opus-mt-en-jap", "opus-mt-en-jap",
            busy: "翻訳中…", font: "NotoSansCJKjp-Regular.otf", glyphs: CjkGlyphs),
        Lang("zh", "Chinese (Simplified)", "Helsinki-NLP/opus-mt-en-zh", "opus-mt-en-zh",
            busy: "翻译中…", token: ">>cmn_Hans<<",
            font: "NotoSansCJKsc-Regular.otf", glyphs: CjkGlyphs),
        Lang("ko", "Korean", "Helsinki-NLP/opus-mt-tc-big-en-ko", "opus-mt-tc-big-en-ko",
            busy: "번역 중…", token: ">>kor<<",
            font: "NotoSansCJKkr-Regular.otf", glyphs: CjkGlyphs),
        Lang("vi", "Vietnamese", "Helsinki-NLP/opus-mt-en-vi", "opus-mt-en-vi",
            busy: "Đang dịch…"),
        Lang("id", "Indonesian", "Helsinki-NLP/opus-mt-en-id", "opus-mt-en-id",
            busy: "Menerjemahkan…"),
        Lang("fa", "Persian", "Helsinki-NLP/opus-mt-en-fa", "opus-mt-en-fa",
            rtl: true, arabic: true, busy: "در حال ترجمه…",
            font: "NotoNaskhArabic-Regular.ttf", glyphs: ArabicGlyphs),
        Lang("hi", "Hindi", "Helsinki-NLP/opus-mt-en-hi", "opus-mt-en-hi",
            busy: "अनुवाद हो रहा है…",
            font: "NotoSansDevanagari-Regular.ttf", glyphs: DevanagariGlyphs),
        Lang("th", "Thai", "Helsinki-NLP/opus-mt-en-th", "opus-mt-en-th",
            busy: "กำลังแปล…",
            font: "NotoSansThai-Regular.ttf", glyphs: ThaiGlyphs),
    ];

    public static readonly TargetLanguageInfo Custom = new()
    {
        Code = CustomCode,
        Name = "Custom model folder",
        HuggingFaceRepo = "",
        ModelFolder = "",
        BusyText = "Translating…",
    };

    public static string[] ComboLabels()
    {
        var labels = new string[All.Length + 1];
        for (var i = 0; i < All.Length; i++)
            labels[i] = All[i].Label;
        labels[^1] = Custom.Label;
        return labels;
    }

    public static int ComboIndex(string? code)
    {
        var c = NormalizeCode(code);
        if (c == CustomCode)
            return All.Length;
        for (var i = 0; i < All.Length; i++)
        {
            if (All[i].Code == c)
                return i;
        }

        return 0;
    }

    public static string CodeFromCombo(int index)
    {
        if (index < 0)
            return All[0].Code;
        if (index >= All.Length)
            return CustomCode;
        return All[index].Code;
    }

    public static TargetLanguageInfo Current() => Get(Plugin.Configuration.TargetLanguage);

    public static TargetLanguageInfo Get(string? code)
    {
        var c = NormalizeCode(code);
        if (c == CustomCode)
        {
            var cfg = Plugin.Configuration;
            return new TargetLanguageInfo
            {
                Code = CustomCode,
                Name = "Custom",
                HuggingFaceRepo = "",
                ModelFolder = "",
                RightToLeft = cfg.CustomRightToLeft,
                ArabicScript = cfg.CustomRightToLeft,
                BusyText = "Translating…",
                TargetToken = (cfg.CustomTargetToken ?? "").Trim(),
            };
        }

        foreach (var lang in All)
        {
            if (lang.Code == c)
                return lang;
        }

        return new TargetLanguageInfo
        {
            Code = c,
            Name = c.ToUpperInvariant(),
            HuggingFaceRepo = "Helsinki-NLP/opus-mt-en-" + c,
            ModelFolder = "opus-mt-en-" + c,
            BusyText = "Translating…",
        };
    }

    public static bool IsBusyPlaceholder(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var t = text.Trim();
        if (t.Contains("translating", StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var lang in All)
        {
            if (t.Equals(lang.BusyText, StringComparison.Ordinal))
                return true;
        }

        return t.Contains("جاري الترجمة", StringComparison.Ordinal);
    }

    public static string NormalizeCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return "ar";
        return code.Trim().ToLowerInvariant();
    }

    private static TargetLanguageInfo Lang(
        string code,
        string name,
        string repo,
        string folder,
        bool rtl = false,
        bool arabic = false,
        string busy = "Translating…",
        string token = "",
        string? font = null,
        ushort[]? glyphs = null) =>
        new()
        {
            Code = code,
            Name = name,
            HuggingFaceRepo = repo,
            ModelFolder = folder,
            RightToLeft = rtl,
            ArabicScript = arabic,
            BusyText = busy,
            TargetToken = token,
            FontFile = font,
            GlyphRanges = glyphs,
        };
}
