using System.Globalization;
using ClosedXML.Excel;

namespace TopoRapportWin.ControlePolygonale;

// Module manager "Contrôle de polygonale" : lit un classeur Excel "Classeur de comparaison"
// (contrôle GNSS/optique de points XY et/ou Z par rapport à des coordonnées théoriques) et un
// fichier GeoBase texte, calcule les écarts et statistiques par onglet. Portage C# du moteur
// Python de l'outil de référence indépendant (analyze_sources()/read_xy_sheet()/read_z_sheet()/
// xy_stats()/z_stats() dans server.py) - mêmes règles de classification d'onglet et de calcul,
// réécrites sans dépendance externe autre que ClosedXML pour la lecture du .xlsx.
public static class ControlePolygonaleService
{
    // Un onglet dont le nom contient "gnss" (insensible à la casse) est un contrôle planimétrique
    // (XY) ; un onglet dont le nom contient "niv" est un contrôle altimétrique (Z). Les autres
    // onglets (ex. "Export") ne sont pas exploités par le rapport v1.
    public static CpolyWorkbook ReadWorkbook(Stream xlsxStream)
    {
        using var wb = new XLWorkbook(xlsxStream);
        var xySheets = new List<CpolyXySheet>();
        var zSheets = new List<CpolyZSheet>();

        foreach (var ws in wb.Worksheets)
        {
            var lower = ws.Name.ToLowerInvariant();
            if (lower.Contains("gnss"))
            {
                var rows = ReadXySheet(ws);
                xySheets.Add(new CpolyXySheet(ws.Name, rows, ComputeXyStats(rows)));
            }
            else if (lower.Contains("niv"))
            {
                var rows = ReadZSheet(ws);
                zSheets.Add(new CpolyZSheet(ws.Name, rows, ComputeZStats(rows)));
            }
        }

        return new CpolyWorkbook(xySheets, zSheets);
    }

    // Point(1), X théorique(2), Y théorique(3), X contrôle(4), Y contrôle(5), ΔX(6), ΔY(7) -
    // données à partir de la ligne 3 (2 lignes d'en-tête). ΔX/ΔY sont recalculés si absents et que
    // théorique+contrôle sont tous deux renseignés.
    private static List<CpolyXyRow> ReadXySheet(IXLWorksheet ws)
    {
        var rows = new List<CpolyXyRow>();
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        for (int row = 3; row <= lastRow; row++)
        {
            var point = ws.Cell(row, 1).GetString().Trim();
            if (point.Length == 0) continue;

            var xt = ParseNumber(ws.Cell(row, 2));
            var yt = ParseNumber(ws.Cell(row, 3));
            var xc = ParseNumber(ws.Cell(row, 4));
            var yc = ParseNumber(ws.Cell(row, 5));
            var dx = ParseNumber(ws.Cell(row, 6));
            var dy = ParseNumber(ws.Cell(row, 7));

            if (dx == null && xt != null && xc != null) dx = xc - xt;
            if (dy == null && yt != null && yc != null) dy = yc - yt;

            double? deltaXy = (dx != null && dy != null) ? Math.Sqrt(dx.Value * dx.Value + dy.Value * dy.Value) : null;
            bool complete = xt != null && yt != null && xc != null && yc != null;

            rows.Add(new CpolyXyRow(point, xt, yt, xc, yc, dx, dy, deltaXy, complete));
        }
        return rows;
    }

    // Point(1), Z théorique(2), Z contrôle(3), ΔZ(4) - données à partir de la ligne 4 (3 lignes
    // d'en-tête, format observé sur les classeurs réels).
    private static List<CpolyZRow> ReadZSheet(IXLWorksheet ws)
    {
        var rows = new List<CpolyZRow>();
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        for (int row = 4; row <= lastRow; row++)
        {
            var point = ws.Cell(row, 1).GetString().Trim();
            if (point.Length == 0) continue;

            var zt = ParseNumber(ws.Cell(row, 2));
            var zc = ParseNumber(ws.Cell(row, 3));
            var dz = ParseNumber(ws.Cell(row, 4));
            if (dz == null && zt != null && zc != null) dz = zc - zt;

            bool complete = zt != null && zc != null;
            rows.Add(new CpolyZRow(point, zt, zc, dz, complete));
        }
        return rows;
    }

    private static CpolyXyStats ComputeXyStats(IReadOnlyList<CpolyXyRow> rows)
    {
        var complete = rows.Where(r => r.Complete && r.DeltaXy != null).ToList();
        if (complete.Count == 0)
            return new CpolyXyStats(rows.Count, 0, rows.Count, null, null, null, null, null);

        var maxRow = complete.OrderByDescending(r => Math.Abs(r.DeltaXy!.Value)).First();
        return new CpolyXyStats(
            Count: rows.Count,
            Complete: complete.Count,
            Missing: rows.Count - complete.Count,
            MaxAbsDx: complete.Where(r => r.Dx != null).Select(r => Math.Abs(r.Dx!.Value)).DefaultIfEmpty(0).Max(),
            MaxAbsDy: complete.Where(r => r.Dy != null).Select(r => Math.Abs(r.Dy!.Value)).DefaultIfEmpty(0).Max(),
            MaxDeltaXy: Math.Abs(maxRow.DeltaXy!.Value),
            MaxDeltaXyPoint: maxRow.Point,
            MeanDeltaXy: complete.Average(r => Math.Abs(r.DeltaXy!.Value)));
    }

    private static CpolyZStats ComputeZStats(IReadOnlyList<CpolyZRow> rows)
    {
        var complete = rows.Where(r => r.Complete && r.Dz != null).ToList();
        if (complete.Count == 0)
            return new CpolyZStats(rows.Count, 0, rows.Count, null, null, null);

        var maxRow = complete.OrderByDescending(r => Math.Abs(r.Dz!.Value)).First();
        return new CpolyZStats(
            Count: rows.Count,
            Complete: complete.Count,
            Missing: rows.Count - complete.Count,
            MaxAbsDz: Math.Abs(maxRow.Dz!.Value),
            MaxAbsDzPoint: maxRow.Point,
            MeanAbsDz: complete.Average(r => Math.Abs(r.Dz!.Value)));
    }

    private static double? ParseNumber(IXLCell cell)
    {
        if (cell.IsEmpty()) return null;
        if (cell.TryGetValue<double>(out var d)) return d;
        var text = cell.GetString().Trim().Replace(",", ".");
        if (text.Length == 0) return null;
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    // Fichier GeoBase : une ligne par point, tokens séparés par espaces, "point x y z ...".
    // Lignes ne comportant pas au moins 4 tokens numériques valides sont ignorées.
    public static IReadOnlyList<CpolyGeoBaseRow> ReadGeoBase(string text)
    {
        var rows = new List<CpolyGeoBaseRow>();
        if (string.IsNullOrWhiteSpace(text)) return rows;

        foreach (var line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4) continue;

            if (!TryParseInvariant(parts[1], out var x)) continue;
            if (!TryParseInvariant(parts[2], out var y)) continue;
            if (!TryParseInvariant(parts[3], out var z)) continue;

            rows.Add(new CpolyGeoBaseRow(parts[0], x, y, z));
        }
        return rows;
    }

    private static bool TryParseInvariant(string raw, out double value)
    {
        return double.TryParse(raw.Replace(",", "."), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
