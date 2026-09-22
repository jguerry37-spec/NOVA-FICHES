namespace TopoRapportWin;

// Adaptateur fin au-dessus de KmzExportService.ToWgs84 : la colonne CSV "epsg_source" porte un
// code EPSG brut ("3949", "EPSG:2154") plutôt que le nom affiché ("RGF93 / CC49 (EPSG:3949)")
// attendu historiquement par ToWgs84. En pratique ToWgs84 matche déjà sur une sous-chaîne
// (Contains "2154", "27561", "CC49"/"3949", ...), donc un code EPSG brut suffit tel quel : pas
// besoin de reconstruire le nom affiché complet.
public static class FicheSignaletiqueReprojection
{
    // EPSG:27564 (NTF Lambert IV / Corse) n'est volontairement pas supporté : KmzExportService
    // ne connaît pas ce cas (voir ToWgs84), donc l'appel échoue proprement et retombe sur null
    // ci-dessous - pas de risque de coordonnées fausses sur un système non vérifié.
    public static (double Lon, double Lat)? ToWgs84FromEpsg(double x, double y, string? epsgCodeRaw)
    {
        if (string.IsNullOrWhiteSpace(epsgCodeRaw))
            return null;

        var code = epsgCodeRaw.Trim();
        if (code.StartsWith("EPSG:", StringComparison.OrdinalIgnoreCase))
            code = code[5..].Trim();
        if (code.Length == 0)
            return null;

        try
        {
            return KmzExportService.ToWgs84(x, y, code);
        }
        catch (InvalidOperationException)
        {
            // Systeme de coordonnees non pris en charge : une ligne en echec ne doit pas
            // bloquer l'export des autres lignes du CSV.
            return null;
        }
    }
}
