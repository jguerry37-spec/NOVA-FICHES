using System;
using System.Globalization;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace NovaFiches.PdfSharpEngine;

public static class LigneReferenceReportRenderer
{
    // Basic A4 metrics (points). Keep deterministic.
    private const double PageW = 595; // A4 portrait width in points
    private const double PageH = 842; // A4 portrait height in points

    private static readonly XColor BrandBlue = XColor.FromArgb(18, 103, 243);
    private static readonly XColor LightGray = XColor.FromArgb(230, 230, 230);
    private static readonly XColor LineGray  = XColor.FromArgb(200, 200, 200);

    private const double MarginL = 36;
    private const double MarginR = 36;
    private const double MarginT = 36;
    private const double MarginB = 36;

    private static PdfPage AddPage(PdfDocument doc) { var p = doc.AddPage(); p.Size = PdfSharp.PageSize.A4; return p; }

    private static string GetString(JsonElement root, params string[] path)
    {
        JsonElement cur = root;
        foreach (var k in path)
        {
            if (cur.ValueKind != JsonValueKind.Object) return "";
            if (!cur.TryGetProperty(k, out cur)) return "";
        }
        return cur.ValueKind switch
        {
            JsonValueKind.String => cur.GetString() ?? "",
            JsonValueKind.Number => cur.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => cur.ToString() ?? ""
        };
    }

    // Fit long strings into a given width (used for IDs that can exceed cell width).
    // Strategy: try shrinking font down to min size, then apply ellipsis.
    private static XFont FitFontToWidth(XGraphics g, XFont baseFont, string text, double maxW, double minSize)
    {
        text ??= "";
        if (maxW <= 0) return baseFont;
        if (g.MeasureString(text, baseFont).Width <= maxW) return baseFont;

        var size = baseFont.Size;
        while (size > minSize)
        {
            size -= 0.4;
            var f = new XFont(baseFont.FontFamily.Name, size, baseFont.Style, baseFont.PdfOptions);
            if (g.MeasureString(text, f).Width <= maxW) return f;
        }
        return new XFont(baseFont.FontFamily.Name, minSize, baseFont.Style, baseFont.PdfOptions);
    }

    private static string EllipsizeToWidth(XGraphics g, XFont font, string text, double maxW)
    {
        text ??= "";
        if (maxW <= 0) return "";
        if (g.MeasureString(text, font).Width <= maxW) return text;

        const string ell = "…";
        // Fast path
        if (g.MeasureString(ell, font).Width > maxW) return "";

        int lo = 0, hi = text.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            var s = text.Substring(0, mid) + ell;
            if (g.MeasureString(s, font).Width <= maxW) lo = mid; else hi = mid - 1;
        }
        return text.Substring(0, lo) + ell;
    }

    private static double? GetDoubleByAny(JsonElement root, IEnumerable<string> candidates)
    {
        foreach (var c in candidates)
        {
            if (TryGetDoubleByPath(root, c, out var v))
                return v;
        }
        return null;
    }

    private static bool TryGetDoubleByPath(JsonElement root, string path, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(path)) return false;

        JsonElement cur = root;
        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (cur.ValueKind != JsonValueKind.Object) return false;
            if (!cur.TryGetProperty(part, out cur)) return false;
        }

        if (cur.ValueKind == JsonValueKind.Number && cur.TryGetDouble(out var d))
        {
            value = d;
            return true;
        }

        if (cur.ValueKind == JsonValueKind.String && double.TryParse(cur.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var ds))
        {
            value = ds;
            return true;
        }

        return false;
    }


    private static void DrawFooter(XGraphics g, PdfPage page, int pageIndex, int totalPages, string buildFooter)
    {
        double yLine = page.Height.Point - Units.MmToPt(14);
        g.DrawLine(new XPen(LineGray, 0.4), MarginL, yLine, page.Width.Point - MarginR, yLine);

        var f = NovatlasTheme.FontBody(9);
        var f2 = NovatlasTheme.FontBody(8);

        string address = NovatlasTheme.NovatlasAddress;
        g.DrawString(address, f, XBrushes.Black,
            new XRect(MarginL, yLine + Units.MmToPt(2.5), page.Width.Point - MarginL - MarginR, Units.MmToPt(5)),
            XStringFormats.Center);

        if (!string.IsNullOrWhiteSpace(buildFooter))
        {
            g.DrawString(buildFooter, f2, XBrushes.Black,
                new XRect(MarginL, yLine + Units.MmToPt(6.5), page.Width.Point - MarginL - MarginR, Units.MmToPt(4.5)),
                XStringFormats.Center);
        }

        g.DrawString($"Page {pageIndex} / {totalPages}", f, XBrushes.Black,
            new XRect(MarginL, yLine + Units.MmToPt(2.5), page.Width.Point - MarginL - MarginR, Units.MmToPt(5)),
            XStringFormats.CenterRight);
    }

    private static double DrawHeader(XGraphics g, PdfPage page, JsonElement root)
    {
        // ===== Top header zone (legacy-like) =====
        // Two bordered boxes: left = logo, right = chantier infos (Ville/Adresse/CHA)
        double y = Units.MmToPt(6); // slight top padding (closer to legacy than MarginT)
        double boxH = Units.MmToPt(22);

        double contentW = page.Width.Point - MarginL - MarginR;
        double gap = Units.MmToPt(6);

        // Legacy proportions: logo box ~ 1/3 of content width
        double leftW = Units.MmToPt(65); // ~65 mm
        if (leftW > contentW * 0.45) leftW = contentW * 0.45;
        double rightW = contentW - leftW - gap;
        if (rightW < Units.MmToPt(60))
        {
            // safety fallback
            leftW = contentW * 0.35;
            rightW = contentW - leftW - gap;
        }

        var penBox = new XPen(XColors.Black, 0.8);

        var rectLeft = new XRect(MarginL, y, leftW, boxH);
        var rectRight = new XRect(MarginL + leftW + gap, y, rightW, boxH);

        g.DrawRectangle(penBox, rectLeft);
        g.DrawRectangle(penBox, rectRight);

        // Logo centered inside left box
        var logo = NovatlasTheme.TryLoadLogo();
        if (logo != null)
        {
            double pad = Units.MmToPt(4);
            double maxW = rectLeft.Width - 2 * pad;
            double maxH = rectLeft.Height - 2 * pad;

            // Keep aspect: assume logo is wider than tall
            double w = maxW;
            double h = maxH;
            try
            {
                double ar = (double)logo.PixelWidth / (double)logo.PixelHeight;
                // fit
                w = maxW;
                h = w / ar;
                if (h > maxH)
                {
                    h = maxH;
                    w = h * ar;
                }
            }
            catch { /* ignore */ }

            double lx = rectLeft.Left + (rectLeft.Width - w) / 2.0;
            double ly = rectLeft.Top + (rectLeft.Height - h) / 2.0;

            g.DrawImage(logo, lx, ly, w, h);
        }

        // Right box content (Ville / Adresse chantier / CHA)
        DrawHeaderRightBox(g, rectRight, root);

        // ===== Title band =====
        double bandY = rectLeft.Bottom + Units.MmToPt(4);
        double bandH = Units.MmToPt(12);

        g.DrawRectangle(new XSolidBrush(BrandBlue), MarginL, bandY, contentW, bandH);
        g.DrawString("RAPPORT D'INTERVENTION", NovatlasTheme.FontBold(12), XBrushes.White,
            new XRect(MarginL, bandY, contentW, bandH), XStringFormats.Center);

        // ===== Intervention name box =====
        double boxY = bandY + bandH + Units.MmToPt(4);
        double nameH = Units.MmToPt(8);
        g.DrawRectangle(new XPen(XColors.Black, 0.8), MarginL, boxY, contentW, nameH);

        // (legacy) no "INTERVENTION" label in this box

        string interName =
            GetString(root, "elements");
        if (string.IsNullOrWhiteSpace(interName))
            interName = GetString(root, "intervention");
        if (string.IsNullOrWhiteSpace(interName))
            interName = GetString(root, "chantier");
        interName = (interName ?? "").ToUpperInvariant();

        g.DrawString(interName, NovatlasTheme.FontBold(11), XBrushes.Black,
            new XRect(MarginL, boxY, contentW, nameH), XStringFormats.Center);

        return boxY + nameH + Units.MmToPt(4);
    }

    private static double DrawRepeatHeaderLogo(XGraphics g, PdfPage page)
    {
        // Continuation pages: small centered logo only (as in legacy PDF)
        double y = Units.MmToPt(6);
        var logo = NovatlasTheme.TryLoadLogo();
        if (logo != null)
        {
            double maxW = Units.MmToPt(28); // ~28 mm
            double maxH = Units.MmToPt(12); // ~12 mm
            double w = maxW, h = maxH;
            try
            {
                double ar = (double)logo.PixelWidth / (double)logo.PixelHeight;
                w = maxW;
                h = w / ar;
                if (h > maxH)
                {
                    h = maxH;
                    w = h * ar;
                }
            }
            catch { /* ignore */ }

            double x = page.Width.Point - MarginR - w;
            g.DrawImage(logo, x, y, w, h);
            y += h + Units.MmToPt(6);
        }
        else
        {
            y = MarginT;
        }
        return y;
    }

    private static void DrawFinalControlsAndSignatures(XGraphics g, PdfPage page, JsonElement root)
    {
                // Bottom-of-report blocks (legacy): CONTROLES + (OBSERVATIONS / REALISÉ PAR / VALIDÉ PAR)
        // Layout is matched to the validated legacy PDF using page-relative ratios (A4).
        double w = page.Width.Point - MarginL - MarginR;

        // A4 reference ratios (from validated PDF):
        // Controls top ~ 73.72% ; controls bottom ~ 79.08%
        // Boxes top ~ 82.44% ; boxes bottom ~ 94.58%
        double yCtlTop   = page.Height.Point * 0.73717218;
        double yCtlBot   = page.Height.Point * 0.79076397;
        double yBoxesTopRef = page.Height.Point * 0.82440137;
        double yBoxesBotRef = page.Height.Point * 0.94583808;

        // Requested tweak: reduce vertical gap between CONTROLES and the 3 bottom blocks.
        // Make it equal to the small horizontal gap (≈ 2mm), while keeping the legacy block height.
        double gapH = Units.MmToPt(2);
        double boxesHRef = yBoxesBotRef - yBoxesTopRef;
        double yBoxesTop = yCtlBot + gapH;
        double yBoxesBot = yBoxesTop + boxesHRef;
double controlesH = yCtlBot - yCtlTop;
        double boxesH     = yBoxesBot - yBoxesTop;
var pen = new XPen(XColors.Black, 0.8);
        var penThin = new XPen(LineGray, 0.6);
        // Use the existing LightGray color for header strips (legacy-style)
        var headBrush = new XSolidBrush(LightGray);

        var fHead = NovatlasTheme.FontBold(10);
        var fSmall = NovatlasTheme.FontBody(9);

        // === CONTROLES (full width) ===
        double y = yCtlTop;

        var rectCtl = new XRect(MarginL, y, w, controlesH);
        double headH = Units.MmToPt(6.5);
        // header strip
        g.DrawRectangle(headBrush, new XRect(rectCtl.Left, rectCtl.Top, rectCtl.Width, headH));
        // outer border (draw last so the fill never masks the ends)
        g.DrawRectangle(pen, rectCtl);
        g.DrawLine(pen, rectCtl.Left, rectCtl.Top + headH, rectCtl.Right, rectCtl.Top + headH);
        g.DrawString("CONTRÔLES", fHead, XBrushes.Black, new XRect(rectCtl.Left, rectCtl.Top, rectCtl.Width, headH), XStringFormats.Center);

        // counts line
        var cc = ControlsCounter.ComputeForLigneRef(root);

        int pointsMesures = cc.PointsMesures, valides = cc.Valides, refuses = cc.Refuses, noneval = cc.NonEval;

        string counts = $"Points mesurés : {pointsMesures}    Valides : {valides}    Refuses : {refuses}    Non eval. : {noneval}";
        g.DrawString(counts, fSmall, XBrushes.Black,
            new XRect(rectCtl.Left + Units.MmToPt(3), rectCtl.Top + headH, rectCtl.Width - Units.MmToPt(6), rectCtl.Height - headH),
            XStringFormats.CenterLeft);

        y = yBoxesTop;

        // === 3 boxes ===
        double boxW = (w - 2 * gapH) / 3.0;
        var rectObs = new XRect(MarginL, y, boxW, boxesH);
        var rectReal = new XRect(MarginL + boxW + gapH, y, boxW, boxesH);
        var rectVal = new XRect(MarginL + 2 * (boxW + gapH), y, boxW, boxesH);

        void DrawBoxHeader(XRect r, string title, string subtitle = "")
        {
            // header fill first
            g.DrawRectangle(headBrush, new XRect(r.Left, r.Top, r.Width, headH));
            // outer border last so the fill never masks the ends
            g.DrawRectangle(pen, r);
            g.DrawLine(pen, r.Left, r.Top + headH, r.Right, r.Top + headH);
            g.DrawString(title, fHead, XBrushes.Black, new XRect(r.Left, r.Top, r.Width, headH), XStringFormats.Center);
            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                g.DrawString(subtitle, NovatlasTheme.FontBody(8), XBrushes.Black, new XRect(r.Left, r.Top + Units.MmToPt(4.2), r.Width, headH), XStringFormats.Center);
            }
        }

        DrawBoxHeader(rectObs, "OBSERVATIONS");
        DrawBoxHeader(rectReal, "RÉALISÉ PAR");
        // (client) must stay inside the header frame → put it on the same line.
        DrawBoxHeader(rectVal, "VALIDÉ PAR ( client)");

        // inner lines for REALISE / VALIDE
        void DrawSignLines(XRect r)
        {
            double yy = r.Top + headH;
            double contentH = (r.Height - headH);
            double row1 = contentH * 0.25;
            double row2 = contentH * 0.25;
            double row3 = contentH - row1 - row2; // ≈ 50%
            g.DrawLine(penThin, r.Left, yy + row1, r.Right, yy + row1);
            g.DrawLine(penThin, r.Left, yy + row1 + row2, r.Right, yy + row1 + row2);

            var labF = NovatlasTheme.FontBody(9);
            g.DrawString("Nom :", labF, XBrushes.Black, new XRect(r.Left + Units.MmToPt(3), yy + Units.MmToPt(2), r.Width - Units.MmToPt(6), row1), XStringFormats.TopLeft);
            g.DrawString("Date :", labF, XBrushes.Black, new XRect(r.Left + Units.MmToPt(3), yy + row1 + Units.MmToPt(2), r.Width - Units.MmToPt(6), row2), XStringFormats.TopLeft);
            g.DrawString("Visa :", labF, XBrushes.Black, new XRect(r.Left + Units.MmToPt(3), yy + row1 + row2 + Units.MmToPt(2), r.Width - Units.MmToPt(6), row3), XStringFormats.TopLeft);
        }
        DrawSignLines(rectReal);
        DrawSignLines(rectVal);

        // Fill "Réalisé par" from UI fields (if provided). Date must be today's date.
        try
        {
            var realName = FirstNonEmpty(
                GetStr(root, "surveyor"),
                GetStr(root, "geometre"),
                GetStr(root, "Geometre"),
                GetStr(root, "Utilisateur"),
                GetStr(root, "user"),
                GetStr(root, "Intervenant"),
                GetStr(root, "intervenant"),
                GetStr(root, "Operateur"),
                GetStr(root, "Opérateur"),
                GetStr(root, "Operator")
            );

            var realDate = DateTime.Now.ToString("dd/MM/yyyy");
            if (!string.IsNullOrWhiteSpace(realName))
            {
                double yy = rectReal.Top + headH;
                double contentH = (rectReal.Height - headH);
                double row1 = contentH * 0.25;
                double row2 = contentH * 0.25;
                var valF = NovatlasTheme.FontBody(9);
                double pad = Units.MmToPt(3);
                double labelW = Units.MmToPt(18);
                g.DrawString(realName, valF, XBrushes.Black,
                    new XRect(rectReal.Left + pad + labelW, yy + Units.MmToPt(2), rectReal.Width - (pad * 2) - labelW, row1),
                    XStringFormats.TopLeft);
                g.DrawString(realDate, valF, XBrushes.Black,
                    new XRect(rectReal.Left + pad + labelW, yy + row1 + Units.MmToPt(2), rectReal.Width - (pad * 2) - labelW, row2),
                    XStringFormats.TopLeft);
            }

            // Visa (signature) — optional, coming from UI import.
            // We accept multiple payload keys for backward compatibility.
            var sig = FirstNonEmpty(
                GetStr(root, "signatureDataUrl"),
                GetStr(root, "sigDataUrl"),
                GetStr(root, "signature"),
                GetStr(root, "sig")
            );
            if (!string.IsNullOrWhiteSpace(sig))
            {
                double yy = rectReal.Top + headH;
                double contentH = (rectReal.Height - headH);
                double row1 = contentH * 0.25;
                double row2 = contentH * 0.25;
                double row3 = contentH - row1 - row2;

                double pad = Units.MmToPt(3);
                double labelW = Units.MmToPt(18);
                var visaRect = new XRect(
                    rectReal.Left + pad + labelW,
                    yy + row1 + row2 + Units.MmToPt(2),
                    rectReal.Width - (pad * 2) - labelW,
                    row3 - Units.MmToPt(4));
                PdfImageHelper.DrawDataUrlImage(g, sig, visaRect);
            }
        }
        catch { /* ignore */ }

        // Observations text (from UI fields)
        try
        {
            string obs = FirstNonEmpty(
                GetStr(root, "obs"),
                GetStr(root, "observations"),
                GetStr(root, "observation"),
                GetStr(root, "Observations"),
                GetStr(root, "Observation")
            );
            if (!string.IsNullOrWhiteSpace(obs))
            {
                var rectTxt = new XRect(rectObs.Left + Units.MmToPt(3), rectObs.Top + headH + Units.MmToPt(2), rectObs.Width - Units.MmToPt(6), rectObs.Height - headH - Units.MmToPt(4));
                DrawWrappedText(g, obs, NovatlasTheme.FontBody(9), XBrushes.Black, rectTxt, 8);
            }
        }
        catch { /* ignore */ }
    }


    private static double DrawInfoCartouche(XGraphics g, PdfPage page, double y, JsonElement root)
{
    // Cartouche geometry (aligned with validated IMP tuning / user red-line reference)
    // We keep the left column ratio stable (0.295), and tune the right-side separators:
    //  - Row1 (Entreprise/Contact) and Row3 (Appareil/Intervenant): x2Common ≈ 0.676 * w
    //  - Row2 (PPM/Plan): keep functional split (PPM narrower) -> 30% of right-side width
    double w = page.Width.Point - MarginL - MarginR;
    double rowH = Units.MmToPt(14);

    double x0 = MarginL;
    double xR = x0 + w;

    double x1 = x0 + w * 0.295;
    double rightW = xR - x1;

    double x2Common = x0 + w * 0.676;
    double x2Row2 = x1 + rightW * 0.30;
    double x2Row1 = x2Common;
    double x2Row3 = x2Common;

    var pen = new XPen(XColors.Black, 0.8);
    var fLabel = NovatlasTheme.FontBody(10.0);
    var fValue = NovatlasTheme.FontBold(11.0);

    void Cell(double xL, double yy, double xRight, string label, string value)
    {
        double ww = xRight - xL;
        g.DrawRectangle(pen, xL, yy, ww, rowH);
        g.DrawString(label, fLabel, XBrushes.Black,
            new XRect(xL, yy + Units.MmToPt(1.0), ww, Units.MmToPt(6.0)),
            XStringFormats.TopCenter);
        if (!string.IsNullOrWhiteSpace(value))
        {
            var rectVal = new XRect(xL, yy + Units.MmToPt(6.0), ww, rowH - Units.MmToPt(7.0));

            // Plan de référence can be very long: wrap + shrink + ellipsis as last resort.
            if (!string.IsNullOrWhiteSpace(label) && label.StartsWith("Plan de", StringComparison.OrdinalIgnoreCase))
            {
                TextFitHelper.DrawCenteredWrapped(
                    g,
                    rectVal,
                    value,
                    size => NovatlasTheme.FontBold(size),
                    startFontSize: 11.0,
                    minFontSize: 7.5,
                    maxLines: 2);
            }
            else
            {
                g.DrawString(value, fValue, XBrushes.Black, rectVal, XStringFormats.Center);
            }
        }
    }

    string date = GetString(root, "date");
    if (string.IsNullOrWhiteSpace(date)) date = GetString(root, "interventionDate");
    string entreprise = GetString(root, "entreprise");
    string contact = GetString(root, "contactClient");
    string coordSys = GetString(root, "systemeCoord");
    string ppm = GetString(root, "ppm");
    if (!string.IsNullOrWhiteSpace(ppm) && !ppm.Contains("mm/km", StringComparison.OrdinalIgnoreCase))
        ppm = ppm.Trim() + " mm/km";
    string planRef = GetString(root, "planRef");
    string altSys = GetString(root, "systemeAlti");

    // Appareil: show model + serialNumber when available (same rule as IMP)
    string appareilModel = GetString(root, "appareil");
    if (string.IsNullOrWhiteSpace(appareilModel)) appareilModel = GetString(root, "appareilModel");
    if (string.IsNullOrWhiteSpace(appareilModel)) appareilModel = GetString(root, "model");

    string appareilSerial = GetString(root, "serialNumber");
    if (string.IsNullOrWhiteSpace(appareilSerial)) appareilSerial = GetString(root, "appareilSerial");
    if (string.IsNullOrWhiteSpace(appareilSerial)) appareilSerial = GetString(root, "serial");
    if (string.IsNullOrWhiteSpace(appareilSerial)) appareilSerial = GetString(root, "numeroSerie");
    if (string.IsNullOrWhiteSpace(appareilSerial)) appareilSerial = GetString(root, "numero_série");

    string appareil = appareilModel;
    if (!string.IsNullOrWhiteSpace(appareilSerial))
    {
        if (!string.IsNullOrWhiteSpace(appareil)) appareil = $"{appareil} – {appareilSerial}";
        else appareil = appareilSerial;
    }
    string intervenant = GetString(root, "intervenant");
    if (string.IsNullOrWhiteSpace(intervenant)) intervenant = GetString(root, "operator");
    if (string.IsNullOrWhiteSpace(intervenant)) intervenant = GetString(root, "operateur");

    // Row 1
    Cell(x0, y, x1, "Intervention du", date);
    Cell(x1, y, x2Row1, "Entreprise", entreprise);
    Cell(x2Row1, y, xR, "Contact client", contact);
    y += rowH;

    // Row 2
    Cell(x0, y, x1, "Système de coordonnées", coordSys);
    Cell(x1, y, x2Row2, "PPM", ppm);
    Cell(x2Row2, y, xR, "Plan de référence", planRef);
    y += rowH;

    // Row 3
    Cell(x0, y, x1, "Système altimétrique", altSys);
    Cell(x1, y, x2Row3, "Appareil", appareil);
    Cell(x2Row3, y, xR, "Intervenant", intervenant);
    y += rowH + Units.MmToPt(4);

    return y;
}

private static double GetRefAltiSectionHeight(JsonElement root)
{
    if (!root.TryGetProperty("refAltiPoints", out var arr) || arr.ValueKind != JsonValueKind.Array)
        return 0;

    int count = 0;
    foreach (var it in arr.EnumerateArray())
        if (it.ValueKind == JsonValueKind.Object) count++;
    if (count == 0) return 0;

    double headH = Units.MmToPt(7);
    double rowH = Units.MmToPt(6.5);
    // DrawBar height + gap + table + bottom gap
    return Units.MmToPt(7) + Units.MmToPt(2) + headH + count * rowH + Units.MmToPt(4);
}

private static double DrawRefAltiSection(XGraphics g, PdfPage page, double y, JsonElement root)
{
    if (!root.TryGetProperty("refAltiPoints", out var arr) || arr.ValueKind != JsonValueKind.Array)
        return y;

    var pts = new List<JsonElement>();
    foreach (var it in arr.EnumerateArray())
        if (it.ValueKind == JsonValueKind.Object) pts.Add(it);
    if (pts.Count == 0) return y;

    double w = page.Width.Point - MarginL - MarginR;
    var pen = new XPen(XColors.Black, 0.6);
    var fLabel = NovatlasTheme.FontBold(9);
    var fVal = NovatlasTheme.FontBody(8.5);
    double headH = Units.MmToPt(7);
    double rowH = Units.MmToPt(6.5);

    y = DrawBar(g, page, y, "RÉFÉRENCE ALTIMÉTRIQUE", LightGray);

    double[] cols = { 0.28, 0.24, 0.24, 0.24 };
    double[] xs = new double[cols.Length + 1];
    xs[0] = MarginL;
    for (int i = 0; i < cols.Length; i++) xs[i + 1] = xs[i] + w * cols[i];
    double totalH = headH + pts.Count * rowH;
    g.DrawRectangle(pen, MarginL, y, w, totalH);
    for (int i = 1; i < xs.Length - 1; i++) g.DrawLine(pen, xs[i], y, xs[i], y + totalH);
    g.DrawLine(pen, MarginL, y + headH, MarginL + w, y + headH);

    string[] headers = { "ID point", "X", "Y", "Z" };
    for (int i = 0; i < headers.Length; i++)
        g.DrawString(headers[i], fLabel, XBrushes.Black, new XRect(xs[i], y, xs[i + 1] - xs[i], headH), XStringFormats.Center);

    for (int r = 0; r < pts.Count; r++)
    {
        var p = pts[r];
        double yy = y + headH + r * rowH;
        if (r > 0) g.DrawLine(pen, MarginL, yy, MarginL + w, yy);
        var vals = new[] { StripAtId(GetString(p, "id")), Format3Decimals(GetString(p, "E")), Format3Decimals(GetString(p, "N")), Format3Decimals(GetString(p, "H")) };
        for (int i = 0; i < vals.Length; i++)
            g.DrawString(vals[i] ?? "", fVal, XBrushes.Black, new XRect(xs[i] + 2, yy, xs[i + 1] - xs[i] - 4, rowH), XStringFormats.CenterLeft);
    }

    return y + totalH + Units.MmToPt(4);
}

    
    


private static double DrawBar(
        XGraphics g,
        PdfPage page,
        double y,
        string title,
        XColor? fill = null,
        XColor? border = null,
        double borderWidth = 0.8)
    {
        double h = Units.MmToPt(7);
        var brush = new XSolidBrush(fill ?? LightGray);
        g.DrawRectangle(brush, MarginL, y, page.Width.Point - MarginL - MarginR, h);
        var bPen = new XPen(border ?? XColors.Black, borderWidth);
        g.DrawRectangle(bPen, MarginL, y, page.Width.Point - MarginL - MarginR, h);
        g.DrawString(title, NovatlasTheme.FontBold(9.5), XBrushes.Black,
            new XRect(MarginL, y, page.Width.Point - MarginL - MarginR, h), XStringFormats.Center);
        return y + h + Units.MmToPt(2);
    }

    private static string Fmt(JsonElement el, params string[] path)
    {
        var s = GetString(el, path);
        return string.IsNullOrWhiteSpace(s) ? "" : s;
    }

    private static string FmtNum(JsonElement el, params string[] path)
    {
        var s = GetString(el, path);
        if (string.IsNullOrWhiteSpace(s)) return "";
        return Format3Decimals(s);
    }

    // Constante prisme: conserver 4 décimales (ex: 0.017500 -> 0.0175)
    private static string FmtPrismConst(JsonElement el, params string[] path)
    {
        var s = GetString(el, path);
        if (string.IsNullOrWhiteSpace(s)) return "";
        return Format4Decimals(s);
    }

    // Display helper: remove Leica "@NN" suffixes so IDs match between sections
    private static string StripAtId(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var at = s.IndexOf('@');
        return (at > 0) ? s.Substring(0, at) : s;
    }


    // Format any numeric value to 3 decimals, keeping the original decimal separator when possible.
    private static string Format3Decimals(string s)
    {
        var raw = (s ?? "").Trim();
        if (raw.Length == 0) return "";

        bool hadComma = raw.Contains(',');
        var norm = raw.Replace(',', '.');
        if (!double.TryParse(norm, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v))
            return raw;

        var outStr = v.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
        return hadComma ? outStr.Replace('.', ',') : outStr;
    }

    // Format any numeric value to 4 decimals (used for prism constant).
    private static string Format4Decimals(string s)
    {
        var raw = (s ?? "").Trim();
        if (raw.Length == 0) return "";

        bool hadComma = raw.Contains(',');
        var norm = raw.Replace(',', '.');
        if (!double.TryParse(norm, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v))
            return raw;

        var outStr = v.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);
        return hadComma ? outStr.Replace('.', ',') : outStr;
    }

    private static string FmtD(double? v)
    {
        if (!v.HasValue) return "";
        return v.Value.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void EnsurePage(PdfDocument doc, ref PdfPage page, ref XGraphics g, ref double y, double needH)
    {
        // Keep a safe zone for footer (line + 2 text rows)
        double footerSafe = LayoutConstants.FooterReservePt;
        if (y + needH <= page.Height.Point - MarginB - footerSafe) return;

        g.Dispose();
        page = AddPage(doc);
        g = XGraphics.FromPdfPage(page);
        // Repeat header (small centered logo) on continuation pages
        y = DrawRepeatHeaderLogo(g, page);
    }

    private static double DrawTextLine(XGraphics g, PdfPage page, double y, string text)
    {
        var f = NovatlasTheme.FontBody(8.2);
        g.DrawString(text, f, XBrushes.Black,
            new XRect(MarginL, y, page.Width.Point - MarginL - MarginR, Units.MmToPt(5)),
            XStringFormats.CenterLeft);
        return y + Units.MmToPt(5.2);
    }
    private static void DrawSimpleTable(PdfDocument doc, ref PdfPage page, ref XGraphics g, ref double y,
        string? title, string[] header, List<string[]> rows, double rowHeight)
    {
        if (header == null || header.Length == 0) return;
        rows ??= new();

        const double headerHeight = 16;
        const double gap = 6;
        double x = MarginL;
        double tableW = page.Width.Point - MarginL - MarginR;
        double pageBottom = page.Height.Point - MarginB - LayoutConstants.FooterReservePt;

        var pen = new XPen(NovatlasTheme.Black, 0.6);
        var headerBrush = new XSolidBrush(NovatlasTheme.LightGrey);
        var titleFont = NovatlasTheme.FontSectionTitle;
        var headFont = NovatlasTheme.FontBodyBold(9);
        var cellFont = NovatlasTheme.FontBody(8.5);

	    // PdfSharp 6.1: GetHeight(XGraphics) is obsolete, use parameterless GetHeight().
	    double neededForTitle = title != null ? (titleFont.GetHeight() + gap) : 0;
        if (y + neededForTitle + headerHeight + rowHeight > pageBottom)
        {
            // IMPORTANT: dispose the current XGraphics before creating a new one.
            // Otherwise PDFsharp can throw: "An XGraphics object already exists for this page..."
            g.Dispose();
            page = doc.AddPage();
            g = XGraphics.FromPdfPage(page);
            y = DrawRepeatHeaderLogo(g, page);
            pageBottom = page.Height.Point - MarginB - LayoutConstants.FooterReservePt;
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            g.DrawString(title, titleFont, XBrushes.Black, new XRect(x, y, tableW, 20), XStringFormats.TopLeft);
	        y += titleFont.GetHeight() + gap;
        }

        int cols = header.Length;
        double colW = tableW / cols;

        // Header row (drawn initially, and again after each page break)
        g.DrawRectangle(headerBrush, x, y, tableW, headerHeight);
        g.DrawRectangle(pen, x, y, tableW, headerHeight);
        for (int c = 0; c < cols; c++)
        {
            if (c > 0) g.DrawLine(pen, x + c * colW, y, x + c * colW, y + headerHeight);
            g.DrawString(header[c] ?? "", headFont, XBrushes.Black,
                new XRect(x + c * colW + 2, y + 2, colW - 4, headerHeight - 4), XStringFormats.TopLeft);
        }
        y += headerHeight;

        foreach (var row in rows)
        {
            if (y + rowHeight > pageBottom)
            {
                g.Dispose();
                page = doc.AddPage();
                g = XGraphics.FromPdfPage(page);
                y = DrawRepeatHeaderLogo(g, page);
                pageBottom = page.Height.Point - MarginB - LayoutConstants.FooterReservePt;
                // redraw header on new page
                g.DrawRectangle(headerBrush, x, y, tableW, headerHeight);
                g.DrawRectangle(pen, x, y, tableW, headerHeight);
                for (int c = 0; c < cols; c++)
                {
                    if (c > 0) g.DrawLine(pen, x + c * colW, y, x + c * colW, y + headerHeight);
                    g.DrawString(header[c], headFont, XBrushes.White,
                        new XRect(x + c * colW + 2, y, colW - 4, headerHeight),
                        XStringFormats.Center);
                }
                y += headerHeight;
            }

            g.DrawRectangle(pen, x, y, tableW, rowHeight);
            for (int c = 0; c < cols; c++)
            {
                if (c > 0) g.DrawLine(pen, x + c * colW, y, x + c * colW, y + rowHeight);
                string cell = (row != null && c < row.Length) ? (row[c] ?? "") : "";
                var rectCell = new XRect(x + c * colW + 2, y + 2, colW - 4, rowHeight - 4);
                if (c == 0)
                {
                    XFont f = cellFont;
                    double fs = cellFont.Size;
                    for (int k = 0; k < 8; k++)
                    {
                        f = new XFont("Arial", fs, cellFont.Style);
                        if (g.MeasureString(cell, f).Width <= rectCell.Width) break;
                        fs = Math.Max(6.0, fs - 0.75);
                    }
                    g.DrawString(cell, f, XBrushes.Black, rectCell, XStringFormats.TopLeft);
                }
                else
                {
                    g.DrawString(cell, cellFont, XBrushes.Black, rectCell, XStringFormats.TopLeft);
                }
            }
            y += rowHeight;
        }

        y += gap;
    }


    private static void DrawLigneRefTableInline(
        PdfDocument doc,
        ref PdfPage page,
        ref XGraphics g,
        ref double y,
        string[] header,
        List<JsonElement> points,
        TableRenderer.TableLayout layout)
    {

        // Expand each rabPoint into 3 visual rows (ID / Point théorique / Delta ligne) like legacy PDF.
        var rows = new List<string[]>();
        foreach (var p in points)
        {
            string id = StripAtId(GetString(p, "id")) ?? GetString(p, "ID") ?? GetString(p, "name") ?? "";

            double? xCalc = GetDoubleByAny(p, new[] { "calc.E", "calc.X", "E_calc", "X_calc", "Xcalc", "xCalc", "x_calc" });
            double? yCalc = GetDoubleByAny(p, new[] { "calc.N", "calc.Y", "N_calc", "Y_calc", "Ycalc", "yCalc", "y_calc" });
            double? zCalc = GetDoubleByAny(p, new[] { "calc.H", "calc.Z", "H_calc", "Z_calc", "Zcalc", "zCalc", "z_calc" });

            double? xMes = GetDoubleByAny(p, new[] { "mes.E", "mes.X", "E_mes", "X_mes", "Xmes", "xMes", "x_mes" });
            double? yMes = GetDoubleByAny(p, new[] { "mes.N", "mes.Y", "N_mes", "Y_mes", "Ymes", "yMes", "y_mes" });
            double? zMes = GetDoubleByAny(p, new[] { "mes.H", "mes.Z", "H_mes", "Z_mes", "Zmes", "zMes", "z_mes" });

            double? dx = GetDoubleByAny(p, new[] { "d.dx", "d.dX", "dx", "dX", "Dx" });
            double? dy = GetDoubleByAny(p, new[] { "d.dy", "d.dY", "dy", "dY", "Dy" });
            double? dz = GetDoubleByAny(p, new[] { "d.dz", "d.dZ", "dz", "dZ", "Dz" });

            // Delta-ligne values are stored as ec.dL / ec.dT / ec.dA in the LandXML parser output.
            // Keep compatibility with older payload variants too.
            double? dL = GetDoubleByAny(p, new[] { "ec.dL", "dL", "DL", "d.l", "d.dL" });
            double? dT = GetDoubleByAny(p, new[] { "ec.dT", "dT", "DT", "d.t", "d.dT" });
            double? dA = GetDoubleByAny(p, new[] { "ec.dA", "dA", "DA", "d.a", "d.dA" });

            // Row 1: ID only
            rows.Add(new[] { id, "", "", "", "", "", "", "", "", "", "" });

            // Row 2: Point théorique + coords + dx/dy/dz
            rows.Add(new[]
            {
                "Pt théo",
                FmtD(xCalc), FmtD(yCalc), FmtD(zCalc),
                FmtD(xMes), FmtD(yMes), FmtD(zMes),
                FmtD(dx), FmtD(dy), FmtD(dz),
                ""
            });

            // Row 3: Delta ligne, dL/dT/dA placed in last three columns
            rows.Add(new[]
            {
                "Δ ligne",
                "", "", "",
                "", "", "",
                FmtD(dL), FmtD(dT), FmtD(dA),
                ""
            });
        }


        const double headerH = 18;
        const double rowH = 16;

        double w = page.Width.Point - MarginL - MarginR;
        int colCount = Math.Max(1, header.Length);

        // Column widths from layout (may be based on slightly different margins) -> normalize to current width.
        var colW = (layout.ColumnWidths != null && layout.ColumnWidths.Length == colCount)
            ? layout.ColumnWidths.ToArray()
            : new double[colCount];

        if (layout.ColumnWidths == null || layout.ColumnWidths.Length != colCount)
        {
            for (int i = 0; i < colCount; i++) colW[i] = 1.0;
        }
        double sumW = colW.Sum();
        if (sumW <= 0.0001) sumW = 1.0;
        for (int i = 0; i < colCount; i++) colW[i] = (colW[i] / sumW) * w;

        // Thick separators (indices in [0..colCount])
        var thick = (layout.ThickVerticalSeparators != null && layout.ThickVerticalSeparators.Count > 0)
            ? layout.ThickVerticalSeparators
            : new List<int> { 0, colCount };

        var thinPen = new XPen(XColors.Black, 0.6);
        var thickPen = new XPen(XColors.Black, 1.2);
        var headBrush = NovatlasTheme.HeaderFillBrush();
        var fH = NovatlasTheme.TableHeaderFont();
        var fC = NovatlasTheme.TableCellFont();

        // Do not capture ref parameter 'g' in local functions (C# forbids it).
        // Use a local variable that we refresh after each EnsurePage() call.
        XGraphics gfx = g;

        void DrawHeaderRow(double yy)
        {
            double xx = MarginL;
            for (int c = 0; c < colCount; c++)
            {
                var rect = new XRect(xx, yy, colW[c], headerH);
                gfx.DrawRectangle(headBrush, rect);
                gfx.DrawString(header[c] ?? "", fH, XBrushes.Black, rect, XStringFormats.Center);
                xx += colW[c];
            }
        }

        // Draw a single body row.
        // Applies:
        //  - alternating group shading (3 visual rows per point)
        //  - text fitting + ellipsis
        //  - clipping inside cell rect to avoid overflows
        void DrawRow(double yy, string[] row, int globalRowIndex)
        {
            // Legacy look: alternate a light gray background per point (3 rows).
            // Point group index = floor(rowIndex / 3) because we expand each point into 3 rows.
            int groupIndex = Math.Max(0, globalRowIndex) / 3;
            bool shade = (groupIndex % 2) == 1;
            if (shade)
            {
                var shadeBrush = new XSolidBrush(XColor.FromArgb(240, 240, 240));
                gfx.DrawRectangle(shadeBrush, new XRect(MarginL, yy, w, rowH));
            }

            double xx = MarginL;
            for (int c = 0; c < colCount; c++)
            {
                string txt = c < row.Length ? (row[c] ?? "") : "";
                var rect = new XRect(xx, yy, colW[c], rowH);

                // Content rect with padding
                var padX = Units.MmToPt(1);
                var padY = Units.MmToPt(0.6);
                var content = new XRect(rect.X + padX, rect.Y + padY, Math.Max(0, rect.Width - 2 * padX), Math.Max(0, rect.Height - 2 * padY));

                // Font: keep default for values, italic for the left labels.
                bool isLeftLabel = (c == 0) && (txt.Equals("Pt théo", StringComparison.OrdinalIgnoreCase) || txt.Equals("Δ ligne", StringComparison.OrdinalIgnoreCase));
                var font = isLeftLabel ? new XFont("Arial", 8.0, XFontStyleEx.Italic) : fC;

                // Fit + ellipsis
                var fittedFont = FitFontToWidth(gfx, font, txt, content.Width, 6.6);
                var finalTxt = (gfx.MeasureString(txt, fittedFont).Width <= content.Width)
                    ? txt
                    : EllipsizeToWidth(gfx, fittedFont, txt, content.Width);

                // Clip to prevent any overflow outside the cell.
                var state = gfx.Save();
                gfx.IntersectClip(rect);

                var fmt = (c == 0)
                    ? (isLeftLabel ? XStringFormats.CenterLeft : XStringFormats.Center)
                    : XStringFormats.Center;

                gfx.DrawString(finalTxt, fittedFont, XBrushes.Black, content, fmt);
                gfx.Restore(state);

                xx += colW[c];
            }
        }

        void DrawGrid(double y0, double gridH, int rowCount)
        {
            // outer border + thin verticals
            double x0 = MarginL;
            gfx.DrawRectangle(thinPen, x0, y0, w, gridH);
            double xx = x0;
            for (int c = 0; c < colCount; c++)
            {
                xx += colW[c];
                gfx.DrawLine(thinPen, xx, y0, xx, y0 + gridH);
            }

            // header separator
            gfx.DrawLine(thinPen, x0, y0 + headerH, x0 + w, y0 + headerH);

            // row separators
            double yy = y0 + headerH;
            for (int r = 0; r < rowCount; r++)
            {
                yy += rowH;
                gfx.DrawLine(thinPen, x0, yy, x0 + w, yy);
            }

            // thick separators
            if (thick.Count > 0)
            {
                double x = x0;
                for (int i = 0; i <= colCount; i++)
                {
                    if (thick.Contains(i))
                        gfx.DrawLine(thickPen, x, y0, x, y0 + gridH);
                    if (i < colCount) x += colW[i];
                }
            }
        }

        int rowIndex = 0;
        while (rowIndex < rows.Count)
        {
            // How many rows fit on current page?
            double footerSafe = LayoutConstants.FooterReservePt;
            double avail = page.Height.Point - MarginB - footerSafe - y;
            int rowsOnPage = (int)Math.Floor((avail - headerH) / rowH);
            if (rowsOnPage < 1)
            {
                // new page
                EnsurePage(doc, ref page, ref g, ref y, headerH + rowH + Units.MmToPt(10));
                gfx = g;
                continue;
            }

            int take = Math.Min(rowsOnPage, rows.Count - rowIndex);
            double y0 = y;
            DrawHeaderRow(y0);
            double yy = y0 + headerH;
            for (int r = 0; r < take; r++)
            {
                DrawRow(yy, rows[rowIndex + r], rowIndex + r);
                yy += rowH;
            }
            double gridH = headerH + take * rowH;

            // draw grid on top (so borders are consistent)
            DrawGrid(y0, gridH, take);

            y = y0 + gridH + Units.MmToPt(2);
            rowIndex += take;
        }
    }

    private static double DrawZoneHeaderInline(PdfDocument doc, ref PdfPage page, ref XGraphics g, ref double y, string label)
    {
        EnsurePage(doc, ref page, ref g, ref y, Units.MmToPt(14));
        double h = Units.MmToPt(7.0);
        double w = page.Width.Point - MarginL - MarginR;
        var fill = new XSolidBrush(XColor.FromArgb(235, 235, 235));
        var pen = new XPen(XColors.Black, 0.8);
        var rect = new XRect(MarginL, y, w, h);
        g.DrawRectangle(fill, rect);
        g.DrawRectangle(pen, rect);
        g.DrawString($"{label}", NovatlasTheme.FontBold(9.2), XBrushes.Black,
            new XRect(MarginL + Units.MmToPt(2), y, w - Units.MmToPt(4), h), XStringFormats.CenterLeft);
        return y + h + Units.MmToPt(1.5);
    }

    public static void Render(PdfDocument doc, string payloadJson, string buildFooter)
    {
        using var jd = JsonDocument.Parse(payloadJson);
        var root = jd.RootElement;

        PdfPage page = AddPage(doc);
        var g = XGraphics.FromPdfPage(page);

        try
        {
            double y = DrawHeader(g, page, root);
            y = DrawInfoCartouche(g, page, y, root);


            // Resolve station runs (preferred), else fallback to stationLibre
            var runs = new List<JsonElement>();
            if (root.TryGetProperty("stationLibreRuns", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var it in arr.EnumerateArray())
                {
                    if (it.ValueKind == JsonValueKind.Object) runs.Add(it);
                }
            }
            if (runs.Count == 0 && root.TryGetProperty("stationLibre", out var st) && st.ValueKind == JsonValueKind.Object)
                runs.Add(st);

            // Prepare ligne reference points grouped by stationId (provided by JS as lastData.ligneRef[].rabPoints).
            var lrByStation = new Dictionary<string, List<JsonElement>>();
            var lrZonesByStation = new Dictionary<string, List<(string label, List<JsonElement> points)>>();
            if (root.TryGetProperty("ligneRef", out var lrArr) && lrArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var lrIt in lrArr.EnumerateArray())
                {
                    if (lrIt.ValueKind != JsonValueKind.Object) continue;
                    var sid = GetString(lrIt, "stationId") ?? "";
                    if (!lrByStation.TryGetValue(sid, out var list))
                    {
                        list = new List<JsonElement>();
                        lrByStation[sid] = list;
                    }

                    if (lrIt.TryGetProperty("rabPoints", out var ptsEl) && ptsEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var pEl in ptsEl.EnumerateArray())
                        {
                            if (pEl.ValueKind == JsonValueKind.Object)
                                list.Add(pEl);
                        }
                    }
                }
            }

            if (root.TryGetProperty("ligneRefRowsByStation", out var lrzArr) && lrzArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var it in lrzArr.EnumerateArray())
                {
                    if (it.ValueKind != JsonValueKind.Object) continue;
                    var sid = GetString(it, "stationId") ?? "";
                    var zones = new List<(string label, List<JsonElement> points)>();
                    if (it.TryGetProperty("zoneGroups", out var zgEl) && zgEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var zEl in zgEl.EnumerateArray())
                        {
                            if (zEl.ValueKind != JsonValueKind.Object) continue;
                            var label = GetString(zEl, "label") ?? GetString(zEl, "code") ?? "";
                            var pts = new List<JsonElement>();
                            if (zEl.TryGetProperty("points", out var ptsEl) && ptsEl.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var pEl in ptsEl.EnumerateArray())
                                {
                                    if (pEl.ValueKind == JsonValueKind.Object) pts.Add(pEl);
                                }
                            }
                            zones.Add((label, pts));
                        }
                    }
                    lrZonesByStation[sid] = zones;
                }
            }

            var groupByZone = root.TryGetProperty("groupByZone", out var gbz) && gbz.ValueKind == JsonValueKind.True;

            // Keep only station runs that actually carry ligne de référence rows for this report.
            bool RunHasLigneRefRows(JsonElement runEl)
            {
                string sidLocal = Fmt(runEl, "results", "idStation");
                if (string.IsNullOrWhiteSpace(sidLocal)) sidLocal = Fmt(runEl, "idStation");
                sidLocal = sidLocal ?? "";

                if (lrByStation.TryGetValue(sidLocal, out var directPts) && directPts != null && directPts.Count > 0)
                    return true;

                if (runs.Count == 1 && lrByStation.TryGetValue("", out var emptyPts) && emptyPts != null && emptyPts.Count > 0)
                    return true;

                if (groupByZone && lrZonesByStation.TryGetValue(sidLocal, out var zoneGroups) && zoneGroups != null)
                {
                    foreach (var zg in zoneGroups)
                    {
                        if (zg.points != null && zg.points.Count > 0) return true;
                    }
                }

                return false;
            }

            runs = runs.Where(RunHasLigneRefRows).ToList();

// Render each station block (TYPE DE STATION + OBSERVATIONS + RÉSIDUS + IMPLANTATION)
            foreach (var run in runs)
            {
                EnsurePage(doc, ref page, ref g, ref y, Units.MmToPt(60));
                // Validated palette: light orange fill + strong orange border
                var stationBorder = XColor.FromArgb(255, 89, 22);
                var stationFill = stationBorder;
                y = DrawBar(g, page, y, "TYPE DE STATION", stationFill, stationBorder, borderWidth: 1.4);

                var pen = new XPen(XColors.Black, 0.6);
                double w = page.Width.Point - MarginL - MarginR;
                double boxH = Units.MmToPt(28);
                g.DrawRectangle(pen, MarginL, y, w, boxH);

                var f = NovatlasTheme.FontBody(8.2);
                double yy = y + Units.MmToPt(2);

                // Station key (for grouping) + display name (for PDF)
                string id = Fmt(run, "results", "idStation");
                if (string.IsNullOrWhiteSpace(id)) id = Fmt(run, "idStation");
                string stationLabel = Fmt(run, "stationName");
                if (string.IsNullOrWhiteSpace(stationLabel) || stationLabel.StartsWith("TPSSetupID_", StringComparison.OrdinalIgnoreCase)) stationLabel = Fmt(run, "results", "stationName");
                if (string.IsNullOrWhiteSpace(stationLabel) || stationLabel.StartsWith("TPSSetupID_", StringComparison.OrdinalIgnoreCase)) stationLabel = id;

                // Coords (E/N/H)
                string E = FmtNum(run, "results", "E");
                string N = FmtNum(run, "results", "N");
                string H = FmtNum(run, "results", "H");

                // Orientation / deviations (best-effort)
                string corrOrient = FmtNum(run, "results", "corrOrient");
                string devE = FmtNum(run, "results", "devE");
                string devN = FmtNum(run, "results", "devN");
                string devH = FmtNum(run, "results", "devH");
                string devOri = FmtNum(run, "results", "devOri");

                g.DrawString($"Méthode : Station libre", f, XBrushes.Black,
                    new XRect(MarginL + Units.MmToPt(2), yy, w - Units.MmToPt(4), Units.MmToPt(4.8)), XStringFormats.CenterLeft);
                yy += Units.MmToPt(4.8);

                g.DrawString($"Station : {stationLabel}", f, XBrushes.Black,
                    new XRect(MarginL + Units.MmToPt(2), yy, w - Units.MmToPt(4), Units.MmToPt(4.8)), XStringFormats.CenterLeft);
                yy += Units.MmToPt(4.8);

                g.DrawString($"Coordonnées : E={E}  N={N}  H={H}", f, XBrushes.Black,
                    new XRect(MarginL + Units.MmToPt(2), yy, w - Units.MmToPt(4), Units.MmToPt(4.8)), XStringFormats.CenterLeft);
                yy += Units.MmToPt(4.8);

                // Legacy-like line template (best-effort)
                string azOrient = FmtNum(run, "results", "azOrient");
                string factScale = FmtNum(run, "results", "factScale");
                var line1 = $"Corr. orientat° {corrOrient}     |     Fact. échelle {factScale}     |     Dev.std E/N/H {devE} / {devN} / {devH}     |     Ori. {azOrient}";
                var line2 = $"Orientation : CorrOri={corrOrient}  AzOri={azOrient}";
                g.DrawString(line1, f, XBrushes.Black,
                    new XRect(MarginL + Units.MmToPt(2), yy, w - Units.MmToPt(4), Units.MmToPt(4.8)), XStringFormats.CenterLeft);
                yy += Units.MmToPt(4.8);
                g.DrawString(line2, f, XBrushes.Black,
                    new XRect(MarginL + Units.MmToPt(2), yy, w - Units.MmToPt(4), Units.MmToPt(4.8)), XStringFormats.CenterLeft);

                y += boxH + Units.MmToPt(3);

                // OBSERVATIONS table
                EnsurePage(doc, ref page, ref g, ref y, Units.MmToPt(30));
                y = DrawBar(g, page, y, "OBSERVATIONS", LightGray);

                var obsRows = new List<string[]>();
                if (run.TryGetProperty("observations", out var obs) && obs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var o in obs.EnumerateArray())
                    {
                        if (o.ValueKind != JsonValueKind.Object) continue;
                        obsRows.Add(new[]
                        {
                            StripAtId(Fmt(o, "id")),
                            FmtNum(o, "hz"),
                            FmtNum(o, "vz"),
                            FmtNum(o, "dp"),
                            FmtNum(o, "hr"),
                            FmtPrismConst(o, "constPrisme")
                        });
                    }
                }

                if (obsRows.Count == 0)
                {
                    y = DrawTextLine(g, page, y, "Aucune observation.");
                }
                else
                {
                    DrawSimpleTable(doc, ref page, ref g, ref y,
                        null,
                        new[] { "ID", "Hz", "Vz", "Dp", "Hr", "Const prisme" },
                        obsRows,
                        Units.MmToPt(6.5));
                    y += Units.MmToPt(2);
                }

                // RÉSIDUS table
                EnsurePage(doc, ref page, ref g, ref y, Units.MmToPt(30));
                y = DrawBar(g, page, y, "RÉSIDUS", LightGray);

                var resRows = new List<string[]>();
                if (run.TryGetProperty("residuals", out var res) && res.ValueKind == JsonValueKind.Array)
                {
                    foreach (var r in res.EnumerateArray())
                    {
                        if (r.ValueKind != JsonValueKind.Object) continue;
                        resRows.Add(new[]
                        {
                            StripAtId(Fmt(r, "id")),
                            FmtNum(r, "dHz"),
                            FmtNum(r, "dAlti"),
                            FmtNum(r, "dDH"),
                            Fmt(r, "used")
                        });
                    }
                }

                if (resRows.Count == 0)
                {
                    y = DrawTextLine(g, page, y, "Aucun résidu.");
                }
                else
                {
                    DrawSimpleTable(doc, ref page, ref g, ref y,
                        null,
                        new[] { "ID", "dHz", "dAlti", "dDH", "Utilisé" },
                        resRows,
                        Units.MmToPt(6.5));
                    y += Units.MmToPt(2);
                }

                // IMPLANTATION (per station)
                EnsurePage(doc, ref page, ref g, ref y, Units.MmToPt(40));
                y = DrawBar(g, page, y, "MESURE SUR LIGNE", BrandBlue);
                // subtitle (tolerances)
                var sub = GetString(root, "subTitle");
                if (!string.IsNullOrWhiteSpace(sub))
                {
                    g.DrawString(sub, NovatlasTheme.FontBody(8.0), XBrushes.Black,
                        new XRect(MarginL, y - Units.MmToPt(1), page.Width.Point - MarginL - MarginR, Units.MmToPt(5)),
                        XStringFormats.CenterLeft);
                    y += Units.MmToPt(4.5);
                }

                string sid2 = id;
                if (string.IsNullOrWhiteSpace(sid2)) sid2 = "";
                if (!lrByStation.TryGetValue(sid2, out var lrPts) || lrPts.Count == 0)
                {
                    if (runs.Count == 1 && lrByStation.TryGetValue("", out var lrPtsEmpty) && lrPtsEmpty.Count > 0)
                    {
                        lrPts = lrPtsEmpty;
                    }
                    else
                    {
                        // Station already pre-filtered above; nothing to render here.
                        lrPts = new List<JsonElement>();
                    }
                }

                if (lrPts.Count > 0)
                {
                    var layout = PdfSharpReports.MakeImplantationLayoutForPublic(11);
                    if (groupByZone && lrZonesByStation.TryGetValue(sid2, out var zoneGroups) && zoneGroups.Count > 0)
                    {
                        foreach (var zg in zoneGroups)
                        {
                            if (zg.points == null || zg.points.Count == 0) continue;
                            if (!string.IsNullOrWhiteSpace(zg.label))
                                y = DrawZoneHeaderInline(doc, ref page, ref g, ref y, zg.label);
                            DrawLigneRefTableInline(doc, ref page, ref g, ref y,
                                new[] { "ID point","X calc","Y calc","Z calc","X mes","Y mes","Z mes","Dx / dL","Dy / dT","Dz / dA","Statut" },
                                zg.points,
                                layout);
                        }
                    }
                    else
                    {
                        DrawLigneRefTableInline(doc, ref page, ref g, ref y,
                            new[] { "ID point","X calc","Y calc","Z calc","X mes","Y mes","Z mes","Dx / dL","Dy / dT","Dz / dA","Statut" },
                            lrPts,
                            layout);
                    }
                }

            }


            // Final blocks on last page:
            // 1) Référence altimétrique (just above the controls box)
            // 2) CONTROLES + OBSERVATIONS + signatures
            {
                // Position the Réf Alti block immediately above CONTRÔLES, with a tiny fixed gap.
                // Do not reserve a large generic footer area, otherwise we create a big useless blank gap.
                double yCtlTop = page.Height.Point * 0.73717218;
                double refAltiH = GetRefAltiSectionHeight(root);
                double gapBeforeControls = Units.MmToPt(2);
                double neededBottom = (page.Height.Point - MarginB - yCtlTop) + (refAltiH > 0 ? refAltiH + gapBeforeControls : 0);

                // Ensure we are on a page with enough bottom room; if not, create a new page.
                if (y > page.Height.Point - MarginB - neededBottom)
                {
                    g.Dispose();
                    page = AddPage(doc);
                    g = XGraphics.FromPdfPage(page);
                    y = DrawRepeatHeaderLogo(g, page);
                }

                if (refAltiH > 0)
                {
                    double yRef = yCtlTop - refAltiH - gapBeforeControls;
                    // Safety clamp: if content already reaches too low, move to new page.
                    if (y > yRef)
                    {
                        g.Dispose();
                        page = AddPage(doc);
                        g = XGraphics.FromPdfPage(page);
                        y = DrawRepeatHeaderLogo(g, page);
                        yCtlTop = page.Height.Point * 0.73717218;
                        yRef = yCtlTop - refAltiH - gapBeforeControls;
                    }
                    DrawRefAltiSection(g, page, yRef, root);
                }

                DrawFinalControlsAndSignatures(g, page, root);
            }

            // No global IMPLANTATION table here: the legacy report is per station.
        }
        finally
        {
            g.Dispose();
        }

        // Stamp footer on all pages (with total pages)
        int total = doc.PageCount;
        for (int i = 0; i < total; i++)
        {
            var p = doc.Pages[i];
            using var gg = XGraphics.FromPdfPage(p, XGraphicsPdfPageOptions.Append);
            DrawFooter(gg, p, i + 1, total, buildFooter);
        }
    }
    private static void DrawHeaderRightBox(XGraphics gfx, XRect rect, JsonElement root)
    {
	        // Ville + Adresse + CHA : centrés dans le cadre (Ville+Adresse en plus gros)
	        // Encadré droite :
	        // 1) Ville (en haut)
	        // 2) Adresse (au milieu)
	        // 3) CHA + numéro (en bas) -> valeur issue de l'info dossier
	        string ville = GetStr(root, "ville");
	        string adr = GetStr(root, "adresseChantier");
	        if (string.IsNullOrWhiteSpace(adr)) adr = GetStr(root, "adresse");
	        if (string.IsNullOrWhiteSpace(adr)) adr = GetStr(root, "siteAddress");
	        string chaRaw = GetStr(root, "cha");
	        // Saisie UI = chiffres (ex: 02782). PDF : afficher "CHA02782".
	        string chaDigits = (chaRaw ?? "").Trim();
	        if (!string.IsNullOrWhiteSpace(chaDigits) && chaDigits.StartsWith("CHA", System.StringComparison.OrdinalIgnoreCase))
	            chaDigits = chaDigits.Substring(3);
	        chaDigits = chaDigits.Trim().TrimStart('-', '_', ':').Trim();
	        string chaLine = string.IsNullOrWhiteSpace(chaDigits) ? "" : ("CHA" + chaDigits);

	        string lineVille = (ville ?? "").Trim();
	        string lineAdr = (adr ?? "").Trim();
	        string lineCha = chaLine;

	        var fontVille = NovatlasTheme.FontBold(12);
	        var fontAdr = NovatlasTheme.FontBody(11);
	        var fontCha = NovatlasTheme.FontBody(10);

	        double hVille = string.IsNullOrWhiteSpace(lineVille) ? 0 : gfx.MeasureString(lineVille, fontVille).Height;
	        double hAdr = string.IsNullOrWhiteSpace(lineAdr) ? 0 : gfx.MeasureString(lineAdr, fontAdr).Height;
	        double hCha = string.IsNullOrWhiteSpace(lineCha) ? 0 : gfx.MeasureString(lineCha, fontCha).Height;
	        double gap = 2;
	        double total = hVille
	            + (hVille > 0 && hAdr > 0 ? gap : 0) + hAdr
	            + ((hVille + hAdr) > 0 && hCha > 0 ? gap : 0) + hCha;
	        double y0 = rect.Top + (rect.Height - total) / 2.0;
	        double y = y0;
	        if (hVille > 0)
	        {
	            gfx.DrawString(lineVille, fontVille, XBrushes.Black, new XRect(rect.Left, y, rect.Width, hVille), XStringFormats.TopCenter);
	            y += hVille;
	        }
	        if (hVille > 0 && hAdr > 0) y += gap;
	        if (hAdr > 0)
	        {
	            gfx.DrawString(lineAdr, fontAdr, XBrushes.Black, new XRect(rect.Left, y, rect.Width, hAdr), XStringFormats.TopCenter);
	            y += hAdr;
	        }
	        if ((hVille + hAdr) > 0 && hCha > 0) y += gap;
	        if (hCha > 0)
	        {
	            gfx.DrawString(lineCha, fontCha, XBrushes.Black, new XRect(rect.Left, y, rect.Width, hCha), XStringFormats.TopCenter);
	            y += hCha;
	        }
    }

    private static string GetStr(JsonElement root, string key)
    {
        try
        {
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(key, out var v))
                return v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : v.ToString();

            // common nesting patterns used by the UI payload
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("cartouche", out var c) && c.ValueKind == JsonValueKind.Object && c.TryGetProperty(key, out var vc))
                    return vc.ValueKind == JsonValueKind.String ? (vc.GetString() ?? "") : vc.ToString();

                if (root.TryGetProperty("project", out var p) && p.ValueKind == JsonValueKind.Object && p.TryGetProperty(key, out var vp))
                    return vp.ValueKind == JsonValueKind.String ? (vp.GetString() ?? "") : vp.ToString();

                // current payload root often stores user-entered fields under info
                if (root.TryGetProperty("info", out var info) && info.ValueKind == JsonValueKind.Object && info.TryGetProperty(key, out var vi))
                    return vi.ValueKind == JsonValueKind.String ? (vi.GetString() ?? "") : vi.ToString();
            }
        }
        catch { /* ignore */ }
        return "";
    }

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
                return v.Trim();
        }
        return "";
    }

    private static void DrawWrappedText(XGraphics gfx, string text, XFont font, XBrush brush, XRect rect, int maxLines)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        // naive wrap by words
        var words = text.Split(new[]{' '}, StringSplitOptions.RemoveEmptyEntries);
        var lines = new System.Collections.Generic.List<string>();
        string cur = "";
        foreach (var w in words)
        {
            string cand = string.IsNullOrEmpty(cur) ? w : (cur + " " + w);
            if (gfx.MeasureString(cand, font).Width <= rect.Width)
            {
                cur = cand;
            }
            else
            {
                if (!string.IsNullOrEmpty(cur)) lines.Add(cur);
                cur = w;
                if (lines.Count >= maxLines) break;
            }
        }
        if (lines.Count < maxLines && !string.IsNullOrEmpty(cur)) lines.Add(cur);
        double lineH = gfx.MeasureString("Ag", font).Height;
        for (int i=0;i<lines.Count && i<maxLines;i++)
        {
            gfx.DrawString(lines[i], font, brush, new XRect(rect.Left, rect.Top + i*lineH, rect.Width, lineH), XStringFormats.TopLeft);
        }
    }

}
