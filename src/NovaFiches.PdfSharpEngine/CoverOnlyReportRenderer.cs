using System.Text.Json;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace NovaFiches.PdfSharpEngine;

/// <summary>
/// "Page de garde" export : une seule page, en-tête + cartouche NOVATLAS (infos dossier)
/// identiques à celles du PDF Station, corps vide, pied de page standard. Réutilise
/// StationReportRenderer.DrawTopHeader/DrawInfoCartouche/DrawFooterAllPages (rendus
/// internal pour l'occasion) plutôt que de dupliquer ~150 lignes de mise en page de
/// cartouche - contrairement aux autres pages annexes (Photo, Plan station) qui
/// dupliquent délibérément un en-tête plus simple, ce cartouche-ci est visuellement
/// riche (logo, bandeau titre, grille 3 lignes) et doit rester identique au PDF Station
/// sans risque de dérive entre deux copies.
/// </summary>
internal static class CoverOnlyReportRenderer
{
    public static void Render(PdfDocument doc, string payloadJson, string buildFooter)
    {
        using var jd = JsonDocument.Parse(payloadJson);
        var root = jd.RootElement;

        var page = doc.AddPage();
        page.Size = PdfSharp.PageSize.A4;

        using (var g = XGraphics.FromPdfPage(page))
        {
            double y = StationReportRenderer.DrawTopHeader(g, page, root);
            StationReportRenderer.DrawInfoCartouche(g, page, y, root);
        }

        // XGraphics distinct en Append : PdfSharp n'autorise qu'un seul XGraphics actif
        // par page à la fois (même contrainte que StationReportRenderer.Render).
        using (var gg = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append))
        {
            StationReportRenderer.DrawFooterAllPages(gg, page, 1, 1, buildFooter);
        }
    }
}
