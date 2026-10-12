using TopoRapportWin;
using Xunit;

namespace NovaFiches.Tests;

public class NominatimGeocodingServiceTests
{
    [Fact]
    public void ParseGeocodeResponse_RoadAndAddressDetails_BuildsSuffixedAddress()
    {
        const string json = """
            {
              "name": "",
              "display_name": "Port de Tolbiac, Paris, Ile-de-France, France metropolitaine, France",
              "address": {
                "road": "Port de Tolbiac",
                "postcode": "75013",
                "city": "Paris",
                "state_district": "Ile-de-France",
                "country": "France"
              }
            }
            """;

        var result = NominatimGeocodingService.ParseGeocodeResponse(json);

        Assert.NotNull(result);
        Assert.Equal("Port de Tolbiac, 75013, Paris", result!.Adresse);
        Assert.Equal("Paris", result.Commune);
        Assert.Equal("Ile-de-France", result.Departement);
    }

    [Fact]
    public void ParseGeocodeResponse_HouseNumberAndRoad_AreJoined()
    {
        const string json = """
            {
              "address": { "house_number": "24", "road": "Boulevard Paul Vaillant Couturier", "city": "Ivry-sur-Seine", "postcode": "94200" }
            }
            """;

        var result = NominatimGeocodingService.ParseGeocodeResponse(json);

        Assert.NotNull(result);
        Assert.StartsWith("24 Boulevard Paul Vaillant Couturier", result!.Adresse);
        Assert.Equal("Ivry-sur-Seine", result.Commune);
    }

    [Fact]
    public void ParseGeocodeResponse_NoRoad_FallsBackToDisplayName()
    {
        const string json = """
            {
              "display_name": "Quelque part, Commune, Departement, France",
              "address": {}
            }
            """;

        var result = NominatimGeocodingService.ParseGeocodeResponse(json);

        Assert.NotNull(result);
        Assert.Equal("Quelque part, Commune", result!.Adresse);
    }

    [Fact]
    public void ParseGeocodeResponse_TownVillageHamletFallbacks_ResolveCommune()
    {
        const string json = """{ "address": { "road": "Chemin Vert", "village": "Petit Village" } }""";

        var result = NominatimGeocodingService.ParseGeocodeResponse(json);

        Assert.NotNull(result);
        Assert.Equal("Petit Village", result!.Commune);
    }

    [Fact]
    public void ParseGeocodeResponse_EmptyAddressAndNoName_ReturnsNull()
    {
        const string json = """{ "address": {} }""";

        var result = NominatimGeocodingService.ParseGeocodeResponse(json);

        Assert.Null(result);
    }

    [Fact]
    public void ParseGeocodeResponse_MalformedJson_ReturnsNullNotThrow()
    {
        var exception = Record.Exception(() => NominatimGeocodingService.ParseGeocodeResponse("not json"));

        Assert.Null(exception);
        Assert.Null(NominatimGeocodingService.ParseGeocodeResponse("not json"));
    }

    [Fact]
    public void ParseGeocodeResponse_AddressAlreadyContainsPostcodeAndCommune_DoesNotDuplicateSuffix()
    {
        // Postcode déjà présent dans l'adresse -> le suffixe "postcode, commune" est sauté ;
        // commune déjà présente aussi -> aucun des deux suffixes n'est ajouté (contrairement au
        // cas où seul le postcode est déjà présent, voir le test précédent).
        const string json = """
            {
              "address": { "road": "75013 Rue Test Paris", "postcode": "75013", "city": "Paris" }
            }
            """;

        var result = NominatimGeocodingService.ParseGeocodeResponse(json);

        Assert.NotNull(result);
        Assert.Equal("75013 Rue Test Paris", result!.Adresse);
    }

    [Fact]
    public void ParseForwardGeocodeResponse_FirstResult_ExtractsLatLonAndDisplayName()
    {
        const string json = """
            [
              { "lat": "48.8698264", "lon": "2.3072663", "display_name": "Port de Tolbiac, Paris, France" },
              { "lat": "0", "lon": "0", "display_name": "Autre résultat, ignoré" }
            ]
            """;

        var result = NominatimGeocodingService.ParseForwardGeocodeResponse(json);

        Assert.NotNull(result);
        Assert.Equal(48.8698264, result!.Lat, 6);
        Assert.Equal(2.3072663, result.Lon, 6);
        Assert.Equal("Port de Tolbiac, Paris, France", result.DisplayName);
    }

    [Fact]
    public void ParseForwardGeocodeResponse_EmptyArray_ReturnsNull()
    {
        Assert.Null(NominatimGeocodingService.ParseForwardGeocodeResponse("[]"));
    }

    [Fact]
    public void ParseForwardGeocodeResponse_MissingLatLon_ReturnsNull()
    {
        const string json = """[ { "display_name": "Sans coordonnées" } ]""";

        Assert.Null(NominatimGeocodingService.ParseForwardGeocodeResponse(json));
    }

    [Fact]
    public void ParseForwardGeocodeResponse_MalformedJson_ReturnsNullNotThrow()
    {
        var exception = Record.Exception(() => NominatimGeocodingService.ParseForwardGeocodeResponse("not json"));

        Assert.Null(exception);
        Assert.Null(NominatimGeocodingService.ParseForwardGeocodeResponse("not json"));
    }

    [Fact]
    public void ParseForwardGeocodeResponse_NoDisplayName_FallsBackToLatLonText()
    {
        const string json = """[ { "lat": "45.0", "lon": "3.5" } ]""";

        var result = NominatimGeocodingService.ParseForwardGeocodeResponse(json);

        Assert.NotNull(result);
        Assert.Equal("45, 3.5", result!.DisplayName);
    }

    [Theory]
    [InlineData("Paris 01", "75001 Paris")]
    [InlineData("paris 1", "75001 paris")]
    [InlineData("Paris 15", "75015 Paris")]
    [InlineData("Paris 20e", "75020 Paris")]
    [InlineData("Paris 7eme arrondissement", "75007 Paris")]
    [InlineData("Lyon 3", "69003 Lyon")]
    [InlineData("Marseille 08", "13008 Marseille")]
    public void TryNormalizeArrondissementQuery_KnownShorthand_ReturnsPostalCodeQuery(string input, string expected)
    {
        Assert.Equal(expected, NominatimGeocodingService.TryNormalizeArrondissementQuery(input));
    }

    [Theory]
    [InlineData("12 rue de la Paix, Paris")]
    [InlineData("Paris")]
    [InlineData("Paris 21")] // n'existe pas (max 20)
    [InlineData("Toulouse 3")] // pas une ville a arrondissements geree
    public void TryNormalizeArrondissementQuery_NotAShorthand_ReturnsNull(string input)
    {
        Assert.Null(NominatimGeocodingService.TryNormalizeArrondissementQuery(input));
    }
}
