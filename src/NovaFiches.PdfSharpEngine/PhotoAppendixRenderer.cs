using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace NovaFiches.PdfSharpEngine;

internal static class PhotoAppendixRenderer
{
    private const double MarginL = 36;
    private const double MarginR = 36;
    private static readonly XColor BrandBlue = XColor.FromArgb(18, 103, 243);
    private static readonly XColor LineGray = XColor.FromArgb(200, 200, 200);

    internal sealed record PhotoItem(string ModuleLabel, string Name, string Caption, string ImageData);

    public static void AppendFromPayload(PdfDocument doc, string payloadJson, string buildFooter)
    {
        var photos = ReadPhotos(payloadJson).ToList();
        if (photos.Count == 0) return;
        Append(doc, photos, buildFooter);
    }

    public static void RenderStandaloneReport(PdfDocument doc, string payloadJson, string buildFooter)
    {
        var info = ReadInfo(payloadJson);
        var photosPerPage = ReadPhotosPerPage(payloadJson);
        var photos = ReadPhotos(payloadJson)
            .Select(p => p with { ModuleLabel = "Reportage photo" })
            .ToList();
        if (photos.Count == 0) return;
        Append(doc, photos, buildFooter, "REPORTAGE PHOTOS", info, photosPerPage);
    }

    private static int ReadPhotosPerPage(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return 4;
        try
        {
            using var jd = JsonDocument.Parse(payloadJson);
            var root = jd.RootElement;
            if (root.TryGetProperty("photoAppendix", out var appendix)
                && appendix.ValueKind == JsonValueKind.Object
                && appendix.TryGetProperty("photosPerPage", out var v)
                && v.TryGetInt32(out var n)
                && n >= 1 && n <= 4)
                return n;
        }
        catch { }
        return 4;
    }

    private static Dictionary<string, string> ReadInfo(string payloadJson)
    {
        var info = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(payloadJson)) return info;
        JsonDocument? jd = null;
        try { jd = JsonDocument.Parse(payloadJson); }
        catch { return info; }
        using (jd)
        {
            var root = jd.RootElement;
            if (!root.TryGetProperty("info", out var obj) || obj.ValueKind != JsonValueKind.Object) return info;
            foreach (var p in obj.EnumerateObject())
            {
                var value = p.Value.ValueKind == JsonValueKind.String ? (p.Value.GetString() ?? "") : p.Value.ToString();
                if (!string.IsNullOrWhiteSpace(value)) info[p.Name] = value.Trim();
            }
        }
        return info;
    }

    private static void DrawStandaloneCover(PdfDocument doc, Dictionary<string, string> info, string buildFooter)
    {
        var page = doc.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        using var g = XGraphics.FromPdfPage(page);

        DrawHeader(g, page, "Parametres projet", "REPORTAGE PHOTOS");

        double x = Units.MmToPt(16);
        double y = Units.MmToPt(40);
        double w = page.Width.Point - Units.MmToPt(32);
        double rowH = Units.MmToPt(8);
        var keyFont = NovatlasTheme.FontBold(9);
        var valFont = NovatlasTheme.FontBody(9);
        var pen = new XPen(LineGray, 0.5);

        var rows = new (string Label, string Value)[]
        {
            ("Intervention", First(GetInfo(info, "elements"), GetInfo(info, "repElements"))),
            ("Type de plan", First(GetInfo(info, "planType"), GetInfo(info, "typePlan"))),
            ("Ville / site", First(GetInfo(info, "ville"), GetInfo(info, "zone"))),
            ("Adresse", First(GetInfo(info, "siteAddress"), GetInfo(info, "adresseChantier"), GetInfo(info, "adresse"))),
            ("Client", GetInfo(info, "client")),
            ("CHA", GetInfo(info, "cha")),
            ("Date", GetInfo(info, "date")),
            ("Phase", GetInfo(info, "phase")),
            ("Type", First(GetInfo(info, "typeDoc"), GetInfo(info, "type"))),
            ("Zone", First(GetInfo(info, "cartoucheZone"), GetInfo(info, "zoneCartouche"))),
            ("Indice", GetInfo(info, "indice")),
            ("Coordonnees", GetInfo(info, "coordSystem")),
            ("Altimetrie", GetInfo(info, "altimetricSystem")),
            ("PPM", GetInfo(info, "ppm")),
            ("Intervenant", First(GetInfo(info, "intervenant"), GetInfo(info, "surveyor"))),
            ("Commentaire", GetInfo(info, "obs"))
        }.Where(r => !string.IsNullOrWhiteSpace(r.Value)).ToArray();

        g.DrawString("PARAMETRES DU PROJET", NovatlasTheme.FontBold(13), XBrushes.Black,
            new XRect(x, y, w, Units.MmToPt(10)), XStringFormats.CenterLeft);
        y += Units.MmToPt(13);

        foreach (var row in rows)
        {
            if (y + rowH > page.Height.Point - Units.MmToPt(28)) break;
            g.DrawRectangle(pen, x, y, w, rowH);
            g.DrawString(row.Label, keyFont, XBrushes.Black,
                new XRect(x + Units.MmToPt(3), y + Units.MmToPt(1), Units.MmToPt(42), rowH), XStringFormats.CenterLeft);
            g.DrawString(row.Value, valFont, XBrushes.Black,
                new XRect(x + Units.MmToPt(48), y + Units.MmToPt(1), w - Units.MmToPt(51), rowH), XStringFormats.CenterLeft);
            y += rowH;
        }

        DrawFooter(g, page, buildFooter);
    }

    private static string GetInfo(Dictionary<string, string> info, string key)
    {
        return info.TryGetValue(key, out var v) ? (v ?? "") : "";
    }

    private static IEnumerable<PhotoItem> ReadPhotos(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) yield break;
        JsonDocument? jd = null;
        try { jd = JsonDocument.Parse(payloadJson); }
        catch { yield break; }
        using (jd)
        {
            var root = jd.RootElement;
            if (!root.TryGetProperty("photoAppendix", out var appendix) || appendix.ValueKind != JsonValueKind.Object)
                yield break;
            if (!appendix.TryGetProperty("photos", out var arr) || arr.ValueKind != JsonValueKind.Array)
                yield break;

            foreach (var p in arr.EnumerateArray())
            {
                var image = Get(p, "imageData");
                if (string.IsNullOrWhiteSpace(image)) continue;
                yield return new PhotoItem(
                    First(Get(p, "moduleLabel"), "Photos"),
                    Get(p, "name"),
                    Get(p, "caption"),
                    image);
            }
        }
    }

    private static void Append(PdfDocument doc, List<PhotoItem> photos, string buildFooter, string pageTitle = "ANNEXE PHOTOS", Dictionary<string, string>? reportInfo = null, int photosPerPage = 4)
    {
        photosPerPage = Math.Clamp(photosPerPage, 1, 4);
        int index = 0;
        while (index < photos.Count)
        {
            var page = doc.AddPage();
            page.Size = PdfSharp.PageSize.A4;
            using var g = XGraphics.FromPdfPage(page);
            DrawPhotoPage(g, page, photos.Skip(index).Take(photosPerPage).ToList(), buildFooter, pageTitle, reportInfo, photosPerPage);
            index += photosPerPage;
        }

        RestampFooters(doc, buildFooter);
    }

    private static void DrawPhotoPage(XGraphics g, PdfPage page, List<PhotoItem> photos, string buildFooter, string pageTitle, Dictionary<string, string>? reportInfo, int photosPerPage)
    {
        double top;
        if (reportInfo != null)
        {
            top = DrawStandardReportHeader(g, page, reportInfo) + Units.MmToPt(6);
        }
        else
        {
            DrawHeader(g, page, photos.FirstOrDefault()?.ModuleLabel ?? "Photos", pageTitle);
            top = Units.MmToPt(38);
        }
        DrawFooter(g, page, buildFooter);

        double left = Units.MmToPt(10);
        double gapX = Units.MmToPt(8);
        double gapY = Units.MmToPt(8);
        double footerReserve = Units.MmToPt(22);
        double usableW = page.Width.Point - left * 2;
        double usableH = page.Height.Point - top - footerReserve - Units.MmToPt(8);
        double captionH = Units.MmToPt(10);
        var slots = BuildPhotoSlots(photosPerPage, left, top, usableW, usableH, gapX, gapY);

        for (int i = 0; i < photos.Count; i++)
        {
            var slot = slots[Math.Min(i, slots.Count - 1)];
            var imageBox = new XRect(slot.Left, slot.Top, slot.Width, Math.Max(Units.MmToPt(20), slot.Height - captionH));
            var captionBox = new XRect(slot.Left, imageBox.Bottom + Units.MmToPt(2), slot.Width, captionH - Units.MmToPt(2));

            g.DrawRectangle(new XPen(LineGray, 0.6), imageBox);
            DrawImage(g, photos[i].ImageData, imageBox);

            var caption = First(photos[i].Caption, photos[i].Name);
            g.DrawString(caption, NovatlasTheme.FontBody(8), XBrushes.Black, captionBox, XStringFormats.TopLeft);
        }
    }

    private static List<XRect> BuildPhotoSlots(int photosPerPage, double left, double top, double usableW, double usableH, double gapX, double gapY)
    {
        photosPerPage = Math.Clamp(photosPerPage, 1, 4);
        if (photosPerPage == 1)
            return new List<XRect> { new(left, top, usableW, usableH) };

        if (photosPerPage == 2)
        {
            double h = (usableH - gapY) / 2.0;
            return new List<XRect>
            {
                new(left, top, usableW, h),
                new(left, top + h + gapY, usableW, h)
            };
        }

        if (photosPerPage == 3)
        {
            double topH = (usableH - gapY) * 0.52;
            double bottomH = usableH - gapY - topH;
            double bottomW = (usableW - gapX) / 2.0;
            return new List<XRect>
            {
                new(left, top, usableW, topH),
                new(left, top + topH + gapY, bottomW, bottomH),
                new(left + bottomW + gapX, top + topH + gapY, bottomW, bottomH)
            };
        }

        double cellW = (usableW - gapX) / 2.0;
        double cellH = (usableH - gapY) / 2.0;
        return new List<XRect>
        {
            new(left, top, cellW, cellH),
            new(left + cellW + gapX, top, cellW, cellH),
            new(left, top + cellH + gapY, cellW, cellH),
            new(left + cellW + gapX, top + cellH + gapY, cellW, cellH)
        };
    }

    private static double DrawStandardReportHeader(XGraphics g, PdfPage page, Dictionary<string, string> info)
    {
        double y = Units.MmToPt(6);
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
        var rectLeft = new XRect(MarginL, y, leftW, boxH);
        var rectRight = new XRect(MarginL + leftW + gap, y, rightW, boxH);
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
                double ar = (double)logo.PixelWidth / Math.Max(1, logo.PixelHeight);
                h = w / ar;
                if (h > maxH) { h = maxH; w = h * ar; }
            }
            catch { }
            g.DrawImage(logo, rectLeft.Left + (rectLeft.Width - w) / 2.0, rectLeft.Top + (rectLeft.Height - h) / 2.0, w, h);
        }

        DrawStandardHeaderRightBox(g, rectRight, info);

        double bandY = rectLeft.Bottom + Units.MmToPt(4);
        double bandH = Units.MmToPt(12);
        g.DrawRectangle(new XSolidBrush(BrandBlue), MarginL, bandY, contentW, bandH);
        g.DrawString("RAPPORT D'INTERVENTION", NovatlasTheme.FontBold(12), XBrushes.White,
            new XRect(MarginL, bandY, contentW, bandH), XStringFormats.Center);

        double boxY = bandY + bandH + Units.MmToPt(4);
        double nameH = Units.MmToPt(8);
        g.DrawRectangle(new XPen(XColors.Black, 0.8), MarginL, boxY, contentW, nameH);

        string interName = First(GetInfo(info, "elements"), GetInfo(info, "intervention"), GetInfo(info, "chantier")).ToUpperInvariant();
        g.DrawString(interName, NovatlasTheme.FontBold(11), XBrushes.Black,
            new XRect(MarginL, boxY, contentW, nameH), XStringFormats.Center);

        return DrawStandardInfoCartouche(g, page, boxY + nameH + Units.MmToPt(6), info);
    }

    private static double DrawStandardInfoCartouche(XGraphics g, PdfPage page, double y, Dictionary<string, string> info)
    {
        double x = MarginL;
        double w = page.Width.Point - MarginL - MarginR;
        double rowH = Units.MmToPt(14);
        double h = rowH * 3;
        var pen = new XPen(XColors.Black, 0.8);
        var fLabel = NovatlasTheme.FontBody(10.0);
        var fValue = NovatlasTheme.FontBold(10.5);

        g.DrawRectangle(pen, x, y, w, h);

        double c1 = w * 0.295;
        double c2 = w * 0.382;
        double c3 = w - c1 - c2;
        double r1 = y + rowH;
        double r2 = y + rowH * 2;

        g.DrawLine(pen, x, r1, x + w, r1);
        g.DrawLine(pen, x, r2, x + w, r2);

        // Row 1: Intervention du | Entreprise | Contact client
        g.DrawLine(pen, x + c1, y, x + c1, r1);
        g.DrawLine(pen, x + c1 + c2, y, x + c1 + c2, r1);

        // Row 2: Systeme coordonnees | PPM | Plan reference
        double r2c1 = c1;
        double r2c2 = w * 0.212;
        g.DrawLine(pen, x + r2c1, r1, x + r2c1, r2);
        g.DrawLine(pen, x + r2c1 + r2c2, r1, x + r2c1 + r2c2, r2);

        // Row 3: Systeme altimetrique | Appareil | Intervenant
        double r3c1 = c1;
        double r3c2 = w * 0.382;
        g.DrawLine(pen, x + r3c1, r2, x + r3c1, y + h);
        g.DrawLine(pen, x + r3c1 + r3c2, r2, x + r3c1 + r3c2, y + h);

        void Label(XRect rect, string text) =>
            g.DrawString(text, fLabel, XBrushes.Black, rect, XStringFormats.TopCenter);

        void Value(XRect rect, string text) =>
            g.DrawString(text ?? "", fValue, XBrushes.Black, rect, XStringFormats.Center);

        double padTop = Units.MmToPt(1.5);
        var row1a = new XRect(x, y + padTop, c1, rowH);
        var row1b = new XRect(x + c1, y + padTop, c2, rowH);
        var row1c = new XRect(x + c1 + c2, y + padTop, c3, rowH);
        Label(row1a, "Intervention du");
        Label(row1b, "Entreprise");
        Label(row1c, "Contact client");
        Value(new XRect(row1a.Left, row1a.Top + Units.MmToPt(4), row1a.Width, Units.MmToPt(8)), GetInfo(info, "date"));
        Value(new XRect(row1b.Left, row1b.Top + Units.MmToPt(4), row1b.Width, Units.MmToPt(8)), GetInfo(info, "client"));
        Value(new XRect(row1c.Left, row1c.Top + Units.MmToPt(4), row1c.Width, Units.MmToPt(8)), GetInfo(info, "siteContact"));

        var row2a = new XRect(x, r1 + padTop, r2c1, rowH);
        var row2b = new XRect(x + r2c1, r1 + padTop, r2c2, rowH);
        var row2c = new XRect(x + r2c1 + r2c2, r1 + padTop, w - r2c1 - r2c2, rowH);
        Label(row2a, "Système de coordonnées");
        Label(row2b, "PPM");
        Label(row2c, "Plan de référence");
        Value(new XRect(row2a.Left, row2a.Top + Units.MmToPt(4), row2a.Width, Units.MmToPt(8)), GetInfo(info, "coordSystem"));
        Value(new XRect(row2b.Left, row2b.Top + Units.MmToPt(4), row2b.Width, Units.MmToPt(8)), GetInfo(info, "ppm"));
        Value(new XRect(row2c.Left, row2c.Top + Units.MmToPt(4), row2c.Width, Units.MmToPt(8)), First(GetInfo(info, "planRef"), GetInfo(info, "dwg")));

        var row3a = new XRect(x, r2 + padTop, r3c1, rowH);
        var row3b = new XRect(x + r3c1, r2 + padTop, r3c2, rowH);
        var row3c = new XRect(x + r3c1 + r3c2, r2 + padTop, w - r3c1 - r3c2, rowH);
        Label(row3a, "Système altimétrique");
        Label(row3b, "Appareil");
        Label(row3c, "Intervenant");
        Value(new XRect(row3a.Left, row3a.Top + Units.MmToPt(4), row3a.Width, Units.MmToPt(8)), GetInfo(info, "altimetricSystem"));
        Value(new XRect(row3b.Left, row3b.Top + Units.MmToPt(4), row3b.Width, Units.MmToPt(8)), First(GetInfo(info, "instrument"), GetInfo(info, "serial")));
        Value(new XRect(row3c.Left, row3c.Top + Units.MmToPt(4), row3c.Width, Units.MmToPt(8)), First(GetInfo(info, "intervenant"), GetInfo(info, "surveyor")));

        return y + h + Units.MmToPt(6);
    }

    private static void DrawStandardHeaderRightBox(XGraphics g, XRect rect, Dictionary<string, string> info)
    {
        string ville = First(GetInfo(info, "ville"), GetInfo(info, "zone"));
        string adresse = First(GetInfo(info, "siteAddress"), GetInfo(info, "adresseChantier"), GetInfo(info, "adresse"));
        string cha = GetInfo(info, "cha");
        if (!string.IsNullOrWhiteSpace(cha) && !cha.StartsWith("CHA", StringComparison.OrdinalIgnoreCase))
            cha = "CHA " + cha;

        double pad = Units.MmToPt(3);
        g.DrawString(ville, NovatlasTheme.FontBold(12), XBrushes.Black,
            new XRect(rect.Left + pad, rect.Top + Units.MmToPt(2.5), rect.Width - 2 * pad, Units.MmToPt(6)), XStringFormats.CenterLeft);
        g.DrawString(adresse, NovatlasTheme.FontBody(11), XBrushes.Black,
            new XRect(rect.Left + pad, rect.Top + Units.MmToPt(9), rect.Width - 2 * pad, Units.MmToPt(6)), XStringFormats.CenterLeft);
        g.DrawString(cha, NovatlasTheme.FontBody(10), XBrushes.Black,
            new XRect(rect.Left + pad, rect.Top + Units.MmToPt(15.5), rect.Width - 2 * pad, Units.MmToPt(5)), XStringFormats.CenterLeft);
    }

    private static void DrawHeader(XGraphics g, PdfPage page, string moduleLabel, string pageTitle)
    {
        double x = Units.MmToPt(10);
        double y = Units.MmToPt(7);
        double w = page.Width.Point - Units.MmToPt(20);
        double logoW = Units.MmToPt(42);
        double h = Units.MmToPt(18);

        g.DrawRectangle(new XPen(XColors.Black, 0.8), x, y, logoW, h);
        var logo = NovatlasTheme.TryLoadLogo();
        if (logo != null)
        {
            double pad = Units.MmToPt(3);
            double maxW = logoW - pad * 2;
            double maxH = h - pad * 2;
            double ar = (double)logo.PixelWidth / Math.Max(1, logo.PixelHeight);
            double iw = maxW;
            double ih = iw / ar;
            if (ih > maxH) { ih = maxH; iw = ih * ar; }
            g.DrawImage(logo, x + (logoW - iw) / 2.0, y + (h - ih) / 2.0, iw, ih);
        }

        double titleX = x + logoW + Units.MmToPt(5);
        double titleW = w - logoW - Units.MmToPt(5);
        g.DrawRectangle(new XSolidBrush(BrandBlue), titleX, y, titleW, Units.MmToPt(8));
        g.DrawString(string.IsNullOrWhiteSpace(pageTitle) ? "ANNEXE PHOTOS" : pageTitle, NovatlasTheme.FontBold(11), XBrushes.White,
            new XRect(titleX, y, titleW, Units.MmToPt(8)), XStringFormats.Center);
        g.DrawRectangle(new XPen(XColors.Black, 0.8), titleX, y + Units.MmToPt(8), titleW, Units.MmToPt(10));
        g.DrawString((moduleLabel ?? "Photos").ToUpperInvariant(), NovatlasTheme.FontBold(10), XBrushes.Black,
            new XRect(titleX, y + Units.MmToPt(8), titleW, Units.MmToPt(10)), XStringFormats.Center);
    }

    private static void DrawFooter(XGraphics g, PdfPage page, string buildFooter)
    {
        double yLine = page.Height.Point - Units.MmToPt(14);
        g.DrawLine(new XPen(LineGray, 0.4), MarginL, yLine, page.Width.Point - MarginR, yLine);

        g.DrawString(NovatlasTheme.NovatlasAddress, NovatlasTheme.FontBody(9), XBrushes.Black,
            new XRect(MarginL, yLine + Units.MmToPt(2.5), page.Width.Point - MarginL - MarginR, Units.MmToPt(5)),
            XStringFormats.Center);

        if (!string.IsNullOrWhiteSpace(buildFooter))
        {
            g.DrawString(buildFooter, NovatlasTheme.FontBody(8), XBrushes.Black,
                new XRect(MarginL, yLine + Units.MmToPt(6.5), page.Width.Point - MarginL - MarginR, Units.MmToPt(4.5)),
                XStringFormats.Center);
        }
    }

    private static void DrawImage(XGraphics g, string dataUrl, XRect box)
    {
        try
        {
            var bytes = DecodeDataUrl(dataUrl);
            if (bytes == null || bytes.Length == 0) return;
            using var ms = new MemoryStream(bytes);
            using var img = XImage.FromStream(ms);
            if (img.PixelWidth <= 0 || img.PixelHeight <= 0) return;
            double s = Math.Min(box.Width / img.PixelWidth, box.Height / img.PixelHeight);
            double w = img.PixelWidth * s;
            double h = img.PixelHeight * s;
            double x = box.Left + (box.Width - w) / 2.0;
            double y = box.Top + (box.Height - h) / 2.0;
            g.DrawImage(img, x, y, w, h);
        }
        catch { }
    }

    private static void RestampFooters(PdfDocument doc, string buildFooter)
    {
        int total = doc.PageCount;
        for (int i = 0; i < total; i++)
        {
            var page = doc.Pages[i];
            using var g = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            double yLine = page.Height.Point - Units.MmToPt(14);
            double wipeY = yLine - Units.MmToPt(1);
            g.DrawRectangle(XBrushes.White, new XRect(0, wipeY, page.Width.Point, page.Height.Point - wipeY));
            g.DrawLine(new XPen(LineGray, 0.4), MarginL, yLine, page.Width.Point - MarginR, yLine);

            g.DrawString(NovatlasTheme.NovatlasAddress, NovatlasTheme.FontBody(9), XBrushes.Black,
                new XRect(MarginL, yLine + Units.MmToPt(2.5), page.Width.Point - MarginL - MarginR, Units.MmToPt(5)),
                XStringFormats.Center);

            if (!string.IsNullOrWhiteSpace(buildFooter))
            {
                g.DrawString(buildFooter, NovatlasTheme.FontBody(8), XBrushes.Black,
                    new XRect(MarginL, yLine + Units.MmToPt(6.5), page.Width.Point - MarginL - MarginR, Units.MmToPt(4.5)),
                    XStringFormats.Center);
            }

            g.DrawString($"Page {i + 1} / {total}", NovatlasTheme.FontBody(9), XBrushes.Black,
                new XRect(MarginL, yLine + Units.MmToPt(2.5), page.Width.Point - MarginL - MarginR, Units.MmToPt(5)),
                XStringFormats.CenterRight);
        }
    }

    private static byte[]? DecodeDataUrl(string s)
    {
        try
        {
            s = (s ?? "").Trim();
            int idx = s.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0) s = s[(idx + "base64,".Length)..];
            return Convert.FromBase64String(s);
        }
        catch { return null; }
    }

    private static string Get(JsonElement el, string key)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(key, out var v)) return "";
        return v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : v.ToString();
    }

    private static string First(params string[] vals)
    {
        foreach (var v in vals)
            if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
        return "";
    }
}
