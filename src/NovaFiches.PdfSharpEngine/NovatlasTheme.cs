using System;
using System.IO;
using System.Text.Json;
using PdfSharp.Drawing;

namespace NovaFiches.PdfSharpEngine;

public static class NovatlasTheme
{
    public static XColor NovaBlue => XColor.FromArgb(18, 103, 243);

    // Backward-compat aliases used by some renderers
    public static XColor Blue => NovaBlue;
    public static XColor Orange => XColor.FromArgb(255, 90, 23);
    public static XColor Grey => XColor.FromArgb(200, 200, 200);
    public static XColor LightGrey => XColor.FromArgb(235, 235, 235);

    // Common aliases used by renderers
    public static XColor Black => XColors.Black;
    public static XFont FontSectionTitle => new("Arial", 11, XFontStyleEx.Bold);


public const string NovatlasAddress = "NOVATLAS — 24 boulevard Paul Vaillant Couturier — 94200 IVRY SUR SEINE";

public static XImage? TryLoadLogo()
{
    try
    {
        // Ne pas mettre en cache l'XImage : PdfSharp/GDI+ n'est pas thread-safe et un
        // rendu PDF en parallele (plusieurs PdfDocument embarquant la meme instance en
        // meme temps) fait planter System.Drawing.Image.Save avec "Object is currently
        // in use elsewhere". Un fichier logo est minuscule - le recharger a chaque appel
        // coute rien et garantit qu'aucun etat mutable n'est partage entre threads.
        var path = Path.Combine(AppContext.BaseDirectory, "assets", "novatlas_logo.png");
        return File.Exists(path) ? XImage.FromFile(path) : null;
    }
    catch { return null; }
}

// ---- Résolveurs de branding (module manager "Paramètres") ----
// Le payload envoyé par le JS peut porter un noeud "branding" (logo/adresse/couleurs
// personnalisés), injecté côté MainForm avant l'appel au renderer - PdfSharpEngine ne
// référence jamais les services NovaFiches (BrandingService), donc c'est le seul point
// d'entrée pour cette personnalisation ici. Absent/invalide => identité NOVATLAS par défaut.

public static XColor ResolveBlue(JsonElement root) => TryReadBrandingColor(root, "colorBlue") ?? NovaBlue;

public static XColor ResolveOrange(JsonElement root) => TryReadBrandingColor(root, "colorOrange") ?? Orange;

public static string ResolveFooterAddress(JsonElement root)
{
    var s = TryReadBrandingString(root, "footerAddress");
    return string.IsNullOrWhiteSpace(s) ? NovatlasAddress : s!;
}

/// <summary>
/// Logo personnalisé si présent et lisible, sinon repli sur le logo NOVATLAS (TryLoadLogo).
/// Comme TryLoadLogo, ne met rien en cache et n'est pas partagé entre threads.
/// </summary>
public static XImage? ResolveLogo(JsonElement root)
{
    var b64 = TryReadBrandingString(root, "logoPngBase64");
    if (!string.IsNullOrWhiteSpace(b64))
    {
        try
        {
            var bytes = Convert.FromBase64String(b64);
            var ms = new MemoryStream(bytes);
            return XImage.FromStream(ms);
        }
        catch { /* logo personnalisé illisible : repli sur le logo par défaut */ }
    }
    return TryLoadLogo();
}

private static string? TryReadBrandingString(JsonElement root, string key)
{
    try
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("branding", out var b) && b.ValueKind == JsonValueKind.Object &&
            b.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
        {
            return v.GetString();
        }
    }
    catch { }
    return null;
}

private static XColor? TryReadBrandingColor(JsonElement root, string key)
{
    var hex = TryReadBrandingString(root, key)?.Trim().TrimStart('#');
    if (string.IsNullOrWhiteSpace(hex) || hex.Length != 6) return null;
    try
    {
        int r = Convert.ToInt32(hex.Substring(0, 2), 16);
        int g = Convert.ToInt32(hex.Substring(2, 2), 16);
        int b = Convert.ToInt32(hex.Substring(4, 2), 16);
        return XColor.FromArgb(r, g, b);
    }
    catch { return null; }
}


    // Fonts
    // Backward-compatible helpers (used by cover page, etc.)
    public static XFont FontBold(double size) => new("Arial", size, XFontStyleEx.Bold);
    public static XFont FontBody(double size) => new("Arial", size, XFontStyleEx.Regular);
    public static XFont FontBodyBold(double size) => new("Arial", size, XFontStyleEx.Bold);
    public static XFont FontBodyItalic(double size) => new("Arial", size, XFontStyleEx.Italic);


    public static XFont TitleFont() => new("Arial", 16, XFontStyleEx.Bold);
    public static XFont SubTitleFont() => new("Arial", 10, XFontStyleEx.Regular);
    public static XFont TableHeaderFont() => new("Arial", 9, XFontStyleEx.Bold);
    public static XFont TableCellFont() => new("Arial", 9, XFontStyleEx.Regular);
    public static XFont FooterFont() => new("Arial", 8, XFontStyleEx.Regular);

    // Colors / fills
    public static XColor HeaderFill() => XColor.FromArgb(240, 240, 240);
    public static XBrush HeaderFillBrush() => new XSolidBrush(HeaderFill());

    // Pens
    public static XPen GridPenThin() => new(XColors.LightGray, 0.35);
    public static XPen GridPenNormal() => new(XColors.Black, 0.8);
    public static XPen GridPenThick() => new(XColors.Black, 1.2);
    public static XPen GridPenOuter() => GridPenThick();
}
