using NovaFiches.PdfSharpEngine;
using PdfSharp.Pdf;
using Xunit;

namespace NovaFiches.Tests;

/// <summary>
/// "Rapport complet" (Récolement / Intervention pieux) : ce renderer a sa propre copie
/// du bloc "TYPE DE STATION" (distincte de StationReportRenderer). Vérifie qu'un run
/// GNSS (sans résection TPS) ne fait pas planter le rendu, tables Observations/Résidus
/// exclues.
/// </summary>
public class ImplantationFullReportRendererGnssTests
{
    [Fact]
    public void Render_GnssRun_DoesNotThrowAndAddsAtLeastOnePage()
    {
        const string json = """
            {
              "stationLibreRuns": [
                {
                  "method": "GNSS",
                  "observations": [],
                  "residuals": [],
                  "results": {
                    "idStation": null,
                    "method": "GNSS",
                    "receiver": "Leica Geosystems AG CS20 (série 2426194)",
                    "antennaHeight": 2.0,
                    "rtkRef": { "name": "RTCM-Ref 0000", "E": 1675815.99, "N": 8148844.30, "H": 84.74 }
                  }
                }
              ]
            }
            """;
        var doc = new PdfDocument();
        var before = doc.PageCount;

        ImplantationFullReportRenderer.Render(doc, json, "build-footer");

        Assert.True(doc.PageCount > before);
    }

    [Fact]
    public void Render_TpsRun_DoesNotThrowAndAddsAtLeastOnePage()
    {
        const string json = """
            {
              "stationLibreRuns": [
                {
                  "method": "Station libre",
                  "observations": [],
                  "residuals": [],
                  "results": { "idStation": "S1", "method": "Station libre", "E": "1000.000", "N": "2000.000", "H": "50.000" }
                }
              ]
            }
            """;
        var doc = new PdfDocument();
        var before = doc.PageCount;

        ImplantationFullReportRenderer.Render(doc, json, "build-footer");

        Assert.True(doc.PageCount > before);
    }
}
