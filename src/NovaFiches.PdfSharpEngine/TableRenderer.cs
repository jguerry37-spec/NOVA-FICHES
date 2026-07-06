using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Drawing.Layout;
using PdfSharp.Pdf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace NovaFiches.PdfSharpEngine;

public static class TableRenderer
{

public static ImplantationTablePayload PayloadFromJsonTable(JsonElement root, string defaultTitle)
{
    string title = defaultTitle;
    if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("title", out var tEl) && tEl.ValueKind == JsonValueKind.String)
        title = tEl.GetString() ?? defaultTitle;

    string subTitle = "";
    if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("subTitle", out var stEl) && stEl.ValueKind == JsonValueKind.String)
        subTitle = stEl.GetString() ?? "";

    string[] header = Array.Empty<string>();
    if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("header", out var hEl) && hEl.ValueKind == JsonValueKind.Array)
        header = hEl.EnumerateArray().Select(x => x.GetString() ?? "").ToArray();

    var rows = new List<string[]>();
    if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("rows", out var rEl) && rEl.ValueKind == JsonValueKind.Array)
    {
        foreach (var rowEl in rEl.EnumerateArray())
        {
            if (rowEl.ValueKind == JsonValueKind.Array)
                rows.Add(rowEl.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? (x.GetString() ?? "") : x.ToString()).ToArray());
        }
    }

    return new ImplantationTablePayload
    {
        Title = title,
        SubTitle = subTitle,
        Header = header,
        Rows = rows
    };
}

    // Layout constants (points)
    private const double LeftMargin = 36;
    private const double RightMargin = 36;
    private const double TopMargin = 72;
    // Zone de sécurité en bas de page pour éviter tout chevauchement avec le pied de page
    private static double BottomMargin => LayoutConstants.FooterReservePt;

    private const double TitleHeight = 22;
    private const double SubTitleHeight = 16;
    private const double GapAfterTitle = 10;

    private const double HeaderHeight = 18;
    private const double RowHeight = 16;
    private const double FooterHeight = 18;

    public sealed class TableLayout
    {
        public double[]? ColumnWidths { get; set; }

        /// <summary>
        /// Column boundary indices where a thick line must be drawn.
        /// 0 = left border, N = right border (after last column).
        /// </summary>
        public List<int>? ThickVerticalSeparators { get; set; }

        public bool ThickAllVerticals { get; set; } = false;

        // If true, long cell text will wrap to multiple lines (within row height) using smaller font.
        public bool WrapText { get; set; } = true;
        public int WrapMaxLines { get; set; } = 2;

        // Optional shading (used e.g. for MESURE SUR LIGNE: alternate by point group)
        public bool AlternateRowShading { get; set; } = false;

        /// <summary>
        /// If AlternateRowShading is enabled, rows are shaded by groups.
        /// 1 = every other row, 3 = every other group of 3 rows, etc.
        /// </summary>
        public int ShadeGroupSize { get; set; } = 1;

        /// <summary>
        /// Brush used for shaded rows. If null, a light grey default is used.
        /// </summary>
        public XBrush? ShadeBrush { get; set; } = null;
    }

    /// <summary>
    /// Generic table renderer entrypoint (used by PdfSharpReports).
    /// Currently delegates to the deterministic table renderer.
    /// </summary>
    public static void RenderTable(PdfDocument doc, ImplantationTablePayload payload, TableLayout layout, string buildFooter)
        => RenderImplantationTable(doc, payload, layout, buildFooter);

    public static void RenderImplantationTable(PdfDocument doc, ImplantationTablePayload payload, TableLayout layout, string buildFooter)
    {
        if (doc == null) throw new ArgumentNullException(nameof(doc));
        if (payload == null) throw new ArgumentNullException(nameof(payload));
        layout ??= new TableLayout();

        var header = payload.Header ?? Array.Empty<string>();
        int colCount = Math.Max(1, header.Length);

        // Column widths
        double pageW = 595;
        double pageH = 842;
        if (doc.Pages.Count > 0)
        {
            pageW = doc.Pages[0].Width.Point;
            pageH = doc.Pages[0].Height.Point;
        }

        double tableW = pageW - LeftMargin - RightMargin;
        var colWidths = layout.ColumnWidths != null && layout.ColumnWidths.Length == colCount
            ? layout.ColumnWidths
            : Enumerable.Repeat(tableW / colCount, colCount).ToArray();

        // Compute thick separators
        List<int> thickSep;
        if (layout.ThickVerticalSeparators != null && layout.ThickVerticalSeparators.Count > 0)
            thickSep = layout.ThickVerticalSeparators;
        else if (layout.ThickAllVerticals)
            thickSep = Enumerable.Range(0, colCount + 1).ToList();
        else
            thickSep = new List<int> { 0, colCount };

        // Pagination plan
        double footerHUsed = string.IsNullOrWhiteSpace(buildFooter) ? 0 : FooterHeight;
        var plan = BuildPagePlan(pageH, payload.Rows, footerHUsed);
        int totalPages = plan.Count;

        for (int i = 0; i < totalPages; i++)
        {
            var (start, count) = plan[i];
            var page = doc.AddPage();
            page.Size = PdfSharp.PageSize.A4;

            using var gfx = XGraphics.FromPdfPage(page);
            RenderSinglePage(gfx, page, payload, header, colWidths, thickSep, layout, buildFooter, footerHUsed, i + 1, totalPages, start, count);
        }
    }

    private static List<(int Start, int Count)> BuildPagePlan(double pageH, List<string[]> rows, double footerHUsed)
    {
        double available = pageH - TopMargin - BottomMargin - TitleHeight - SubTitleHeight - GapAfterTitle - HeaderHeight - footerHUsed;
        int rowsPerPage = Math.Max(1, (int)Math.Floor(available / RowHeight));

        var plan = new List<(int, int)>();
        int idx = 0;
        while (idx < rows.Count)
        {
            int take = Math.Min(rowsPerPage, rows.Count - idx);
            plan.Add((idx, take));
            idx += take;
        }
        if (plan.Count == 0) plan.Add((0, 0));
        return plan;
    }

    private static void RenderSinglePage(
        XGraphics gfx,
        PdfPage page,
        ImplantationTablePayload payload,
        string[] header,
        double[] colWidths,
        List<int> thickSep,
        TableLayout layout,
        string buildFooter,
	        double footerHUsed,
        int pageNumber,
        int totalPages,
        int startIndex,
        int count)
    {
        // Title / subtitle
        double y = TopMargin;
        gfx.DrawString(payload.Title ?? "", NovatlasTheme.TitleFont(), XBrushes.Black,
            new XRect(LeftMargin, y, page.Width.Point - LeftMargin - RightMargin, TitleHeight), XStringFormats.TopLeft);
        y += TitleHeight;

        gfx.DrawString(payload.SubTitle ?? "", NovatlasTheme.SubTitleFont(), XBrushes.Black,
            new XRect(LeftMargin, y, page.Width.Point - LeftMargin - RightMargin, SubTitleHeight), XStringFormats.TopLeft);
        y += SubTitleHeight + GapAfterTitle;

        double tableX = LeftMargin;
        double tableY = y;

        DrawHeaderRow(gfx, tableX, tableY, colWidths, header);

        double rowY = tableY + HeaderHeight;
        for (int r = 0; r < count; r++)
        {
            int idx = startIndex + r;
            var row = idx < payload.Rows.Count ? payload.Rows[idx] : Array.Empty<string>();
            DrawDataRow(gfx, tableX, rowY, colWidths, row, layout, idx);
            rowY += RowHeight;
        }

        double gridH = HeaderHeight + (count * RowHeight);
        DrawThinGrid(gfx, tableX, tableY, colWidths, gridH);
        // Optional emphasized vertical separators (avoid thick lines everywhere)
        if (thickSep.Any(s => s > 0 && s < colWidths.Length))
            DrawThickVerticals(gfx, tableX, tableY, colWidths, gridH, thickSep);

        if (footerHUsed > 0)
            DrawFooter(gfx, page, buildFooter, pageNumber, totalPages);
    }

    private static void DrawHeaderRow(XGraphics gfx, double x, double y, double[] widths, string[] header)
    {
        double cx = x;
        for (int c = 0; c < header.Length; c++)
        {
            var rect = new XRect(cx, y, widths[c], HeaderHeight);
            gfx.DrawRectangle(NovatlasTheme.HeaderFillBrush(), rect);
            gfx.DrawString(header[c] ?? "", NovatlasTheme.TableHeaderFont(), XBrushes.Black, rect, XStringFormats.Center);
            cx += widths[c];
        }
    }

    private static void DrawDataRow(XGraphics gfx, double x, double y, double[] widths, string[] row, TableLayout layout, int globalRowIndex)
    {
        double cx = x;

        // Optional row shading (before drawing text)
        if (layout.AlternateRowShading)
        {
            int groupSize = Math.Max(1, layout.ShadeGroupSize);
            int groupIndex = globalRowIndex / groupSize;
            bool shade = (groupIndex % 2) == 1;
            if (shade)
            {
                var brush = layout.ShadeBrush ?? new XSolidBrush(XColor.FromArgb(255, 242, 242, 242));
                gfx.DrawRectangle(brush, x, y, widths.Sum(), RowHeight);
            }
        }

        for (int c = 0; c < widths.Length; c++)
        {
            string txt = c < row.Length ? (row[c] ?? "") : "";
            var rect = new XRect(cx, y, widths[c], RowHeight);

            // Colonne "ID point" : toujours privilégier l'ajustement de taille (pas de retour à la ligne).
            if (c == 0)
                DrawCellTextFit(gfx, rect, txt, NovatlasTheme.TableCellFont());
            else
                DrawCellText(gfx, rect, txt, NovatlasTheme.TableCellFont(), layout);

            cx += widths[c];
        }
    }

    /// <summary>
    /// Texte ajusté pour la colonne "ID point" : on réduit la taille pour tenir dans la cellule,
    /// sans wrap (un point ne doit pas passer sur 2 lignes). Ellipsis en dernier recours.
    /// </summary>
    private static void DrawCellTextFit(XGraphics gfx, XRect rect, string text, XFont baseFont)
    {
        text ??= "";

        const double padX = 2.0;
        const double padY = 1.0;
        var inner = new XRect(rect.X + padX, rect.Y + padY, Math.Max(0, rect.Width - padX * 2), Math.Max(0, rect.Height - padY * 2));

        // Try shrinking from base size down to 6pt.
        double fs = baseFont.Size;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            var f = new XFont("Arial", fs, baseFont.Style);
            if (gfx.MeasureString(text, f).Width <= inner.Width)
            {
                gfx.DrawString(text, f, XBrushes.Black, inner, XStringFormats.Center);
                return;
            }
            fs = Math.Max(6.0, fs - 0.75);
        }

        // Last resort: ellipsis at base font.
        gfx.DrawString(EllipsizeToWidth(gfx, text, baseFont, inner.Width), baseFont, XBrushes.Black, inner, XStringFormats.Center);
    }

    private static void DrawThinGrid(XGraphics gfx, double x, double y, double[] widths, double height)
    {
        var pen = NovatlasTheme.GridPenThin();
        var outerPen = NovatlasTheme.GridPenOuter();
        double totalW = widths.Sum();

        gfx.DrawRectangle(outerPen, x, y, totalW, height);

        double cx = x;
        for (int c = 0; c < widths.Length - 1; c++)
        {
            cx += widths[c];
            gfx.DrawLine(pen, cx, y, cx, y + height);
        }

        gfx.DrawLine(pen, x, y + HeaderHeight, x + totalW, y + HeaderHeight);

        int rows = (int)Math.Round((height - HeaderHeight) / RowHeight);
        for (int r = 1; r <= rows; r++)
        {
            double yy = y + HeaderHeight + r * RowHeight;
            gfx.DrawLine(pen, x, yy, x + totalW, yy);
        }
    }

    private static void DrawThickVerticals(XGraphics gfx, double x, double y, double[] widths, double height, List<int> separators)
    {
        var pen = NovatlasTheme.GridPenOuter();

        double[] boundaries = new double[widths.Length + 1];
        boundaries[0] = x;
        for (int i = 0; i < widths.Length; i++)
            boundaries[i + 1] = boundaries[i] + widths[i];

        foreach (int b in separators.Distinct().OrderBy(v => v))
        {
            if (b < 0 || b > widths.Length) continue;
            double xx = boundaries[b];
            gfx.DrawLine(pen, xx, y, xx, y + height);
        }
    }

    private static void DrawFooter(XGraphics gfx, PdfPage page, string buildFooter, int pageNumber, int totalPages)
    {
        string left = buildFooter ?? "";
        string right = $"{pageNumber}/{totalPages}";

        double y = page.Height.Point - BottomMargin + 6;

        gfx.DrawString(left, NovatlasTheme.FooterFont(), XBrushes.Gray,
            new XRect(LeftMargin, y, page.Width.Point / 2, FooterHeight), XStringFormats.TopLeft);

        gfx.DrawString(right, NovatlasTheme.FooterFont(), XBrushes.Gray,
            new XRect(page.Width.Point / 2, y, page.Width.Point / 2 - RightMargin, FooterHeight), XStringFormats.TopRight);
    }

    
    private static bool IsNumericLike(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;

        // Normalize decimal separator; accept numbers like -123, 123.45, 123,45, 1.2E-3
        var t = s.Trim();
        // Remove spaces used as thousand separators
        t = t.Replace(" ", "");
        if (double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
            return true;
        if (t.Contains(','))
        {
            var t2 = t.Replace(',', '.');
            if (double.TryParse(t2, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
                return true;
        }
        return false;
    }

/// <summary>
    /// Draw a cell's text centered, with optional wrapping when it exceeds the cell width.
    /// Wrapping is capped (default 2 lines) and will ellipsis on the last line if needed.
    /// </summary>
    private static void DrawCellText(XGraphics gfx, XRect rect, string text, XFont baseFont, TableLayout layout)
    {
        text ??= "";

        // Small inner padding so text doesn't touch grid lines
        const double padX = 2.0;
        const double padY = 1.0;
        var inner = new XRect(rect.X + padX, rect.Y + padY, Math.Max(0, rect.Width - padX * 2), Math.Max(0, rect.Height - padY * 2));

        // Fast path: fits on one line
        var size = gfx.MeasureString(text, baseFont);
        if (size.Width <= inner.Width)
        {
            gfx.DrawString(text, baseFont, XBrushes.Black, inner, XStringFormats.Center);
            return;
        }

        bool isNumeric = IsNumericLike(text);

        // For numeric values (X/Y/Z/Dx/Dy/Dz): NEVER wrap.
        // Instead, shrink font until it fits (down to 6pt), then ellipsis if needed.
        if (isNumeric)
        {
            double fontSize = baseFont.Size;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                var font = new XFont("Arial", fontSize, baseFont.Style);
                if (gfx.MeasureString(text, font).Width <= inner.Width)
                {
                    gfx.DrawString(text, font, XBrushes.Black, inner, XStringFormats.Center);
                    return;
                }
                fontSize = Math.Max(6.0, fontSize - 1.0);
            }

            gfx.DrawString(EllipsizeToWidth(gfx, text, baseFont, inner.Width), baseFont, XBrushes.Black, inner, XStringFormats.Center);
            return;
        }

        // For text values: wrap is allowed (default 2 lines) with mild font shrinking.
        if (!layout.WrapText)
        {
            gfx.DrawString(EllipsizeToWidth(gfx, text, baseFont, inner.Width), baseFont, XBrushes.Black, inner, XStringFormats.Center);
            return;
        }

        double fs = baseFont.Size;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            var font = new XFont("Arial", fs, baseFont.Style);
            var lines = WrapTextToLines(gfx, text, font, inner.Width, layout.WrapMaxLines);
            if (lines.Count > 0 && lines.Count <= layout.WrapMaxLines)
            {
                DrawMultilineCentered(gfx, inner, lines, font);
                return;
            }
            fs = Math.Max(6.0, fs - 1.0);
        }

        // Last resort
        gfx.DrawString(EllipsizeToWidth(gfx, text, baseFont, inner.Width), baseFont, XBrushes.Black, inner, XStringFormats.Center);
    }

    private static void DrawMultilineCentered(XGraphics gfx, XRect rect, List<string> lines, XFont font)
    {
        if (lines.Count == 0) return;

        // Approximate line height (PDFsharp doesn't expose font metrics directly)
        double lh = Math.Max(8.0, font.Size * 1.05);
        double totalH = lines.Count * lh;
        double startY = rect.Y + (rect.Height - totalH) / 2.0;

        for (int i = 0; i < lines.Count; i++)
        {
            var lineRect = new XRect(rect.X, startY + i * lh, rect.Width, lh);
            gfx.DrawString(lines[i], font, XBrushes.Black, lineRect, XStringFormats.Center);
        }
    }

    private static List<string> WrapTextToLines(XGraphics gfx, string text, XFont font, double maxWidth, int maxLines)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(text) || maxWidth <= 1) return result;

        // Prefer splitting on spaces; if none, do a hard wrap.
        var words = text.Contains(' ') ? text.Split(' ', StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();
        if (words.Length == 0)
        {
            // Hard wrap
            string remaining = text;
            while (remaining.Length > 0 && result.Count < maxLines)
            {
                int cut = FindMaxSubstringThatFits(gfx, remaining, font, maxWidth);
                if (cut <= 0) break;
                result.Add(remaining.Substring(0, cut));
                remaining = remaining.Substring(cut).TrimStart();
            }

            if (remaining.Length > 0)
                result[result.Count - 1] = EllipsizeToWidth(gfx, result[result.Count - 1] + remaining, font, maxWidth);

            return result;
        }

        string line = "";
        foreach (var w in words)
        {
            string candidate = string.IsNullOrEmpty(line) ? w : (line + " " + w);
            if (gfx.MeasureString(candidate, font).Width <= maxWidth)
            {
                line = candidate;
                continue;
            }

            if (!string.IsNullOrEmpty(line))
                result.Add(line);
            else
                result.Add(EllipsizeToWidth(gfx, w, font, maxWidth));

            line = w;
            if (result.Count >= maxLines) break;
        }

        if (result.Count < maxLines && !string.IsNullOrEmpty(line))
            result.Add(line);

        // If we still exceed max lines, ellipsis the last line
        if (result.Count > maxLines)
        {
            result = result.Take(maxLines).ToList();
            result[maxLines - 1] = EllipsizeToWidth(gfx, result[maxLines - 1], font, maxWidth);
        }

        // If we reached max lines but still have remaining words, ellipsis last line
        if (result.Count == maxLines)
        {
            // Ensure last line doesn't exceed width
            result[maxLines - 1] = EllipsizeToWidth(gfx, result[maxLines - 1], font, maxWidth);
        }

        return result;
    }

    private static int FindMaxSubstringThatFits(XGraphics gfx, string text, XFont font, double maxWidth)
    {
        // Binary search the max length that fits
        int lo = 1, hi = text.Length, best = 0;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            string sub = text.Substring(0, mid);
            if (gfx.MeasureString(sub, font).Width <= maxWidth)
            {
                best = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return best;
    }

    private static string EllipsizeToWidth(XGraphics gfx, string text, XFont font, double maxWidth)
    {
        if (string.IsNullOrEmpty(text)) return "";
        if (gfx.MeasureString(text, font).Width <= maxWidth) return text;

        const string ell = "…";
        int lo = 0, hi = text.Length, best = 0;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            string candidate = text.Substring(0, mid) + ell;
            if (gfx.MeasureString(candidate, font).Width <= maxWidth)
            {
                best = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return (best <= 0) ? ell : text.Substring(0, best) + ell;
    }
}