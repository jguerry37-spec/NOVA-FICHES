using NovaFiches.PdfSharpEngine;
using PdfSharp.Pdf;
using Xunit;

namespace NovaFiches.Tests;

/// <summary>
/// "Envoyer sur la fiche station" (onglet Plan station) : StationPlanRenderer ajoute une
/// page uniquement si le payload contient une clé "stationPlanView" (cochée côté JS) et
/// n'échoue jamais silencieusement au point de faire disparaître le reste du PDF.
/// </summary>
public class StationPlanRendererTests
{
    private const string ValidPayload = """
        {
          "ville": "Romainville",
          "stationPlanView": {
            "stations": [
              { "label": "ST1", "e": 1658860.1, "n": 8188559.8, "color": "#1267f3" },
              { "label": "ST2", "e": 1658870.2, "n": 8188565.3, "color": "#f76707" }
            ],
            "points": [
              { "id": "27", "e": 1658855.0, "n": 8188540.0, "included": true },
              { "id": "14", "e": 1658880.0, "n": 8188570.0, "included": false }
            ],
            "sightings": [
              { "stationLabel": "ST1", "pointId": "27", "color": "#1267f3", "included": true },
              { "stationLabel": "ST2", "pointId": "14", "color": "#f76707", "included": false }
            ]
          }
        }
        """;

    [Fact]
    public void AppendFromPayload_WithStationPlanView_AddsOnePage()
    {
        var doc = new PdfDocument();
        var before = doc.PageCount;

        StationPlanRenderer.AppendFromPayload(doc, ValidPayload, "build-footer");

        Assert.Equal(before + 1, doc.PageCount);
    }

    [Fact]
    public void AppendFromPayload_WithoutStationPlanViewKey_IsNoOp()
    {
        const string json = """{ "ville": "Romainville" }""";
        var doc = new PdfDocument();
        var before = doc.PageCount;

        StationPlanRenderer.AppendFromPayload(doc, json, "build-footer");

        Assert.Equal(before, doc.PageCount);
    }

    [Fact]
    public void AppendFromPayload_WithEmptyStationsAndPoints_IsNoOp()
    {
        const string json = """{ "stationPlanView": { "stations": [], "points": [] } }""";
        var doc = new PdfDocument();
        var before = doc.PageCount;

        StationPlanRenderer.AppendFromPayload(doc, json, "build-footer");

        Assert.Equal(before, doc.PageCount);
    }

    [Fact]
    public void AppendFromPayload_MalformedJson_DoesNotThrow()
    {
        var doc = new PdfDocument();
        var before = doc.PageCount;

        StationPlanRenderer.AppendFromPayload(doc, "not json", "build-footer");

        Assert.Equal(before, doc.PageCount);
    }
}
