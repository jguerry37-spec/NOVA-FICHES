using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace NovaFiches.PdfSharpEngine;

/// <summary>
/// Rapport "Points topo (levé)".
/// Objectif : conserver la même charte / mise en page que les autres rapports PdfSharp,
/// et ne changer que les tableaux (observations polaires + résultats rectangulaires).
/// </summary>
internal static class PointsTopoReportRenderer
{
    public static byte[] Render(byte[] payloadJson, string? exportsDir)
    {
        using var doc = JsonDocument.Parse(payloadJson);
        var root = doc.RootElement;

        using var pdf = new PdfDocument();
        pdf.Info.Title = "NOVA - Points topo (levé)";

        // Logo NOVATLAS
        // NOTE: this report is generated from JSON produced by the WebView. Depending on the
        // caller/version, some blocks (like "info") can be missing. We keep the renderer tolerant.
        // For the logo, reuse the shared loader used by other PdfSharp reports.
        // IMPORTANT: with PDFsharp, XImage resources must stay alive until after pdf.Save(...)
        var novaLogoImage = NovatlasTheme.TryLoadLogo();

        // topoStations : tableau de stations (même logique que Station, mais multi-pages)
        var stations = GetArray(root, "topoStations").ToList();
        if (stations.Count == 0)
        {
            // fallback : certaines anciennes versions utilisaient "stations"
            stations = GetArray(root, "stations").ToList();
        }

        // Si aucune station, on génère quand même une page "vide" (cartouche + message)
        if (stations.Count == 0)
        {
            var page = pdf.AddPage();
            page.Size = PdfSharp.PageSize.A4;
            using var g = XGraphics.FromPdfPage(page);
            var pageW = page.Width.Point;
            var y = Units.MmToPt(10);
            DrawHeaderAndCartouche(g, page, root, ref y, "POINTS TOPO (LEVÉ)", novaLogoImage);
            y += Units.MmToPt(10);
            g.DrawString(
                "Aucune station / observation trouvée.",
                Font(10, true),
                XBrushes.Black,
                new XRect(Units.MmToPt(15), y, pageW - Units.MmToPt(30), Units.MmToPt(10)),
                XStringFormats.TopLeft);
            DrawFooter(g, page, 1, 1);
        }
        else
        {
            for (int i = 0; i < stations.Count; i++)
            {
                var st = stations[i];
                var page = pdf.AddPage();
                page.Size = PdfSharp.PageSize.A4;

                using var g = XGraphics.FromPdfPage(page);
                var y = Units.MmToPt(10);

                DrawHeaderAndCartouche(g, page, root, ref y, "POINTS TOPO (LEVÉ)", novaLogoImage);
                y += Units.MmToPt(2);

                DrawTypeStationBlock(g, page, st, ref y);
                y += Units.MmToPt(3);

                DrawPointsTopoBlocks(g, page, st, ref y);

                DrawFooter(g, page, i + 1, stations.Count);
            }
        }

        using var ms = new MemoryStream();
        pdf.Save(ms, false);

        // IMPORTANT: do NOT dispose the shared cached logo (NovatlasTheme caches it).

        // Sauvegarde optionnelle dans Exports (cohérent avec les autres rapports)
        if (!string.IsNullOrWhiteSpace(exportsDir))
        {
            try
            {
                Directory.CreateDirectory(exportsDir);
                var fileName = GetStr(root, "fileName") ?? "NOVA_PointsTopo_PdfSharp.pdf";
                var outPath = Path.Combine(exportsDir, fileName);
                File.WriteAllBytes(outPath, ms.ToArray());
            }
            catch
            {
                // on n'échoue pas la génération si l'écriture n'est pas possible
            }
        }

        return ms.ToArray();
    }

    // Compatibility overload: some callers pass a "build/footer" string like other PdfSharp renderers.
    // We don't need it here (footer is already drawn), so we ignore it.
    public static byte[] Render(byte[] payloadJson, string? exportsDir, string? _buildFooter)
        => Render(payloadJson, exportsDir);

    // =========================
    // Layout: header/cartouche
    // =========================

    private static void DrawHeaderAndCartouche(
        XGraphics g,
        PdfPage page,
        JsonElement root,
        ref double y,
        string reportTitle,
        XImage? logoImage)
    {
        var pageW = page.Width.Point;

        // --- Bandeau logos (même gabarit que les autres rapports)
        var leftX = Units.MmToPt(15);
        var topY = y;
        var boxH = Units.MmToPt(18);
        var boxW = (pageW - Units.MmToPt(30) - Units.MmToPt(2)) / 2;

        var r1 = new XRect(leftX, topY, boxW, boxH);
        var r2 = new XRect(leftX + boxW + Units.MmToPt(2), topY, boxW, boxH);
        g.DrawRectangle(XPens.Black, r1);
        g.DrawRectangle(XPens.Black, r2);

        // Logo NOVATLAS (si dispo)
        // IMPORTANT: ne pas disposer l'image avant pdf.Save(), sinon PDFsharp peut lever
        // "Operation is not valid due to the current state of the object" au moment du Save.
        if (logoImage is not null)
        {
            try
            {
                var pad = Units.MmToPt(2);
                var iw = r1.Width - pad * 2;
                var ih = r1.Height - pad * 2;
                g.DrawImage(logoImage, r1.X + pad, r1.Y + pad, iw, ih);
            }
            catch
            {
                // optional
            }
        }

        y += boxH + Units.MmToPt(6);

        // --- Barre bleue "RAPPORT D'INTERVENTION"
        var barH = Units.MmToPt(10);
        var bar = new XRect(leftX, y, pageW - Units.MmToPt(30), barH);
        g.DrawRectangle(new XSolidBrush(NovatlasTheme.Blue), bar);
        g.DrawString("RAPPORT D'INTERVENTION", Font(10, true), XBrushes.White, bar, XStringFormats.Center);
        y += barH + Units.MmToPt(3);

        // --- Sous-titre (cadre)
        var sub = new XRect(leftX, y, pageW - Units.MmToPt(30), Units.MmToPt(8));
        g.DrawRectangle(XPens.Black, sub);
        g.DrawString(reportTitle, Font(9, true), XBrushes.Black, sub, XStringFormats.Center);
        y += sub.Height + Units.MmToPt(4);

        // --- Cartouche 3 colonnes x 3 lignes (comme Implantation/Station)
        var cart = GetObj(root, "info");

        var cartX = leftX;
        var cartW = pageW - Units.MmToPt(30);
        var rowH = Units.MmToPt(10);
        var rows = 4;
        var cartH = rowH * rows;
        var cartR = new XRect(cartX, y, cartW, cartH);
        g.DrawRectangle(XPens.Black, cartR);

        // colonnes: 1/3 + 1/3 + 1/3
        var c1 = cartW / 3;
        var c2 = cartW / 3;
        var c3 = cartW - c1 - c2;

        // lignes
        for (int r = 1; r < rows; r++)
            g.DrawLine(XPens.Black, cartX, y + rowH * r, cartX + cartW, y + rowH * r);

        // colonnes (sur toutes les lignes)
        g.DrawLine(XPens.Black, cartX + c1, y, cartX + c1, y + cartH);
        g.DrawLine(XPens.Black, cartX + c1 + c2, y, cartX + c1 + c2, y + cartH);

        // ligne 1 : Intervention du | Entreprise | Contact client
        DrawCartCell(g, cartX, y, c1, rowH, "Intervention du", GetStr(cart, "Date") ?? GetStr(cart, "date") ?? "");
        DrawCartCell(g, cartX + c1, y, c2, rowH, "Entreprise", GetStr(cart, "Entreprise") ?? "");
        DrawCartCell(g, cartX + c1 + c2, y, c3, rowH, "Contact client", GetStr(cart, "Client") ?? "");

        // ligne 2 : Système de coordonnées | PPM | Plan de référence
        DrawCartCell(g, cartX, y + rowH, c1, rowH, "Système de coordonnées", GetStr(cart, "SystemeCoordonnees") ?? GetStr(cart, "SystemeCoord") ?? "");
        DrawCartCell(g, cartX + c1, y + rowH, c2, rowH, "PPM", GetStr(cart, "PPM") ?? "");
        DrawCartCell(g, cartX + c1 + c2, y + rowH, c3, rowH, "Plan de référence", GetStr(cart, "PlanReference") ?? "");

        // ligne 3 : Système altimétrique | Appareil | Intervenant
        DrawCartCell(g, cartX, y + rowH * 2, c1, rowH, "Système altimétrique", GetStr(cart, "SystemeAltimetrique") ?? GetStr(cart, "SystemeAlti") ?? "");
        DrawCartCell(g, cartX + c1, y + rowH * 2, c2, rowH, "Appareil", GetStr(cart, "Appareil") ?? "");
        DrawCartCell(g, cartX + c1 + c2, y + rowH * 2, c3, rowH, "Intervenant", GetStr(cart, "Intervenant") ?? "");

        // ligne 4 : vides (réservé / compat future)
        DrawCartCell(g, cartX, y + rowH * 3, c1, rowH, "", "");
        DrawCartCell(g, cartX + c1, y + rowH * 3, c2, rowH, "", "");
        DrawCartCell(g, cartX + c1 + c2, y + rowH * 3, c3, rowH, "", "");

        y += cartH + Units.MmToPt(5);
    }

    private static void DrawCartCell(XGraphics g, double x, double y, double w, double h, string label, string value)
    {
        var labelRect = new XRect(x, y, w, h / 2);
        var valueRect = new XRect(x, y + h / 2, w, h / 2);
        g.DrawString(label, Font(7, true), XBrushes.Black, labelRect, XStringFormats.Center);
        var v = value ?? "";
        if (!string.IsNullOrWhiteSpace(label) && label.StartsWith("Plan de", StringComparison.OrdinalIgnoreCase))
        {
            // Guarantee no overflow for long DWG filenames: wrap + shrink + ellipsis.
            TextFitHelper.DrawCenteredWrapped(
                g,
                valueRect,
                v,
                size => Font(size, false),
                startFontSize: 8.0,
                minFontSize: 6.5,
                maxLines: 2);
        }
        else
        {
            g.DrawString(v, Font(8, false), XBrushes.Black, valueRect, XStringFormats.Center);
        }
    }

    // =========================
    // Type station + tableaux
    // =========================

    private static void DrawTypeStationBlock(XGraphics g, PdfPage page, JsonElement station, ref double y)
    {
        var leftX = Units.MmToPt(15);
        var w = page.Width.Point - Units.MmToPt(30);

        // Barre orange "TYPE DE STATION" (identique aux autres rapports)
        var barH = Units.MmToPt(8);
        var bar = new XRect(leftX, y, w, barH);
        g.DrawRectangle(new XSolidBrush(NovatlasTheme.Orange), bar);
        g.DrawString("TYPE DE STATION", Font(8, true), XBrushes.White, bar, XStringFormats.Center);
        y += barH;

        // Bloc infos station (cadre)
        var infoH = Units.MmToPt(18);
        var r = new XRect(leftX, y, w, infoH);
        g.DrawRectangle(XPens.Black, r);

        var lines = new List<string>();
        var method = GetAnyString(station, "method", "methode", "Methode") ?? "";
        if (string.IsNullOrWhiteSpace(method)) method = "Station libre";
        var stName = GetAnyString(station, "stationName", "station", "Station", "setupId") ?? "";
        if (string.IsNullOrWhiteSpace(stName) && station.ValueKind == JsonValueKind.Object && station.TryGetProperty("results", out var stRes) && stRes.ValueKind == JsonValueKind.Object)
            stName = GetAnyString(stRes, "idStation", "stationName", "setupId") ?? "";
        var coords = GetAnyString(station, "coordinates", "Coordonnees") ?? "";
        if (string.IsNullOrWhiteSpace(coords) && station.ValueKind == JsonValueKind.Object)
        {
            string e = GetAnyString(station, "E", "X") ?? "";
            string n = GetAnyString(station, "N", "Y") ?? "";
            string h = GetAnyString(station, "H", "Z") ?? "";
            if (string.IsNullOrWhiteSpace(e) && station.TryGetProperty("station", out var stPt) && stPt.ValueKind == JsonValueKind.Object)
            {
                e = GetAnyString(stPt, "E", "X") ?? "";
                n = GetAnyString(stPt, "N", "Y") ?? "";
                h = GetAnyString(stPt, "H", "Z") ?? "";
            }
            if (!string.IsNullOrWhiteSpace(e) || !string.IsNullOrWhiteSpace(n) || !string.IsNullOrWhiteSpace(h))
                coords = $"E={Fmt3(e)} N={Fmt3(n)} H={Fmt3(h)}";
        }
        var orient = GetAnyString(station, "orientation", "Orientation") ?? "";
        var corr = GetAnyString(station, "corr", "Corr") ?? "";
        var extra = GetAnyString(station, "extra", "Extra") ?? "";

        if (!string.IsNullOrWhiteSpace(method)) lines.Add($"Méthode : {method}");
        if (!string.IsNullOrWhiteSpace(stName)) lines.Add($"Station : {stName}");
        if (!string.IsNullOrWhiteSpace(coords)) lines.Add($"Coordonnées : {coords}");
        if (!string.IsNullOrWhiteSpace(corr)) lines.Add($"Corr. orient° : {corr}");
        if (!string.IsNullOrWhiteSpace(orient)) lines.Add($"Orientation : {orient}");
        if (!string.IsNullOrWhiteSpace(extra)) lines.Add(extra);

        var tx = r.X + Units.MmToPt(2);
        var ty = r.Y + Units.MmToPt(2);
        var lineH = Units.MmToPt(3.5);
        foreach (var ln in lines.Take(5))
        {
            g.DrawString(ln, Font(7, false), XBrushes.Black, new XRect(tx, ty, r.Width - Units.MmToPt(4), lineH), XStringFormats.TopLeft);
            ty += lineH;
        }

        y += infoH;
    }

    private static void DrawPointsTopoBlocks(XGraphics g, PdfPage page, JsonElement station, ref double y)
    {
        var leftX = Units.MmToPt(15);
        var w = page.Width.Point - Units.MmToPt(30);
        var rowH = Units.MmToPt(6);
        var headerH = Units.MmToPt(6);
        var gap = Units.MmToPt(4);

        var observations = GetArray(station, "observations").ToList();
        var results = GetArray(station, "results").ToList();

        // Même logique de présentation que les autres rapports :
        // on conserve le bloc station, puis on affiche les informations polaires
        // et rectangulaires dans des tableaux dédiés.
        if (observations.Count > 0)
        {
            DrawSectionBar(g, page, ref y, "OBSERVATIONS POLAIRES");
            var obsRows = observations.Select(o => new[]
            {
                GetAnyString(o, "id", "ID") ?? "",
                Fmt3(GetAnyString(o, "hz", "Hz") ?? ""),
                Fmt3(GetAnyString(o, "vz", "Vz") ?? ""),
                Fmt3(GetAnyString(o, "dp", "Dp", "di", "Dh") ?? ""),
                Fmt3(GetAnyString(o, "hr", "Hr", "th") ?? ""),
                Fmt4(GetAnyString(o, "prismConst", "constPrisme", "reflectorConstant") ?? "")
            }).ToList();

            DrawSimpleTable(
                g,
                x: leftX,
                y: y,
                width: w,
                header: new[] { "ID", "Hz", "Vz", "Dp", "Hr", "Const prisme" },
                rows: obsRows,
                rowHeight: rowH,
                headerHeight: headerH);

            y += headerH + (rowH * Math.Max(1, obsRows.Count)) + gap;
        }

        if (results.Count > 0)
        {
            DrawSectionBar(g, page, ref y, "RÉSULTATS RECTANGULAIRES");
            var resultRows = results.Select(r => new[]
            {
                GetAnyString(r, "id", "ID") ?? "",
                Fmt3(GetAnyString(r, "x", "X", "E") ?? ""),
                Fmt3(GetAnyString(r, "y", "Y", "N") ?? ""),
                Fmt3(GetAnyString(r, "z", "Z", "H") ?? "")
            }).ToList();

            DrawSimpleTable(
                g,
                x: leftX,
                y: y,
                width: w,
                header: new[] { "ID", "X", "Y", "Z" },
                rows: resultRows,
                rowHeight: rowH,
                headerHeight: headerH);

            y += headerH + (rowH * Math.Max(1, resultRows.Count)) + gap;
        }

        // Compatibilité visuelle : si aucune donnée filtrée, afficher un bloc explicite.
        if (observations.Count == 0 && results.Count == 0)
        {
            DrawSectionBar(g, page, ref y, "POINTS TOPO (LEVÉ)");
            g.DrawRectangle(XPens.Black, leftX, y, w, Units.MmToPt(10));
            g.DrawString("Aucune donnée de levé topo.", Font(8, false), XBrushes.Black,
                new XRect(leftX + Units.MmToPt(2), y, w - Units.MmToPt(4), Units.MmToPt(10)), XStringFormats.CenterLeft);
            y += Units.MmToPt(14);
        }
    }




    private static void DrawSectionBar(XGraphics g, PdfPage page, ref double y, string title)
    {
        var leftX = Units.MmToPt(15);
        var w = page.Width.Point - Units.MmToPt(30);
        var h = Units.MmToPt(6);
        g.DrawRectangle(new XSolidBrush(NovatlasTheme.LightGrey), leftX, y, w, h);
        g.DrawRectangle(XPens.Black, leftX, y, w, h);
        g.DrawString(title, Font(8, true), XBrushes.Black, new XRect(leftX, y, w, h), XStringFormats.Center);
        y += h;
    }

    // =========================
    // Footer
    // =========================

    private static void DrawFooter(XGraphics g, PdfPage page, int pageNo, int pageCount)
    {
        var leftX = Units.MmToPt(15);
        var y = page.Height.Point - Units.MmToPt(12);
        var w = page.Width.Point - Units.MmToPt(30);
        g.DrawLine(XPens.Black, leftX, y, leftX + w, y);
        g.DrawString($"{pageNo}/{pageCount}", Font(7, false), XBrushes.Black, new XRect(leftX, y + Units.MmToPt(2), w, Units.MmToPt(8)), XStringFormats.Center);
    }

    // =========================
    // JSON helpers
    // =========================

    private static JsonElement GetObj(JsonElement root, string prop)
    {
        // JsonElement.TryGetProperty throws InvalidOperationException if the element isn't an Object.
        if (root.ValueKind != JsonValueKind.Object) return default;
        return root.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Object ? v : default;
    }

    private static IEnumerable<JsonElement> GetArray(JsonElement root, string prop)
    {
        if (root.ValueKind != JsonValueKind.Object) return Enumerable.Empty<JsonElement>();
        return root.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : Enumerable.Empty<JsonElement>();
    }

    private static string? GetStr(JsonElement root, string prop)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        return root.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    private static string? GetAnyString(JsonElement obj, params string[] props)
    {
        foreach (var p in props)
        {
            if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(p, out var v))
            {
                if (v.ValueKind == JsonValueKind.String) return v.GetString();
                if (v.ValueKind == JsonValueKind.Number) return v.GetRawText();
            }
        }
        return null;
    }

    private static XFont Font(double sizePt, bool bold)
        => new XFont("Arial", sizePt, bold ? XFontStyleEx.Bold : XFontStyleEx.Regular);

    private static void DrawSimpleTable(
        XGraphics g,
        double x,
        double y,
        double width,
        string[]? header,
        List<string[]> rows,
        double rowHeight,
        double headerHeight)
    {
        rows ??= new List<string[]>();
        header ??= Array.Empty<string>();
        int cols = header.Length;
        if (cols == 0) return;

        // Equal column widths (simple tables)
        double colW = width / cols;
        var pen = new XPen(XColors.Black, 0.5);
        var headerBrush = new XSolidBrush(NovatlasTheme.LightGrey);
        var fH = NovatlasTheme.FontBodyBold(8.2);
        var fR = NovatlasTheme.FontBody(8.2);

        // header
        for (int c = 0; c < cols; c++)
        {
            var rect = new XRect(x + c * colW, y, colW, headerHeight);
            g.DrawRectangle(headerBrush, rect);
            g.DrawRectangle(pen, rect);
            g.DrawString(header[c] ?? "", fH, XBrushes.Black, rect, XStringFormats.Center);
        }

        double cy = y + headerHeight;
        foreach (var r in rows)
        {
            for (int c = 0; c < cols; c++)
            {
                var rect = new XRect(x + c * colW, cy, colW, rowHeight);
                g.DrawRectangle(XBrushes.White, rect);
                g.DrawRectangle(pen, rect);
                string val = (r != null && c < r.Length) ? (r[c] ?? "") : "";
                g.DrawString(val, fR, XBrushes.Black, rect, XStringFormats.Center);
            }
            cy += rowHeight;
        }
    }


    private static string Fmt3(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        if (!double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
            return s.Trim();
        return v.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
    }

	// Constante prisme: conserver 4 décimales (ex: 0.017500 -> 0.0175)
	private static string Fmt4(string? s)
	{
	    if (string.IsNullOrWhiteSpace(s)) return "";
	    if (!double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
	        return s.Trim();
	    // 4 décimales puis trim des zéros inutiles
	    var t = v.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);
	    t = t.TrimEnd('0').TrimEnd('.');
	    return t;
	}

}