using System;
using System.Collections.Generic;
using System.Text;
using ArabicRt;

namespace Lisan.Services;

/// <summary>
/// Overlay line prep. Arabic-script uses ArabicRt bake + RTL. Other RTL wraps then right-aligns.
/// LTR languages can use ImGui wrap instead.
/// </summary>
public static class DialogueDisplay
{
    private static string? lastLogical;
    private static int lastMaxChars;
    private static bool lastRtl;
    private static bool lastArabic;
    private static string[] lastLines = [];

    public static string[] LinesForImGui(string? logical, int maxLineChars, bool rtl, bool arabicScript)
    {
        maxLineChars = Math.Clamp(maxLineChars, 8, 120);

        if (string.IsNullOrWhiteSpace(logical))
            return [];

        if (string.Equals(logical, lastLogical, StringComparison.Ordinal)
            && lastMaxChars == maxLineChars
            && lastRtl == rtl
            && lastArabic == arabicScript)
            return lastLines;

        string[] lines;
        if (arabicScript && Arabic.ContainsArabic(logical))
            lines = BakeArabic(logical, maxLineChars);
        else
            lines = WrapLatin(logical.Trim(), maxLineChars);

        lastLogical = logical;
        lastMaxChars = maxLineChars;
        lastRtl = rtl;
        lastArabic = arabicScript;
        lastLines = lines;
        return lastLines;
    }

    public static int EstimateMaxChars(float wrapWidthPx, float fontSizePx)
    {
        var avg = Math.Max(5f, fontSizePx * 0.46f);
        return Math.Clamp((int)(wrapWidthPx / avg), 18, 100);
    }

    private static string[] BakeArabic(string logicalArabic, int maxLineChars)
    {
        var flat = logicalArabic.Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\n', ' ')
            .Replace('\r', ' ');

        while (flat.Contains("  ", StringComparison.Ordinal))
            flat = flat.Replace("  ", " ", StringComparison.Ordinal);

        flat = flat.Trim();

        var opts = new Options
        {
            CombineAllah = true,
            ReverseWordOrder = true,
            PreventWordSplit = true,
            MaxLineChars = maxLineChars,
        };

        return Arabic.Fix(flat, opts).Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
    }

    private static string[] WrapLatin(string text, int maxChars)
    {
        if (text.Length <= maxChars)
            return [text];

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var buf = new StringBuilder();
        foreach (var w in words)
        {
            if (buf.Length == 0)
            {
                buf.Append(w);
                continue;
            }

            if (buf.Length + 1 + w.Length > maxChars)
            {
                lines.Add(buf.ToString());
                buf.Clear();
                buf.Append(w);
            }
            else
            {
                buf.Append(' ').Append(w);
            }
        }

        if (buf.Length > 0)
            lines.Add(buf.ToString());

        return lines.ToArray();
    }
}
