using TopoRapportWin.Branding;
using Xunit;

namespace NovaFiches.Tests;

/// <summary>
/// Teste uniquement BrandingService.Validate(options) : logique pure, sans I/O. Ne teste PAS
/// LoadOrDefault()/TrySave(), qui lisent/écrivent %LOCALAPPDATA%\NOVATLAS\Nova-Fiches\
/// branding.json - un chemin partagé avec la vraie installation de l'application sur la
/// machine, qu'un test ne doit jamais toucher (même règle que LicenseServiceTests).
/// </summary>
public class BrandingServiceTests
{
    // PNG minimal valide : signature (8 octets) + longueur chunk IHDR (4) + "IHDR" (4) +
    // largeur/hauteur big-endian (4 chacune). Le reste du fichier (CRC, IDAT...) n'est jamais
    // lu par TryReadPngDimensions, donc inutile pour ce test.
    private static byte[] BuildFakePng(int width, int height)
    {
        var bytes = new byte[24];
        byte[] sig = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        Array.Copy(sig, 0, bytes, 0, 8);
        // bytes 8-11 (longueur chunk) et 12-15 ("IHDR") non lus par le code testé : laissés à 0.
        bytes[12] = (byte)'I'; bytes[13] = (byte)'H'; bytes[14] = (byte)'D'; bytes[15] = (byte)'R';
        bytes[16] = (byte)(width >> 24); bytes[17] = (byte)(width >> 16); bytes[18] = (byte)(width >> 8); bytes[19] = (byte)width;
        bytes[20] = (byte)(height >> 24); bytes[21] = (byte)(height >> 16); bytes[22] = (byte)(height >> 8); bytes[23] = (byte)height;
        return bytes;
    }

    [Fact]
    public void Validate_EmptyOptions_ReturnsNull()
    {
        var error = BrandingService.Validate(BrandingOptions.Empty);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("#1267F3")]
    [InlineData("#ff5a17")]
    public void Validate_ValidHexColors_ReturnsNull(string hex)
    {
        var options = new BrandingOptions(null, null, hex, hex);
        var error = BrandingService.Validate(options);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("1267F3")]      // pas de #
    [InlineData("#1267F")]      // trop court
    [InlineData("#GGGGGG")]     // pas hexadécimal
    [InlineData("red")]         // nom CSS, pas hex
    public void Validate_InvalidHexColor_ReturnsError(string hex)
    {
        var options = new BrandingOptions(null, null, hex, null);
        var error = BrandingService.Validate(options);
        Assert.NotNull(error);
    }

    [Fact]
    public void Validate_FooterAddressTooLong_ReturnsError()
    {
        var options = new BrandingOptions(null, new string('A', BrandingService.MaxFooterAddressLength + 1), null, null);
        var error = BrandingService.Validate(options);
        Assert.NotNull(error);
    }

    [Fact]
    public void Validate_FooterAddressAtMaxLength_ReturnsNull()
    {
        var options = new BrandingOptions(null, new string('A', BrandingService.MaxFooterAddressLength), null, null);
        var error = BrandingService.Validate(options);
        Assert.Null(error);
    }

    [Fact]
    public void Validate_LogoInvalidBase64_ReturnsError()
    {
        var options = new BrandingOptions("ceci n'est pas du base64 !!!", null, null, null);
        var error = BrandingService.Validate(options);
        Assert.NotNull(error);
    }

    [Fact]
    public void Validate_LogoNotPng_ReturnsError()
    {
        // Données valides en base64 mais sans la signature PNG.
        var notPng = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
        var options = new BrandingOptions(notPng, null, null, null);
        var error = BrandingService.Validate(options);
        Assert.NotNull(error);
    }

    [Fact]
    public void Validate_LogoTooLarge_ReturnsError()
    {
        // Peu importe le contenu ici : la taille est vérifiée avant la lecture du PNG.
        var huge = new byte[BrandingService.MaxLogoBytes + 1024];
        var options = new BrandingOptions(Convert.ToBase64String(huge), null, null, null);
        var error = BrandingService.Validate(options);
        Assert.NotNull(error);
        Assert.Contains("volumineux", error);
    }

    [Fact]
    public void Validate_LogoDimensionsTooLarge_ReturnsError()
    {
        var png = BuildFakePng(BrandingService.MaxLogoDimensionPx + 1, 500);
        var options = new BrandingOptions(Convert.ToBase64String(png), null, null, null);
        var error = BrandingService.Validate(options);
        Assert.NotNull(error);
        Assert.Contains("grand", error);
    }

    [Fact]
    public void Validate_ValidSmallPng_ReturnsNull()
    {
        var png = BuildFakePng(800, 450); // ~16:9, sous la limite
        var options = new BrandingOptions(Convert.ToBase64String(png), "Mon Entreprise - 1 rue Exemple", "#1267F3", "#FF5A17");
        var error = BrandingService.Validate(options);
        Assert.Null(error);
    }
}
