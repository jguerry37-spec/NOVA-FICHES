using System.Text.Json;
using NovaFiches.PdfSharpEngine;
using PdfSharp.Pdf;
using Xunit;

namespace NovaFiches.Tests;

/// <summary>
/// Régression : la "Vue en plan" (Récolement pieux + plan) réutilise la liste théorique
/// complète du TXT (pointsAll), qui mélange des points numérotés (pieux, "base" numérique)
/// et des points nommés sans numéro (repères, "base": null). RecolementPlanViewRenderer
/// appelait JsonElement.TryGetDouble() sur "base" sans vérifier son ValueKind, ce qui lève
/// une InvalidOperationException dès le premier point à "base": null — silencieusement avalée
/// par l'appelant (ImplantationFullReportRenderer), donc la page plan disparaissait sans
/// aucune trace. Voir AUDIT/session du 2026-07-07.
/// </summary>
public class RecolementPlanViewRendererTests
{
    [Fact]
    public void Render_PointsAllWithNullBase_DoesNotThrowAndAddsPage()
    {
        const string json = """
            {
              "planView": {
                "title": "VUE EN PLAN",
                "pointsAll": [
                  { "id": "A", "key": "A", "base": null, "x": 100.0, "y": 200.0 },
                  { "id": "PI.4827", "key": "4827", "base": 4827, "x": 105.0, "y": 205.0 }
                ],
                "pointsImplanted": [
                  { "id": "PI.4827", "key": "4827", "base": 4827, "x": 105.0, "y": 205.0 }
                ]
              }
            }
            """;

        using var jd = JsonDocument.Parse(json);
        var root = jd.RootElement;
        var planView = root.GetProperty("planView");

        var doc = new PdfDocument();
        var before = doc.PageCount;

        RecolementPlanViewRenderer.Render(doc, root, planView);

        Assert.Equal(before + 1, doc.PageCount);
    }
}
