namespace TopoRapportWin.Branding;

/// <summary>
/// Personnalisation optionnelle de l'identité visuelle des PDF générés (logo, pied de page,
/// couleurs). Tout champ null/vide retombe sur l'identité NOVATLAS par défaut - voir
/// NovaFiches.PdfSharpEngine.NovatlasTheme (résolveurs Resolve*).
/// </summary>
public sealed record BrandingOptions(
    string? LogoPngBase64,
    string? FooterAddress,
    string? ColorBlueHex,
    string? ColorOrangeHex
)
{
    public static BrandingOptions Empty => new(null, null, null, null);
}
