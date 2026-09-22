using System;
using System.Globalization;
using System.Text.Json;
using PdfSharp.Drawing;

namespace NovaFiches.PdfSharpEngine;

// Reads the tolerance settings JS already computed STATUT from (payload "validation" object)
// and offers the two things every "rapport" renderer needs on top of the plain STATUT string:
// a compact one-line summary for the CONTRÔLES box, and a per-component out-of-range check so
// individual Dx/Dy/Dz cells can be highlighted without re-deciding STATUT itself (still JS-owned).
internal static class ToleranceDisplay
{
    internal readonly struct Settings
    {
        public readonly bool StatutOn;
        public readonly bool XYOn;
        public readonly bool ZOn;
        public readonly double XYPlus;
        public readonly double XYMinus;
        public readonly double ZPlus;
        public readonly double ZMinus;

        public Settings(bool statutOn, bool xyOn, bool zOn, double xyPlus, double xyMinus, double zPlus, double zMinus)
        {
            StatutOn = statutOn;
            XYOn = xyOn;
            ZOn = zOn;
            XYPlus = xyPlus;
            XYMinus = xyMinus;
            ZPlus = zPlus;
            ZMinus = zMinus;
        }

        public bool HasXY => StatutOn && XYOn && double.IsFinite(XYPlus) && double.IsFinite(XYMinus);
        public bool HasZ => StatutOn && ZOn && double.IsFinite(ZPlus) && double.IsFinite(ZMinus);
    }

    internal static readonly XColor OutOfToleranceColor = XColor.FromArgb(239, 68, 68);

    internal static Settings Parse(in JsonElement root)
    {
        if (!root.TryGetProperty("validation", out var v) || v.ValueKind != JsonValueKind.Object)
            return new Settings(false, true, true, double.NaN, double.NaN, double.NaN, double.NaN);

        bool statutOn = GetBool(v, "statutOn", false);
        bool xyOn = GetBool(v, "tolXYOn", true);
        bool zOn = GetBool(v, "tolZOn", true);
        double xyPlus = GetDouble(v, "tolXY");
        double zPlus = GetDouble(v, "tolZ");
        double xyMinus = GetDouble(v, "tolXYMinus");
        if (double.IsNaN(xyMinus)) xyMinus = xyPlus;
        double zMinus = GetDouble(v, "tolZMinus");
        if (double.IsNaN(zMinus)) zMinus = zPlus;

        return new Settings(statutOn, xyOn, zOn, xyPlus, xyMinus, zPlus, zMinus);
    }

    // Compact "Tol. XY = ... Tol. Z = ..." line for the CONTRÔLES box ; "" when STATUT is off.
    internal static string FormatLine(in Settings s)
    {
        if (!s.StatutOn) return "";

        string xy = s.XYOn ? FormatRange(s.XYPlus, s.XYMinus) : "désactivée";
        string z = s.ZOn ? FormatRange(s.ZPlus, s.ZMinus) : "désactivée";
        return $"Tolérance XY : {xy}    Tolérance Z : {z}";
    }

    private static string FormatRange(double plus, double minus)
    {
        if (!double.IsFinite(plus)) return "-";
        var p = plus.ToString("0.###", CultureInfo.InvariantCulture);
        if (!double.IsFinite(minus) || Math.Abs(minus - plus) < 1e-9) return p + " m";
        var m = minus.ToString("0.###", CultureInfo.InvariantCulture);
        return $"[-{m};+{p}] m";
    }

    // Bounds mirror statusFromTol() in m01_core.js exactly: value must stay within [-minus, +plus].
    internal static bool IsOutOfRangeXY(double? value, in Settings s)
    {
        if (!s.HasXY || value == null || !double.IsFinite(value.Value)) return false;
        return value.Value > s.XYPlus || value.Value < -s.XYMinus;
    }

    internal static bool IsOutOfRangeZ(double? value, in Settings s)
    {
        if (!s.HasZ || value == null || !double.IsFinite(value.Value)) return false;
        return value.Value > s.ZPlus || value.Value < -s.ZMinus;
    }

    internal static double? ParseCell(string? cell)
    {
        if (string.IsNullOrWhiteSpace(cell)) return null;
        var norm = cell.Trim().Replace(',', '.');
        return double.TryParse(norm, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : (double?)null;
    }

    private static bool GetBool(JsonElement obj, string name, bool def)
    {
        if (obj.TryGetProperty(name, out var el))
        {
            if (el.ValueKind == JsonValueKind.True) return true;
            if (el.ValueKind == JsonValueKind.False) return false;
        }
        return def;
    }

    private static double GetDouble(JsonElement obj, string name)
    {
        if (obj.TryGetProperty(name, out var el))
        {
            if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var d)) return d;
            if (el.ValueKind == JsonValueKind.String &&
                double.TryParse(el.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d2)) return d2;
        }
        return double.NaN;
    }
}
