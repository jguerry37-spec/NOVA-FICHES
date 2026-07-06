using System.IO.Compression;
using TopoRapportWin;
using Xunit;

namespace NovaFiches.Tests;

public class KmzExportServiceTests
{
    [Theory]
    [InlineData("leve_CC49_secteur1.txt", "RGF93 / CC49 (EPSG:3949)")]
    [InlineData("chantier_L93.dxf", "RGF93 / Lambert-93 (EPSG:2154)")]
    [InlineData("points_LAMBERT_2_ETENDU.txt", "NTF / Lambert 2 étendu (EPSG:27572)")]
    [InlineData("sans_indice.txt", "RGF93 / CC49 (EPSG:3949)")]
    public void GuessCoordinateSystemFromFileName_MatchesExpectedToken(string fileName, string expected)
    {
        var result = KmzExportService.GuessCoordinateSystemFromFileName(fileName);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void DetectCoordinateSystem_FindsEpsgMarkerInFileContent()
    {
        var content = "some header ... EPSG:2154 ... more content";
        var result = KmzExportService.DetectCoordinateSystem("inconnu.txt", content);

        Assert.Equal("RGF93 / Lambert-93 (EPSG:2154)", result.CoordinateSystem);
        Assert.Equal("métadonnées du fichier", result.Method);
    }

    [Fact]
    public void DetectCoordinateSystem_FallsBackToFileNameWhenNoMetadata()
    {
        var result = KmzExportService.DetectCoordinateSystem("leve_CC46.txt", fileContent: null);
        Assert.Equal("RGF93 / CC46 (EPSG:3946)", result.CoordinateSystem);
        Assert.Equal("nom du fichier", result.Method);
    }

    [Fact]
    public void DetectCoordinateSystem_UsesPointRangeAsLastResort_Wgs84()
    {
        var points = new[]
        {
            new KmzExportService.KmzPoint("P1", 2.35, 48.85, 0, null),
        };

        var result = KmzExportService.DetectCoordinateSystem("sans_indice_ambigu.txt", fileContent: null, points: points);

        Assert.Equal("WGS84 lon/lat (EPSG:4326)", result.CoordinateSystem);
        Assert.Equal("plage des coordonnées", result.Method);
    }

    [Fact]
    public void ProjectForPreview_Wgs84Source_IsPassthrough()
    {
        var points = new[] { new KmzExportService.KmzPoint("P1", 2.35, 48.85, 100, "A") };

        var result = KmzExportService.ProjectForPreview(points, "WGS84 lon/lat (EPSG:4326)");

        Assert.Single(result);
        Assert.Equal(2.35, result[0].Lon);
        Assert.Equal(48.85, result[0].Lat);
    }

    [Fact]
    public void ProjectForPreview_Lambert93Point_ProjectsInsideFranceBoundingBox()
    {
        // Point Lambert-93 plausible pour un chantier en France métropolitaine
        // (X/Y dans les plages attendues par KmzExportService.DetectCoordinateSystem).
        var points = new[] { new KmzExportService.KmzPoint("P1", 650000, 6860000, 100, "A") };

        var result = KmzExportService.ProjectForPreview(points, "RGF93 / Lambert-93 (EPSG:2154)");

        Assert.Single(result);
        // Vérification de cohérence géographique large (métropole française), pas d'une valeur
        // exacte tierce : détecte les régressions grossières (inversion lat/lon, échelle fausse,
        // NaN) sans dépendre d'un outil de référence externe.
        Assert.InRange(result[0].Lon, -5.5, 9.6);
        Assert.InRange(result[0].Lat, 41.0, 51.5);
    }

    [Fact]
    public void ProjectForPreview_UnknownCoordinateSystem_Throws()
    {
        var points = new[] { new KmzExportService.KmzPoint("P1", 1, 1, 0, null) };

        Assert.Throws<InvalidOperationException>(() =>
            KmzExportService.ProjectForPreview(points, "Systeme inexistant"));
    }

    [Fact]
    public void ExportPointsToKmz_EmptyList_Throws()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"nf-test-empty-{Guid.NewGuid():N}.kmz");

        Assert.Throws<InvalidOperationException>(() =>
            KmzExportService.ExportPointsToKmz(Array.Empty<KmzExportService.KmzPoint>(), "WGS84 lon/lat (EPSG:4326)", outputPath, "Doc"));
    }

    [Fact]
    public void ExportPointsToKmz_WritesValidZipContainingPlacemark()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"nf-test-{Guid.NewGuid():N}.kmz");
        try
        {
            var points = new[] { new KmzExportService.KmzPoint("P1", 2.35, 48.85, 100, "SONDAGE") };

            KmzExportService.ExportPointsToKmz(points, "WGS84 lon/lat (EPSG:4326)", outputPath, "Chantier test");

            Assert.True(File.Exists(outputPath));

            using var archive = ZipFile.OpenRead(outputPath);
            var entry = archive.GetEntry("doc.kml");
            Assert.NotNull(entry);

            using var reader = new StreamReader(entry!.Open());
            var kml = reader.ReadToEnd();

            Assert.Contains("<Placemark>", kml);
            Assert.Contains("P1", kml);
            Assert.Contains("SONDAGE", kml);
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }
}
