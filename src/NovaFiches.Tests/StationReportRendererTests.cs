using NovaFiches.PdfSharpEngine;
using PdfSharp.Pdf;
using Xunit;

namespace NovaFiches.Tests;

/// <summary>
/// Fiche PDF "Station" : run TPS classique (résection) vs run GNSS de synthèse
/// (récepteur + référence RTK, pas d'observations/résidus angulaires). Vérifie que
/// les deux chemins produisent un PDF sans exception, avec au moins une page.
/// </summary>
public class StationReportRendererTests
{
    [Fact]
    public void Render_TpsStationLibre_AddsAtLeastOnePage()
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

        StationReportRenderer.Render(doc, json, "build-footer");

        Assert.True(doc.PageCount > before);
    }

    [Fact]
    public void Render_GnssRun_AddsAtLeastOnePage()
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

        StationReportRenderer.Render(doc, json, "build-footer");

        Assert.True(doc.PageCount > before);
    }

    [Fact]
    public void Render_NoRuns_DoesNotThrowAndAddsOnePage()
    {
        const string json = "{}";
        var doc = new PdfDocument();
        var before = doc.PageCount;

        StationReportRenderer.Render(doc, json, "build-footer");

        Assert.Equal(before + 1, doc.PageCount);
    }
}
