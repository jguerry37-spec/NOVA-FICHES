using NovaFiches.PdfSharpEngine;
using Xunit;

namespace NovaFiches.Tests;

// Ajout du 2026-09-17 : les tuiles OpenStreetMap directes (tile.openstreetmap.org) se sont fait
// bloquer ("Access blocked - App is not following the tile usage policy") - leur politique
// d'usage interdit en principe de distribuer une application qui les utilise sans autorisation
// préalable. Remplacées par le Plan IGN v2 (Géoplateforme, data.geopf.fr) : service public en
// accès libre (sans clé API), grille standard 256px z/x/y compatible telle quelle avec le reste
// du code (contrairement à MapTiler, essayé un temps, dont les tuiles "retina" 512px auraient
// demandé un découpage en quadrants - abandonné au profit de cette solution plus simple).
public class MapTileFetcherTests
{
    [Fact]
    public void TileUrl_Plan_UsesIgnGeoplateformeWmtsWithoutApiKey()
    {
        var url = MapTileFetcher.TileUrl(x: 12, y: 34, z: 9, kind: "plan");

        Assert.StartsWith("https://data.geopf.fr/wmts?", url);
        Assert.Contains("LAYER=GEOGRAPHICALGRIDSYSTEMS.PLANIGNV2", url);
        Assert.Contains("TILEMATRIXSET=PM", url);
        Assert.Contains("TILEMATRIX=9", url);
        Assert.Contains("TILEROW=34", url);
        Assert.Contains("TILECOL=12", url);
        Assert.DoesNotContain("apikey", url, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("key=", url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TileUrl_Satellite_UsesEsriNotIgn()
    {
        var url = MapTileFetcher.TileUrl(1, 2, 3, "satellite");
        Assert.Contains("arcgisonline.com", url);
        Assert.DoesNotContain("geopf.fr", url);
    }

    [Fact]
    public void ComputeTileGrid_SmallExtent_PicksMostDetailedZoomWithinBudget()
    {
        // Nozay (91) - petite emprise, doit pouvoir monter jusqu'au zoom natif max (19) commun
        // à OSM et au Plan IGN v2, sans dépasser le budget de tuiles par défaut (6x6).
        var grid = MapTileFetcher.ComputeTileGrid(minLon: 2.201, minLat: 48.699, maxLon: 2.203, maxLat: 48.701);

        Assert.NotNull(grid);
        Assert.InRange(grid!.Value.z, 15, 19);
        Assert.True(grid.Value.txMax - grid.Value.txMin + 1 <= 6);
        Assert.True(grid.Value.tyMax - grid.Value.tyMin + 1 <= 6);
    }
}
