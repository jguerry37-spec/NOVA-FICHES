namespace NovaFiches.PdfSharpEngine;

/// <summary>
/// Centralise les constantes de mise en page communes à tous les rendus PDF.
/// Objectif : éviter les chevauchements (notamment avec le pied de page).
/// </summary>
internal static class LayoutConstants
{
    /// <summary>
    /// Zone réservée en bas de page pour le pied de page (en mm).
    /// </summary>
    public const double FooterReserveMm = 26.0;

    /// <summary>
    /// Zone réservée en bas de page pour le pied de page (en points PDF).
    /// </summary>
    public static double FooterReservePt => Units.MmToPt(FooterReserveMm);
}
