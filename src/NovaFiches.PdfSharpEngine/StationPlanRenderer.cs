using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace NovaFiches.PdfSharpEngine;

/// <summary>
/// "Plan station" appendix page for the Station report: redraws (vector, not a
/// screenshot) the stations/points/sighting-lines currently shown in the app's
/// "Plan station" tab, with the same per-station colors. Reusing coordinates
/// instead of capturing the on-screen map (tiles, WebView2 DOM) avoids any
/// offline/CORS fragility and gives print-quality output - same approach already
/// used for the "Récolement" plan-view page (see RecolementPlanViewRenderer).
/// Sent only when the user ticks "Envoyer sur la fiche station"; the JS side
/// (m02_parser_calc.js, window.nfGetStationPlanViewForPdf) omits the
/// "stationPlanView" payload key entirely otherwise, so AppendFromPayload below
/// is a no-op for every other export.
/// </summary>
internal static class StationPlanRenderer
{
    private const double MarginL = 36;
    private const double MarginR = 36;

    private static readonly XColor BrandBlue = XColor.FromArgb(18, 103, 243);
    private static readonly XColor LineGray = XColor.FromArgb(200, 200, 200);
    private static readonly XColor Green = XColor.FromArgb(47, 158, 68);
    private static readonly XColor Red = XColor.FromArgb(185, 28, 28);

    private sealed record St(string Label, double E, double N, string ColorHex);
    private sealed record Pt(string Id, double E, double N, bool Included);
    private sealed record Sight(string StationLabel, string PointId, string ColorHex, bool Included);

    public static void AppendFromPayload(PdfDocument doc, string payloadJson, string buildFooter)
    {
        JsonElement root;
        JsonElement planView;
        try
        {
            using var jd = JsonDocument.Parse(payloadJson);
            root = jd.RootElement.Clone();
            if (!root.TryGetProperty("stationPlanView", out planView) || planView.ValueKind != JsonValueKind.Object)
                return;

            var stations = ReadStations(planView);
            var points = ReadPoints(planView);
            var sightings = ReadSightings(planView);
            if (stations.Count == 0 && points.Count == 0) return;

            var page = doc.AddPage();
            page.Size = PdfSharp.PageSize.A4;
            using (var g = XGraphics.FromPdfPage(page))
            {
                DrawHeader(g, page, root);
                double y = Units.MmToPt(6) + Units.MmToPt(22) + Units.MmToPt(6);
                DrawTitleBar(g, page, ref y, "PLAN STATION");
                DrawPlan(g, page, y, stations, points, sightings);
                DrawFooter(g, page, buildFooter);
            }

            RestampFooters(doc, buildFooter);
        }
        catch (Exception ex)
        {
            // Ne jamais bloquer la génération du rapport si ce plan échoue, mais laisser
            // une trace (AppLog est interne au projet NovaFiches, inaccessible ici -
            // même pattern de repli que RecolementPlanViewRenderer/ImplantationFullReportRenderer).
            try
            {
                var logDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NOVATLAS", "Nova-Fiches", "Logs");
                System.IO.Directory.CreateDirectory(logDir);
                var logPath = System.IO.Path.Combine(logDir, $"Nova-Fiches_{DateTime.Now:yyyy-MM-dd}.log");
                System.IO.File.AppendAllText(logPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [ERROR] StationPlanRenderer.AppendFromPayload a échoué (page plan station absente du PDF){Environment.NewLine}{ex}{Environment.NewLine}");
            }
            catch { /* le logging ne doit jamais faire planter la génération du PDF */ }
        }
    }

    private static List<St> ReadStations(JsonElement pv)
    {
        var list = new List<St>();
        if (!pv.TryGetProperty("stations", out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
        foreach (var it in arr.EnumerateArray())
        {
            if (it.ValueKind != JsonValueKind.Object) continue;
            if (!TryGetDouble(it, "e", out var e) || !TryGetDouble(it, "n", out var n)) continue;
            list.Add(new St(GetStr(it, "label"), e, n, GetStr(it, "color")));
        }
        return list;
    }

    private static List<Pt> ReadPoints(JsonElement pv)
    {
        var list = new List<Pt>();
        if (!pv.TryGetProperty("points", out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
        foreach (var it in arr.EnumerateArray())
        {
            if (it.ValueKind != JsonValueKind.Object) continue;
            if (!TryGetDouble(it, "e", out var e) || !TryGetDouble(it, "n", out var n)) continue;
            list.Add(new Pt(GetStr(it, "id"), e, n, GetBool(it, "included", true)));
        }
        return list;
    }

    private static List<Sight> ReadSightings(JsonElement pv)
    {
        var list = new List<Sight>();
        if (!pv.TryGetProperty("sightings", out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
        foreach (var it in arr.EnumerateArray())
        {
            if (it.ValueKind != JsonValueKind.Object) continue;
            list.Add(new Sight(GetStr(it, "stationLabel"), GetStr(it, "pointId"), GetStr(it, "color"), GetBool(it, "included", true)));
        }
        return list;
    }

    private static void DrawPlan(XGraphics g, PdfPage page, double yTop, List<St> stations, List<Pt> points, List<Sight> sightings)
    {
        double footerSafe = Units.MmToPt(26);
        var frame = new XRect(MarginL, yTop, page.Width.Point - MarginL - MarginR, page.Height.Point - yTop - footerSafe - Units.MmToPt(14));
        g.DrawRectangle(new XPen(LineGray, 0.8), frame);

        if (stations.Count == 0 && points.Count == 0)
        {
            g.DrawString("Aucune station à afficher.", NovatlasTheme.FontBody(10), XBrushes.Black,
                new XRect(frame.X, frame.Y + Units.MmToPt(10), frame.Width, Units.MmToPt(10)), XStringFormats.Center);
            return;
        }

        var allE = stations.Select(s => s.E).Concat(points.Select(p => p.E)).ToList();
        var allN = stations.Select(s => s.N).Concat(points.Select(p => p.N)).ToList();
        double rawMinE = allE.Min(), rawMaxE = allE.Max();
        double rawMinN = allN.Min(), rawMaxN = allN.Max();
        double rawDe = Math.Max(0.001, rawMaxE - rawMinE);
        double rawDn = Math.Max(0.001, rawMaxN - rawMinN);
        // Emprise plus large que haute : on garde une page A4 portrait (pas de gestion à
        // part d'une page paysage - en-tête/pied de page/pagination restent identiques
        // sur toutes les pages) et on pivote juste le contenu de 90°, comme la vue en
        // plan "Récolement" (RecolementPlanViewRenderer) le fait déjà pour le même cas.
        bool rotate = rawDe > rawDn;

        (double u, double v) Project(double e, double n) => rotate ? (n, -e) : (e, n);

        var allUV = stations.Select(s => Project(s.E, s.N)).Concat(points.Select(p => Project(p.E, p.N))).ToList();
        double minU = allUV.Min(p => p.u), maxU = allUV.Max(p => p.u);
        double minV = allUV.Min(p => p.v), maxV = allUV.Max(p => p.v);
        double du = Math.Max(0.001, maxU - minU);
        double dv = Math.Max(0.001, maxV - minV);
        double eu = du * 0.08, ev = dv * 0.08;
        minU -= eu; maxU += eu; minV -= ev; maxV += ev;
        du = Math.Max(0.001, maxU - minU);
        dv = Math.Max(0.001, maxV - minV);

        double pad = Units.MmToPt(6);
        var inner = new XRect(frame.X + pad, frame.Y + pad, frame.Width - 2 * pad, frame.Height - 2 * pad);
        double scale = Math.Min(inner.Width / du, inner.Height / dv);
        if (!double.IsFinite(scale) || scale <= 0) scale = 1;
        double usedW = du * scale, usedH = dv * scale;
        double offsetX = Math.Max(0, (inner.Width - usedW) / 2.0);
        double offsetY = Math.Max(0, (inner.Height - usedH) / 2.0);

        XPoint Map(double e, double n)
        {
            var q = Project(e, n);
            double px = inner.X + offsetX + (q.u - minU) * scale;
            double py = inner.Y + offsetY + usedH - (q.v - minV) * scale;
            return new XPoint(px, py);
        }

        var stationByLabel = stations.ToDictionary(s => s.Label, s => s, StringComparer.OrdinalIgnoreCase);

        // 1) Traits de visée (en dessous des marqueurs)
        foreach (var s2 in sightings)
        {
            if (!stationByLabel.TryGetValue(s2.StationLabel, out var st)) continue;
            var pt = points.FirstOrDefault(p => string.Equals(p.Id, s2.PointId, StringComparison.OrdinalIgnoreCase));
            if (pt == null) continue;
            var a = Map(st.E, st.N);
            var b = Map(pt.E, pt.N);
            var pen = new XPen(ParseHexColorAlpha(s2.ColorHex, 140, BrandBlue), 1.1);
            if (!s2.Included) pen.DashStyle = XDashStyle.Dash;
            g.DrawLine(pen, a, b);
        }

        var fontLbl = NovatlasTheme.FontBody(8);
        double r = 3.2;

        // 2) Points (vert = inclus, rouge = exclu), ID au-dessus/en dessous en alternance
        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];
            var pt = Map(p.E, p.N);
            var fill = p.Included ? Green : Red;
            g.DrawEllipse(new XPen(XColors.White, 1.0), new XSolidBrush(fill), pt.X - r, pt.Y - r, 2 * r, 2 * r);

            var text = p.Id;
            if (string.IsNullOrWhiteSpace(text)) continue;
            var size = g.MeasureString(text, fontLbl);
            double lx = pt.X - size.Width / 2.0;
            double ly = (i % 2 == 0) ? pt.Y - r - size.Height - 1 : pt.Y + r + 1;
            var halo = XColor.FromArgb(190, 255, 255, 255);
            var rectLbl = new XRect(lx, ly, size.Width, size.Height);
            g.DrawRectangle(new XSolidBrush(halo), rectLbl);
            g.DrawString(text, fontLbl, XBrushes.Black, rectLbl, XStringFormats.TopLeft);
        }

        // 3) Stations (triangle plein, couleur propre), libellé au-dessus
        var fontSt = NovatlasTheme.FontBold(9);
        double sz = Units.MmToPt(3.2);
        foreach (var s in stations)
        {
            var pt = Map(s.E, s.N);
            var color = ParseHexColor(s.ColorHex, BrandBlue);
            var tri = new XPoint[]
            {
                new XPoint(pt.X, pt.Y - sz),
                new XPoint(pt.X - sz, pt.Y + sz * 0.75),
                new XPoint(pt.X + sz, pt.Y + sz * 0.75)
            };
            g.DrawPolygon(new XPen(XColors.White, 1.0), new XSolidBrush(color), tri, XFillMode.Winding);

            var text = s.Label ?? "";
            if (string.IsNullOrWhiteSpace(text)) continue;
            var size = g.MeasureString(text, fontSt);
            var rectLbl = new XRect(pt.X - size.Width / 2.0, pt.Y - sz - size.Height - 2, size.Width, size.Height);
            g.DrawString(text, fontSt, XBrushes.Black, rectLbl, XStringFormats.TopLeft);
        }

        // Légende (bande fine sous le cadre)
        DrawLegend(g, frame, stations);
    }

    private static void DrawLegend(XGraphics g, XRect frame, List<St> stations)
    {
        double y = frame.Bottom + Units.MmToPt(3);
        double x = frame.X;
        var font = NovatlasTheme.FontBody(8);
        double sq = Units.MmToPt(2.6);
        double gap = Units.MmToPt(3);

        void Chip(string label, XColor color)
        {
            var size = g.MeasureString(label, font);
            double w = sq + Units.MmToPt(1.5) + size.Width + gap * 2;
            if (x + w > frame.Right) { x = frame.X; y += Units.MmToPt(5); }
            g.DrawRectangle(new XSolidBrush(color), x, y + (size.Height - sq) / 2.0, sq, sq);
            g.DrawString(label, font, XBrushes.Black, new XRect(x + sq + Units.MmToPt(1.5), y, size.Width + gap, size.Height), XStringFormats.TopLeft);
            x += w;
        }

        Chip("Point inclus", Green);
        Chip("Point exclu", Red);
        foreach (var s in stations)
            if (!string.IsNullOrWhiteSpace(s.Label))
                Chip(s.Label, ParseHexColor(s.ColorHex, BrandBlue));
    }

    private static bool TryParseHexRgb(string? hex, out int r, out int g, out int b)
    {
        r = g = b = 0;
        var h = (hex ?? "").Trim().TrimStart('#');
        if (h.Length != 6) return false;
        try
        {
            r = Convert.ToInt32(h.Substring(0, 2), 16);
            g = Convert.ToInt32(h.Substring(2, 2), 16);
            b = Convert.ToInt32(h.Substring(4, 2), 16);
            return true;
        }
        catch { return false; }
    }

    private static XColor ParseHexColor(string? hex, XColor fallback)
        => TryParseHexRgb(hex, out var r, out var g, out var b) ? XColor.FromArgb(r, g, b) : fallback;

    private static XColor ParseHexColorAlpha(string? hex, int alpha, XColor fallback)
        => TryParseHexRgb(hex, out var r, out var g, out var b) ? XColor.FromArgb(alpha, r, g, b) : fallback;

    private static bool TryGetDouble(JsonElement el, string key, out double val)
    {
        val = 0;
        return el.ValueKind == JsonValueKind.Object && el.TryGetProperty(key, out var v)
            && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out val) && double.IsFinite(val);
    }

    private static bool GetBool(JsonElement el, string key, bool defaultValue)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(key, out var v)) return defaultValue;
        if (v.ValueKind == JsonValueKind.True) return true;
        if (v.ValueKind == JsonValueKind.False) return false;
        return defaultValue;
    }

    private static string GetStr(JsonElement el, string key)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(key, out var v)) return "";
        return v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : (v.ToString() ?? "");
    }

    private static string GetRootStr(JsonElement root, string key)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(key, out var v)) return "";
        return v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : (v.ToString() ?? "");
    }

    // Header/footer identiques (même style, mêmes helpers) que les autres pages annexes
    // (PhotoAppendixRenderer) pour garder un rendu cohérent d'une page ajoutée à l'autre.
    private static void DrawHeader(XGraphics g, PdfPage page, JsonElement root)
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
        g.DrawString("ANNEXE", NovatlasTheme.FontBold(11), XBrushes.White,
            new XRect(titleX, y, titleW, Units.MmToPt(8)), XStringFormats.Center);
        g.DrawRectangle(new XPen(XColors.Black, 0.8), titleX, y + Units.MmToPt(8), titleW, Units.MmToPt(10));

        string ville = GetRootStr(root, "ville");
        string adr = GetRootStr(root, "adresse");
        if (string.IsNullOrWhiteSpace(adr)) adr = GetRootStr(root, "adresseChantier");
        string cha = GetRootStr(root, "cha");
        string subtitle = string.Join("  —  ", new[] { ville, adr, cha }.Where(s => !string.IsNullOrWhiteSpace(s)));
        g.DrawString(string.IsNullOrWhiteSpace(subtitle) ? "PLAN STATION" : subtitle, NovatlasTheme.FontBold(10), XBrushes.Black,
            new XRect(titleX, y + Units.MmToPt(8), titleW, Units.MmToPt(10)), XStringFormats.Center);
    }

    private static void DrawTitleBar(XGraphics g, PdfPage page, ref double y, string title)
    {
        double barH = Units.MmToPt(10);
        double w = page.Width.Point - MarginL - MarginR;
        var rect = new XRect(MarginL, y, w, barH);
        g.DrawRectangle(new XSolidBrush(BrandBlue), rect);
        g.DrawString(title, NovatlasTheme.FontBold(12), XBrushes.White, rect, XStringFormats.Center);
        y += barH + Units.MmToPt(4);
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

    // Ré-écrit le pied de page + la pagination "Page i / total" sur TOUTES les pages du
    // document : ce plan devient la toute dernière page ("à la suite", comme demandé),
    // donc c'est le seul endroit qui connaît le compte final de pages.
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
}
