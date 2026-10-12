using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using NovaFiches.PdfSharpEngine;
using TopoRapportWin.Branding;

namespace TopoRapportWin.ControlePolygonale;

// Carte de situation "Contrôle de polygonale" - copie de ControlePrecisionMapService (voir ce
// fichier pour le détail des choix : centrage sur les points topo eux-mêmes, pas de géocodage
// d'adresse ; auto-détection du système de coordonnées via KmzExportService.DetectCoordinateSystem
// plutôt qu'un système déclaré à l'avance). Revenu à l'auto-détection après un premier essai avec
// le champ Projection des Référentiels (thème 1) : le champ Projection reste un texte déclaratif
// pour le corps du rapport (ce que le géomètre affirme comme système officiel), pas forcément une
// valeur reconnue telle quelle par ToWgs84 (ex. "Autre (préciser)") - le faire dépendre de ce champ
// faisait donc échouer la génération de carte sans rapport avec un vrai problème de coordonnées.
// L'auto-détection à partir des valeurs X/Y elles-mêmes, déjà éprouvée par ControlePrecision, est
// indépendante de ce que l'utilisateur a rempli ailleurs dans le formulaire.
public static class ControlePolygonaleMapService
{
    public sealed record MapResult(string ImageDataUrl, string CoordinateSystem, int Zoom);

    // maxZoom : plafond de niveau de zoom demandé par l'utilisateur (null = le plus détaillé
    // possible, comportement historique) - transmis tel quel à MapTileFetcher.ComputeTileGrid, qui
    // reste libre de redescendre si l'emprise ne tient pas dans le budget de tuiles à ce niveau.
    public static async Task<MapResult?> BuildMapFromPointsAsync(IReadOnlyList<(string Id, double X, double Y)> points, int? maxZoom = null)
    {
        if (points.Count == 0) return null;

        var kmzPoints = points.Select(p => new KmzExportService.KmzPoint(p.Id, p.X, p.Y, 0, null)).ToList();
        var detection = KmzExportService.DetectCoordinateSystem("", null, kmzPoints);

        var lonLat = new List<(double Lon, double Lat)>();
        foreach (var p in points)
        {
            try { lonLat.Add(KmzExportService.ToWgs84(p.X, p.Y, detection.CoordinateSystem)); }
            catch (InvalidOperationException) { /* système non supporté : point ignoré, pas bloquant */ }
        }
        if (lonLat.Count == 0) return null;

        double minLon = lonLat.Min(p => p.Lon), maxLon = lonLat.Max(p => p.Lon);
        double minLat = lonLat.Min(p => p.Lat), maxLat = lonLat.Max(p => p.Lat);
        double padLon = Math.Max((maxLon - minLon) * 0.15, 0.0006);
        double padLat = Math.Max((maxLat - minLat) * 0.15, 0.0004);
        minLon -= padLon; maxLon += padLon; minLat -= padLat; maxLat += padLat;

        var grid = MapTileFetcher.ComputeTileGrid(minLon, minLat, maxLon, maxLat, startZoom: maxZoom ?? 19);
        if (grid == null) return null;
        var (z, txMin, tyMin, txMax, tyMax) = grid.Value;

        using var stitched = await MapTileFetcher.FetchAndStitchTilesAsync(txMin, tyMin, txMax, tyMax, z, "plan").ConfigureAwait(false);
        if (stitched == null) return null;

        DrawMarkers(stitched, lonLat, txMin, tyMin, z);

        using var ms = new MemoryStream();
        stitched.Save(ms, ImageFormat.Png);
        var imageDataUrl = "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
        return new MapResult(imageDataUrl, detection.CoordinateSystem, z);
    }

    // Identique à ControlePrecisionMapService.DrawMarkers (rayon/contour épaissis pour rester nets
    // une fois la mosaïque de tuiles agrandie à la largeur de la page dans le PDF).
    private static void DrawMarkers(Bitmap bitmap, IReadOnlyList<(double Lon, double Lat)> pts, int txMin, int tyMin, int z)
    {
        using var gfx = Graphics.FromImage(bitmap);
        gfx.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(ResolveMarkerColor());
        using var pen = new Pen(Color.White, 1.8f);
        const float r = 6.5f;
        foreach (var (lon, lat) in pts)
        {
            float px = (float)(MapTileFetcher.LonToTileX(lon, z) * 256.0 - txMin * 256.0);
            float py = (float)(MapTileFetcher.LatToTileY(lat, z) * 256.0 - tyMin * 256.0);
            gfx.FillEllipse(brush, px - r, py - r, r * 2, r * 2);
            gfx.DrawEllipse(pen, px - r, py - r, r * 2, r * 2);
        }
    }

    private static Color ResolveMarkerColor()
    {
        var hex = BrandingService.LoadOrDefault().ColorBlueHex?.Trim().TrimStart('#');
        if (string.IsNullOrWhiteSpace(hex) || hex.Length != 6) return Color.FromArgb(0x12, 0x67, 0xF3);
        try
        {
            int r = Convert.ToInt32(hex.Substring(0, 2), 16);
            int g = Convert.ToInt32(hex.Substring(2, 2), 16);
            int b = Convert.ToInt32(hex.Substring(4, 2), 16);
            return Color.FromArgb(r, g, b);
        }
        catch { return Color.FromArgb(0x12, 0x67, 0xF3); }
    }
}
