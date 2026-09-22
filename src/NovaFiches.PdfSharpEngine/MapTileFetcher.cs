using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace NovaFiches.PdfSharpEngine;

/// <summary>
/// Fond de carte (tuiles standard "slippy map") pour les pages PDF vectorielles qui ont besoin
/// d'un fond de plan réel. Extrait de StationPlanRenderer (page "Plan station") pour être
/// réutilisé tel quel par FicheSignaletiqueRenderer (carte de situation par fiche) - déplacement
/// pur, aucun changement de comportement. Public (pas internal) : réutilisé aussi côté NovaFiches
/// par ControlePrecisionMapService pour composer une carte PNG autonome (pas une page PdfSharp) -
/// le projet NovaFiches référence déjà PdfSharpEngine.
/// </summary>
public static class MapTileFetcher
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };

    // Débit raisonnable côté réseau, quel que soit le fournisseur - voir aussi le cache
    // ci-dessous (une tuile déjà récupérée pendant la session n'est pas redemandée).
    private static readonly SemaphoreSlim ConcurrencyLimiter = new(2, 2);
    private static readonly ConcurrentDictionary<string, Task<byte[]?>> TileCache = new();

    static MapTileFetcher()
    {
        try { Http.DefaultRequestHeaders.UserAgent.ParseAdd("Nova-Fiches/PdfExport (+https://novatlas.fr)"); } catch { }
    }

    public static double LonToTileX(double lon, int z) => (lon + 180.0) / 360.0 * (1 << z);

    public static double LatToTileY(double lat, int z)
    {
        double latRad = lat * Math.PI / 180.0;
        return (1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * (1 << z);
    }

    // Choisit le zoom le plus détaillé dont la grille de tuiles couvrant l'emprise reste
    // dans le budget (maxTilesPerSide) - évite de télécharger des dizaines de tuiles pour
    // une emprise large, tout en restant aussi net que possible pour une petite emprise. 19 :
    // zoom natif max à la fois d'OSM et du Plan IGN v2 (au-delà, tuiles inexistantes côté serveur).
    public static (int z, int txMin, int tyMin, int txMax, int tyMax)? ComputeTileGrid(
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

    public static string TileUrl(int x, int y, int z, string kind)
    {
        if (string.Equals(kind, "satellite", StringComparison.OrdinalIgnoreCase))
            return $"https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}";
        // Plan IGN v2 (Géoplateforme, data.geopf.fr) : grille standard PM (Pseudo-Mercator) en
        // tuiles 256px, exactement la même convention z/x/y que le reste de ce fichier - aucun
        // repli/découpage nécessaire, contrairement aux fournisseurs "retina" (MapTiler) essayés
        // avant. Service public en accès libre, sans clé API (a remplacé l'ancien système à clé
        // de wxs.ign.fr) - même domaine data.geopf.fr déjà utilisé par ce projet pour les repères
        // NGF (IgnGeodesyService). Préféré à OpenStreetMap direct : bloqué à plusieurs reprises
        // (leur politique d'usage interdit en principe de distribuer une application qui s'en
        // sert, indépendamment du rythme des requêtes - voir historique des versions).
        return "https://data.geopf.fr/wmts?SERVICE=WMTS&REQUEST=GetTile&VERSION=1.0.0"
             + "&LAYER=GEOGRAPHICALGRIDSYSTEMS.PLANIGNV2&STYLE=normal&FORMAT=image/png&TILEMATRIXSET=PM"
             + $"&TILEMATRIX={z}&TILEROW={y}&TILECOL={x}";
    }

    // Récupère les octets bruts d'UNE tuile (mise en cache pour la durée du processus - voir
    // TileCache) en respectant ConcurrencyLimiter, sans jamais lever : un échec (réseau, 404...)
    // renvoie null plutôt que de faire échouer tout le fond de carte - cohérent avec la
    // tolérance déjà en place pour les repères NGF (IgnGeodesyService) et l'affichage carte à
    // l'écran.
    private static Task<byte[]?> FetchBytesAsync(string url)
    {
        return TileCache.GetOrAdd(url, async u =>
        {
            await ConcurrencyLimiter.WaitAsync().ConfigureAwait(false);
            try { return await Http.GetByteArrayAsync(u).ConfigureAwait(false); }
            catch { return null; }
            finally { ConcurrencyLimiter.Release(); }
        });
    }

    public static async Task<Bitmap?> FetchAndStitchTilesAsync(int txMin, int tyMin, int txMax, int tyMax, int z, string kind)
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
                    var bytes = await FetchBytesAsync(TileUrl(localTx, localTy, z, kind)).ConfigureAwait(false);
                    if (bytes == null) return;
                    try
                    {
                        using var ms = new System.IO.MemoryStream(bytes);
                        using var tileImg = Image.FromStream(ms);
                        lock (lockObj)
                        {
                            using var gfx = System.Drawing.Graphics.FromImage(bitmap);
                            gfx.DrawImage(tileImg, (localTx - txMin) * 256, (localTy - tyMin) * 256, 256, 256);
                        }
                    }
                    catch { /* image invalide : trou gris à cet endroit, le reste continue */ }
                }));
            }
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);
        return bitmap;
    }
}
