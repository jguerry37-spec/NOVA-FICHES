using System.Collections.Generic;
using System.Text.Json;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace NovaFiches.PdfSharpEngine;

public static class ImplantationFullReportRenderer
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
        var cc = ControlsCounter.ComputeForImplantation(root);

        int pointsMesures = cc.PointsMesures, valides = cc.Valides, refuses = cc.Refuses, noneval = cc.NonEval;

        // "LEVÉ" (points topo) must only display measured + non-evaluated counts.
        // The payload is detected by the presence of topoStations (LandXML parsing).
        bool isLeve = root.TryGetProperty("topoStations", out var ts) && ts.ValueKind == System.Text.Json.JsonValueKind.Array;

        string counts = isLeve
            ? $"Points mesurés : {pointsMesures}    Non eval. : {noneval}"
            : $"Points mesurés : {pointsMesures}    Valides : {valides}    Refuses : {refuses}    Non eval. : {noneval}";
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
    // Cartouche geometry
    // NOTE: We deliberately tune IMP first (per user request) to match the validated STATION layout intent.
    // We keep the left column (date / systems) fixed, and only adjust the right-side splits:
    //  - Row1: Entreprise x2, Contact client shrinks
    //  - Row2: PPM shrinks by 1/4, Plan de référence grows
    //  - Row3: Appareil grows x1.5 (to fit serial), Intervenant shrinks
    double w = page.Width.Point - MarginL - MarginR;
    double rowH = Units.MmToPt(14);

    double x0 = MarginL;
    double xR = x0 + w;

    // Left column stays stable (same as previous Station-like ratio)
    double x1 = x0 + w * 0.295;
    double rightW = xR - x1;

    // IMPORTANT (user validation): we use explicit ratios (not derived heuristics)
    // to match the red-line reference the user provided.
    // Left column stays fixed. Only the right-side split changes per row.
    //  - Row1: Entreprise/Contact split must match the user's red-line reference.
    //  - Row3: Appareil/Intervenant split must match the same red-line reference.
    // We express that split as an absolute ratio of the whole cartouche width (w),
    // because the left column stays fixed (x1) and we want the separator to land
    // at the exact same x-position on the page.
    // Measured from the reference screenshot: x2 ≈ 0.676 * w.
    double x2Common = x0 + w * 0.676;

    // Row2 keeps the functional rule: PPM reduced, Plan increased.
    // (No red-line reference was given for this separator in the latest capture.)
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
        bool hasLabel = !string.IsNullOrWhiteSpace(label);
        if (hasLabel)
        {
            g.DrawString(label, fLabel, XBrushes.Black,
                new XRect(xL, yy + Units.MmToPt(1.0), ww, Units.MmToPt(6.0)),
                XStringFormats.TopCenter);
        }
        if (!string.IsNullOrWhiteSpace(value))
        {
            // If there is no label (e.g., "Intervention" cell), center the value vertically.
            var rectVal = hasLabel
                ? new XRect(xL, yy + Units.MmToPt(6.0), ww, rowH - Units.MmToPt(7.0))
                : new XRect(xL, yy, ww, rowH);

            // Plan de référence can be very long (DWG filename + texte de réf). Guarantee no overflow:
            // wrap + shrink font + ellipsis as last resort.
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
    // PPM: always display unit "mm/km" when a value exists (user requirement)
    if (!string.IsNullOrWhiteSpace(ppm) && !ppm.Contains("mm/km", StringComparison.OrdinalIgnoreCase))
        ppm = ppm.Trim() + " mm/km";
    string planRef = GetString(root, "planRef");
    string altSys = GetString(root, "systemeAlti");

    // Appareil: we want BOTH model and serialNumber (LandXML often provides: model="..." serialNumber="...").
    // Keep backward compatibility with existing payload keys.
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
        // Use an en-dash separator like the validated station header.
        if (!string.IsNullOrWhiteSpace(appareil)) appareil = $"{appareil} – {appareilSerial}";
        else appareil = appareilSerial;
    }
    // GNSS (RTK) : pas de "modèle d'appareil" saisi côté LandXML (Leica ne journalise que le
    // contrôleur, pas l'antenne) - par défaut, on indique juste "GNSS" plutôt que de laisser vide.
    if (string.IsNullOrWhiteSpace(appareil) && RootHasGnssRun(root)) appareil = "GNSS";

    string intervenant = GetString(root, "intervenant");
    if (string.IsNullOrWhiteSpace(intervenant)) intervenant = GetString(root, "operator");
    if (string.IsNullOrWhiteSpace(intervenant)) intervenant = GetString(root, "operateur");

    // Row 1
    // Show "Intervention du" label (Station reference) + date value.
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

    private static bool RootHasGnssRun(JsonElement root)
    {
        try
        {
            if (root.TryGetProperty("stationLibreRuns", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in arr.EnumerateArray())
                {
                    if (string.Equals(Fmt(r, "results", "method"), "GNSS", StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            if (root.TryGetProperty("stationLibre", out var one) && one.ValueKind == JsonValueKind.Object &&
                string.Equals(Fmt(one, "results", "method"), "GNSS", StringComparison.OrdinalIgnoreCase)) return true;
        }
        catch { }
        return false;
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

    // Display helper: remove Leica "@NN" suffixes so IDs match between sections (Observations / Residuals / etc.)
    private static string StripAtId(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var at = s.IndexOf('@');
        return (at > 0) ? s.Substring(0, at) : s;
    }


    /// <summary>
    /// Best-effort match between an AppLog station id (run.results.idStation) and a LandXML setup.
    /// LandXML parsing exposes items like { setupId, stationName, observations[], results[] }.
    /// If no match is found, returns the first topo station.
    /// </summary>
    private static JsonElement FindTopoStationForRun(List<JsonElement> topoStations, string stationId)
    {
        if (topoStations == null || topoStations.Count == 0) return default;

        var id = (stationId ?? "").Trim();
        if (id.Length > 0)
        {
            foreach (var ts in topoStations)
            {
                var name = GetString(ts, "stationName");
                var setup = GetString(ts, "setupId");
                if (string.Equals(id, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(id, setup, StringComparison.OrdinalIgnoreCase))
                    return ts;
            }
        }

        return topoStations[0];
    }

    // Format any numeric value to 3 decimals, keeping the original decimal separator when possible.
    private static string Format3Decimals(string s)
    {
        var raw = (s ?? "").Trim();
        if (raw.Length == 0) return "";

        bool hadComma = raw.Contains(',');
        // Normalize for parsing
        var norm = raw.Replace(',', '.');

        if (!double.TryParse(norm, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
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

        if (!double.TryParse(norm, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
            return raw;

        var outStr = v.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);
        return hadComma ? outStr.Replace('.', ',') : outStr;
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

        // Header row
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
                    g.DrawString(header[c] ?? "", headFont, XBrushes.Black,
                        new XRect(x + c * colW + 2, y + 2, colW - 4, headerHeight - 4), XStringFormats.TopLeft);
                }
                y += headerHeight;
            }

            g.DrawRectangle(pen, x, y, tableW, rowHeight);
            for (int c = 0; c < cols; c++)
            {
                if (c > 0) g.DrawLine(pen, x + c * colW, y, x + c * colW, y + rowHeight);
                string cell = (row != null && c < row.Length) ? (row[c] ?? "") : "";

                // Colonne ID point : ajuste la taille pour tenir dans la cellule (pas de wrap).
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


    private static void DrawImplantationTableInline(
        PdfDocument doc,
        ref PdfPage page,
        ref XGraphics g,
        ref double y,
        string[] header,
        List<string[]> rows,
        TableRenderer.TableLayout layout)
    {
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

        void DrawRow(double yy, string[] row)
        {
            double xx = MarginL;
            for (int c = 0; c < colCount; c++)
            {
                string txt = c < row.Length ? (row[c] ?? "") : "";
                var rect = new XRect(xx, yy, colW[c], rowH);
                gfx.DrawString(txt, fC, XBrushes.Black, rect, XStringFormats.Center);
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
            double footerSafe = Units.MmToPt(20);
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
                DrawRow(yy, rows[rowIndex + r]);
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

        // Some reports (e.g. Récolement pieux) reuse this renderer for its validated
        // look & cartouche, but do NOT want the heavy observation blocks.
        var suppressObsTables = root.TryGetProperty("suppressObsTables", out var sup)
            && sup.ValueKind == JsonValueKind.True;

        // Optional override used by other buttons (e.g., "LEVÉ") that must reuse the
        // exact Implantation layout while changing only the section naming.
        var sectionImplantationTitle = GetString(root, "sectionImplantationTitle");
        // Default: use payload title when available (ex: Récolement de pieux réutilise ce renderer)
        if (string.IsNullOrWhiteSpace(sectionImplantationTitle))
            sectionImplantationTitle = GetString(root, "title");
        if (string.IsNullOrWhiteSpace(sectionImplantationTitle))
            sectionImplantationTitle = "IMPLANTATION";

        // Étape 2 (LEVÉ): si le payload contient des topoStations (LandXML),
        // on remplace le bloc "IMPLANTATION" par :
        //  - Observations polaires (LandXML)
        //  - Résultats rectangulaires (LandXML)
        // tout en conservant le gabarit identique du rapport Implantation.
        var topoStations = new List<JsonElement>();
        if (root.TryGetProperty("topoStations", out var topoArr) && topoArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var it in topoArr.EnumerateArray())
            {
                if (it.ValueKind == JsonValueKind.Object) topoStations.Add(it);
            }
        }

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

            // Prepare implantation rows grouped by stationId (optional, provided by JS).
            var impByStation = new Dictionary<string, List<string[]>>();
            var impZonesByStation = new Dictionary<string, List<(string label, List<string[]> rows)>>();
            if (root.TryGetProperty("implantationByStation", out var impArr) && impArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var it in impArr.EnumerateArray())
                {
                    if (it.ValueKind != JsonValueKind.Object) continue;
                    var sid = GetString(it, "stationId") ?? "";
                    var rr = new List<string[]>();
                    if (it.TryGetProperty("rows", out var rrEl) && rrEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var rEl in rrEl.EnumerateArray())
                        {
                            if (rEl.ValueKind != JsonValueKind.Array) continue;
                            rr.Add(rEl.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? (x.GetString() ?? "") : x.ToString()).ToArray());
                        }
                    }
                    impByStation[sid] = rr;

                    var zones = new List<(string label, List<string[]> rows)>();
                    if (it.TryGetProperty("zoneGroups", out var zgEl) && zgEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var zEl in zgEl.EnumerateArray())
                        {
                            if (zEl.ValueKind != JsonValueKind.Object) continue;
                            var label = GetString(zEl, "label") ?? GetString(zEl, "code") ?? "";
                            var zRows = new List<string[]>();
                            if (zEl.TryGetProperty("rows", out var zrEl) && zrEl.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var rEl in zrEl.EnumerateArray())
                                {
                                    if (rEl.ValueKind != JsonValueKind.Array) continue;
                                    zRows.Add(rEl.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? (x.GetString() ?? "") : x.ToString()).ToArray());
                                }
                            }
                            zones.Add((label, zRows));
                        }
                    }
                    impZonesByStation[sid] = zones;
                }
            }

            var groupByZone = root.TryGetProperty("groupByZone", out var gbz) && gbz.ValueKind == JsonValueKind.True;

            // Keep only station runs that actually carry implantation rows for this report.
            // This preserves Station / Levé topo behavior elsewhere and avoids empty station blocks
            // like "Aucun point d'implantation pour cette station." in implantation PDFs.
            IEnumerable<string> StationKeys(JsonElement runEl)
            {
                return new[]
                {
                    Fmt(runEl, "results", "idStation"),
                    Fmt(runEl, "idStation"),
                    Fmt(runEl, "results", "stationName"),
                    Fmt(runEl, "stationName")
                }
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase);
            }

            bool RunHasImplantationRows(JsonElement runEl)
            {
                foreach (var sidLocal in StationKeys(runEl))
                {
                    if (impByStation.TryGetValue(sidLocal, out var directRows) && directRows != null && directRows.Count > 0)
                        return true;

                    if (groupByZone && impZonesByStation.TryGetValue(sidLocal, out var zoneGroups) && zoneGroups != null)
                    {
                        foreach (var zg in zoneGroups)
                        {
                            if (zg.rows != null && zg.rows.Count > 0) return true;
                        }
                    }
                }

                // Fallback: some payloads group rows under empty stationId when there is only one station.
                if (runs.Count == 1 && impByStation.TryGetValue("", out var emptyRows) && emptyRows != null && emptyRows.Count > 0)
                    return true;

                return false;
            }

            // IMPORTANT : en mode LEVÉ (topoStations présent), on réutilise ce renderer
            // uniquement pour sa mise en page validée. Il ne faut donc PAS filtrer les
            // stations avec la présence de lignes d'implantation, sinon tous les blocs
            // mise en station / résidus / polaires / rectangulaires disparaissent.
            bool isLeveTopoReport = topoStations.Count > 0 && !suppressObsTables;
            if (!isLeveTopoReport)
            {
                runs = runs.Where(RunHasImplantationRows).ToList();
            }

            bool isMntReport = sectionImplantationTitle.IndexOf("MNT", StringComparison.OrdinalIgnoreCase) >= 0;
            bool renderedMntFallback = false;

            // Recolement MNT: the plan view is independent from stationLibreRuns, but the
            // table must never disappear if station metadata is incomplete or filtered out.
            if (isMntReport && runs.Count == 0 && impByStation.Any(kv => kv.Value != null && kv.Value.Count > 0))
            {
                EnsurePage(doc, ref page, ref g, ref y, Units.MmToPt(40));
                y = DrawBar(g, page, y, sectionImplantationTitle, BrandBlue);
                var sub = GetString(root, "subTitle");
                if (!string.IsNullOrWhiteSpace(sub))
                {
                    g.DrawString(sub, NovatlasTheme.FontBody(8.0), XBrushes.Black,
                        new XRect(MarginL, y - Units.MmToPt(1), page.Width.Point - MarginL - MarginR, Units.MmToPt(5)),
                        XStringFormats.CenterLeft);
                    y += Units.MmToPt(4.5);
                }

                var layout = PdfSharpReports.MakeImplantationLayoutForPublic(11);
                var headerMnt = new[] { "ID point","X theo","Y theo","Z theo MNT","X releve","Y releve","Z releve","Dx","Dy","Dz","STATUT" };
                foreach (var kv in impByStation.Where(kv => kv.Value != null && kv.Value.Count > 0).OrderBy(kv => kv.Key))
                {
                    var stationLabel = string.IsNullOrWhiteSpace(kv.Key) ? "STATION MNT" : "STATION " + kv.Key;
                    EnsurePage(doc, ref page, ref g, ref y, Units.MmToPt(24));
                    y = DrawBar(g, page, y, stationLabel, LightGray);
                    DrawImplantationTableInline(doc, ref page, ref g, ref y, headerMnt, kv.Value, layout);
                }
                renderedMntFallback = true;
            }
            // Render each station block (TYPE DE STATION + OBSERVATIONS + RÉSIDUS + IMPLANTATION/LEVÉ)
            foreach (var run in (renderedMntFallback ? Enumerable.Empty<JsonElement>() : runs))
            {
                EnsurePage(doc, ref page, ref g, ref y, Units.MmToPt(60));
                // Validated palette: light orange fill + strong orange border
                var stationBorder = XColor.FromArgb(255, 89, 22);
                var stationFill = stationBorder;
                y = DrawBar(g, page, y, "TYPE DE STATION", stationFill, stationBorder, borderWidth: 1.4);

                var pen = new XPen(XColors.Black, 0.6);
                double w = page.Width.Point - MarginL - MarginR;

                var f = NovatlasTheme.FontBody(8.2);

                string method = Fmt(run, "results", "method");
                if (string.IsNullOrWhiteSpace(method)) method = "Station libre";
                bool isGnss = string.Equals(method, "GNSS", StringComparison.OrdinalIgnoreCase);

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
                string azOrient = FmtNum(run, "results", "azOrient");
                string factScale = FmtNum(run, "results", "factScale");

                var stationLines = new List<string> { $"Méthode : {method}" };
                stationLines.Add($"Station : {stationLabel}");
                if (isGnss)
                {
                    // GNSS (RTK) : pas de résection TPS - récepteur + référence RTK au lieu de
                    // coordonnées de station / corrections d'orientation, qui n'ont pas de sens ici.
                    string receiver = Fmt(run, "results", "receiver");
                    string antennaHeight = FmtNum(run, "results", "antennaHeight");
                    if (!string.IsNullOrWhiteSpace(receiver) || !string.IsNullOrWhiteSpace(antennaHeight))
                    {
                        var recLine = "Récepteur : " + (string.IsNullOrWhiteSpace(receiver) ? "—" : receiver);
                        if (!string.IsNullOrWhiteSpace(antennaHeight)) recLine += $"  |  Hauteur d'antenne : {antennaHeight} m";
                        stationLines.Add(recLine);
                    }
                    if (run.TryGetProperty("results", out var resEl) && resEl.ValueKind == JsonValueKind.Object &&
                        resEl.TryGetProperty("rtkRef", out var rtkRefEl) && rtkRefEl.ValueKind == JsonValueKind.Object)
                    {
                        string rtkName = Fmt(rtkRefEl, "name");
                        string rtkE = FmtNum(rtkRefEl, "E");
                        string rtkN = FmtNum(rtkRefEl, "N");
                        string rtkH = FmtNum(rtkRefEl, "H");
                        if (!string.IsNullOrWhiteSpace(rtkName) || !string.IsNullOrWhiteSpace(rtkE) || !string.IsNullOrWhiteSpace(rtkN))
                        {
                            var rtkLine = "Référence RTK : " + (string.IsNullOrWhiteSpace(rtkName) ? "—" : rtkName);
                            if (!string.IsNullOrWhiteSpace(rtkE) || !string.IsNullOrWhiteSpace(rtkN) || !string.IsNullOrWhiteSpace(rtkH))
                                rtkLine += $"  (E={rtkE}  N={rtkN}  H={rtkH})";
                            stationLines.Add(rtkLine);
                        }
                    }
                }
                else
                {
                    stationLines.Add($"Coordonnées : E={E}  N={N}  H={H}");
                    stationLines.Add($"Corr. orientat° {corrOrient}     |     Fact. échelle {factScale}     |     Dev.std E/N/H {devE} / {devN} / {devH}     |     Ori. {azOrient}");
                    stationLines.Add($"Orientation : CorrOri={corrOrient}  AzOri={azOrient}");
                }

                double lineH = Units.MmToPt(4.8);
                double boxH = Math.Max(Units.MmToPt(14), stationLines.Count * lineH + Units.MmToPt(3));
                g.DrawRectangle(pen, MarginL, y, w, boxH);
                double yy = y + Units.MmToPt(2);
                foreach (var line in stationLines)
                {
                    g.DrawString(line, f, XBrushes.Black,
                        new XRect(MarginL + Units.MmToPt(2), yy, w - Units.MmToPt(4), lineH), XStringFormats.CenterLeft);
                    yy += lineH;
                }

                y += boxH + Units.MmToPt(3);

                // Constante de prisme par point (fallback pour le bloc LEVÉ / LandXML)
                var prismConstById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                // OBSERVATIONS table (optional) - sans objet pour un run GNSS (pas de résection TPS).
                if (!suppressObsTables && !isGnss)
                {
                    EnsurePage(doc, ref page, ref g, ref y, Units.MmToPt(30));
                    y = DrawBar(g, page, y, "OBSERVATIONS", LightGray);

                    var obsRows = new List<string[]>();
                    if (run.TryGetProperty("observations", out var obs) && obs.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var o in obs.EnumerateArray())
                        {
                            if (o.ValueKind != JsonValueKind.Object) continue;

                            var oid = Fmt(o, "id");
                            var cpr = FmtPrismConst(o, "constPrisme");
                            if (!string.IsNullOrWhiteSpace(oid) && !string.IsNullOrWhiteSpace(cpr))
                            {
                                // ID AppLog souvent sous la forme "C07@15673" => on garde aussi la base "C07"
                                var baseId = oid;
                                var at = baseId.IndexOf('@');
                                if (at > 0) baseId = baseId.Substring(0, at);
                                prismConstById[oid] = cpr;
                                prismConstById[baseId] = cpr;
                            }

                            obsRows.Add(new[]
                            {
                                StripAtId(oid),
                                FmtNum(o, "hz"),
                                FmtNum(o, "vz"),
                                FmtNum(o, "dp"),
                                FmtNum(o, "hr"),
                                cpr
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
                }

                // RÉSIDUS table - sans objet pour un run GNSS (pas de résection TPS).
                if (!isGnss)
                {
                    EnsurePage(doc, ref page, ref g, ref y, Units.MmToPt(30));
                    y = DrawBar(g, page, y, "RÉSIDUS", LightGray);

                    var resRows = new List<string[]>();
                    if (run.TryGetProperty("residuals", out var res) && res.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var r in res.EnumerateArray())
                        {
                            if (r.ValueKind != JsonValueKind.Object) continue;
                            var used = Fmt(r, "used");
                            if (string.IsNullOrWhiteSpace(used)) used = Fmt(r, "useKind");
                            if (string.IsNullOrWhiteSpace(used)) used = Fmt(r, "usekind");
                            if (string.IsNullOrWhiteSpace(used)) used = Fmt(r, "UseKind");
                            if (!string.IsNullOrWhiteSpace(used))
                            {
                                used = used.Trim();
                                if (string.Equals(used, "true", StringComparison.OrdinalIgnoreCase)) used = "Oui";
                                else if (string.Equals(used, "false", StringComparison.OrdinalIgnoreCase)) used = "Non";
                                else if (string.Equals(used, "3d", StringComparison.OrdinalIgnoreCase)) used = "3D";
                                else if (string.Equals(used, "2d", StringComparison.OrdinalIgnoreCase)) used = "2D";
                            }

                            resRows.Add(new[]
                            {
                                StripAtId(Fmt(r, "id")),
                                FmtNum(r, "dHz"),
                                FmtNum(r, "dAlti"),
                                FmtNum(r, "dDH"),
                                used
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
                }

                // IMPLANTATION / LEVÉ (per station)
                // Pagination guard:
                // In "LEVÉ" mode (LandXML), the replacement block can be tall (two tables) and may
                // otherwise overlap the footer or collide with the next station block.
                // We pre-compute an estimated required height and force a new page when needed.
                if (topoStations.Count > 0 && !suppressObsTables)
                {
                    var topoForMeasure = FindTopoStationForRun(topoStations, id);
                    int obsCount = 0;
                    if (topoForMeasure.ValueKind == JsonValueKind.Object
                        && topoForMeasure.TryGetProperty("observations", out var _mObs)
                        && _mObs.ValueKind == JsonValueKind.Array)
                        obsCount = _mObs.GetArrayLength();

                    int resCount = 0;
                    if (topoForMeasure.ValueKind == JsonValueKind.Object
                        && topoForMeasure.TryGetProperty("results", out var _mRes)
                        && _mRes.ValueKind == JsonValueKind.Array)
                        resCount = _mRes.GetArrayLength();

                    // DrawSimpleTable row height is ~6.2mm.
                    double rowH = Units.MmToPt(6.2);
                    double barH = Units.MmToPt(8.0);   // DrawBar height approx
                    double pad = Units.MmToPt(6.0);    // gaps between blocks

                    // section bar + optional subtitle + (bar + table) + (bar + table)
                    double tableObsH = rowH * (1 + Math.Max(0, obsCount));
                    double tableResH = rowH * (1 + Math.Max(0, resCount));

                    double needed =
                        barH + Units.MmToPt(6) +
                        (barH + tableObsH + Units.MmToPt(2)) +
                        (barH + tableResH + Units.MmToPt(2)) +
                        pad;

                    // Keep a safety zone above the footer line.
                    double bottomSafe = page.Height.Point - LayoutConstants.FooterReservePt;
                    if (y + needed > bottomSafe)
                    {
                        g.Dispose();
                        page = AddPage(doc);
                        g = XGraphics.FromPdfPage(page);
                        y = DrawRepeatHeaderLogo(g, page);
                    }
                }

                EnsurePage(doc, ref page, ref g, ref y, Units.MmToPt(40));
                y = DrawBar(g, page, y, sectionImplantationTitle, BrandBlue);
                // subtitle (tolerances)
                var sub = GetString(root, "subTitle");
                if (!string.IsNullOrWhiteSpace(sub))
                {
                    g.DrawString(sub, NovatlasTheme.FontBody(8.0), XBrushes.Black,
                        new XRect(MarginL, y - Units.MmToPt(1), page.Width.Point - MarginL - MarginR, Units.MmToPt(5)),
                        XStringFormats.CenterLeft);
                    y += Units.MmToPt(4.5);
                }


                // --- LEVÉ (LandXML) : remplace le tableau Implantation
                if (topoStations.Count > 0)
                {
                    var topo = FindTopoStationForRun(topoStations, id);

                    // Observations polaires
                    EnsurePage(doc, ref page, ref g, ref y, Units.MmToPt(26));
                    y = DrawBar(g, page, y, "OBSERVATIONS POLAIRES", LightGray);

                    var obsRows2 = new List<string[]>();
                    if (topo.ValueKind == JsonValueKind.Object && topo.TryGetProperty("observations", out var obs2) && obs2.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var o in obs2.EnumerateArray())
                        {
                            if (o.ValueKind != JsonValueKind.Object) continue;

                            var pidRaw = Fmt(o, "id");
                            // Display: remove Leica "@NN" suffixes (occurrence index)
                            var pid = pidRaw;
                            if (!string.IsNullOrWhiteSpace(pidRaw))
                            {
                                var at = pidRaw.IndexOf('@');
                                if (at > 0) pid = pidRaw.Substring(0, at);
                            }
                            // Constante de prisme :
                            // 1) LandXML si présent (plusieurs clés possibles)
                            // 2) Sinon fallback depuis les observations AppLog de la station (prismConstById)
                            var cpr = FmtPrismConst(o, "constPrisme");
                            if (string.IsNullOrWhiteSpace(cpr)) cpr = FmtNum(o, "prismConstant");
                            if (string.IsNullOrWhiteSpace(cpr)) cpr = FmtNum(o, "prismConst");
                            if (string.IsNullOrWhiteSpace(cpr))
                            {
                                if (!string.IsNullOrWhiteSpace(pid) && prismConstById.TryGetValue(pid, out var pc1)) cpr = pc1;
                                else if (!string.IsNullOrWhiteSpace(pid))
                                {
                                    var at = pid.IndexOf('@');
                                    var baseId = at > 0 ? pid.Substring(0, at) : pid;
                                    if (prismConstById.TryGetValue(baseId, out var pc2)) cpr = pc2;
                                }
                            }

                            obsRows2.Add(new[]
                            {
                                pid,
                                FmtNum(o, "hz"),
                                FmtNum(o, "vz"),
                                FmtNum(o, "dp"),
                                FmtNum(o, "hr"),
                                cpr
                            });
                        }
                    }

                    if (obsRows2.Count == 0)
                    {
                        y = DrawTextLine(g, page, y, "Aucune observation polaire (LandXML).");
                    }
                    else
                    {
                        DrawSimpleTable(doc, ref page, ref g, ref y,
                            null,
                            new[] { "ID", "Hz", "Vz", "Dp", "Hr", "Const prisme" },
                            obsRows2,
                            Units.MmToPt(6.5));
                        y += Units.MmToPt(2);
                    }

                    // Résultats rectangulaires
                    EnsurePage(doc, ref page, ref g, ref y, Units.MmToPt(26));
                    y = DrawBar(g, page, y, "RÉSULTATS RECTANGULAIRES", LightGray);

                    var rectRows = new List<string[]>();
                    if (topo.ValueKind == JsonValueKind.Object && topo.TryGetProperty("results", out var rr2) && rr2.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var r2 in rr2.EnumerateArray())
                        {
                            if (r2.ValueKind != JsonValueKind.Object) continue;
                            rectRows.Add(new[]
                            {
                                Fmt(r2, "id"),
                                FmtNum(r2, "E"),
                                FmtNum(r2, "N"),
                                FmtNum(r2, "H")
                            });
                        }
                    }

                    if (rectRows.Count == 0)
                    {
                        y = DrawTextLine(g, page, y, "Aucun résultat rectangulaire (LandXML).");
                    }
                    else
                    {
                        DrawSimpleTable(doc, ref page, ref g, ref y,
                            null,
                            new[] { "ID", "E", "N", "H" },
                            rectRows,
                            Units.MmToPt(6.5));
                        y += Units.MmToPt(2);
                    }
                }
                else
                {
                    // Default (Implantation / Recolement MNT): render points per station.
                    string sid2 = StationKeys(run).FirstOrDefault(k => impByStation.TryGetValue(k, out var rowsForKey) && rowsForKey.Count > 0) ?? id;
                    if (string.IsNullOrWhiteSpace(sid2)) sid2 = "";
                    if (!impByStation.TryGetValue(sid2, out var impRows) || impRows.Count == 0)
                    {
                        // Some LandXML exports provide implantation points but no stationLibreRuns mapping.
                        // In that case, JS may leave stationId empty and group rows under "".
                        // If we have a single station block and there are rows under the empty key, use them.
                        if (runs.Count == 1 && impByStation.TryGetValue("", out var impRowsEmpty) && impRowsEmpty.Count > 0)
                        {
                            var layout = PdfSharpReports.MakeImplantationLayoutForPublic(11);
                            DrawImplantationTableInline(doc, ref page, ref g, ref y,
                                new[] { "ID point","X théo","Y théo","Z théo","X mes","Y mes","Z mes","Dx / dL","Dy / dT","Dz / dA","STATUT" },
                                impRowsEmpty,
                                layout);
                        }
                        else
                        {
                            // Station already pre-filtered above; nothing to render here.
                        }
                    }
                    else
                    {
                        var layout = PdfSharpReports.MakeImplantationLayoutForPublic(11);
                        if (groupByZone && impZonesByStation.TryGetValue(sid2, out var zoneGroups) && zoneGroups.Count > 0)
                        {
                            foreach (var zg in zoneGroups)
                            {
                                if (zg.rows == null || zg.rows.Count == 0) continue;
                                if (!string.IsNullOrWhiteSpace(zg.label))
                                    y = DrawZoneHeaderInline(doc, ref page, ref g, ref y, zg.label);
                                DrawImplantationTableInline(doc, ref page, ref g, ref y,
                                    new[] { "ID point","X théo","Y théo","Z théo","X mes","Y mes","Z mes","Dx / dL","Dy / dT","Dz / dA","STATUT" },
                                    zg.rows,
                                    layout);
                            }
                        }
                        else
                        {
                            DrawImplantationTableInline(doc, ref page, ref g, ref y,
                                new[] { "ID point","X théo","Y théo","Z théo","X mes","Y mes","Z mes","Dx / dL","Dy / dT","Dz / dA","STATUT" },
                                impRows,
                                layout);
                        }
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

        // Optional extra page: plan view (used by Récolement de pieux + plan).
        try
        {
            if (root.TryGetProperty("planView", out var pv) && pv.ValueKind == JsonValueKind.Object)
            {
                RecolementPlanViewRenderer.Render(doc, root, pv);
            }
        }
        catch (System.Exception ex)
        {
            // Never block report generation if plan rendering fails, but always leave a
            // trace: this used to fail completely silently, making the missing page
            // impossible to diagnose from a user report alone.
            try
            {
                var logDir = System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    "NOVATLAS", "Nova-Fiches", "Logs");
                System.IO.Directory.CreateDirectory(logDir);
                var logPath = System.IO.Path.Combine(logDir, $"Nova-Fiches_{System.DateTime.Now:yyyy-MM-dd}.log");
                System.IO.File.AppendAllText(logPath,
                    $"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [ERROR] RecolementPlanViewRenderer.Render a échoué (page plan absente du PDF){System.Environment.NewLine}{ex}{System.Environment.NewLine}");
            }
            catch { /* le logging ne doit jamais faire planter la génération du PDF */ }
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
        // Right box content: Ville + Adresse chantier + N° CHA (centré)
        // Objectif : même rendu sur tous les rapports (centrage horizontal + 2 lignes).
        string ville = GetStr(root, "ville");
        string adr = GetStr(root, "adresseChantier");
        if (string.IsNullOrWhiteSpace(adr)) adr = GetStr(root, "adresse");
        if (string.IsNullOrWhiteSpace(adr)) adr = GetStr(root, "siteAddress");
        string cha = GetStr(root, "cha");

        // Polices proches du modèle historique : Ville en 1er, puis adresse, puis CHA.
        var fontVille = NovatlasTheme.FontBold(12);
        var fontAdresse = NovatlasTheme.FontBold(11);
        var fontCha = NovatlasTheme.FontBody(10);

        double pad = 8;
        double usableW = rect.Width - 2 * pad;
        double hVille = gfx.MeasureString("Ag", fontVille).Height;
        double hAdr = gfx.MeasureString("Ag", fontAdresse).Height;
        double hCha = gfx.MeasureString("Ag", fontCha).Height;
        double gap = 2;

	        // 3 lignes centrées : Ville / Adresse / CHAxxxxxx
        string lVille = (ville ?? "").Trim();
        string lAdr = (adr ?? "").Trim();
        string lChaRaw = (cha ?? "").Trim();
	        // Saisie UI = chiffres (ex: 02782). Dans le PDF on affiche "CHA02782".
	        string chaDigits = lChaRaw;
	        if (!string.IsNullOrWhiteSpace(chaDigits) && chaDigits.StartsWith("CHA", System.StringComparison.OrdinalIgnoreCase))
	            chaDigits = chaDigits.Substring(3);
	        chaDigits = (chaDigits ?? "").Trim().TrimStart('-', '_', ':').Trim();
	        string lCha = string.IsNullOrWhiteSpace(chaDigits) ? "" : ("CHA" + chaDigits);

        bool hasVille = !string.IsNullOrWhiteSpace(lVille);
        bool hasAdr = !string.IsNullOrWhiteSpace(lAdr);
	        bool hasCha = !string.IsNullOrWhiteSpace(lCha);


	        double totalH = (hasVille ? hVille : 0)
	                      + ((hasVille && hasAdr) ? gap : 0) + (hasAdr ? hAdr : 0)
	                      + (((hasVille || hasAdr) && hasCha) ? gap : 0) + (hasCha ? hCha : 0);

        double y = rect.Top + (rect.Height - totalH) / 2.0;
        if (hasVille)
        {
            gfx.DrawString(lVille, fontVille, XBrushes.Black, new XRect(rect.Left + pad, y, usableW, hVille), XStringFormats.TopCenter);
            y += hVille + (hasAdr ? gap : (hasCha ? gap : 0));
        }
        if (hasAdr)
        {
            gfx.DrawString(lAdr, fontAdresse, XBrushes.Black, new XRect(rect.Left + pad, y, usableW, hAdr), XStringFormats.TopCenter);
            y += hAdr + (hasCha ? gap : 0);
        }
	        if (hasCha)
	        {
	            gfx.DrawString(lCha, fontCha, XBrushes.Black, new XRect(rect.Left + pad, y, usableW, hCha), XStringFormats.TopCenter);
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



