using System.Text.Json;
using NovaFiches.PdfSharpEngine;
using Xunit;

namespace NovaFiches.Tests;

/// <summary>
/// NovatlasTheme.Resolve* : lisent un noeud "branding" optionnel dans le payload JSON envoyé
/// à un renderer PdfSharpEngine (injecté par MainForm.InjectBranding), avec repli sur
/// l'identité NOVATLAS par défaut si absent/invalide.
/// </summary>
public class NovatlasThemeBrandingTests
{
    private static JsonElement Root(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    [Fact]
    public void ResolveBlue_NoBrandingNode_ReturnsDefaultNovaBlue()
    {
        var root = Root("{}");
        var color = NovatlasTheme.ResolveBlue(root);
        Assert.Equal(NovatlasTheme.NovaBlue.R, color.R);
        Assert.Equal(NovatlasTheme.NovaBlue.G, color.G);
        Assert.Equal(NovatlasTheme.NovaBlue.B, color.B);
    }

    [Fact]
    public void ResolveBlue_CustomHex_ReturnsCustomColor()
    {
        var root = Root("""{"branding":{"colorBlue":"#112233"}}""");
        var color = NovatlasTheme.ResolveBlue(root);
        Assert.Equal(0x11, color.R);
        Assert.Equal(0x22, color.G);
        Assert.Equal(0x33, color.B);
    }

    [Fact]
    public void ResolveOrange_CustomHex_ReturnsCustomColor()
    {
        var root = Root("""{"branding":{"colorOrange":"#AABBCC"}}""");
        var color = NovatlasTheme.ResolveOrange(root);
        Assert.Equal(0xAA, color.R);
        Assert.Equal(0xBB, color.G);
        Assert.Equal(0xCC, color.B);
    }

    [Fact]
    public void ResolveOrange_InvalidHex_FallsBackToDefault()
    {
        var root = Root("""{"branding":{"colorOrange":"not-a-color"}}""");
        var color = NovatlasTheme.ResolveOrange(root);
        Assert.Equal(NovatlasTheme.Orange.R, color.R);
        Assert.Equal(NovatlasTheme.Orange.G, color.G);
        Assert.Equal(NovatlasTheme.Orange.B, color.B);
    }

    [Fact]
    public void ResolveFooterAddress_NoBrandingNode_ReturnsNovatlasAddress()
    {
        var root = Root("{}");
        Assert.Equal(NovatlasTheme.NovatlasAddress, NovatlasTheme.ResolveFooterAddress(root));
    }

    [Fact]
    public void ResolveFooterAddress_CustomAddress_ReturnsCustomAddress()
    {
        var root = Root("""{"branding":{"footerAddress":"Mon Entreprise - 1 rue Exemple - 75000 Paris"}}""");
        Assert.Equal("Mon Entreprise - 1 rue Exemple - 75000 Paris", NovatlasTheme.ResolveFooterAddress(root));
    }

    [Fact]
    public void ResolveFooterAddress_BlankCustomAddress_FallsBackToDefault()
    {
        var root = Root("""{"branding":{"footerAddress":"   "}}""");
        Assert.Equal(NovatlasTheme.NovatlasAddress, NovatlasTheme.ResolveFooterAddress(root));
    }

    [Fact]
    public void ResolveLogo_MalformedBase64_DoesNotThrowAndFallsBack()
    {
        var root = Root("""{"branding":{"logoPngBase64":"not valid base64 !!"}}""");
        // Ne doit jamais lever d'exception, même si aucun logo NOVATLAS n'est présent dans le
        // dossier de sortie des tests (TryLoadLogo retournera simplement null dans ce cas).
        var exception = Record.Exception(() => NovatlasTheme.ResolveLogo(root));
        Assert.Null(exception);
    }
}
