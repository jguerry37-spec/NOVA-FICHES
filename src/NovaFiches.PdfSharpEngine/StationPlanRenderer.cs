using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
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

    // Lon/Lat sont optionnels : renseignés par MainForm (EnrichStationPlanViewWithLonLat)
    // avant l'appel à GenerateStationFromJson, car la reprojection (KmzExportService)
    // vit dans le projet NovaFiches, inaccessible depuis PdfSharpEngine (le sens de
    // référence des projets est NovaFiches -> PdfSharpEngine, jamais l'inverse). Sans
    // ces champs, le plan reste en repère local (pas de fond de carte).
    private sealed record St(string Label, double E, double N, string ColorHex, double? Lon, double? Lat);
    private sealed record Pt(string Id, double E, double N, bool Included, double? Lon, double? Lat);
    private sealed record Sight(string StationLabel, string PointId, string ColorHex, bool Included);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };

    static StationPlanRenderer()
    {
        try { Http.DefaultRequestHeaders.UserAgent.ParseAdd("Nova-Fiches/PdfExport (+https://novatlas.fr)"); } catch { }
    }

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
            string basemapKind = GetStr(planView, "basemap");
            if (string.IsNullOrWhiteSpace(basemapKind)) basemapKind = "plan";

            var page = doc.AddPage();
            page.Size = PdfSharp.PageSize.A4;
            using (var g = XGraphics.FromPdfPage(page))
            {
                DrawHeader(g, page, root);
                double y = Units.MmToPt(6) + Units.MmToPt(22) + Units.MmToPt(6);
                DrawTitleBar(g, page, ref y, "PLAN STATION");
                DrawPlan(g, page, y, stations, points, sightings, basemapKind);
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
            double? lon = TryGetDouble(it, "lon", out var lonV) ? lonV : null;
            double? lat = TryGetDouble(it, "lat", out var latV) ? latV : null;
            list.Add(new St(GetStr(it, "label"), e, n, GetStr(it, "color"), lon, lat));
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
            double? lon = TryGetDouble(it, "lon", out var lonV) ? lonV : null;
            double? lat = TryGetDouble(it, "lat", out var latV) ? latV : null;
            list.Add(new Pt(GetStr(it, "id"), e, n, GetBool(it, "included", true), lon, lat));
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

    private static void DrawPlan(XGraphics g, PdfPage page, double yTop, List<St> stations, List<Pt> points, List<Sight> sightings, string basemapKind)
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

        double pad = Units.MmToPt(6);
        var inner = new XRect(frame.X + pad, frame.Y + pad, frame.Width - 2 * pad, frame.Height - 2 * pad);

        if (!TryDrawGeoPlan(g, inner, stations, points, sightings, basemapKind))
            DrawLocalPlan(g, inner, stations, points, sightings);

        DrawLegend(g, frame, stations);
    }

    // Fond de carte réel (tuiles OSM/Esri, assemblées côté C# - pas une capture d'écran,
    // donc aucun souci CORS/DOM WebView2). Nécessite lon/lat sur au moins un point ou
    // une station (renseignés par MainForm.EnrichStationPlanViewWithLonLat avant l'appel
    // à StationPlanRenderer) et une connexion Internet au moment de générer le PDF ;
    // renvoie false pour tout échec (pas de lon/lat, pas de réseau, timeout...), auquel
    // cas DrawLocalPlan prend le relais avec le repère local (comportement 2.3.1.36).
    private static bool TryDrawGeoPlan(XGraphics g, XRect inner, List<St> stations, List<Pt> points, List<Sight> sightings, string basemapKind)
    {
        try
        {
            var allLon = stations.Where(s => s.Lon.HasValue).Select(s => s.Lon!.Value)
                .Concat(points.Where(p => p.Lon.HasValue).Select(p => p.Lon!.Value)).ToList();
            var allLat = stations.Where(s => s.Lat.HasValue).Select(s => s.Lat!.Value)
                .Concat(points.Where(p => p.Lat.HasValue).Select(p => p.Lat!.Value)).ToList();
            if (allLon.Count == 0 || allLat.Count == 0) return false;

            double minLon = allLon.Min(), maxLon = allLon.Max();
            double minLat = allLat.Min(), maxLat = allLat.Max();
            double padLon = Math.Max((maxLon - minLon) * 0.15, 0.0003);
            double padLat = Math.Max((maxLat - minLat) * 0.15, 0.0002);
            minLon -= padLon; maxLon += padLon; minLat -= padLat; maxLat += padLat;

            var grid = ComputeTileGrid(minLon, minLat, maxLon, maxLat);
            if (grid == null) return false;
            var (z, txMin, tyMin, txMax, tyMax) = grid.Value;

            // Task.Run ici (pas un simple .GetAwaiter().GetResult() sur l'appel direct) :
            // AppendFromPayload est invoqué depuis le handler WebView2, sur le thread UI
            // WinForms, qui a un SynchronizationContext. Sans ce Task.Run, le "await
            // Task.WhenAll" dans FetchAndStitchTilesAsync tenterait de reprendre sur ce
            // même thread UI pour continuer - or ce thread est bloqué juste ici en
            // attendant le résultat : blocage total de l'application (deadlock classique
            // "sync-over-async"). Task.Run fait démarrer toute la chaîne async sur un
            // thread du pool, sans SynchronizationContext capturé, donc sans ce risque.
            using var bitmap = Task.Run(() => FetchAndStitchTilesAsync(txMin, tyMin, txMax, tyMax, z, basemapKind)).GetAwaiter().GetResult();
            if (bitmap == null) return false;

            double bitmapW = (txMax - txMin + 1) * 256.0;
            double bitmapH = (tyMax - tyMin + 1) * 256.0;
            double scale = Math.Min(inner.Width / bitmapW, inner.Height / bitmapH);
            if (!double.IsFinite(scale) || scale <= 0) scale = 1;
            double usedW = bitmapW * scale, usedH = bitmapH * scale;
            double offX = inner.X + (inner.Width - usedW) / 2.0;
            double offY = inner.Y + (inner.Height - usedH) / 2.0;

            using (var ms = new System.IO.MemoryStream())
            {
                bitmap.Save(ms, ImageFormat.Png);
                ms.Position = 0;
                using var ximg = XImage.FromStream(ms);
                g.DrawImage(ximg, new XRect(offX, offY, usedW, usedH));
            }

            XPoint? MapGeo(double? lon, double? lat)
            {
                if (!lon.HasValue || !lat.HasValue) return null;
                double wx = LonToTileX(lon.Value, z) * 256.0 - txMin * 256.0;
                double wy = LatToTileY(lat.Value, z) * 256.0 - tyMin * 256.0;
                return new XPoint(offX + wx * scale, offY + wy * scale);
            }

            // Pas de rotation ici (contrairement à DrawLocalPlan) : un fond de carte est
            // toujours nord en haut, on ne peut pas pivoter les tuiles comme un simple repère local.
            DrawOverlay(g, stations, points, sightings, s => MapGeo(s.Lon, s.Lat), p => MapGeo(p.Lon, p.Lat));
            return true;
        }
        catch { return false; }
    }

    // Repli sans fond de carte (repère E/N local du chantier) : comportement 2.3.1.36,
    // utilisé quand la reprojection ou le fond de carte ne sont pas disponibles.
    private static void DrawLocalPlan(XGraphics g, XRect inner, List<St> stations, List<Pt> points, List<Sight> sightings)
    {
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

        DrawOverlay(g, stations, points, sightings, s => Map(s.E, s.N), p => Map(p.E, p.N));
    }

    // Traits de visée + points + stations, communs aux deux modes (fond de carte réel /
    // repère local) - seule la fonction de projection (mapStation/mapPoint) change.
    private static void DrawOverlay(XGraphics g, List<St> stations, List<Pt> points, List<Sight> sightings,
        Func<St, XPoint?> mapStation, Func<Pt, XPoint?> mapPoint)
    {
        var stationByLabel = new Dictionary<string, St>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in stations)
            if (!string.IsNullOrWhiteSpace(s.Label) && !stationByLabel.ContainsKey(s.Label)) stationByLabel[s.Label] = s;
        var pointById = new Dictionary<string, Pt>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in points)
            if (!string.IsNullOrWhiteSpace(p.Id) && !pointById.ContainsKey(p.Id)) pointById[p.Id] = p;

        // 1) Traits de visée (en dessous des marqueurs)
        foreach (var s2 in sightings)
        {
            if (!stationByLabel.TryGetValue(s2.StationLabel, out var st)) continue;
            if (!pointById.TryGetValue(s2.PointId, out var pt2)) continue;
            var a = mapStation(st);
            var b = mapPoint(pt2);
            if (a == null || b == null) continue;
            var pen = new XPen(ParseHexColorAlpha(s2.ColorHex, 140, BrandBlue), 1.1);
            if (!s2.Included) pen.DashStyle = XDashStyle.Dash;
            g.DrawLine(pen, a.Value, b.Value);
        }

        var fontLbl = NovatlasTheme.FontBody(8);
        double r = 3.2;

        // 2) Points (vert = inclus, rouge = exclu), ID au-dessus/en dessous en alternance
        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];
            var ptXY = mapPoint(p);
            if (ptXY == null) continue;
            var pt = ptXY.Value;
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
            var ptXY = mapStation(s);
            if (ptXY == null) continue;
            var pt = ptXY.Value;
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
    }

    // ===== Tuiles (Web Mercator, formules standard "slippy map") =====

    private static double LonToTileX(double lon, int z) => (lon + 180.0) / 360.0 * (1 << z);

    private static double LatToTileY(double lat, int z)
    {
        double latRad = lat * Math.PI / 180.0;
        return (1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * (1 << z);
    }

    // Choisit le zoom le plus détaillé dont la grille de tuiles couvrant l'emprise reste
    // dans le budget (maxTilesPerSide) - évite de télécharger des dizaines de tuiles pour
    // une emprise large, tout en restant aussi net que possible pour une petite emprise
    // de chantier (cas courant du Plan station).
    private static (int z, int txMin, int tyMin, int txMax, int tyMax)? ComputeTileGrid(
        double minLon, double minLat, double maxLon, double maxLat, int maxTilesPerSide = 6)
    {
        for (int z = 19; z >= 2; z--)
        {
            double xA = LonToTileX(minLon, z), xB = LonToTileX(maxLon, z);
            double yA = LatToTileY(minLat, z), yB = LatToTileY(maxLat, z);
            int txMin = (int)Math.Floor(Math.Min(xA, xB));
            int txMax = (int)Math.Floor(Math.Max(xA, xB));
            int tyMin = (int)Math.Floor(Math.Min(yA, yB));
            int tyMax = (int)Math.Floor(Math.Max(yA, yB));
            if (txMax - txMin + 1 <= maxTilesPerSide && tyMax - tyMin + 1 <= maxTilesPerSide)
                return (z, txMin, tyMin, txMax, tyMax);
        }
        return null;
    }

    private static string TileUrl(int x, int y, int z, string kind)
    {
        if (string.Equals(kind, "satellite", StringComparison.OrdinalIgnoreCase))
            return $"https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}";
        // OSM répartit la charge sur les sous-domaines a/b/c ; répartition simple et déterministe.
        char sub = "abc"[Math.Abs(x + y) % 3];
        return $"https://{sub}.tile.openstreetmap.org/{z}/{x}/{y}.png";
    }

    // Une tuile en échec (réseau, 404...) laisse juste un trou gris à cet endroit plutôt
    // que de faire échouer tout le fond de carte - cohérent avec la tolérance déjà en
    // place pour les repères NGF (IgnGeodesyService) et l'affichage carte à l'écran.
    private static async Task<Bitmap?> FetchAndStitchTilesAsync(int txMin, int tyMin, int txMax, int tyMax, int z, string kind)
    {
        int cols = txMax - txMin + 1;
        int rows = tyMax - tyMin + 1;
        var bitmap = new Bitmap(cols * 256, rows * 256);
        using (var gfx = System.Drawing.Graphics.FromImage(bitmap))
            gfx.Clear(Color.FromArgb(235, 235, 235));

        var lockObj = new object();
        var tasks = new List<Task>();
        for (int tx = txMin; tx <= txMax; tx++)
        {
            for (int ty = tyMin; ty <= tyMax; ty++)
            {
                int localTx = tx, localTy = ty;
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var bytes = await Http.GetByteArrayAsync(TileUrl(localTx, localTy, z, kind)).ConfigureAwait(false);
                        using var ms = new System.IO.MemoryStream(bytes);
                        using var tileImg = Image.FromStream(ms);
                        lock (lockObj)
                        {
                            using var gfx = System.Drawing.Graphics.FromImage(bitmap);
                            gfx.DrawImage(tileImg, (localTx - txMin) * 256, (localTy - tyMin) * 256, 256, 256);
                        }
                    }
                    catch { /* tuile manquante : trou gris à cet endroit, le reste continue */ }
                }));
            }
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);
        return bitmap;
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
