using System;
using System.IO;
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

private static XImage? _logo;
public static XImage? TryLoadLogo()
{
    try
    {
        if (_logo != null) return _logo;
        var path = Path.Combine(AppContext.BaseDirectory, "assets", "novatlas_logo.png");
        if (File.Exists(path))
            _logo = XImage.FromFile(path);
        return _logo;
    }
    catch { return null; }
}


    // Fonts
    // Backward-compatible helpers (used by cover page, etc.)
    public static XFont FontBold(double size) => new("Arial", size, XFontStyleEx.Bold);
    public static XFont FontBody(double size) => new("Arial", size, XFontStyleEx.Regular);
    public static XFont FontBodyBold(double size) => new("Arial", size, XFontStyleEx.Bold);


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
