using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace NovaFiches.PdfSharpEngine;

/// <summary>
/// Minimal cover page renderer (deterministic). Purpose: provide a stable page 1
/// and ensure the full report is produced without any jsPDF / WebView2 rendering side-effects.
/// </summary>
public static class CoverPageRenderer
{
    public static void Render(PdfDocument doc, Dictionary<string, string> info, string buildFooter)
    {
        var page = doc.AddPage();
        page.Size = PdfSharp.PageSize.A4;

        using var g = XGraphics.FromPdfPage(page);

        double pageW = page.Width.Point;
        double pageH = page.Height.Point;

        double margin = Units.MmToPt(10);
        double y = Units.MmToPt(15);

        // Title
        g.DrawString("RAPPORT COMPLET", NovatlasTheme.FontBold(18), XBrushes.Black,
            new XRect(margin, y, pageW - 2 * margin, Units.MmToPt(12)), XStringFormats.CenterLeft);
        y += Units.MmToPt(14);

        // Small subtitle
        g.DrawString("Nova-Fiches — moteur PdfSharp", NovatlasTheme.FontBody(10), XBrushes.Black,
            new XRect(margin, y, pageW - 2 * margin, Units.MmToPt(7)), XStringFormats.CenterLeft);
        y += Units.MmToPt(10);

        // Info box
        double boxW = pageW - 2 * margin;
        double boxH = Units.MmToPt(80);
        var pen = new XPen(XColors.Black, 0.6);
        g.DrawRectangle(pen, margin, y, boxW, boxH);

        double lineY = y + Units.MmToPt(6);
        var fKey = NovatlasTheme.FontBold(10);
        var fVal = NovatlasTheme.FontBody(10);

        foreach (var kv in info)
        {
            if (lineY > y + boxH - Units.MmToPt(8)) break;
            g.DrawString(kv.Key + " :", fKey, XBrushes.Black, new XRect(margin + Units.MmToPt(4), lineY, Units.MmToPt(55), Units.MmToPt(6)), XStringFormats.CenterLeft);
            g.DrawString(kv.Value ?? "", fVal, XBrushes.Black, new XRect(margin + Units.MmToPt(62), lineY, boxW - Units.MmToPt(66), Units.MmToPt(6)), XStringFormats.CenterLeft);
            lineY += Units.MmToPt(7);
        }

        // Footer proof
        var fFooter = NovatlasTheme.FontBody(8);
        double fy = pageH - Units.MmToPt(8);
        g.DrawString("NOVATLAS — Nova-Fiches", fFooter, XBrushes.Black,
            new XRect(margin, fy, pageW / 2 - margin, Units.MmToPt(6)), XStringFormats.CenterLeft);
        g.DrawString(buildFooter + "  1/" + doc.Pages.Count, fFooter, XBrushes.Black,
            new XRect(pageW / 2, fy, pageW / 2 - margin, Units.MmToPt(6)), XStringFormats.CenterRight);
    }
}
