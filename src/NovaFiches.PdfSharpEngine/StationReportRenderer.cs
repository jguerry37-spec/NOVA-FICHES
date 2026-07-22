using PdfSharp.Drawing;
using PdfSharp.Pdf;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace NovaFiches.PdfSharpEngine;

/// <summary>
/// Station report (PdfSharp).
/// First iteration: provides the SAME header + cartouche geometry as IMP (validated)
/// and a starting "TYPE DE STATION" section. We will iterate on the body to fully
/// match the legacy station report.
/// </summary>
internal static class StationReportRenderer
{
    private static readonly XColor BrandBlue = XColor.FromArgb(18, 103, 243);
    private static readonly XColor Orange = XColor.FromArgb(255, 90, 23);
    private static readonly XColor LightGray = XColor.FromArgb(230, 230, 230);
    private static readonly XColor LineGray = XColor.FromArgb(200, 200, 200);

    private const double MarginL = 36;
    private const double MarginR = 36;
    private const double MarginT = 28;
    private const double MarginB = 28;
    private static JsonElement _rootForRefAlti;

    internal static void Render(PdfDocument doc, string payloadJson, string buildFooter)
    {
        using var json = JsonDocument.Parse(payloadJson);
        var root = json.RootElement;
        _rootForRefAlti = root;

        // Collect station runs (order AppLog).
        var runs = new List<JsonElement>();
        if (root.TryGetProperty("stationLibreRuns", out var runsEl) && runsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in runsEl.EnumerateArray())
                runs.Add(r);
        }
        else if (root.TryGetProperty("stationLibre", out var oneEl) && oneEl.ValueKind == JsonValueKind.Object)
        {
            runs.Add(oneEl);
        }

        // Document assembly with manual pagination (station blocks + tables).
        PdfPage page = AddPage(doc);
        XGraphics? g = null;
        try
        {
            g = XGraphics.FromPdfPage(page);

            double y = MarginT;
            y = DrawTopHeader(g, page, root);
            y = DrawInfoCartouche(g, page, y, root);

            if (runs.Count == 0)
            {
                y = EnsurePage(doc, ref page, ref g, y, Units.MmToPt(30));
                g.DrawString("Aucune station trouvée dans l'AppLog.", NovatlasTheme.FontBody(10), XBrushes.Black,
                    new XRect(MarginL, y, page.Width.Point - MarginL - MarginR, Units.MmToPt(10)), XStringFormats.TopLeft);
                y += Units.MmToPt(12);
            }
            else
            {
                for (int si = 0; si < runs.Count; si++)
                {
                    var run = runs[si];
                    y = RenderOneStation(doc, ref page, ref g, ref y, root, run);

                    // light spacing between stations
                    if (si < runs.Count - 1)
                    {
                        y += Units.MmToPt(2);
                        y = EnsurePage(doc, ref page, ref g, y, Units.MmToPt(18));
                    }
                }
            }

            // Final controls/signatures block must be on the last page, with enough room.
            y = EnsureRoomForLastFooter(doc, ref page, ref g, y, Units.MmToPt(95));
            DrawFinalControlsAndSignatures(g, page, root);

            // IMPORTANT: PdfSharp allows only one XGraphics instance per page at a time.
            // Dispose the main graphics BEFORE we append footers/logos on all pages,
            // otherwise we get: "An XGraphics object already exists for this page...".
            g.Dispose();
            g = null;
        }
        finally
        {
            // Ensure we never keep a live XGraphics in case an exception occurs mid-render.
            // This avoids cascading "XGraphics already exists" errors on subsequent PDF generation.
            g?.Dispose();
        }

        // Footer + pagination on all pages
        int total = doc.PageCount;
        for (int i = 0; i < total; i++)
        {
            var p = doc.Pages[i];
            using var gg = XGraphics.FromPdfPage(p, XGraphicsPdfPageOptions.Append);
            DrawFooterAllPages(gg, p, i + 1, total, buildFooter);
            // Add small logo on pages after the first (jsPDF-like)
            if (i > 0)
                DrawRepeatHeaderLogo(gg, p);
        }

        // (g already disposed above)
    }

    private static double RenderOneStation(
        PdfDocument doc,
        ref PdfPage page,
        ref XGraphics g,
        ref double y,
        JsonElement root,
        JsonElement run)
    {
        // Ensure room for the station block + at least one table header.
        y = EnsurePage(doc, ref page, ref g, y, Units.MmToPt(55));

        // TYPE DE STATION
        y = DrawBar(g, page, y, "TYPE DE STATION", Orange);
        y += Units.MmToPt(1.5);

        // Station block (boxed text)
        y = DrawStationBlock(g, page, y, run);

        // OBSERVATIONS
        y = EnsurePage(doc, ref page, ref g, y, Units.MmToPt(40));
        y = DrawLightBar(g, page, y, "OBSERVATIONS", LightGray);
        var obsRows = ReadObservations(run);
        DrawSimpleTable(doc, ref page, ref g, ref y,
            null,
            new[] { "ID", "Hz", "Vz", "Dp", "Hr", "Const prisme" },
            obsRows,
            Units.MmToPt(6.5));
        y += Units.MmToPt(2.5);

        // RÉSIDUS
        y = EnsurePage(doc, ref page, ref g, y, Units.MmToPt(40));
        y = DrawLightBar(g, page, y, "RÉSIDUS", XColor.FromArgb(230, 230, 230));
        var resRows = ReadResiduals(run);
        DrawSimpleTable(doc, ref page, ref g, ref y,
            null,
            new[] { "ID", "dHz", "dAlti", "dDH", "Utilisé" },
            resRows,
            Units.MmToPt(6.5));
        y += Units.MmToPt(3);

        // RÉFÉRENCE ALTIMÉTRIQUE (globale)
        var refAltiH = GetRefAltiSectionHeight(root: default);
        try
        {
            refAltiH = GetRefAltiSectionHeight(_rootForRefAlti);
        }
        catch { }
        if (refAltiH > 0)
        {
            y = EnsurePage(doc, ref page, ref g, y, refAltiH + Units.MmToPt(4));
            y = DrawRefAltiSection(g, page, y, _rootForRefAlti);
            y += Units.MmToPt(3);
        }

        return y;
    }

    private static double DrawStationBlock(XGraphics g, PdfPage page, double y, JsonElement run)
    {
        // Extract station result object
        JsonElement S = default;
        if (run.ValueKind == JsonValueKind.Object)
        {
            if (run.TryGetProperty("results", out var rEl) && rEl.ValueKind == JsonValueKind.Object) S = rEl;
            else if (run.TryGetProperty("stationLibre", out var slEl) && slEl.ValueKind == JsonValueKind.Object) S = slEl;
            else S = run;
        }

        string method = GetAnyString(S, "method", "type", "methode")
            .Trim();
        if (string.IsNullOrWhiteSpace(method)) method = "Station libre";

        string id = FirstNonEmptyNonSetupId(
            GetAnyString(S, "stationName"),
            GetAnyString(run, "stationName"),
            GetAnyString(S, "name", "station"),
            GetAnyString(run, "name", "station")
        ).Trim();
        if (string.IsNullOrWhiteSpace(id))
            id = GetAnyString(S, "idStation", "stationId", "id").Trim();
        string E = GetAnyString(S, "E", "X").Trim();
        string N = GetAnyString(S, "N", "Y").Trim();
        string H = GetAnyString(S, "H", "Z").Trim();

        string corr = GetAnyString(S, "corrOrient", "CorrOri", "corrOri").Trim();
        string az = GetAnyString(S, "azOrient", "azOri", "AzOri", "azimuthOri", "azimuthOrient").Trim();
        string scale = GetAnyString(S, "scaleFactor", "facteurEchelle", "facteurEchellePpm", "scale").Trim();

        string devE = GetAnyString(S, "devE", "sE", "AE", "sigmaE").Trim();
        string devN = GetAnyString(S, "devN", "sN", "AN", "sigmaN").Trim();
        string devH = GetAnyString(S, "devH", "sH", "AH", "sigmaH").Trim();
        string devOri = GetAnyString(S, "devOri", "sOri", "sigmaOri", "SigmaOri").Trim();

        // Force 3 decimals on numeric values (requirement: all Hz/Hv/Dp/... to 3 decimals)
        corr = Fmt3(corr);
        az = Fmt3(az);
        scale = Fmt3(scale);
        devE = Fmt3(devE);
        devN = Fmt3(devN);
        devH = Fmt3(devH);
        devOri = Fmt3(devOri);

        var lines = new List<string>
        {
            $"Methode : {method}"
        };
        if (!string.IsNullOrWhiteSpace(id)) lines.Add($"Station : {id}");
        if (!string.IsNullOrWhiteSpace(E) || !string.IsNullOrWhiteSpace(N) || !string.IsNullOrWhiteSpace(H))
            lines.Add($"Coordonnees : E={E}  N={N}  H={H}");

        if (!string.IsNullOrWhiteSpace(corr) || !string.IsNullOrWhiteSpace(az) || !string.IsNullOrWhiteSpace(devOri) ||
            !string.IsNullOrWhiteSpace(devE) || !string.IsNullOrWhiteSpace(devN) || !string.IsNullOrWhiteSpace(devH) ||
            !string.IsNullOrWhiteSpace(scale))
        {
            string azDisp = string.IsNullOrWhiteSpace(az) ? "—" : az;
            string corrDisp = string.IsNullOrWhiteSpace(corr) ? "—" : corr;
            string scaleDisp = string.IsNullOrWhiteSpace(scale) ? "—" : scale;
            string devEDisp = string.IsNullOrWhiteSpace(devE) ? "—" : devE;
            string devNDisp = string.IsNullOrWhiteSpace(devN) ? "—" : devN;
            string devHDisp = string.IsNullOrWhiteSpace(devH) ? "—" : devH;
            string devODisp = string.IsNullOrWhiteSpace(devOri) ? "—" : devOri;
            lines.Add($"Corr. orientat° : {corrDisp} | Fact. échelle : {scaleDisp} | Dev.std E : {devEDisp} | N : {devNDisp} | Z : {devHDisp} | Ori : {devODisp}");
            lines.Add($"Orientation : CorrOri={corrDisp}  AzOri={azDisp}");
        }

        // Wrap lines to box width
        var f = NovatlasTheme.FontBody(8.8);
        double x = MarginL;
        double w = page.Width.Point - MarginL - MarginR;
        double pad = Units.MmToPt(3);
        double maxW = w - 2 * pad;

        var wrapped = new List<string>();
        foreach (var t in lines)
        {
            foreach (var wline in WrapToWidth(g, f, t, maxW))
                wrapped.Add(wline);
        }

        double lineH = Units.MmToPt(3.9);
        double boxH = Math.Max(Units.MmToPt(12), wrapped.Count * lineH + Units.MmToPt(5));

        var pen = new XPen(XColors.Black, 0.8);
        g.DrawRectangle(pen, x, y, w, boxH);

        double yy = y + Units.MmToPt(6);
        foreach (var t in wrapped)
        {
            g.DrawString(t, f, XBrushes.Black, new XRect(x + pad, yy - Units.MmToPt(2.6), maxW, lineH), XStringFormats.TopLeft);
            yy += lineH;
        }

        return y + boxH + Units.MmToPt(2);
    }

    private static List<string[]> ReadObservations(JsonElement run)
    {
        var rows = new List<string[]>();
        if (run.ValueKind != JsonValueKind.Object) return rows;
        if (!run.TryGetProperty("observations", out var obsEl) || obsEl.ValueKind != JsonValueKind.Array) return rows;

        foreach (var o in obsEl.EnumerateArray())
        {
            if (o.ValueKind != JsonValueKind.Object) continue;
            string id = StripAt(GetAnyString(o, "id"));
            rows.Add(new[]
            {
                id,
                Fmt3(GetAnyString(o, "hz")),
                Fmt3(GetAnyString(o, "vz")),
                Fmt3(GetAnyString(o, "dp")),
                Fmt3(GetAnyString(o, "hr")),
                // IMPORTANT: Constante prisme (ne pas arrondir à 3 décimales)
                Fmt4(GetAnyString(o, "prismConst", "constPrisme", "reflectorConstant"))
            });
        }
        return rows;
    }

    private static List<string[]> ReadResiduals(JsonElement run)
    {
        var rows = new List<string[]>();
        if (run.ValueKind != JsonValueKind.Object) return rows;
        if (!run.TryGetProperty("residuals", out var resEl) || resEl.ValueKind != JsonValueKind.Array) return rows;

        foreach (var r in resEl.EnumerateArray())
        {
            if (r.ValueKind != JsonValueKind.Object) continue;
            string used = GetAnyString(r, "used");
            if (!string.IsNullOrWhiteSpace(used) && used.Trim().Equals("non", StringComparison.OrdinalIgnoreCase))
                continue;

            rows.Add(new[]
            {
                StripAt(GetAnyString(r, "id")),
                Fmt3(GetAnyString(r, "dHz")),
                Fmt3(GetAnyString(r, "dAlti")),
                Fmt3(GetAnyString(r, "dDH")),
                used
            });
        }
        return rows;
    }

    private static PdfPage AddPage(PdfDocument doc)
    {
        var p = doc.AddPage();
        p.Size = PdfSharp.PageSize.A4;
        return p;
    }

    private static double EnsurePage(PdfDocument doc, ref PdfPage page, ref XGraphics g, double y, double needH)
    {
        double limit = page.Height.Point - MarginB - LayoutConstants.FooterReservePt; // zone de sécurité pour le pied de page
        if (y + needH <= limit) return y;

        g.Dispose();
        page = AddPage(doc);
        g = XGraphics.FromPdfPage(page);
        // After first page, start a bit higher (like jsPDF). Logo will be added later via Append.
        return Units.MmToPt(24);
    }

    private static double EnsureRoomForLastFooter(PdfDocument doc, ref PdfPage page, ref XGraphics g, double y, double minFree)
    {
        double limit = page.Height.Point - MarginB - minFree;
        if (y <= limit) return y;

        g.Dispose();
        page = AddPage(doc);
        g = XGraphics.FromPdfPage(page);
        return Units.MmToPt(24);
    }

    // internal (pas private) : réutilisée par CoverOnlyReportRenderer pour l'export
    // "Page de garde" - même en-tête/cartouche/pied de page que le PDF Station.
    internal static void DrawFooterAllPages(XGraphics g, PdfPage page, int pageIndex, int totalPages, string buildFooter)
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

    private static double DrawRepeatHeaderLogo(XGraphics g, PdfPage page)
    {
        // small logo top-right on pages >1
        var logo = NovatlasTheme.TryLoadLogo();
        if (logo == null) return Units.MmToPt(24);

        double w = Units.MmToPt(26);
        double h = Units.MmToPt(12);
        double x = page.Width.Point - MarginR - w;
        double y = Units.MmToPt(6);
        g.DrawImage(logo, x, y, w, h);
        return Units.MmToPt(24);
    }

    private static double DrawLightBar(XGraphics g, PdfPage page, double y, string text, XColor fill)
    {
        double h = Units.MmToPt(7.5);
        g.DrawRectangle(new XSolidBrush(fill), MarginL, y, page.Width.Point - MarginL - MarginR, h);
        g.DrawString(text, NovatlasTheme.FontBold(10.5), XBrushes.Black,
            new XRect(MarginL, y, page.Width.Point - MarginL - MarginR, h), XStringFormats.Center);
        return y + h;
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

	    // PdfSharp 6.1: prefer parameterless GetHeight() (the overload with XGraphics is obsolete)
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

        // header row
        g.DrawRectangle(headerBrush, x, y, tableW, headerHeight);
        g.DrawRectangle(pen, x, y, tableW, headerHeight);
        for (int c = 0; c < cols; c++)
        {
            if (c > 0) g.DrawLine(pen, x + c * colW, y, x + c * colW, y + headerHeight);
            g.DrawString(header[c] ?? "", headFont, XBrushes.Black,
                new XRect(x + c * colW + 2, y + 2, colW - 4, headerHeight - 4), XStringFormats.TopLeft);
        }
        y += headerHeight;

        // rows
        foreach (var row in rows)
        {
            if (y + rowHeight > pageBottom)
            {
                g.Dispose();
                page = doc.AddPage();
                g = XGraphics.FromPdfPage(page);
                y = DrawRepeatHeaderLogo(g, page);
                pageBottom = page.Height.Point - MarginB - LayoutConstants.FooterReservePt;
                // repeat header on new page
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
                g.DrawString(cell, cellFont, XBrushes.Black,
                    new XRect(x + c * colW + 2, y + 2, colW - 4, rowHeight - 4), XStringFormats.TopLeft);
            }
            y += rowHeight;
        }

        y += gap;
    }




    private static void DrawFinalControlsAndSignatures(XGraphics g, PdfPage page, JsonElement root)
    {
        // Match the validated IMP footer (same geometry and headers).
        double w = page.Width.Point - MarginL - MarginR;

        // A4 reference ratios (from validated PDF):
        double yCtlTop = page.Height.Point * 0.73717218;
        double yCtlBot = page.Height.Point * 0.79076397;
        double yBoxesTopRef = page.Height.Point * 0.82440137;
        double yBoxesBotRef = page.Height.Point * 0.94583808;

        double gapH = Units.MmToPt(2);
        double boxesHRef = yBoxesBotRef - yBoxesTopRef;
        double yBoxesTop = yCtlBot + gapH;
        double yBoxesBot = yBoxesTop + boxesHRef;

        double controlesH = yCtlBot - yCtlTop;
        double boxesH = yBoxesBot - yBoxesTop;

        var pen = new XPen(XColors.Black, 0.8);
        var penThin = new XPen(LineGray, 0.6);
        var headBrush = new XSolidBrush(LightGray);

        var fHead = NovatlasTheme.FontBold(10);
        var fSmall = NovatlasTheme.FontBody(9);

        // === CONTROLES (full width) ===
        double y = yCtlTop;
        var rectCtl = new XRect(MarginL, y, w, controlesH);
        double headH = Units.MmToPt(6.5);
        g.DrawRectangle(headBrush, new XRect(rectCtl.Left, rectCtl.Top, rectCtl.Width, headH));
        g.DrawRectangle(pen, rectCtl);
        g.DrawLine(pen, rectCtl.Left, rectCtl.Top + headH, rectCtl.Right, rectCtl.Top + headH);
        g.DrawString("CONTRÔLES", fHead, XBrushes.Black, new XRect(rectCtl.Left, rectCtl.Top, rectCtl.Width, headH), XStringFormats.Center);

        var cc = ControlsCounter.ComputeForStation(root);

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
            g.DrawRectangle(headBrush, new XRect(r.Left, r.Top, r.Width, headH));
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

        void DrawSignLines(XRect r)
        {
            double yy = r.Top + headH;
            double contentH = (r.Height - headH);
            double row1 = contentH * 0.25;
            double row2 = contentH * 0.25;
            double row3 = contentH - row1 - row2;
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
                GetString(root, "info", "surveyor"),
                GetString(root, "surveyor"),
                GetString(root, "info", "geometre"),
                GetString(root, "geometre"),
                GetString(root, "info", "Geometre"),
                GetString(root, "Geometre"),
                GetString(root, "info", "Utilisateur"),
                GetString(root, "Utilisateur"),
                GetString(root, "user"),
                GetString(root, "info", "Intervenant"),
                GetString(root, "info", "intervenant"),
                GetString(root, "Intervenant"),
                GetString(root, "intervenant"),
                GetString(root, "info", "Operateur"),
                GetString(root, "info", "Opérateur"),
                GetString(root, "Operateur"),
                GetString(root, "Opérateur"),
                GetString(root, "info", "Operator"),
                GetString(root, "Operator")
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
                GetString(root, "signatureDataUrl"),
                GetString(root, "sigDataUrl"),
                GetString(root, "signature"),
                GetString(root, "sig"),
                GetString(root, "info", "signatureDataUrl"),
                GetString(root, "info", "sigDataUrl")
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

        try
        {
            string obs = FirstNonEmpty(
                GetString(root, "obs"),
                GetString(root, "observations"),
                GetString(root, "observation"),
                GetString(root, "info", "obs"),
                GetString(root, "info", "observations"),
                GetString(root, "info", "observation"),
                GetString(root, "validation", "observations")
            );
            if (!string.IsNullOrWhiteSpace(obs))
            {
                var rectTxt = new XRect(rectObs.Left + Units.MmToPt(3), rectObs.Top + headH + Units.MmToPt(2), rectObs.Width - Units.MmToPt(6), rectObs.Height - headH - Units.MmToPt(4));
                DrawWrappedText(g, obs, NovatlasTheme.FontBody(9), XBrushes.Black, rectTxt, 8);
            }
        }
        catch { /* ignore */ }
    }

    private static List<string> WrapToWidth(XGraphics g, XFont font, string text, double maxWidth)
    {
        var words = (text ?? "").Split(' ');
        var lines = new List<string>();
        string cur = "";
        foreach (var w in words)
        {
            string test = string.IsNullOrEmpty(cur) ? w : cur + " " + w;
            if (g.MeasureString(test, font).Width <= maxWidth)
            {
                cur = test;
                continue;
            }
            if (!string.IsNullOrEmpty(cur)) lines.Add(cur);
            cur = w;
        }
        if (!string.IsNullOrEmpty(cur)) lines.Add(cur);
        if (lines.Count == 0) lines.Add("");
        return lines;
    }

    private static void DrawWrappedText(XGraphics gfx, string text, XFont font, XBrush brush, XRect rect, int maxLines)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var lines = WrapToWidth(gfx, font, text, rect.Width);
        if (lines.Count > maxLines) lines = lines.Take(maxLines).ToList();
        double lineH = gfx.MeasureString("Ag", font).Height;
        for (int i = 0; i < lines.Count && i < maxLines; i++)
        {
            gfx.DrawString(lines[i], font, brush,
                new XRect(rect.Left, rect.Top + i * lineH, rect.Width, lineH),
                XStringFormats.TopLeft);
        }
    }

    private static bool LooksLikeSetupId(string? s)
        => !string.IsNullOrWhiteSpace(s) && s.Trim().StartsWith("TPSSetupID_", StringComparison.OrdinalIgnoreCase);

    private static string FirstNonEmptyNonSetupId(params string[] values)
    {
        string fallback = "";
        foreach (var v in values)
        {
            var s = (v ?? "").Trim();
            if (string.IsNullOrWhiteSpace(s)) continue;
            if (string.IsNullOrWhiteSpace(fallback)) fallback = s;
            if (!LooksLikeSetupId(s)) return s;
        }
        return fallback;
    }

    private static string GetAnyString(JsonElement el, params string[] keys)
    {
        foreach (var k in keys)
        {
            if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(k, out var v))
            {
                if (v.ValueKind == JsonValueKind.String) return v.GetString() ?? "";
                return v.ToString() ?? "";
            }
        }
        return "";
    }

    private static string Fmt3(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        // allow comma decimals
        var norm = s.Trim().Replace(',', '.');
        if (double.TryParse(norm, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return d.ToString("0.000", CultureInfo.InvariantCulture);
        return s;
    }

    // Constante prisme: conserver 4 décimales (ex: 0.017500 -> 0.0175)
    private static string Fmt4(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var norm = s.Trim().Replace(',', '.');
        if (double.TryParse(norm, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return d.ToString("0.0000", CultureInfo.InvariantCulture);
        return s;
    }

    internal static double DrawTopHeader(XGraphics g, PdfPage page, JsonElement root)
    {
        double contentW = page.Width.Point - MarginL - MarginR;

        // Boxes: left logo + right chantier
        double boxH = Units.MmToPt(26);
        double gap = Units.MmToPt(4);
        double leftW = Units.MmToPt(40);
        double rightW = contentW - leftW - gap;

        var penBox = new XPen(XColors.Black, 0.8);
        var rectLeft = new XRect(MarginL, MarginT, leftW, boxH);
        var rectRight = new XRect(MarginL + leftW + gap, MarginT, rightW, boxH);
        g.DrawRectangle(penBox, rectLeft);
        g.DrawRectangle(penBox, rectRight);

        var logo = NovatlasTheme.TryLoadLogo();
        if (logo != null)
        {
            double pad = Units.MmToPt(4);
            double maxW = rectLeft.Width - 2 * pad;
            double maxH = rectLeft.Height - 2 * pad;
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
            catch { }
            double lx = rectLeft.Left + (rectLeft.Width - w) / 2.0;
            double ly = rectLeft.Top + (rectLeft.Height - h) / 2.0;
            g.DrawImage(logo, lx, ly, w, h);
        }

        // Right box content (MÊME mise en page que IMP)
        DrawHeaderRightBox(g, rectRight, root);

        // Title band
        double bandY = rectLeft.Bottom + Units.MmToPt(4);
        double bandH = Units.MmToPt(12);
        g.DrawRectangle(new XSolidBrush(BrandBlue), MarginL, bandY, contentW, bandH);
        g.DrawString("RAPPORT D'INTERVENTION", NovatlasTheme.FontBold(12), XBrushes.White,
            new XRect(MarginL, bandY, contentW, bandH), XStringFormats.Center);

        // Intervention name box
        double boxY = bandY + bandH + Units.MmToPt(4);
        double nameH = Units.MmToPt(8);
        g.DrawRectangle(new XPen(XColors.Black, 0.8), MarginL, boxY, contentW, nameH);
        string interName = FirstNonEmpty(
            GetString(root, "reportTitle"),
            GetString(root, "title"),
            GetString(root, "nomIntervention"),
            GetString(root, "intervention")
        );
        if (string.Equals(GetString(root, "type"), "pdfsharp_station", StringComparison.OrdinalIgnoreCase))
            interName = "STATION";
        interName = (interName ?? "").ToUpperInvariant();
        g.DrawString(interName, NovatlasTheme.FontBold(11), XBrushes.Black,
            new XRect(MarginL, boxY, contentW, nameH), XStringFormats.Center);

        return boxY + nameH + Units.MmToPt(4);
    }

    private static void DrawHeaderRightBox(XGraphics gfx, XRect rect, JsonElement root)
    {
	        // Cadre droite : Ville (haut), Adresse (milieu), puis CHA (bas), centrés.
	        string ville = GetString(root, "ville");
	        string adr = GetString(root, "adresseChantier");
	        if (string.IsNullOrWhiteSpace(adr)) adr = GetString(root, "adresse");
	        if (string.IsNullOrWhiteSpace(adr)) adr = GetString(root, "siteAddress");
	        string chaRaw = GetString(root, "cha");
	        // Saisie UI = chiffres (ex: 02782). Dans le PDF on affiche "CHA02782".
	        string chaDigits = (chaRaw ?? "").Trim();
	        if (!string.IsNullOrWhiteSpace(chaDigits) && chaDigits.StartsWith("CHA", System.StringComparison.OrdinalIgnoreCase))
	            chaDigits = chaDigits.Substring(3);
	        chaDigits = chaDigits.Trim().TrimStart('-', '_', ':').Trim();
	        string cha = string.IsNullOrWhiteSpace(chaDigits) ? "" : ("CHA" + chaDigits);

	        var fVille = NovatlasTheme.FontBold(12);
	        var fAdr = NovatlasTheme.FontBody(11);
	        var fCha = NovatlasTheme.FontBody(10);
	        double pad = 8;
	        double x = rect.Left + pad;
	        double w = rect.Width - 2 * pad;
	        double yTop = rect.Top + pad;
	        double yBottom = rect.Bottom - pad;

	        // Mesures simples pour centrer verticalement.
	        double hVille = string.IsNullOrWhiteSpace(ville) ? 0 : fVille.GetHeight();
	        double hAdr = string.IsNullOrWhiteSpace(adr) ? 0 : fAdr.GetHeight();
	        double hCha = string.IsNullOrWhiteSpace(cha) ? 0 : fCha.GetHeight();
	        double gap1 = (hVille > 0 && hAdr > 0) ? 2 : 0;
	        double gap2 = ((hVille > 0 || hAdr > 0) && hCha > 0) ? 3 : 0;
	        double total = hVille + gap1 + hAdr + gap2 + hCha;
	        double startY = yTop + ((yBottom - yTop) - total) / 2.0;

	        double yy = startY;
	        if (!string.IsNullOrWhiteSpace(ville))
	        {
	            gfx.DrawString(ville, fVille, XBrushes.Black, new XRect(x, yy, w, hVille + 2), XStringFormats.TopCenter);
	            yy += hVille + gap1;
	        }
	        if (!string.IsNullOrWhiteSpace(adr))
	        {
	            gfx.DrawString(adr, fAdr, XBrushes.Black, new XRect(x, yy, w, hAdr + 2), XStringFormats.TopCenter);
	            yy += hAdr + gap2;
	        }
	        if (!string.IsNullOrWhiteSpace(cha))
	        {
	            gfx.DrawString(cha, fCha, XBrushes.Black, new XRect(x, yy, w, hCha + 2), XStringFormats.TopCenter);
	        }
    }

    internal static double DrawInfoCartouche(XGraphics g, PdfPage page, double y, JsonElement root)
    {
        // Match validated IMP splits (including the explicit red-line separator at 0.676*w)
        double w = page.Width.Point - MarginL - MarginR;
        double rowH = Units.MmToPt(14);

        double x0 = MarginL;
        double xR = x0 + w;
        double x1 = x0 + w * 0.295;
        double rightW = xR - x1;

        double x2Common = x0 + w * 0.676;       // Entreprise/Contact + Appareil/Intervenant
        double x2Row2 = x1 + rightW * 0.30;     // PPM/Plan

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

        // Appareil model + serial
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
            appareil = string.IsNullOrWhiteSpace(appareil) ? appareilSerial : $"{appareil} – {appareilSerial}";

        string intervenant = GetString(root, "intervenant");
        if (string.IsNullOrWhiteSpace(intervenant)) intervenant = GetString(root, "operator");
        if (string.IsNullOrWhiteSpace(intervenant)) intervenant = GetString(root, "operateur");

        // Row 1
        Cell(x0, y, x1, "Intervention du", date);
        Cell(x1, y, x2Common, "Entreprise", entreprise);
        Cell(x2Common, y, xR, "Contact client", contact);
        y += rowH;

        // Row 2
        Cell(x0, y, x1, "Système de coordonnées", coordSys);
        Cell(x1, y, x2Row2, "PPM", ppm);
        Cell(x2Row2, y, xR, "Plan de référence", planRef);
        y += rowH;

        // Row 3
        Cell(x0, y, x1, "Système altimétrique", altSys);
        Cell(x1, y, x2Common, "Appareil", appareil);
        Cell(x2Common, y, xR, "Intervenant", intervenant);
        y += rowH + Units.MmToPt(4);

        return y;
    }

    private static double DrawBar(XGraphics g, PdfPage page, double y, string title, XColor fill)
    {
        double h = Units.MmToPt(7);
        g.DrawRectangle(new XSolidBrush(fill), MarginL, y, page.Width.Point - MarginL - MarginR, h);
        g.DrawString(title, NovatlasTheme.FontBold(9.5), XBrushes.Black,
            new XRect(MarginL, y, page.Width.Point - MarginL - MarginR, h), XStringFormats.Center);
        return y + h + Units.MmToPt(2);
    }

    private static void DrawFooter(XGraphics g, PdfPage page, string buildFooter)
    {
        string txt = buildFooter ?? "";
        if (string.IsNullOrWhiteSpace(txt)) return;
        double y = page.Height.Point - MarginB + Units.MmToPt(10);
        g.DrawString(txt, NovatlasTheme.FontBody(7.5), XBrushes.Gray,
            new XRect(MarginL, y, page.Width.Point - MarginL - MarginR, Units.MmToPt(5)),
            XStringFormats.BottomLeft);
    }


private static double GetRefAltiSectionHeight(JsonElement root)
{
    if (root.ValueKind != JsonValueKind.Object)
        return 0;
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
    if (root.ValueKind != JsonValueKind.Object)
        return y;
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
        var vals = new[] { StripAt(GetString(p, "id")), Fmt3(FirstNonEmpty(GetString(p, "E"), GetString(p, "x"))), Fmt3(FirstNonEmpty(GetString(p, "N"), GetString(p, "y"))), Fmt3(FirstNonEmpty(GetString(p, "H"), GetString(p, "z"))) };
        for (int i = 0; i < vals.Length; i++)
            g.DrawString(vals[i] ?? "", fVal, XBrushes.Black, new XRect(xs[i] + 2, yy, xs[i + 1] - xs[i] - 4, rowH), XStringFormats.CenterLeft);
    }

    return y + totalH + Units.MmToPt(4);
}
    private static string GetString(JsonElement el, params string[] path)
    {
        try
        {
            JsonElement cur = el;
            foreach (var p in path)
            {
                if (cur.ValueKind != JsonValueKind.Object || !cur.TryGetProperty(p, out cur)) return "";
            }
            if (cur.ValueKind == JsonValueKind.String) return cur.GetString() ?? "";
            return cur.ToString() ?? "";
        }
        catch { return ""; }
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

    private static string StripAt(string? value)
    {
        var s = value ?? "";
        var i = s.IndexOf('@');
        return i >= 0 ? s.Substring(0, i) : s;
    }

}
