using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TopoRapportWin;

public static class DxfKmzService
{
    public sealed record DxfPoint(
        string Key,
        string Id,
        string Layer,
        string Block,
        double X,
        double Y,
        double Z,
        string? Code,
        bool HasZ);

    public sealed record DxfLine(
        string Key,
        string Layer,
        double X1,
        double Y1,
        double Z1,
        double X2,
        double Y2,
        double Z2,
        bool HasZ);

    public sealed record DxfText(
        string Key,
        string Layer,
        string Text,
        double X,
        double Y,
        double Z,
        bool HasZ);

    public sealed record DxfLayer(string Name, int PointCount, int LineCount, int TextCount);

    public sealed record DxfDocument(
        string FileName,
        string RawText,
        IReadOnlyList<DxfPoint> Points,
        IReadOnlyList<DxfLine> Lines,
        IReadOnlyList<DxfText> Texts,
        IReadOnlyList<DxfLayer> Layers,
        KmzExportService.CoordinateSystemDetection Detection);

    private sealed record Pair(int Code, string Value);

    public static DxfDocument Load(string path)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        byte[] bytes;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
        }
        var text = DecodeDxfText(bytes);
        var pairs = ParsePairs(text);
        var points = new List<DxfPoint>();
        var lines = new List<DxfLine>();
        var texts = new List<DxfText>();

        bool inEntities = false;
        int pointIndex = 0;
        int lineIndex = 0;
        int textIndex = 0;
        int i = 0;
        while (i < pairs.Count)
        {
            var pair = pairs[i];
            if (pair.Code == 0 && pair.Value.Equals("SECTION", StringComparison.OrdinalIgnoreCase) &&
                i + 1 < pairs.Count && pairs[i + 1].Code == 2)
            {
                inEntities = pairs[i + 1].Value.Equals("ENTITIES", StringComparison.OrdinalIgnoreCase);
                i += 2;
                continue;
            }

            if (pair.Code == 0 && pair.Value.Equals("ENDSEC", StringComparison.OrdinalIgnoreCase))
            {
                inEntities = false;
                i++;
                continue;
            }

            if (!inEntities || pair.Code != 0)
            {
                i++;
                continue;
            }

            string type = pair.Value.ToUpperInvariant();
            if (type == "LINE")
            {
                var entity = ReadEntity(pairs, ref i);
                lines.Add(new DxfLine(
                    $"L{++lineIndex}",
                    Get(entity, 8, "0"),
                    Num(entity, 10),
                    Num(entity, 20),
                    Num(entity, 30),
                    Num(entity, 11),
                    Num(entity, 21),
                    Num(entity, 31),
                    Has(entity, 30) && Has(entity, 31)));
                continue;
            }

            if (type == "LWPOLYLINE")
            {
                var entity = ReadEntity(pairs, ref i);
                string layer = Get(entity, 8, "0");
                bool closed = ((int)Num(entity, 70) & 1) == 1;
                double defaultZ = Has(entity, 38) ? Num(entity, 38) : Num(entity, 30);
                bool hasZ = Has(entity, 38) || Has(entity, 30);
                var vertices = ReadLwPolylineVertices(entity, defaultZ, hasZ);
                AddPolylineSegments(lines, vertices, closed, layer, ref lineIndex);
                continue;
            }

            if (type == "POLYLINE")
            {
                var entity = ReadEntity(pairs, ref i);
                string layer = Get(entity, 8, "0");
                bool closed = ((int)Num(entity, 70) & 1) == 1;
                var vertices = new List<(double X, double Y, double Z, bool HasZ)>();

                while (i < pairs.Count && pairs[i].Code == 0 &&
                       pairs[i].Value.Equals("VERTEX", StringComparison.OrdinalIgnoreCase))
                {
                    var vertex = ReadEntity(pairs, ref i);
                    vertices.Add((Num(vertex, 10), Num(vertex, 20), Num(vertex, 30), Has(vertex, 30)));
                }

                if (i < pairs.Count && pairs[i].Code == 0 &&
                    pairs[i].Value.Equals("SEQEND", StringComparison.OrdinalIgnoreCase))
                    ReadEntity(pairs, ref i);

                AddPolylineSegments(lines, vertices, closed, layer, ref lineIndex);
                continue;
            }

            if (type == "POINT")
            {
                var entity = ReadEntity(pairs, ref i);
                string layer = Get(entity, 8, "0");
                string id = $"POINT_{++pointIndex}";
                points.Add(new DxfPoint(id, id, layer, "POINT", Num(entity, 10), Num(entity, 20), Num(entity, 30), null, Has(entity, 30)));
                continue;
            }

            if (type is "TEXT" or "MTEXT")
            {
                var entity = ReadEntity(pairs, ref i);
                string value = type == "MTEXT"
                    ? string.Concat(entity.Where(pair => pair.Code is 3 or 1).Select(pair => pair.Value))
                    : Get(entity, 1, "");
                value = CleanDxfText(value);
                if (value.Length > 0)
                {
                    texts.Add(new DxfText(
                        $"T{++textIndex}",
                        Get(entity, 8, "0"),
                        value,
                        Num(entity, 10),
                        Num(entity, 20),
                        Num(entity, 30),
                        Has(entity, 30)));
                }
                continue;
            }

            if (type == "INSERT")
            {
                var insert = ReadEntity(pairs, ref i);
                string layer = Get(insert, 8, "0");
                string block = Get(insert, 2, "INSERT");
                double x = Num(insert, 10);
                double y = Num(insert, 20);
                double z = Num(insert, 30);
                bool hasZ = Has(insert, 30);
                string? id = null;
                string? code = null;

                while (i < pairs.Count && pairs[i].Code == 0 &&
                       pairs[i].Value.Equals("ATTRIB", StringComparison.OrdinalIgnoreCase))
                {
                    var attribute = ReadEntity(pairs, ref i);
                    string tag = Get(attribute, 2, "").Trim();
                    string value = Get(attribute, 1, "").Trim();
                    if (tag.Equals("MAT", StringComparison.OrdinalIgnoreCase) && value.Length > 0)
                        id = value;
                    else if (tag.Equals("COD", StringComparison.OrdinalIgnoreCase) && value.Length > 0)
                        code = value;
                    else if (tag.Equals("ALT", StringComparison.OrdinalIgnoreCase) &&
                             TryNum(value, out var altitude))
                    {
                        z = altitude;
                        hasZ = true;
                    }
                }

                if (i < pairs.Count && pairs[i].Code == 0 &&
                    pairs[i].Value.Equals("SEQEND", StringComparison.OrdinalIgnoreCase))
                    ReadEntity(pairs, ref i);

                string key = $"P{++pointIndex}";
                points.Add(new DxfPoint(key, id ?? key, layer, block, x, y, z, code, hasZ));
                continue;
            }

            ReadEntity(pairs, ref i);
        }

        var layers = points.Select(p => p.Layer)
            .Concat(lines.Select(l => l.Layer))
            .Concat(texts.Select(t => t.Layer))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => new DxfLayer(
                name,
                points.Count(p => p.Layer.Equals(name, StringComparison.OrdinalIgnoreCase)),
                lines.Count(l => l.Layer.Equals(name, StringComparison.OrdinalIgnoreCase)),
                texts.Count(t => t.Layer.Equals(name, StringComparison.OrdinalIgnoreCase))))
            .ToList();

        var detectionPoints = points.Select(p => new KmzExportService.KmzPoint(p.Id, p.X, p.Y, p.Z, p.Code, p.HasZ));
        var detection = KmzExportService.DetectCoordinateSystem(path, text, detectionPoints);
        return new DxfDocument(Path.GetFileName(path), text, points, lines, texts, layers, detection);
    }

    /// <summary>
    /// Les DXF ASCII classiques sont encodés selon le codepage Windows (1252 par défaut,
    /// caractères accentués via échappement \U+XXXX). Certains exports plus récents
    /// (QGIS, LibreCAD...) produisent en revanche de l'UTF-8 avec BOM : on le détecte
    /// pour éviter de corrompre les accents dans ce cas, sans changer le comportement
    /// par défaut pour les DXF existants.
    /// </summary>
    private static string DecodeDxfText(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        return Encoding.GetEncoding(1252).GetString(bytes);
    }

    private static List<Pair> ParsePairs(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var pairs = new List<Pair>(lines.Length / 2);
        for (int i = 0; i + 1 < lines.Length; i += 2)
        {
            if (int.TryParse(lines[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int code))
                pairs.Add(new Pair(code, lines[i + 1].TrimEnd()));
        }
        return pairs;
    }

    private static List<Pair> ReadEntity(IReadOnlyList<Pair> pairs, ref int index)
    {
        var entity = new List<Pair>();
        index++;
        while (index < pairs.Count && pairs[index].Code != 0)
        {
            entity.Add(pairs[index]);
            index++;
        }
        return entity;
    }

    private static string Get(IReadOnlyList<Pair> entity, int code, string fallback)
        => entity.FirstOrDefault(pair => pair.Code == code)?.Value ?? fallback;

    private static double Num(IReadOnlyList<Pair> entity, int code)
        => TryNum(Get(entity, code, "0"), out var value) ? value : 0d;

    private static bool Has(IReadOnlyList<Pair> entity, int code)
        => entity.Any(pair => pair.Code == code && TryNum(pair.Value, out _));

    private static bool TryNum(string text, out double value)
        => double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static List<(double X, double Y, double Z, bool HasZ)> ReadLwPolylineVertices(
        IReadOnlyList<Pair> entity,
        double defaultZ,
        bool hasZ)
    {
        var vertices = new List<(double X, double Y, double Z, bool HasZ)>();
        double? x = null;

        foreach (var pair in entity)
        {
            if (pair.Code == 10 && TryNum(pair.Value, out var vx))
            {
                x = vx;
                continue;
            }

            if (pair.Code == 20 && x.HasValue && TryNum(pair.Value, out var vy))
            {
                vertices.Add((x.Value, vy, defaultZ, hasZ));
                x = null;
            }
        }

        return vertices;
    }

    private static void AddPolylineSegments(
        List<DxfLine> lines,
        IReadOnlyList<(double X, double Y, double Z, bool HasZ)> vertices,
        bool closed,
        string layer,
        ref int lineIndex)
    {
        if (vertices.Count < 2)
            return;

        for (int n = 0; n < vertices.Count - 1; n++)
            AddSegment(lines, vertices[n], vertices[n + 1], layer, ref lineIndex);

        if (closed)
            AddSegment(lines, vertices[^1], vertices[0], layer, ref lineIndex);
    }

    private static void AddSegment(
        List<DxfLine> lines,
        (double X, double Y, double Z, bool HasZ) a,
        (double X, double Y, double Z, bool HasZ) b,
        string layer,
        ref int lineIndex)
    {
        lines.Add(new DxfLine(
            $"L{++lineIndex}",
            layer,
            a.X,
            a.Y,
            a.Z,
            b.X,
            b.Y,
            b.Z,
            a.HasZ && b.HasZ));
    }

    private static string CleanDxfText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        string text = value.Replace("\\P", "\n", StringComparison.Ordinal)
            .Replace("\\~", " ", StringComparison.Ordinal);
        text = Regex.Replace(text, @"\\[A-Za-z][^;]*;", "");
        text = Regex.Replace(text, @"\\[LlOoKk]", "");
        text = text.Replace("{", "").Replace("}", "");
        return Regex.Replace(text, @"\s+", " ").Trim();
    }
}
