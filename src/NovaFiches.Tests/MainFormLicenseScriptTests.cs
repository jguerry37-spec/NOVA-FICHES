using TopoRapportWin;
using TopoRapportWin.Licensing;
using Xunit;

namespace NovaFiches.Tests;

/// <summary>
/// MainForm.BuildFooterLicenseScript(LicenseValidationResult) construit le script JS injecté
/// après navigation : statut de licence (pastille footer/sidebar) ET masquage des éléments
/// data-nf-feature="cle" selon les modules complémentaires actifs - fondation du gating par
/// licence pour les futurs modules manager. Teste uniquement la génération du script (chaîne
/// JS), pas son exécution dans un WebView2 réel.
/// </summary>
public class MainFormLicenseScriptTests
{
    [Fact]
    public void BuildFooterLicenseScript_ValidLicenseWithFeatures_IncludesFeatureArray()
    {
        var payload = new LicensePayload("Client Manager", DateTime.UtcNow, null, null, new[] { "fiches-signaletiques", "devis" });
        var result = new LicenseValidationResult(LicenseStatus.Valid, payload, "Licence valide");

        var script = MainForm.BuildFooterLicenseScript(result);

        Assert.Contains("data-nf-feature", script);
        Assert.Contains("\"fiches-signaletiques\"", script);
        Assert.Contains("\"devis\"", script);
    }

    [Fact]
    public void BuildFooterLicenseScript_ValidLicenseNoFeatures_EmptyFeatureArray()
    {
        var payload = new LicensePayload("Client Standard", DateTime.UtcNow, null, null, Array.Empty<string>());
        var result = new LicenseValidationResult(LicenseStatus.Valid, payload, "Licence valide");

        var script = MainForm.BuildFooterLicenseScript(result);

        Assert.Contains("var feats=[]", script);
    }

    [Fact]
    public void BuildFooterLicenseScript_InvalidLicense_FailsClosedWithEmptyFeatureArray()
    {
        // Licence invalide/corrompue : aucun module complémentaire ne doit apparaître,
        // même si un payload partiel a pu être lu (échec fermé).
        var payload = new LicensePayload("Client Manager", DateTime.UtcNow, null, null, new[] { "fiches-signaletiques" });
        var result = new LicenseValidationResult(LicenseStatus.InvalidSignature, payload, "Signature invalide");

        var script = MainForm.BuildFooterLicenseScript(result);

        Assert.Contains("var feats=[]", script);
        Assert.DoesNotContain("fiches-signaletiques", script);
    }

    [Fact]
    public void BuildFooterLicenseScript_NotActivated_FailsClosedWithEmptyFeatureArray()
    {
        var result = new LicenseValidationResult(LicenseStatus.NotActivated, null, "Aucune licence activée sur ce poste.");

        var script = MainForm.BuildFooterLicenseScript(result);

        Assert.Contains("var feats=[]", script);
    }
}
