using System;
using System.IO;
using PdfSharp.Drawing;

namespace NovaFiches.PdfSharpEngine;

/// <summary>
/// Small helper to render base64 data-URL images (signature) safely.
/// Must never throw; PDFs must still be generated even if image is invalid.
/// </summary>
internal static class PdfImageHelper
{
    public static void DrawDataUrlImage(XGraphics g, string dataUrl, XRect box)
    {
        try
        {
            if (g == null) return;
            if (string.IsNullOrWhiteSpace(dataUrl)) return;

            var bytes = TryDecodeDataUrl(dataUrl);
            if (bytes == null || bytes.Length == 0) return;

            // PdfSharp 6.x expects a Stream (not a lambda factory).
            // Keep the stream alive for the lifetime of the XImage.
            using var ms = new MemoryStream(bytes);
            using var img = XImage.FromStream(ms);

            // Fit image into the box while keeping aspect ratio.
            double iw = img.PixelWidth;
            double ih = img.PixelHeight;
            if (iw <= 0 || ih <= 0) return;

            double sx = box.Width / iw;
            double sy = box.Height / ih;
            double s = Math.Min(sx, sy);
            if (s <= 0) return;

            double w = iw * s;
            double h = ih * s;
            double x = box.Left + (box.Width - w) / 2.0;
            double y = box.Top + (box.Height - h) / 2.0;

            g.DrawImage(img, x, y, w, h);
        }
        catch
        {
            // Never block PDF generation
        }
    }

    private static byte[]? TryDecodeDataUrl(string s)
    {
        try
        {
            s = s.Trim();
            // Accept raw base64 or full data URL
            int idx = s.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0) s = s.Substring(idx + "base64,".Length);
            return Convert.FromBase64String(s);
        }
        catch
        {
            return null;
        }
    }
}
