using System;
using System.Collections.Generic;
using System.Linq;
using PdfSharp.Drawing;

namespace NovaFiches.PdfSharpEngine;

/// <summary>
/// Helper to safely render long text inside a fixed rectangle:
/// - word wrap
/// - font size reduction
/// - ellipsis as last resort
/// 
/// Designed to guarantee "no overflow" in cartouche cells (e.g., Plan de référence).
/// </summary>
internal static class TextFitHelper
{
    public static void DrawCenteredWrapped(
        XGraphics g,
        XRect rect,
        string text,
        Func<double, XFont> fontFactory,
        double startFontSize,
        double minFontSize,
        int maxLines = 2)
    {
        if (g == null) throw new ArgumentNullException(nameof(g));
        if (string.IsNullOrWhiteSpace(text)) return;

        text = text.Trim();
        var brush = XBrushes.Black;

        // Use a small step; 0.5 is a good compromise between speed and stability.
        const double step = 0.5;

        for (double size = startFontSize; size >= minFontSize - 0.001; size -= step)
        {
            var font = fontFactory(size);
            // PdfSharp 6.x: the XGraphics overload of GetHeight may throw at runtime depending on build.
            // Use parameterless GetHeight() to stay compatible with our packaged PdfSharp.
            double lineH = Math.Max(1, font.GetHeight());

            // If even maxLines won't fit vertically, continue shrinking.
            if (maxLines * lineH > rect.Height + 0.01) continue;

            var lines = WrapWords(g, text, font, rect.Width);
            if (lines.Count <= maxLines)
            {
                // Perfect fit.
                DrawLines(g, rect, lines, font, lineH, brush);
                return;
            }

            // Too many lines: try truncation with ellipsis on the last visible line.
            var clipped = lines.Take(maxLines).ToList();
            clipped[^1] = Ellipsize(g, clipped[^1], font, rect.Width);
            DrawLines(g, rect, clipped, font, lineH, brush);
            return;
        }

        // Worst case: draw single ellipsized line with minimum font.
        var minFont = fontFactory(minFontSize);
        var one = new List<string> { Ellipsize(g, text, minFont, rect.Width) };
        DrawLines(g, rect, one, minFont, Math.Max(1, minFont.GetHeight()), XBrushes.Black);
    }

    private static void DrawLines(XGraphics g, XRect rect, List<string> lines, XFont font, double lineH, XBrush brush)
    {
        int n = Math.Max(1, lines.Count);
        double totalH = n * lineH;
        double y = rect.Y + (rect.Height - totalH) / 2.0;

        for (int i = 0; i < lines.Count; i++)
        {
            var r = new XRect(rect.X, y + i * lineH, rect.Width, lineH);
            g.DrawString(lines[i], font, brush, r, XStringFormats.TopCenter);
        }
    }

    private static List<string> WrapWords(XGraphics g, string text, XFont font, double maxWidth)
    {
        // Fast path.
        if (g.MeasureString(text, font).Width <= maxWidth)
            return new List<string> { text };

        var words = text
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        if (words.Count == 0) return new List<string>();

        var lines = new List<string>();
        string current = words[0];

        for (int i = 1; i < words.Count; i++)
        {
            string candidate = current + " " + words[i];
            if (g.MeasureString(candidate, font).Width <= maxWidth)
            {
                current = candidate;
                continue;
            }

            lines.Add(current);
            current = words[i];
        }

        lines.Add(current);

        // If a single "word" is too long (e.g., filename without spaces), force-break it.
        for (int i = 0; i < lines.Count; i++)
        {
            if (g.MeasureString(lines[i], font).Width <= maxWidth) continue;
            lines[i] = BreakLongToken(g, lines[i], font, maxWidth);
        }

        return lines;
    }

    private static string BreakLongToken(XGraphics g, string token, XFont font, double maxWidth)
    {
        // Simple character-based breaking with an em dash separator.
        // We keep it minimal to avoid layout regressions.
        if (string.IsNullOrEmpty(token)) return token;
        var chars = token.ToCharArray();
        int cut = chars.Length;
        while (cut > 1)
        {
            string left = new string(chars, 0, cut);
            if (g.MeasureString(left, font).Width <= maxWidth) return left;
            cut--;
        }
        return Ellipsize(g, token, font, maxWidth);
    }

    private static string Ellipsize(XGraphics g, string text, XFont font, double maxWidth)
    {
        const string ell = "…";
        if (string.IsNullOrEmpty(text)) return text;
        if (g.MeasureString(text, font).Width <= maxWidth) return text;
        if (g.MeasureString(ell, font).Width > maxWidth) return "";

        int len = text.Length;
        while (len > 0)
        {
            string candidate = text.Substring(0, len).TrimEnd() + ell;
            if (g.MeasureString(candidate, font).Width <= maxWidth) return candidate;
            len--;
        }
        return ell;
    }
}
