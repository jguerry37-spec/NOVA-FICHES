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

    public static async Task<MapResult?> BuildMapFromPointsAsync(IReadOnlyList<(double Lat, double Lon)> points, string basemapKind)
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

        DrawMeasurePath(stitched, points, txMin, tyMin, z);

        using var ms = new MemoryStream();
        stitched.Save(ms, ImageFormat.Png);
        return new MapResult("data:image/png;base64," + Convert.ToBase64String(ms.ToArray()));
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
