using PdfSharp.Drawing;
using PdfSharp.Pdf;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace NovaFiches.PdfSharpEngine;

internal static class HeightTransferReportRenderer
{
    private static readonly XColor BrandBlue = XColor.FromArgb(18, 103, 243);
    private static readonly XColor LightGray = XColor.FromArgb(230, 230, 230);
    private static readonly XColor LineGray = XColor.FromArgb(200, 200, 200);

    private const double MarginL = 36;
    private const double MarginR = 36;
    private const double MarginT = 28;
    private const double MarginB = 34;

    internal static void Render(PdfDocument doc, string payloadJson, string buildFooter)
    {
        using var json = JsonDocument.Parse(payloadJson);
        var root = json.RootElement;
        var transfers = ReadArray(root, "heightTransfers").ToList();

        var page = AddPage(doc);
        XGraphics? g = null;
        try
        {
            g = XGraphics.FromPdfPage(page);
            double y = MarginT;
            y = DrawHeader(g, page, root, y);

            if (transfers.Count == 0)
            {
                y = EnsurePage(doc, ref page, ref g, y, 40);
                g.DrawString("Aucun transfert d'altitude detecte dans le LandXML.", Font(10), XBrushes.Black,
                    new XRect(MarginL, y, page.Width.Point - MarginL - MarginR, 18), XStringFormats.TopLeft);
            }
            else
            {
                foreach (var tr in transfers)
                    y = DrawTransfer(doc, ref page, ref g, y, tr);
            }

            y = EnsureRoomForFinalBlocks(doc, ref page, ref g, y);
            DrawFinalControlsAndSignatures(g, page, root, transfers);

            g.Dispose();
            g = null;
        }
        finally
        {
            g?.Dispose();
        }

        int total = doc.PageCount;
        for (int i = 0; i < total; i++)
        {
            var p = doc.Pages[i];
            using var gg = XGraphics.FromPdfPage(p, XGraphicsPdfPageOptions.Append);
            DrawFooter(gg, p, i + 1, total, buildFooter);
        }
    }

    private static double DrawTransfer(PdfDocument doc, ref PdfPage page, ref XGraphics g, double y, JsonElement tr)
    {
        y = EnsurePage(doc, ref page, ref g, y, 120);
        y = Bar(g, page, y, "TRANSFERT D'ALTITUDE", NovatlasTheme.Orange, XBrushes.White);

        string station = First(Get(tr, "stationName"), Get(tr, "setupId"));
        string setup = Get(tr, "setupId");
        string zInitial = Fmt(First(Get(tr, "stationHOriginal"), Get(tr, "H")));
        string calc = Fmt(Get(tr, "calcHgt"));
        string std = Fmt(Get(tr, "stdDevHgt"), 4);
        string max = "";
        if (tr.TryGetProperty("analysis", out var an) && an.ValueKind == JsonValueKind.Object)
            max = Fmt(Get(an, "maxDeltaHgt"), 4);

        var lines = new[]
        {
            $"Station : {station}    Setup : {setup}",
            $"Methode : transfert d'altitude Leica (heightTransfer)",
            $"Z station initial : {zInitial}    Z station calcule : {calc}    Ecart type H : {std}    Ecart max reference : {max}"
        };
        y = BoxText(g, page, y, lines);
        y += 8;

        y = LightBar(g, page, y, "ANALYSE / CONTROLE");
        var refs = ReadArray(tr, "references").ToList();
        var measured = ReadArray(tr, "measuredPoints").ToList();
        var analysis = $"La station {station} a ete traitee en transfert d'altitude. " +
                       $"{refs.Count} point(s) de reference altimetrique ont ete utilises en controle 1D. " +
                       $"L'altitude calculee est {calc} avec un ecart type H de {std}. " +
                       $"{measured.Count} point(s) ont ensuite ete mesures depuis cette altitude transferee.";
        y = Paragraph(g, page, y, analysis);
        y += 6;

        y = LightBar(g, page, y, "REFERENCES ALTIMETRIQUES");
        var refRows = refs.Select(r =>
        {
            var p = Obj(r, "point");
            var o = Obj(r, "observation");
            return new[]
            {
                Get(r, "id"),
                First(Get(r, "useKind"), "1D"),
                Fmt(Get(p, "H")),
                calc,
                Fmt(Get(r, "deltaHgt"), 4),
                Fmt(Get(o, "hz"), 4),
                Fmt(Get(o, "vz"), 4),
                Fmt(Get(o, "dp")),
                Fmt(Get(o, "hr"))
            };
        }).ToList();
        y = Table(doc, ref page, ref g, y, new[] { "Point", "Utilise", "Z ref", "Z station calc", "Delta H", "Hz", "Vz", "Dp", "Hr" }, refRows);
        y += 8;

        y = LightBar(g, page, y, "POINTS MESURES APRES TRANSFERT");
        var measRows = measured.Select(m =>
        {
            var p = Obj(m, "point");
            var o = Obj(m, "observation");
            return new[]
            {
                Get(m, "id"),
                Fmt(Get(p, "E")),
                Fmt(Get(p, "N")),
                Fmt(Get(p, "H")),
                calc,
                Fmt(Get(o, "hz"), 4),
                Fmt(Get(o, "vz"), 4),
                Fmt(Get(o, "dp")),
                Fmt(Get(o, "hr"))
            };
        }).ToList();
        y = Table(doc, ref page, ref g, y, new[] { "Point", "X", "Y", "Z mesure", "Z station calc", "Hz", "Vz", "Dp", "Hr" }, measRows);
        return y + 12;
    }

    private static PdfPage AddPage(PdfDocument doc)
    {
        var p = doc.AddPage();
        p.Size = PdfSharp.PageSize.A4;
        return p;
    }

    private static double EnsurePage(PdfDocument doc, ref PdfPage page, ref XGraphics g, double y, double need)
    {
        if (y + need <= page.Height.Point - MarginB) return y;
        g.Dispose();
        page = AddPage(doc);
        g = XGraphics.FromPdfPage(page);
        return DrawRepeatHeaderLogo(g, page);
    }

    private static double EnsureRoomForFinalBlocks(PdfDocument doc, ref PdfPage page, ref XGraphics g, double y)
    {
        double top = page.Height.Point * 0.73717218;
        if (y <= top - 10) return y;
        g.Dispose();
        page = AddPage(doc);
        g = XGraphics.FromPdfPage(page);
        return DrawRepeatHeaderLogo(g, page);
    }

    private static double DrawHeader(XGraphics g, PdfPage page, JsonElement root, double y)
    {
        double topY = Units.MmToPt(6);
        double boxH = Units.MmToPt(22);
        double contentW = page.Width.Point - MarginL - MarginR;
        double gap = Units.MmToPt(6);

        double leftW = Units.MmToPt(65);
        if (leftW > contentW * 0.45) leftW = contentW * 0.45;
        double rightW = contentW - leftW - gap;
        if (rightW < Units.MmToPt(60))
        {
            leftW = contentW * 0.35;
            rightW = contentW - leftW - gap;
        }

        var penBox = new XPen(XColors.Black, 0.8);
        var rectLeft = new XRect(MarginL, topY, leftW, boxH);
        var rectRight = new XRect(MarginL + leftW + gap, topY, rightW, boxH);

        g.DrawRectangle(penBox, rectLeft);
        g.DrawRectangle(penBox, rectRight);

        var logo = NovatlasTheme.TryLoadLogo();
        if (logo != null)
        {
            double pad = Units.MmToPt(4);
            double maxW = rectLeft.Width - 2 * pad;
            double maxH = rectLeft.Height - 2 * pad;
            double w = maxW;
            double h = maxH;
            try
            {
                double ar = (double)logo.PixelWidth / (double)logo.PixelHeight;
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
            try { g.DrawImage(logo, lx, ly, w, h); } catch { }
        }

        DrawHeaderRightBox(g, rectRight, root);

        double bandY = rectLeft.Bottom + Units.MmToPt(4);
        double bandH = Units.MmToPt(12);
        g.DrawRectangle(new XSolidBrush(BrandBlue), MarginL, bandY, contentW, bandH);
        g.DrawString("RAPPORT D'INTERVENTION", NovatlasTheme.FontBold(12), XBrushes.White,
            new XRect(MarginL, bandY, contentW, bandH), XStringFormats.Center);

        double boxY = bandY + bandH + Units.MmToPt(4);
        double nameH = Units.MmToPt(8);
        g.DrawRectangle(new XPen(XColors.Black, 0.8), MarginL, boxY, contentW, nameH);
        g.DrawString("TRANSFERT D'ALTITUDE", NovatlasTheme.FontBold(11), XBrushes.Black,
            new XRect(MarginL, boxY, contentW, nameH), XStringFormats.Center);

        return DrawInfoCartouche(g, page, boxY + nameH + Units.MmToPt(4), root);
    }

    private static void DrawHeaderRightBox(XGraphics gfx, XRect rect, JsonElement root)
    {
        string ville = GetStr(root, "ville");
        string adr = GetStr(root, "adresseChantier");
        if (string.IsNullOrWhiteSpace(adr)) adr = GetStr(root, "adresse");
        if (string.IsNullOrWhiteSpace(adr)) adr = GetStr(root, "siteAddress");
        string cha = GetStr(root, "cha");

        var fontVille = NovatlasTheme.FontBold(12);
        var fontAdresse = NovatlasTheme.FontBold(11);
        var fontCha = NovatlasTheme.FontBody(10);
        double pad = 8;
        double usableW = rect.Width - 2 * pad;
        double hVille = gfx.MeasureString("Ag", fontVille).Height;
        double hAdr = gfx.MeasureString("Ag", fontAdresse).Height;
        double hCha = gfx.MeasureString("Ag", fontCha).Height;
        double gap = 2;

        string lVille = (ville ?? "").Trim();
        string lAdr = (adr ?? "").Trim();
        string chaDigits = (cha ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(chaDigits) && chaDigits.StartsWith("CHA", StringComparison.OrdinalIgnoreCase))
            chaDigits = chaDigits.Substring(3);
        chaDigits = (chaDigits ?? "").Trim().TrimStart('-', '_', ':').Trim();
        string lCha = string.IsNullOrWhiteSpace(chaDigits) ? "" : ("CHA" + chaDigits);

        bool hasVille = !string.IsNullOrWhiteSpace(lVille);
        bool hasAdr = !string.IsNullOrWhiteSpace(lAdr);
        bool hasCha = !string.IsNullOrWhiteSpace(lCha);
        double totalH = (hasVille ? hVille : 0)
            + ((hasVille && hasAdr) ? gap : 0) + (hasAdr ? hAdr : 0)
            + (((hasVille || hasAdr) && hasCha) ? gap : 0) + (hasCha ? hCha : 0);

        double yy = rect.Top + (rect.Height - totalH) / 2.0;
        if (hasVille)
        {
            gfx.DrawString(lVille, fontVille, XBrushes.Black, new XRect(rect.Left + pad, yy, usableW, hVille), XStringFormats.TopCenter);
            yy += hVille + (hasAdr ? gap : (hasCha ? gap : 0));
        }
        if (hasAdr)
        {
            gfx.DrawString(lAdr, fontAdresse, XBrushes.Black, new XRect(rect.Left + pad, yy, usableW, hAdr), XStringFormats.TopCenter);
            yy += hAdr + (hasCha ? gap : 0);
        }
        if (hasCha)
            gfx.DrawString(lCha, fontCha, XBrushes.Black, new XRect(rect.Left + pad, yy, usableW, hCha), XStringFormats.TopCenter);
    }

    private static double DrawInfoCartouche(XGraphics g, PdfPage page, double y, JsonElement root)
    {
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
            if (!string.IsNullOrWhiteSpace(label))
                g.DrawString(label, fLabel, XBrushes.Black,
                    new XRect(xL, yy + Units.MmToPt(1.0), ww, Units.MmToPt(6.0)),
                    XStringFormats.TopCenter);
            if (!string.IsNullOrWhiteSpace(value))
            {
                var rectVal = new XRect(xL, yy + Units.MmToPt(6.0), ww, rowH - Units.MmToPt(7.0));
                if (label.StartsWith("Plan de", StringComparison.OrdinalIgnoreCase))
                    TextFitHelper.DrawCenteredWrapped(g, rectVal, value, size => NovatlasTheme.FontBold(size), 11.0, 7.5, 2);
                else
                    g.DrawString(value, fValue, XBrushes.Black, rectVal, XStringFormats.Center);
            }
        }

        string date = GetStr(root, "date");
        string entreprise = GetStr(root, "entreprise");
        string contact = GetStr(root, "contactClient");
        string coordSys = GetStr(root, "systemeCoord");
        string ppm = GetStr(root, "ppm");
        if (!string.IsNullOrWhiteSpace(ppm) && !ppm.Contains("mm/km", StringComparison.OrdinalIgnoreCase))
            ppm = ppm.Trim() + " mm/km";
        string planRef = GetStr(root, "planRef");
        string altSys = GetStr(root, "systemeAlti");
        string appareil = First(GetStr(root, "appareil"), GetStr(root, "appareilModel"), GetStr(root, "model"));
        string serial = First(GetStr(root, "serialNumber"), GetStr(root, "appareilSerial"), GetStr(root, "serial"), GetStr(root, "numeroSerie"), GetStr(root, "numero_série"));
        if (!string.IsNullOrWhiteSpace(serial))
            appareil = string.IsNullOrWhiteSpace(appareil) ? serial : $"{appareil} – {serial}";
        string intervenant = First(GetStr(root, "intervenant"), GetStr(root, "operator"), GetStr(root, "operateur"));

        Cell(x0, y, x1, "Intervention du", date);
        Cell(x1, y, x2Row1, "Entreprise", entreprise);
        Cell(x2Row1, y, xR, "Contact client", contact);
        y += rowH;
        Cell(x0, y, x1, "Système de coordonnées", coordSys);
        Cell(x1, y, x2Row2, "PPM", ppm);
        Cell(x2Row2, y, xR, "Plan de référence", planRef);
        y += rowH;
        Cell(x0, y, x1, "Système altimétrique", altSys);
        Cell(x1, y, x2Row3, "Appareil", appareil);
        Cell(x2Row3, y, xR, "Intervenant", intervenant);
        return y + rowH + Units.MmToPt(4);
    }

    private static double DrawRepeatHeaderLogo(XGraphics g, PdfPage page)
    {
        double y = Units.MmToPt(6);
        var logo = NovatlasTheme.TryLoadLogo();
        if (logo != null)
        {
            double maxW = Units.MmToPt(28);
            double maxH = Units.MmToPt(12);
            double w = maxW;
            double h = maxH;
            try
            {
                double ar = (double)logo.PixelWidth / (double)logo.PixelHeight;
                h = w / ar;
                if (h > maxH)
                {
                    h = maxH;
                    w = h * ar;
                }
            }
            catch { }
            double x = page.Width.Point - MarginR - w;
            try { g.DrawImage(logo, x, y, w, h); } catch { }
            y += h + Units.MmToPt(6);
        }
        return y;
    }

    private static double Bar(XGraphics g, PdfPage page, double y, string text, XColor color, XBrush brush)
    {
        double w = page.Width.Point - MarginL - MarginR;
        g.DrawRectangle(new XSolidBrush(color), MarginL, y, w, 20);
        g.DrawString(text, Font(8, true), brush, new XRect(MarginL, y, w, 20), XStringFormats.Center);
        return y + 22;
    }

    private static double LightBar(XGraphics g, PdfPage page, double y, string text)
        => Bar(g, page, y, text, XColor.FromArgb(230, 230, 230), XBrushes.Black);

    private static double BoxText(XGraphics g, PdfPage page, double y, IEnumerable<string> lines)
    {
        double w = page.Width.Point - MarginL - MarginR;
        double h = 16 + lines.Count() * 13;
        g.DrawRectangle(XPens.Black, MarginL, y, w, h);
        double yy = y + 10;
        foreach (var l in lines)
        {
            g.DrawString(l, Font(8.5), XBrushes.Black, new XRect(MarginL + 8, yy, w - 16, 12), XStringFormats.TopLeft);
            yy += 13;
        }
        return y + h;
    }

    private static double Paragraph(XGraphics g, PdfPage page, double y, string text)
    {
        double w = page.Width.Point - MarginL - MarginR;
        var lines = Wrap(g, Font(8.5), text, w - 12);
        foreach (var l in lines)
        {
            g.DrawString(l, Font(8.5), XBrushes.Black, new XRect(MarginL + 6, y, w - 12, 12), XStringFormats.TopLeft);
            y += 12;
        }
        return y;
    }

    private static double Table(PdfDocument doc, ref PdfPage page, ref XGraphics g, double y, string[] headers, List<string[]> rows)
    {
        if (rows.Count == 0)
        {
            var empty = Enumerable.Repeat("", headers.Length).ToArray();
            empty[0] = "Aucune donnee";
            rows.Add(empty);
        }
        rows = rows.Select(r =>
        {
            var padded = Enumerable.Repeat("", headers.Length).ToArray();
            for (int i = 0; i < headers.Length && i < r.Length; i++)
                padded[i] = r[i] ?? "";
            return padded;
        }).ToList();
        double w = page.Width.Point - MarginL - MarginR;
        double rowH = 17;
        double colW = w / headers.Length;
        y = EnsurePage(doc, ref page, ref g, y, rowH * 2);
        DrawRow(g, y, headers, colW, rowH, true);
        y += rowH;
        foreach (var r in rows)
        {
            y = EnsurePage(doc, ref page, ref g, y, rowH);
            DrawRow(g, y, r, colW, rowH, false);
            y += rowH;
        }
        return y;
    }

    private static void DrawRow(XGraphics g, double y, string[] cells, double colW, double rowH, bool header)
    {
        var brush = header ? new XSolidBrush(XColor.FromArgb(235, 242, 252)) : XBrushes.White;
        int cellCount = Math.Max(1, cells.Length);
        for (int i = 0; i < cellCount; i++)
        {
            double x = MarginL + i * colW;
            g.DrawRectangle(brush, x, y, colW, rowH);
            g.DrawRectangle(XPens.Black, x, y, colW, rowH);
            TextFitHelper.DrawCenteredWrapped(g, new XRect(x + 2, y + 2, colW - 4, rowH - 4), cells[i] ?? "", s => Font(s, header), 7.2, 5.5, 1);
        }
    }

    private static void DrawFinalControlsAndSignatures(XGraphics g, PdfPage page, JsonElement root, List<JsonElement> transfers)
    {
        double w = page.Width.Point - MarginL - MarginR;
        double yCtlTop = page.Height.Point * 0.73717218;
        double yCtlBot = page.Height.Point * 0.79076397;
        double yBoxesTop = yCtlBot + Units.MmToPt(2);
        double yBoxesBot = page.Height.Point * 0.94583808;

        var pen = new XPen(XColors.Black, 0.8);
        var penThin = new XPen(XColor.FromArgb(200, 200, 200), 0.6);
        var headBrush = new XSolidBrush(XColor.FromArgb(230, 230, 230));
        var fHead = Font(10, true);
        var fSmall = Font(8.5);
        double headH = Units.MmToPt(6.5);

        var rectCtl = new XRect(MarginL, yCtlTop, w, yCtlBot - yCtlTop);
        g.DrawRectangle(headBrush, new XRect(rectCtl.Left, rectCtl.Top, rectCtl.Width, headH));
        g.DrawRectangle(pen, rectCtl);
        g.DrawLine(pen, rectCtl.Left, rectCtl.Top + headH, rectCtl.Right, rectCtl.Top + headH);
        g.DrawString("CONTROLES", fHead, XBrushes.Black, new XRect(rectCtl.Left, rectCtl.Top, rectCtl.Width, headH), XStringFormats.Center);

        int stationCount = transfers.Count;
        int refCount = transfers.Sum(t => ReadArray(t, "references").Count());
        int measuredCount = transfers.Sum(t => ReadArray(t, "measuredPoints").Count());
        string counts = $"Stations transferees : {stationCount}    References altimetriques : {refCount}    Points mesures apres transfert : {measuredCount}";
        g.DrawString(counts, fSmall, XBrushes.Black,
            new XRect(rectCtl.Left + Units.MmToPt(3), rectCtl.Top + headH, rectCtl.Width - Units.MmToPt(6), rectCtl.Height - headH),
            XStringFormats.CenterLeft);

        double gap = Units.MmToPt(2);
        double boxW = (w - 2 * gap) / 3.0;
        double boxH = yBoxesBot - yBoxesTop;
        var rectObs = new XRect(MarginL, yBoxesTop, boxW, boxH);
        var rectReal = new XRect(MarginL + boxW + gap, yBoxesTop, boxW, boxH);
        var rectVal = new XRect(MarginL + 2 * (boxW + gap), yBoxesTop, boxW, boxH);

        void Header(XRect r, string title)
        {
            g.DrawRectangle(headBrush, new XRect(r.Left, r.Top, r.Width, headH));
            g.DrawRectangle(pen, r);
            g.DrawLine(pen, r.Left, r.Top + headH, r.Right, r.Top + headH);
            g.DrawString(title, fHead, XBrushes.Black, new XRect(r.Left, r.Top, r.Width, headH), XStringFormats.Center);
        }

        Header(rectObs, "OBSERVATIONS");
        Header(rectReal, "REALISE PAR");
        Header(rectVal, "VALIDE PAR (client)");

        void SignLines(XRect r)
        {
            double yy = r.Top + headH;
            double contentH = r.Height - headH;
            double row1 = contentH * 0.25;
            double row2 = contentH * 0.25;
            g.DrawLine(penThin, r.Left, yy + row1, r.Right, yy + row1);
            g.DrawLine(penThin, r.Left, yy + row1 + row2, r.Right, yy + row1 + row2);
            g.DrawString("Nom :", fSmall, XBrushes.Black, new XRect(r.Left + Units.MmToPt(3), yy + Units.MmToPt(2), r.Width, row1), XStringFormats.TopLeft);
            g.DrawString("Date :", fSmall, XBrushes.Black, new XRect(r.Left + Units.MmToPt(3), yy + row1 + Units.MmToPt(2), r.Width, row2), XStringFormats.TopLeft);
            g.DrawString("Visa :", fSmall, XBrushes.Black, new XRect(r.Left + Units.MmToPt(3), yy + row1 + row2 + Units.MmToPt(2), r.Width, contentH - row1 - row2), XStringFormats.TopLeft);
        }

        SignLines(rectReal);
        SignLines(rectVal);

        var operatorName = First(Get(root, "intervenant"), Get(root, "surveyor"), Get(root, "operateur"), Get(root, "operator"));
        if (!string.IsNullOrWhiteSpace(operatorName))
        {
            double yy = rectReal.Top + headH;
            double contentH = rectReal.Height - headH;
            double row1 = contentH * 0.25;
            double labelW = Units.MmToPt(18);
            g.DrawString(operatorName, fSmall, XBrushes.Black,
                new XRect(rectReal.Left + Units.MmToPt(3) + labelW, yy + Units.MmToPt(2), rectReal.Width - Units.MmToPt(6) - labelW, row1),
                XStringFormats.TopLeft);
            g.DrawString(DateTime.Now.ToString("dd/MM/yyyy"), fSmall, XBrushes.Black,
                new XRect(rectReal.Left + Units.MmToPt(3) + labelW, yy + row1 + Units.MmToPt(2), rectReal.Width - Units.MmToPt(6) - labelW, row1),
                XStringFormats.TopLeft);
        }
    }

    private static void DrawFooter(XGraphics g, PdfPage page, int p, int total, string buildFooter)
    {
        double y = page.Height.Point - 24;
        g.DrawString("NOVATLAS - 24 boulevard Paul Vaillant Couturier - 94200 IVRY SUR SEINE", Font(7), XBrushes.Black,
            new XRect(MarginL, y, page.Width.Point - MarginL - MarginR, 9), XStringFormats.Center);
        g.DrawString($"{buildFooter}    Page {p} / {total}", Font(7), XBrushes.Black,
            new XRect(MarginL, y + 10, page.Width.Point - MarginL - MarginR, 9), XStringFormats.Center);
    }

    private static IEnumerable<JsonElement> ReadArray(JsonElement root, string name)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array)
            foreach (var e in a.EnumerateArray()) yield return e;
    }

    private static JsonElement Obj(JsonElement root, string name)
        => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var o) && o.ValueKind == JsonValueKind.Object ? o : default;

    private static string GetStr(JsonElement root, string key)
    {
        try
        {
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(key, out var v))
                return v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : v.ToString();

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("cartouche", out var c) && c.ValueKind == JsonValueKind.Object && c.TryGetProperty(key, out var vc))
                    return vc.ValueKind == JsonValueKind.String ? (vc.GetString() ?? "") : vc.ToString();
                if (root.TryGetProperty("project", out var p) && p.ValueKind == JsonValueKind.Object && p.TryGetProperty(key, out var vp))
                    return vp.ValueKind == JsonValueKind.String ? (vp.GetString() ?? "") : vp.ToString();
                if (root.TryGetProperty("info", out var info) && info.ValueKind == JsonValueKind.Object && info.TryGetProperty(key, out var vi))
                    return vi.ValueKind == JsonValueKind.String ? (vi.GetString() ?? "") : vi.ToString();
            }
        }
        catch { }
        return "";
    }

    private static string Get(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var v)) return "";
        return v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : v.ToString();
    }

    private static string First(params string[] vals) => vals.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";

    private static string Fmt(string v, int dec = 3)
    {
        if (double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            return d.ToString("F" + dec, CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(v) ? "" : v;
    }

    private static XFont Font(double size, bool bold = false)
        => bold ? NovatlasTheme.FontBodyBold(size) : NovatlasTheme.FontBody(size);

    private static List<string> Wrap(XGraphics g, XFont font, string text, double maxW)
    {
        var words = (text ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var cur = "";
        foreach (var word in words)
        {
            var test = string.IsNullOrWhiteSpace(cur) ? word : cur + " " + word;
            if (g.MeasureString(test, font).Width <= maxW) cur = test;
            else { if (!string.IsNullOrWhiteSpace(cur)) lines.Add(cur); cur = word; }
        }
        if (!string.IsNullOrWhiteSpace(cur)) lines.Add(cur);
        return lines;
    }
}
