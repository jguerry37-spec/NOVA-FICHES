using System.Text.Json;
using System.Text.RegularExpressions;

namespace TopoRapportWin.Branding;

/// <summary>
/// Charge/valide/enregistre la personnalisation d'identité visuelle (logo, adresse de pied de
/// page, couleurs) dans %LOCALAPPDATA%\NOVATLAS\Nova-Fiches\branding.json - poste par poste,
/// comme license.json. Module manager (voir data-nf-feature="branding" côté UI).
/// </summary>
internal static class BrandingService
{
    public const long MaxLogoBytes = 2 * 1024 * 1024; // 2 Mo
    public const int MaxLogoDimensionPx = 2000;
    public const int MaxFooterAddressLength = 200;

    public static string BrandingPath => Path.Combine(AppLog.AppDataDir, "branding.json");

    public static BrandingOptions LoadOrDefault()
    {
        try
        {
            if (!File.Exists(BrandingPath)) return BrandingOptions.Empty;
            var json = File.ReadAllText(BrandingPath);
            return JsonSerializer.Deserialize<BrandingOptions>(json) ?? BrandingOptions.Empty;
        }
        catch (Exception ex)
        {
            AppLog.Error("Branding: lecture branding.json impossible", ex);
            return BrandingOptions.Empty;
        }
    }

    /// <summary>Valide puis enregistre. N'écrit rien si la validation échoue.</summary>
    public static (bool Success, string? Error) TrySave(BrandingOptions options)
    {
        var error = Validate(options);
        if (error != null) return (false, error);

        try
        {
            Directory.CreateDirectory(AppLog.AppDataDir);
            var json = JsonSerializer.Serialize(options, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(BrandingPath, json);
            AppLog.Info("Branding: paramètres personnalisés enregistrés.");
            return (true, null);
        }
        catch (Exception ex)
        {
            AppLog.Error("Branding: écriture branding.json impossible", ex);
            return (false, "Impossible d'enregistrer les paramètres sur ce poste.");
        }
    }

    public static string? Validate(BrandingOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.FooterAddress) && options.FooterAddress.Length > MaxFooterAddressLength)
            return $"L'adresse de pied de page dépasse {MaxFooterAddressLength} caractères.";

        if (!string.IsNullOrWhiteSpace(options.ColorBlueHex) && !IsValidHexColor(options.ColorBlueHex))
            return "Couleur bleue invalide (format attendu : #RRGGBB).";

        if (!string.IsNullOrWhiteSpace(options.ColorOrangeHex) && !IsValidHexColor(options.ColorOrangeHex))
            return "Couleur orange invalide (format attendu : #RRGGBB).";

        if (!string.IsNullOrWhiteSpace(options.LogoPngBase64))
        {
            byte[] bytes;
            try { bytes = Convert.FromBase64String(options.LogoPngBase64); }
            catch { return "Logo illisible (données invalides)."; }

            if (bytes.LongLength > MaxLogoBytes)
                return $"Logo trop volumineux (max {MaxLogoBytes / 1024 / 1024} Mo).";

            if (!IsPngSignature(bytes))
                return "Le logo doit être au format PNG.";

            var dims = TryReadPngDimensions(bytes);
            if (dims == null)
                return "Impossible de lire les dimensions du logo PNG.";
            if (dims.Value.Width > MaxLogoDimensionPx || dims.Value.Height > MaxLogoDimensionPx)
                return $"Logo trop grand (max {MaxLogoDimensionPx}×{MaxLogoDimensionPx}px).";
        }

        return null;
    }

    private static bool IsValidHexColor(string s) => Regex.IsMatch(s.Trim(), "^#[0-9A-Fa-f]{6}$");

    private static bool IsPngSignature(byte[] bytes)
    {
        if (bytes.Length < 8) return false;
        ReadOnlySpan<byte> sig = stackalloc byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        return bytes.AsSpan(0, 8).SequenceEqual(sig);
    }

    // IHDR est toujours le premier chunk d'un PNG valide : 8 octets de signature, puis
    // 4 octets de longueur, 4 octets "IHDR", puis largeur/hauteur en big-endian (4 octets
    // chacun). Lu directement (pas de System.Drawing) pour rester léger et thread-safe.
    private static (int Width, int Height)? TryReadPngDimensions(byte[] bytes)
    {
        if (bytes.Length < 24) return null;
        int width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
        int height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
        if (width <= 0 || height <= 0) return null;
        return (width, height);
    }
}
