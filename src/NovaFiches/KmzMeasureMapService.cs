using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using NovaFiches.PdfSharpEngine;

namespace TopoRapportWin;

// Carte de situation pour l'export PDF de l'outil "Mesurer une distance" (Export KMZ) : même
// principe que ControlePrecisionMapService (MapTileFetcher, tuiles déjà en WGS84 - la mesure
// Leaflet travaille nativement en lat/lon, pas de reprojection à faire ici), mais dessine le
// tracé de la mesure (ligne pointillée + points) au lieu de simples marqueurs, avec les mêmes
// couleurs que sur la carte à l'écran (rouge #e03131 / remplissage #ff8787 - voir
// redrawMeasureLayer() dans m07_export_kmz.js) pour que le PDF corresponde à ce que l'utilisateur
// a sous les yeux au moment d'exporter.
public static class KmzMeasureMapService
{
    public sealed record MapResult(string ImageDataUrl);

    // ngfPoints/topoPoints optionnels : repères NGF (chargés via "Charger repères NGF sur une
    // zone") et points topo importés (TXT/DXF), affichés en contexte autour du tracé de mesure -
    // demande utilisateur explicite ("que les repères et points topo apparaissent" à l'export).
    // L'emprise de la carte reste calculée sur le TRACÉ DE MESURE SEUL (comme avant) : l'inclure
    // dans l'union avec tous les repères/points chargés (essayé dans une version précédente)
    // pouvait faire exploser le cadrage - un simple chargement "sur une zone" peut couvrir
    // plusieurs km autour d'un tracé de quelques dizaines de mètres, rendant tout minuscule et
    // illisible. On filtre plutôt l'inverse : seuls les repères/points qui tombent dans le
    // cadrage déjà centré sur la mesure sont dessinés, chacun avec son étiquette (nom/n°) pour
    // rester identifiable même à cette échelle.
    public static async Task<MapResult?> BuildMapFromPointsAsync(
        IReadOnlyList<(double Lat, double Lon)> points,
        string basemapKind,
        IReadOnlyList<(double Lat, double Lon, string Etat, string Label)>? ngfPoints = null,
        IReadOnlyList<(double Lat, double Lon, string Label)>? topoPoints = null,
        IReadOnlyList<(double Lat, double Lon, string Label)>? nvpPoints = null)
    {
        if (points.Count < 2) return null;

        double minLon = points.Min(p => p.Lon), maxLon = points.Max(p => p.Lon);
        double minLat = points.Min(p => p.Lat), maxLat = points.Max(p => p.Lat);
        double padLon = Math.Max((maxLon - minLon) * 0.15, 0.0006);
        double padLat = Math.Max((maxLat - minLat) * 0.15, 0.0004);
        minLon -= padLon; maxLon += padLon; minLat -= padLat; maxLat += padLat;

        var grid = MapTileFetcher.ComputeTileGrid(minLon, minLat, maxLon, maxLat);
        if (grid == null) return null;
        var (z, txMin, tyMin, txMax, tyMax) = grid.Value;

        using var stitched = await MapTileFetcher.FetchAndStitchTilesAsync(txMin, tyMin, txMax, tyMax, z, basemapKind).ConfigureAwait(false);
        if (stitched == null) return null;

        bool InFrame(double lon, double lat) => lon >= minLon && lon <= maxLon && lat >= minLat && lat <= maxLat;
        var visibleTopo = topoPoints?.Where(p => InFrame(p.Lon, p.Lat)).ToList();
        var visibleNgf = ngfPoints?.Where(p => InFrame(p.Lon, p.Lat)).ToList();
        var visibleNvp = nvpPoints?.Where(p => InFrame(p.Lon, p.Lat)).ToList();

        // Ordre de dessin : contexte d'abord (points topo, repères NGF/NVP), tracé de mesure en
        // dernier pour rester visuellement au premier plan (c'est le sujet de l'export).
        if (visibleTopo != null && visibleTopo.Count > 0) DrawTopoPoints(stitched, visibleTopo, txMin, tyMin, z);
        if (visibleNgf != null && visibleNgf.Count > 0) DrawNgfPoints(stitched, visibleNgf, txMin, tyMin, z);
        if (visibleNvp != null && visibleNvp.Count > 0) DrawNvpPoints(stitched, visibleNvp, txMin, tyMin, z);
        DrawMeasurePath(stitched, points, txMin, tyMin, z);

        using var ms = new MemoryStream();
        stitched.Save(ms, ImageFormat.Png);
        return new MapResult("data:image/png;base64," + Convert.ToBase64String(ms.ToArray()));
    }

    // Étiquette texte avec fond blanc translucide (lisible quel que soit le fond de carte
    // dessous) - aucun équivalent existant à réutiliser dans ce projet (MapTileFetcher ne fait que
    // les tuiles, ControlePrecisionMapService ne dessine que des marqueurs sans texte).
    private static void DrawLabel(Graphics gfx, string text, float anchorX, float anchorY)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        using var font = new Font("Arial", 8f, FontStyle.Bold);
        var size = gfx.MeasureString(text, font);
        float x = anchorX + 6f, y = anchorY - size.Height / 2f;
        using var bgBrush = new SolidBrush(Color.FromArgb(210, 255, 255, 255));
        gfx.FillRectangle(bgBrush, x - 1f, y - 1f, size.Width + 3f, size.Height + 2f);
        using var textBrush = new SolidBrush(Color.FromArgb(30, 30, 30));
        gfx.DrawString(text, font, textBrush, x, y);
    }

    // Points topo (TXT/DXF) : même palette que les marqueurs correspondants à l'écran (voir
    // renderMap() dans m07_export_kmz.js, marqueurs verts pour les points TXT), avec leur
    // identifiant en étiquette.
    private static void DrawTopoPoints(Bitmap bitmap, IReadOnlyList<(double Lat, double Lon, string Label)> pts, int txMin, int tyMin, int z)
    {
        using var gfx = Graphics.FromImage(bitmap);
        gfx.SmoothingMode = SmoothingMode.AntiAlias;
        gfx.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

        PointF ToPx((double Lat, double Lon, string Label) p) => new(
            (float)(MapTileFetcher.LonToTileX(p.Lon, z) * 256.0 - txMin * 256.0),
            (float)(MapTileFetcher.LatToTileY(p.Lat, z) * 256.0 - tyMin * 256.0));

        using var fillBrush = new SolidBrush(Color.FromArgb(99, 230, 190));
        using var circlePen = new Pen(Color.FromArgb(8, 127, 91), 1.5f);
        const float r = 3.5f;
        foreach (var p in pts)
        {
            var px = ToPx(p);
            gfx.FillEllipse(fillBrush, px.X - r, px.Y - r, r * 2, r * 2);
            gfx.DrawEllipse(circlePen, px.X - r, px.Y - r, r * 2, r * 2);
            DrawLabel(gfx, p.Label, px.X + r, px.Y);
        }
    }

    // Repères NGF : même convention que la carte à l'écran (voir m07_export_kmz.js/components.css,
    // .kmz-ngf-triangle-inner--bad) - triangle bleu si "BON ETAT" (ou état inconnu/absent), rouge
    // sinon (mauvais état, non retrouvé, imprenable, présumé déplacé...), avec son nom/n° en
    // étiquette - sans quoi, à l'échelle réduite d'une carte de mesure, un triangle de quelques
    // pixels est indissociable d'un autre.
    private static void DrawNgfPoints(Bitmap bitmap, IReadOnlyList<(double Lat, double Lon, string Etat, string Label)> pts, int txMin, int tyMin, int z)
    {
        using var gfx = Graphics.FromImage(bitmap);
        gfx.SmoothingMode = SmoothingMode.AntiAlias;
        gfx.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

        PointF ToPx((double Lat, double Lon, string Etat, string Label) p) => new(
            (float)(MapTileFetcher.LonToTileX(p.Lon, z) * 256.0 - txMin * 256.0),
            (float)(MapTileFetcher.LatToTileY(p.Lat, z) * 256.0 - tyMin * 256.0));

        using var whiteOutline = new Pen(Color.White, 1f);
        foreach (var p in pts)
        {
            var px = ToPx(p);
            bool isBad = !string.IsNullOrEmpty(p.Etat) && !string.Equals(p.Etat, "BON ETAT", StringComparison.OrdinalIgnoreCase);
            using var fillBrush = new SolidBrush(isBad ? Color.FromArgb(224, 49, 49) : Color.FromArgb(28, 63, 220));
            var tri = new[]
            {
                new PointF(px.X, px.Y - 8f),
                new PointF(px.X - 7f, px.Y + 6f),
                new PointF(px.X + 7f, px.Y + 6f)
            };
            gfx.FillPolygon(fillBrush, tri);
            gfx.DrawPolygon(whiteOutline, tri);
            DrawLabel(gfx, p.Label, px.X + 8f, px.Y);
        }
    }

    // Repères NVP (Ville de Paris) : losange violet, même convention que la carte à l'écran (voir
    // m07_export_kmz.js/components.css, .kmz-nvp-diamond) - pas de notion d'état bon/mauvais ici
    // (pas de code couleur), juste le numéro en étiquette.
    private static void DrawNvpPoints(Bitmap bitmap, IReadOnlyList<(double Lat, double Lon, string Label)> pts, int txMin, int tyMin, int z)
    {
        using var gfx = Graphics.FromImage(bitmap);
        gfx.SmoothingMode = SmoothingMode.AntiAlias;
        gfx.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

        PointF ToPx((double Lat, double Lon, string Label) p) => new(
            (float)(MapTileFetcher.LonToTileX(p.Lon, z) * 256.0 - txMin * 256.0),
            (float)(MapTileFetcher.LatToTileY(p.Lat, z) * 256.0 - tyMin * 256.0));

        using var fillBrush = new SolidBrush(Color.FromArgb(156, 54, 181));
        using var whiteOutline = new Pen(Color.White, 1.5f);
        const float r = 6f;
        foreach (var p in pts)
        {
            var px = ToPx(p);
            var diamond = new[]
            {
                new PointF(px.X, px.Y - r),
                new PointF(px.X + r, px.Y),
                new PointF(px.X, px.Y + r),
                new PointF(px.X - r, px.Y)
            };
            gfx.FillPolygon(fillBrush, diamond);
            gfx.DrawPolygon(whiteOutline, diamond);
            DrawLabel(gfx, p.Label, px.X + r, px.Y);
        }
    }

    private static void DrawMeasurePath(Bitmap bitmap, IReadOnlyList<(double Lat, double Lon)> points, int txMin, int tyMin, int z)
    {
        using var gfx = Graphics.FromImage(bitmap);
        gfx.SmoothingMode = SmoothingMode.AntiAlias;

        PointF ToPx((double Lat, double Lon) p) => new(
            (float)(MapTileFetcher.LonToTileX(p.Lon, z) * 256.0 - txMin * 256.0),
            (float)(MapTileFetcher.LatToTileY(p.Lat, z) * 256.0 - tyMin * 256.0));

        var pxPoints = points.Select(ToPx).ToArray();

        using var linePen = new Pen(Color.FromArgb(224, 49, 49), 3f) { DashPattern = new[] { 6f, 6f } };
        if (pxPoints.Length >= 2) gfx.DrawLines(linePen, pxPoints);

        using var fillBrush = new SolidBrush(Color.FromArgb(255, 135, 135));
        using var circlePen = new Pen(Color.FromArgb(224, 49, 49), 2f);
        const float r = 5f;
        foreach (var pt in pxPoints)
        {
            gfx.FillEllipse(fillBrush, pt.X - r, pt.Y - r, r * 2, r * 2);
            gfx.DrawEllipse(circlePen, pt.X - r, pt.Y - r, r * 2, r * 2);
        }
    }
}
