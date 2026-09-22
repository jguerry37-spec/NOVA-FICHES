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
}
