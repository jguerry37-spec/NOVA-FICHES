using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using NovaFiches.PdfSharpEngine;
using TopoRapportWin.Branding;

namespace TopoRapportWin.ControlePrecision;

// Carte de situation "Contrôle classe de précision" centrée sur les points topo eux-mêmes
// (barycentre + emprise), plutôt que sur un géocodage d'adresse qui échoue dès que l'adresse
// saisie n'est pas un format que Nominatim sait résoudre (ex. "Rues X et Y", plusieurs rues à la
// fois). Réutilise telles quelles l'auto-détection de système de coordonnées et la reprojection
// WGS84 déjà écrites pour l'export KMZ (KmzExportService.DetectCoordinateSystem/ToWgs84), ainsi
// que MapTileFetcher (tuiles OSM) déjà utilisé par Fiches signalétiques - aucune nouvelle
// dépendance réseau ou de projection.
public static class ControlePrecisionMapService
{
    public sealed record MapResult(string ImageDataUrl, string CoordinateSystem);

    public static async Task<MapResult?> BuildMapFromPointsAsync(IReadOnlyList<CpPoint> points)
    {
        if (points.Count == 0) return null;

        // Pas de nom de fichier ici (les points arrivent déjà parsés) : DetectCoordinateSystem
        // bascule directement sur son heuristique par plage de coordonnées (X/Y moyens), déjà
        // conçue pour ce cas - voir KmzExportService.DetectCoordinateSystem.
        var kmzPoints = points.Select(p => new KmzExportService.KmzPoint(p.Id, p.X, p.Y, p.Z, null)).ToList();
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

        var grid = MapTileFetcher.ComputeTileGrid(minLon, minLat, maxLon, maxLat);
        if (grid == null) return null;
        var (z, txMin, tyMin, txMax, tyMax) = grid.Value;

        using var stitched = await MapTileFetcher.FetchAndStitchTilesAsync(txMin, tyMin, txMax, tyMax, z, "plan").ConfigureAwait(false);
        if (stitched == null) return null;

        DrawMarkers(stitched, lonLat, txMin, tyMin, z);

        using var ms = new MemoryStream();
        stitched.Save(ms, ImageFormat.Png);
        return new MapResult("data:image/png;base64," + Convert.ToBase64String(ms.ToArray()), detection.CoordinateSystem);
    }

    // Petit point bleu NOVATLAS par point topo (pas un gros marqueur unique - potentiellement des
    // dizaines de points) - le pixel de chaque point est déduit de sa position lon/lat au zoom
    // choisi, relative au coin haut-gauche de la mosaïque de tuiles.
    private static void DrawMarkers(Bitmap bitmap, IReadOnlyList<(double Lon, double Lat)> pts, int txMin, int tyMin, int z)
    {
        using var gfx = Graphics.FromImage(bitmap);
        gfx.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(ResolveMarkerColor());
        using var pen = new Pen(Color.White, 1.8f);
        // La mosaïque de tuiles (quelques centaines de px de large) est ensuite étirée pour
        // occuper toute la largeur de la page de garde dans le PDF - à taille native (r=3.5),
        // les points devenaient minuscules une fois agrandis (retour utilisateur : "à peine
        // visibles"). Rayon et contour épaissis pour rester nets après cet agrandissement.
        const float r = 6.5f;
        foreach (var (lon, lat) in pts)
        {
            float px = (float)(MapTileFetcher.LonToTileX(lon, z) * 256.0 - txMin * 256.0);
            float py = (float)(MapTileFetcher.LatToTileY(lat, z) * 256.0 - tyMin * 256.0);
            gfx.FillEllipse(brush, px - r, py - r, r * 2, r * 2);
            gfx.DrawEllipse(pen, px - r, py - r, r * 2, r * 2);
        }
    }

    // Bug réel constaté (audit du 2026-09-03) : cette carte bakait sa couleur de marqueur en dur
    // (bleu NOVATLAS par défaut) alors que tous les autres rendus PDF respectent le bleu
    // personnalisé configuré dans Paramètres (voir NovatlasTheme.ResolveBlue côté PdfSharpEngine,
    // dont ce parsing hexadécimal est le miroir - PdfSharpEngine ne référence jamais BrandingService
    // directement, mais ce service-ci vit côté NovaFiches et peut donc l'appeler sans détour).
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
