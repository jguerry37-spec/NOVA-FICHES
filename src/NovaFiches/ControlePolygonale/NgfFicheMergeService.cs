using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace TopoRapportWin.ControlePolygonale;

// Fusionne les fiches PDF officielles IGN des repères NGF sélectionnés à la fin d'un rapport déjà
// généré sur disque - décision utilisateur explicite (module Nivellement du Rapport de
// polygonale) : "vraiment fusionnée (pages ajoutées au rapport)", pas un simple lien/référence.
// Réutilisé tel quel par l'outil "Mesurer une distance" du module Export KMZ (repère(s) touché(s)
// pendant le tracé de la mesure - voir MergeFicheUrlsAsync, appelé depuis
// MainForm.ExportKmzMeasurePdfForUiAsync).
//
// Tolérant à chaque étape (repère introuvable, fiche non téléchargeable, page PDF non
// importable) : ignore silencieusement le repère en cause plutôt que de faire échouer tout
// l'export - cohérent avec MapTileFetcher.FetchBytesAsync et les autres appels réseau non
// bloquants de ce projet.
internal static class NgfFicheMergeService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    // Recherche par emprise géographique + filtrage par identifiant saisi manuellement (module
    // Nivellement du Rapport de polygonale) : pas de requête WFS filtrée par identifiant (jamais
    // exercée/vérifiée côté IGN, risque de syntaxe CQL incorrecte) - on réutilise tel quel le flux
    // déjà éprouvé par l'export KMZ (IgnGeodesyService.FetchBenchmarksAsync par emprise), avec une
    // emprise élargie autour des points GeoBase du projet (système de coordonnées auto-détecté),
    // puis on filtre les résultats par identifiant côté C#.
    public static async Task<int> MergeSelectedFichesAsync(
        string pdfPath,
        IReadOnlyList<string> repereIds,
        IReadOnlyList<(double X, double Y)> points)
    {
        var wanted = repereIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).ToList();
        if (wanted.Count == 0 || points.Count == 0) return 0;

        var kmzPoints = points.Select(p => new KmzExportService.KmzPoint("", p.X, p.Y, 0, null)).ToList();
        var detection = KmzExportService.DetectCoordinateSystem("", null, kmzPoints);

        var lonLat = new List<(double Lon, double Lat)>();
        foreach (var p in points)
        {
            try { lonLat.Add(KmzExportService.ToWgs84(p.X, p.Y, detection.CoordinateSystem)); }
            catch (InvalidOperationException) { /* point ignoré, pas bloquant */ }
        }
        if (lonLat.Count == 0) return 0;

        double minLon = lonLat.Min(p => p.Lon), maxLon = lonLat.Max(p => p.Lon);
        double minLat = lonLat.Min(p => p.Lat), maxLat = lonLat.Max(p => p.Lat);
        // Emprise sensiblement élargie par rapport à la carte de situation : un repère NGF de
        // rattachement n'est pas forcément proche du semis de points levés - en pratique le
        // repère officiel le plus proche peut être à plusieurs kilomètres du chantier (réseau NGF
        // pas toujours dense). Marge mini ~5 km de large (0.05°/0.04° à cette latitude) plutôt que
        // les ~1,5 km utilisés pour la carte de situation (ControlePolygonaleMapService), qui elle
        // doit rester proche du semis de points par nature.
        double padLon = Math.Max((maxLon - minLon) * 0.5, 0.05);
        double padLat = Math.Max((maxLat - minLat) * 0.5, 0.04);
        minLon -= padLon; maxLon += padLon; minLat -= padLat; maxLat += padLat;

        List<IgnGeodesyService.NgfBenchmark> benchmarks;
        try
        {
            benchmarks = await IgnGeodesyService.FetchBenchmarksAsync(minLon, minLat, maxLon, maxLat).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Error("Repères NGF : recherche par emprise échouée (fusion des fiches ignorée)", ex);
            return 0;
        }

        var matchedUrls = benchmarks
            .Where(b => wanted.Any(w => string.Equals(w, b.Id, StringComparison.OrdinalIgnoreCase)
                                      || string.Equals(w, b.Nom, StringComparison.OrdinalIgnoreCase)))
            .Where(b => !string.IsNullOrWhiteSpace(b.FicheUrl))
            .Select(b => b.FicheUrl!)
            .ToList();
        if (matchedUrls.Count == 0) return 0;

        return await MergeFicheUrlsAsync(pdfPath, matchedUrls).ConfigureAwait(false);
    }

    // Fusion directe à partir d'URL de fiches déjà connues côté appelant (pas de recherche WFS) -
    // cas de l'outil "Mesurer une distance" (Export KMZ), où le repère touché pendant le tracé de
    // la mesure est déjà un marqueur affiché à l'écran (chargé via kmz_fetch_ngf) : son ficheUrl
    // est donc déjà en main côté JS, transmis tel quel, sans repasser par une recherche par
    // emprise. Une même mesure peut toucher plusieurs repères (pas seulement au début/à la fin du
    // tracé) - la liste peut donc contenir plusieurs URLs, dédoublonnées ici par prudence.
    public static async Task<int> MergeFicheUrlsAsync(string pdfPath, IReadOnlyList<string> ficheUrls)
    {
        var urls = ficheUrls
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (urls.Count == 0) return 0;

        int appended = 0;
        try
        {
            using var doc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
            foreach (var url in urls)
            {
                try
                {
                    var bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
                    using var ms = new MemoryStream(bytes);
                    using var ficheDoc = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
                    for (int i = 0; i < ficheDoc.PageCount; i++)
                    {
                        doc.AddPage(ficheDoc.Pages[i]);
                        appended++;
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Error($"Fiche NGF {url} : non intégrée", ex);
                }
            }
            if (appended > 0) doc.Save(pdfPath);
        }
        catch (Exception ex)
        {
            AppLog.Error("Fiches NGF : ouverture/sauvegarde du rapport pour fusion échouée", ex);
            return 0;
        }
        return appended;
    }
}
