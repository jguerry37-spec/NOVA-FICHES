using NovaFiches.PdfSharpEngine;
using PdfSharp.Pdf;
using Xunit;

namespace NovaFiches.Tests;

/// <summary>
/// "PDF - Page de garde" (module Projet) : une seule page, en-tête + cartouche, aucune
/// donnée de levé requise. Vérifie que le renderer ajoute exactement une page même avec
/// un payload minimal (cartouche vide) et n'échoue pas sur un payload riche.
/// </summary>
public class CoverOnlyReportRendererTests
{
    [Fact]
    public void Render_MinimalPayload_AddsExactlyOnePage()
    {
        const string json = "{}";
        var doc = new PdfDocument();
        var before = doc.PageCount;

        CoverOnlyReportRenderer.Render(doc, json, "build-footer");

        Assert.Equal(before + 1, doc.PageCount);
    }

    [Fact]
    public void Render_FullCartouchePayload_AddsExactlyOnePage()
    {
        const string json = """
            {
              "nomIntervention": "IMPLANTATION N10",
              "ville": "Romainville",
              "adresse": "24 boulevard Paul Vaillant Couturier",
              "cha": "02782",
              "entreprise": "LOGISUR",
              "contactClient": "Jean Dupont",
              "systemeCoord": "RGF93 CC49",
              "ppm": "0.000",
              "intervenant": "Julien GUERRY",
              "systemeAlti": "IGN 69",
              "planRef": "xxxxx.dwg",
              "date": "21/07/2026",
              "appareil": "TS15 P",
              "serialNumber": "1616544"
            }
            """;
        var doc = new PdfDocument();
        var before = doc.PageCount;

        CoverOnlyReportRenderer.Render(doc, json, "build-footer");

        Assert.Equal(before + 1, doc.PageCount);
    }
}
