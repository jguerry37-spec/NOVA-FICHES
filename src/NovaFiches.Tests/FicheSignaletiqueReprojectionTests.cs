using TopoRapportWin;
using Xunit;

namespace NovaFiches.Tests;

public class FicheSignaletiqueReprojectionTests
{
    [Fact]
    public void ToWgs84FromEpsg_Lambert93_ProjectsInsideFranceBoundingBox()
    {
        // Point Lambert-93 (EPSG:2154) proche de Paris.
        var result = FicheSignaletiqueReprojection.ToWgs84FromEpsg(652709.401, 6862827.204, "2154");

        Assert.NotNull(result);
        Assert.InRange(result!.Value.Lon, -5.5, 9.6);
        Assert.InRange(result.Value.Lat, 41.0, 51.5);
    }

    [Fact]
    public void ToWgs84FromEpsg_RgfCc49_ProjectsInsideFranceBoundingBox()
    {
        // Point RGF93 / CC49 (EPSG:3949), issu de l'exemple du modele CSV de l'outil d'origine.
        var result = FicheSignaletiqueReprojection.ToWgs84FromEpsg(1653717.673, 8168127.579, "3949");

        Assert.NotNull(result);
        Assert.InRange(result!.Value.Lon, -5.5, 9.6);
        Assert.InRange(result.Value.Lat, 41.0, 51.5);
    }

    [Fact]
    public void ToWgs84FromEpsg_NtfLambert2_DelegatesAndReturnsFiniteCoordinates()
    {
        // Pas de bornage strict "France" ici (contrairement aux tests Lambert-93/CC49
        // ci-dessus) : la valeur ne vise qu'à vérifier que l'adaptateur délègue correctement
        // à KmzExportService.ToWgs84 pour un système NTF historique, sans introduire de point
        // de contrôle géodésique non vérifié indépendamment.
        var result = FicheSignaletiqueReprojection.ToWgs84FromEpsg(608000, 2425000, "27562");

        Assert.NotNull(result);
        Assert.True(double.IsFinite(result!.Value.Lon));
        Assert.True(double.IsFinite(result.Value.Lat));
    }

    [Fact]
    public void ToWgs84FromEpsg_EpsgPrefix_IsAcceptedSameAsRawCode()
    {
        var withPrefix = FicheSignaletiqueReprojection.ToWgs84FromEpsg(652709.401, 6862827.204, "EPSG:2154");
        var raw = FicheSignaletiqueReprojection.ToWgs84FromEpsg(652709.401, 6862827.204, "2154");

        Assert.Equal(raw, withPrefix);
    }

    [Fact]
    public void ToWgs84FromEpsg_LambertIV_27564_ReturnsNullNotThrow()
    {
        // Volontairement non supporté pour cette passe (voir plan) : doit degrader proprement.
        var result = FicheSignaletiqueReprojection.ToWgs84FromEpsg(1234.5, 6789.0, "27564");
        Assert.Null(result);
    }

    [Fact]
    public void ToWgs84FromEpsg_UnknownEpsg_ReturnsNull()
    {
        var result = FicheSignaletiqueReprojection.ToWgs84FromEpsg(100, 200, "99999");
        Assert.Null(result);
    }

    [Theory]
    [InlineData((string?)null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ToWgs84FromEpsg_BlankEpsg_ReturnsNull(string? epsg)
    {
        var result = FicheSignaletiqueReprojection.ToWgs84FromEpsg(100, 200, epsg);
        Assert.Null(result);
    }

    [Fact]
    public void ToWgs84FromEpsg_Wgs84Passthrough_ReturnsSameCoordinates()
    {
        var result = FicheSignaletiqueReprojection.ToWgs84FromEpsg(2.35, 48.85, "4326");

        Assert.NotNull(result);
        Assert.Equal(2.35, result!.Value.Lon);
        Assert.Equal(48.85, result.Value.Lat);
    }
}
