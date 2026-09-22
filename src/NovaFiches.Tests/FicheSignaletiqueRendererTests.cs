using NovaFiches.PdfSharpEngine;
using PdfSharp.Pdf;
using Xunit;

namespace NovaFiches.Tests;

/// <summary>
/// AppendFromPayload ajoute une page A4 par ligne de "fichesSignaletiques.rows". Aucun test
/// ici ne fournit lon/lat : une ligne avec coordonnées résolues déclenche un vrai
/// téléchargement de tuiles (MapTileFetcher, réseau) - hors de portée d'un test unitaire
/// rapide et déterministe (même choix déjà fait par StationPlanRendererTests, dont le
/// payload de test n'a pas non plus de lon/lat).
/// </summary>
public class FicheSignaletiqueRendererTests
{
    private const string TwoRowsNoCoordsPayload = """
        {
          "fichesSignaletiques": {
            "defaultZoom": 19,
            "mapProvider": "plan",
            "rows": [
              { "idFiche": "FS-001", "point": "GEO-ST101", "dossier": "CHA03212", "chantier": "Test",
                "client": "NOVATLAS", "commune": "Juvisy", "x": 1653717.673, "y": 8168127.579, "z": 86.437,
                "epsgSource": "3949", "observations": "RAS" },
              { "idFiche": "FS-002", "point": "S.50" }
            ]
          }
        }
        """;

    [Fact]
    public void AppendFromPayload_WithTwoRows_AddsTwoPages()
    {
        var doc = new PdfDocument();
        var before = doc.PageCount;

        FicheSignaletiqueRenderer.AppendFromPayload(doc, TwoRowsNoCoordsPayload, "build-footer");

        Assert.Equal(before + 2, doc.PageCount);
    }

    [Fact]
    public void AppendFromPayload_WithoutFichesSignaletiquesKey_IsNoOp()
    {
        const string json = """{ "dossier": "CHA03212" }""";
        var doc = new PdfDocument();
        var before = doc.PageCount;

        FicheSignaletiqueRenderer.AppendFromPayload(doc, json, "build-footer");

        Assert.Equal(before, doc.PageCount);
    }

    [Fact]
    public void AppendFromPayload_EmptyRowsArray_IsNoOp()
    {
        const string json = """{ "fichesSignaletiques": { "rows": [] } }""";
        var doc = new PdfDocument();
        var before = doc.PageCount;

        FicheSignaletiqueRenderer.AppendFromPayload(doc, json, "build-footer");

        Assert.Equal(before, doc.PageCount);
    }

    [Fact]
    public void AppendFromPayload_MalformedJson_DoesNotThrow()
    {
        var doc = new PdfDocument();
        var before = doc.PageCount;

        FicheSignaletiqueRenderer.AppendFromPayload(doc, "not json", "build-footer");

        Assert.Equal(before, doc.PageCount);
    }

    [Fact]
    public void AppendFromPayload_RowWithoutPhotoOrCoordinates_DoesNotThrow()
    {
        const string json = """
            {
              "fichesSignaletiques": {
                "rows": [ { "idFiche": "FS-003" } ]
              }
            }
            """;
        var doc = new PdfDocument();
        var before = doc.PageCount;

        var exception = Record.Exception(() => FicheSignaletiqueRenderer.AppendFromPayload(doc, json, "build-footer"));

        Assert.Null(exception);
        Assert.Equal(before + 1, doc.PageCount);
    }

    [Fact]
    public void AppendFromPayload_RowWithRevisions_DoesNotThrow()
    {
        const string json = """
            {
              "fichesSignaletiques": {
                "rows": [
                  {
                    "idFiche": "FS-004", "point": "PT-1",
                    "revisions": [
                      { "indice": "A", "date": "30/03/2026", "description": "Création", "auteur": "NVT" },
                      { "indice": "B", "date": "20/02/2026", "description": "Mise à jour", "auteur": "N.E." }
                    ]
                  }
                ]
              }
            }
            """;
        var doc = new PdfDocument();

        var exception = Record.Exception(() => FicheSignaletiqueRenderer.AppendFromPayload(doc, json, "build-footer"));

        Assert.Null(exception);
    }

    [Fact]
    public void AppendFromPayload_WithBrandingNode_DoesNotThrow()
    {
        const string json = """
            {
              "branding": { "colorBlue": "#112233", "footerAddress": "Mon Entreprise" },
              "fichesSignaletiques": {
                "rows": [ { "idFiche": "FS-005", "point": "PT-2" } ]
              }
            }
            """;
        var doc = new PdfDocument();

        var exception = Record.Exception(() => FicheSignaletiqueRenderer.AppendFromPayload(doc, json, "build-footer"));

        Assert.Null(exception);
    }

    [Fact]
    public void AppendFromPayload_AppendsAfterExistingPages_OnlyAddsNewRowPages()
    {
        var doc = new PdfDocument();
        doc.AddPage(); // page pré-existante (ex : page de garde d'un autre renderer)
        var before = doc.PageCount;

        FicheSignaletiqueRenderer.AppendFromPayload(doc, TwoRowsNoCoordsPayload, "build-footer");

        Assert.Equal(before + 2, doc.PageCount);
    }
}
