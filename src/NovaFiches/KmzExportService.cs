using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;

namespace TopoRapportWin;

public static class KmzExportService
{
    public sealed record KmzPoint(string Id, double X, double Y, double Z, string? Code, bool HasZ = true, string Source = "TXT");
    public sealed record KmzPreviewPoint(string Id, double X, double Y, double Z, string? Code, double Lon, double Lat, bool HasZ = true);
    public sealed record KmzLine(string Id, string Layer, double X1, double Y1, double Z1, double X2, double Y2, double Z2, bool HasZ = true);
    public sealed record KmzPreviewLine(string Id, string Layer, double Lon1, double Lat1, double Lon2, double Lat2);
    public sealed record KmzText(string Id, string Layer, string Text, double X, double Y, double Z, bool HasZ = true);
    public sealed record KmzPreviewText(string Id, string Layer, string Text, double Lon, double Lat);
    public sealed record CoordinateSystemDetection(string CoordinateSystem, string Method);
    public sealed record KmzNgfPoint(string Id, string Nom, string? Etat, double? Altitude, double Lon, double Lat);

    public static IReadOnlyList<string> CoordinateSystems { get; } = new[]
    {
        "RGF93 / Lambert-93 (EPSG:2154)",
        "NTF / Lambert 1 (EPSG:27561)",
        "NTF / Lambert 2 (EPSG:27562)",
        "NTF / Lambert 3 (EPSG:27563)",
        "NTF / Lambert 2 étendu (EPSG:27572)",
        "RGF93 / CC42 (EPSG:3942)",
        "RGF93 / CC43 (EPSG:3943)",
        "RGF93 / CC44 (EPSG:3944)",
        "RGF93 / CC45 (EPSG:3945)",
        "RGF93 / CC46 (EPSG:3946)",
        "RGF93 / CC47 (EPSG:3947)",
        "RGF93 / CC48 (EPSG:3948)",
        "RGF93 / CC49 (EPSG:3949)",
        "RGF93 / CC50 (EPSG:3950)",
        "WGS84 lon/lat (EPSG:4326)"
    };

    public static string GuessCoordinateSystemFromFileName(string fileName)
    {
        var name = Path.GetFileName(fileName ?? "");
        for (int z = 42; z <= 50; z++)
        {
            if (name.Contains($"CC{z}", StringComparison.OrdinalIgnoreCase))
                return $"RGF93 / CC{z} (EPSG:39{z})";
        }

        if (name.Contains("L93", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("LAMBERT93", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("LAMBERT_93", StringComparison.OrdinalIgnoreCase))
            return CoordinateSystems[0];

        if (name.Contains("LAMBERT2ETENDU", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("LAMBERT_2_ETENDU", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("L2E", StringComparison.OrdinalIgnoreCase))
            return "NTF / Lambert 2 étendu (EPSG:27572)";

        if (name.Contains("LAMBERT1", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("LAMBERT_1", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("LAMBERT-1", StringComparison.OrdinalIgnoreCase) ||
            HasIsolatedToken(name, "L1"))
            return "NTF / Lambert 1 (EPSG:27561)";

        if (name.Contains("LAMBERT2", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("LAMBERT_2", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("LAMBERT-2", StringComparison.OrdinalIgnoreCase) ||
            HasIsolatedToken(name, "L2"))
            return "NTF / Lambert 2 (EPSG:27562)";

        if (name.Contains("LAMBERT3", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("LAMBERT_3", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("LAMBERT-3", StringComparison.OrdinalIgnoreCase) ||
            HasIsolatedToken(name, "L3"))
            return "NTF / Lambert 3 (EPSG:27563)";

        return "RGF93 / CC49 (EPSG:3949)";
    }

    public static CoordinateSystemDetection DetectCoordinateSystem(
        string fileName,
        string? fileContent = null,
        IEnumerable<KmzPoint>? points = null)
    {
        var text = fileContent ?? "";

        for (int z = 42; z <= 50; z++)
        {
            if (ContainsAny(
                    text,
                    $"EPSG:{3900 + z}",
                    $"ProjectedCoordinateSystem id=\"RGF93.CC{z}\"",
                    $"Alias id=\"{3900 + z}\" type=\"CoordinateSystem\"",
                    $"Projection Lambert 93 Zone {z - 41} (CC{z})"))
                return new CoordinateSystemDetection($"RGF93 / CC{z} (EPSG:{3900 + z})", "métadonnées du fichier");
        }

        if (ContainsAny(text, "EPSG:2154", "ProjectedCoordinateSystem id=\"RGF93.LAMBERT93\"", "Alias id=\"2154\" type=\"CoordinateSystem\""))
            return new CoordinateSystemDetection("RGF93 / Lambert-93 (EPSG:2154)", "métadonnées du fichier");
        if (ContainsAny(text, "EPSG:27572", "Alias id=\"27572\" type=\"CoordinateSystem\""))
            return new CoordinateSystemDetection("NTF / Lambert 2 étendu (EPSG:27572)", "métadonnées du fichier");
        if (ContainsAny(text, "EPSG:27561", "Alias id=\"27561\" type=\"CoordinateSystem\"", "Projection Lambert Zone I"))
            return new CoordinateSystemDetection("NTF / Lambert 1 (EPSG:27561)", "métadonnées du fichier");
        if (ContainsAny(text, "EPSG:27562", "Alias id=\"27562\" type=\"CoordinateSystem\"", "Projection Lambert Zone II"))
            return new CoordinateSystemDetection("NTF / Lambert 2 (EPSG:27562)", "métadonnées du fichier");
        if (ContainsAny(text, "EPSG:27563", "Alias id=\"27563\" type=\"CoordinateSystem\"", "Projection Lambert Zone III"))
            return new CoordinateSystemDetection("NTF / Lambert 3 (EPSG:27563)", "métadonnées du fichier");

        var fromName = GuessCoordinateSystemFromFileName(fileName);
        if (!string.Equals(fromName, "RGF93 / CC49 (EPSG:3949)", StringComparison.Ordinal) ||
            Path.GetFileName(fileName ?? "").Contains("CC49", StringComparison.OrdinalIgnoreCase))
            return new CoordinateSystemDetection(fromName, "nom du fichier");

        var sample = points?.Take(100).ToList() ?? new List<KmzPoint>();
        if (sample.Count > 0)
        {
            double x = sample.Average(p => p.X);
            double y = sample.Average(p => p.Y);

            if (x is >= -180 and <= 180 && y is >= -90 and <= 90)
                return new CoordinateSystemDetection("WGS84 lon/lat (EPSG:4326)", "plage des coordonnées");

            if (x is >= 0 and <= 1300000 && y is >= 6000000 and <= 7300000)
                return new CoordinateSystemDetection("RGF93 / Lambert-93 (EPSG:2154)", "plage des coordonnées");

            if (x is >= 1200000 and <= 2200000 && y is >= 1000000 and <= 10200000)
            {
                int zone = (int)Math.Round((y - 200000d) / 1000000d) + 41;
                zone = Math.Clamp(zone, 42, 50);
                return new CoordinateSystemDetection($"RGF93 / CC{zone} (EPSG:{3900 + zone})", "plage des coordonnées");
            }
        }

        return new CoordinateSystemDetection("RGF93 / CC49 (EPSG:3949)", "valeur par défaut à confirmer");
    }

    private static bool ContainsAny(string text, params string[] values)
        => values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));

    private static bool HasIsolatedToken(string text, string token)
        => Regex.IsMatch(text, $@"(?<![A-Z0-9]){Regex.Escape(token)}(?![A-Z0-9])", RegexOptions.IgnoreCase);

    public static void ExportPointsToKmz(IEnumerable<KmzPoint> points, string sourceCrs, string outputPath, string documentName)
    {
        var list = points?.ToList() ?? new List<KmzPoint>();
        if (list.Count == 0)
            throw new InvalidOperationException("Aucun point a exporter en KMZ.");

        if (string.IsNullOrWhiteSpace(outputPath))
            throw new InvalidOperationException("Chemin KMZ invalide.");

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");

        var kml = BuildKml(list, sourceCrs, documentName);
        if (File.Exists(outputPath))
            File.Delete(outputPath);

        using var zip = ZipFile.Open(outputPath, ZipArchiveMode.Create);
        var entry = zip.CreateEntry("doc.kml", CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(kml);
    }

    public static List<KmzPreviewPoint> ProjectForPreview(IEnumerable<KmzPoint> points, string sourceCrs)
    {
        var outPoints = new List<KmzPreviewPoint>();
        foreach (var p in points ?? Array.Empty<KmzPoint>())
        {
            var (lon, lat) = ToWgs84(p.X, p.Y, sourceCrs);
            outPoints.Add(new KmzPreviewPoint(p.Id, p.X, p.Y, p.Z, p.Code, lon, lat, p.HasZ));
        }
        return outPoints;
    }

    public static List<KmzPreviewLine> ProjectLinesForPreview(IEnumerable<KmzLine> lines, string sourceCrs)
    {
        var output = new List<KmzPreviewLine>();
        foreach (var line in lines ?? Array.Empty<KmzLine>())
        {
            var a = ToWgs84(line.X1, line.Y1, sourceCrs);
            var b = ToWgs84(line.X2, line.Y2, sourceCrs);
            output.Add(new KmzPreviewLine(line.Id, line.Layer, a.Lon, a.Lat, b.Lon, b.Lat));
        }
        return output;
    }

    public static List<KmzPreviewText> ProjectTextsForPreview(IEnumerable<KmzText> texts, string sourceCrs)
    {
        var output = new List<KmzPreviewText>();
        foreach (var text in texts ?? Array.Empty<KmzText>())
        {
            var position = ToWgs84(text.X, text.Y, sourceCrs);
            output.Add(new KmzPreviewText(text.Id, text.Layer, text.Text, position.Lon, position.Lat));
        }
        return output;
    }

    public static void ExportGeometryToKmz(
        IEnumerable<KmzPoint> points,
        IEnumerable<KmzLine> lines,
        IEnumerable<KmzText> texts,
        string sourceCrs,
        string outputPath,
        string documentName,
        IEnumerable<KmzNgfPoint>? ngfPoints = null)
    {
        var pointList = points?.ToList() ?? new List<KmzPoint>();
        var lineList = lines?.ToList() ?? new List<KmzLine>();
        var textList = texts?.ToList() ?? new List<KmzText>();
        var ngfList = ngfPoints?.ToList() ?? new List<KmzNgfPoint>();
        if (pointList.Count == 0 && lineList.Count == 0 && textList.Count == 0 && ngfList.Count == 0)
            throw new InvalidOperationException("Aucun élément à exporter en KMZ.");

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        var kml = BuildGeometryKml(pointList, lineList, textList, sourceCrs, documentName, ngfList);
        if (File.Exists(outputPath))
            File.Delete(outputPath);

        using var zip = ZipFile.Open(outputPath, ZipArchiveMode.Create);
        var entry = zip.CreateEntry("doc.kml", CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(kml);
    }

    private static string BuildKml(List<KmzPoint> points, string sourceCrs, string documentName)
    {
        var sb = new StringBuilder();
        string docName = string.IsNullOrWhiteSpace(documentName) ? "Nova-Fiches KMZ" : documentName.Trim();

        sb.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
        sb.AppendLine("""<kml xmlns="http://www.opengis.net/kml/2.2">""");
        sb.AppendLine("<Document>");
        sb.AppendLine($"  <name>{Xml(docName)}</name>");
        sb.AppendLine("  <open>1</open>");
        AppendPointStyles(sb);
        sb.AppendLine("  <Folder>");
        sb.AppendLine("    <name>Points</name>");

        foreach (var p in points)
        {
            var (lon, lat) = ToWgs84(p.X, p.Y, sourceCrs);
            sb.AppendLine("    <Placemark>");
            sb.AppendLine($"      <name>{Xml(p.Id)}</name>");
            sb.AppendLine("      <visibility>1</visibility>");
            sb.AppendLine("      <styleUrl>#novaTxtPointStyle</styleUrl>");
            sb.AppendLine("      <ExtendedData>");
            Data(sb, "ID", p.Id);
            Data(sb, "X", F(p.X, 3));
            Data(sb, "Y", F(p.Y, 3));
            Data(sb, "Z", p.HasZ ? F(p.Z, 3) : "");
            Data(sb, "Code", p.Code ?? "");
            Data(sb, "Systeme source", sourceCrs);
            Data(sb, "Longitude WGS84", F(lon, 12));
            Data(sb, "Latitude WGS84", F(lat, 12));
            sb.AppendLine("      </ExtendedData>");
            sb.AppendLine("      <Point>");
            sb.AppendLine($"        <altitudeMode>{(p.HasZ ? "absolute" : "clampToGround")}</altitudeMode>");
            sb.AppendLine($"        <coordinates>{F(lon, 12)},{F(lat, 12)},{(p.HasZ ? F(p.Z, 3) : "0")}</coordinates>");
            sb.AppendLine("      </Point>");
            sb.AppendLine("    </Placemark>");
        }

        sb.AppendLine("  </Folder>");
        sb.AppendLine("</Document>");
        sb.AppendLine("</kml>");
        return sb.ToString();
    }

    private static string BuildGeometryKml(
        IReadOnlyList<KmzPoint> points,
        IReadOnlyList<KmzLine> lines,
        IReadOnlyList<KmzText> texts,
        string sourceCrs,
        string documentName,
        IReadOnlyList<KmzNgfPoint>? ngfPoints = null)
    {
        var sb = new StringBuilder();
        string docName = string.IsNullOrWhiteSpace(documentName) ? "Nova-Fiches KMZ" : documentName.Trim();
        sb.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
        sb.AppendLine("""<kml xmlns="http://www.opengis.net/kml/2.2">""");
        sb.AppendLine("<Document>");
        sb.AppendLine($"  <name>{Xml(docName)}</name>");
        AppendPointStyles(sb);
        sb.AppendLine("  <Style id=\"novaLineStyle\"><LineStyle><color>ff1673e6</color><width>3</width></LineStyle></Style>");
        sb.AppendLine("  <Style id=\"novaTextStyle\"><IconStyle><scale>0</scale></IconStyle><LabelStyle><color>ffffffff</color><scale>0.82</scale></LabelStyle></Style>");

        if (points.Count > 0)
        {
            sb.AppendLine("  <Folder><name>Points topo</name>");
            foreach (var p in points)
            {
                var (lon, lat) = ToWgs84(p.X, p.Y, sourceCrs);
                sb.AppendLine("    <Placemark>");
                string style = p.Source.Equals("DXF", StringComparison.OrdinalIgnoreCase)
                    ? "#novaDxfPointStyle"
                    : "#novaTxtPointStyle";
                sb.AppendLine($"      <name>{Xml(p.Id)}</name><visibility>1</visibility><styleUrl>{style}</styleUrl>");
                sb.AppendLine("      <ExtendedData>");
                Data(sb, "ID", p.Id);
                Data(sb, "X", F(p.X, 3));
                Data(sb, "Y", F(p.Y, 3));
                Data(sb, "Z", p.HasZ ? F(p.Z, 3) : "");
                Data(sb, "Code", p.Code ?? "");
                Data(sb, "Source", p.Source);
                Data(sb, "Systeme source", sourceCrs);
                sb.AppendLine("      </ExtendedData>");
                sb.AppendLine($"      <Point><altitudeMode>{(p.HasZ ? "absolute" : "clampToGround")}</altitudeMode><coordinates>{F(lon, 12)},{F(lat, 12)},{(p.HasZ ? F(p.Z, 3) : "0")}</coordinates></Point>");
                sb.AppendLine("    </Placemark>");
            }
            sb.AppendLine("  </Folder>");
        }

        foreach (var group in lines.GroupBy(line => line.Layer, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"  <Folder><name>{Xml(group.Key)}</name>");
            foreach (var line in group)
            {
                var a = ToWgs84(line.X1, line.Y1, sourceCrs);
                var b = ToWgs84(line.X2, line.Y2, sourceCrs);
                sb.AppendLine("    <Placemark>");
                sb.AppendLine($"      <name>{Xml(line.Id)}</name><styleUrl>#novaLineStyle</styleUrl>");
                sb.AppendLine($"      <LineString><tessellate>1</tessellate><altitudeMode>{(line.HasZ ? "absolute" : "clampToGround")}</altitudeMode>");
                sb.AppendLine($"        <coordinates>{F(a.Lon, 12)},{F(a.Lat, 12)},{(line.HasZ ? F(line.Z1, 3) : "0")} {F(b.Lon, 12)},{F(b.Lat, 12)},{(line.HasZ ? F(line.Z2, 3) : "0")}</coordinates>");
                sb.AppendLine("      </LineString>");
                sb.AppendLine("    </Placemark>");
            }
            sb.AppendLine("  </Folder>");
        }

        if (texts.Count > 0)
        {
            sb.AppendLine("  <Folder><name>Textes DXF</name>");
            foreach (var text in texts)
            {
                var position = ToWgs84(text.X, text.Y, sourceCrs);
                sb.AppendLine("    <Placemark>");
                sb.AppendLine($"      <name>{Xml(text.Text)}</name><styleUrl>#novaTextStyle</styleUrl>");
                sb.AppendLine("      <ExtendedData>");
                Data(sb, "Calque", text.Layer);
                Data(sb, "X", F(text.X, 3));
                Data(sb, "Y", F(text.Y, 3));
                Data(sb, "Z", text.HasZ ? F(text.Z, 3) : "");
                sb.AppendLine("      </ExtendedData>");
                sb.AppendLine($"      <Point><altitudeMode>{(text.HasZ ? "absolute" : "clampToGround")}</altitudeMode><coordinates>{F(position.Lon, 12)},{F(position.Lat, 12)},{(text.HasZ ? F(text.Z, 3) : "0")}</coordinates></Point>");
                sb.AppendLine("    </Placemark>");
            }
            sb.AppendLine("  </Folder>");
        }

        if (ngfPoints is { Count: > 0 })
        {
            sb.AppendLine("  <Folder><name>Repères NGF (IGN)</name>");
            foreach (var ngf in ngfPoints)
            {
                sb.AppendLine("    <Placemark>");
                sb.AppendLine($"      <name>{Xml(ngf.Nom)}</name><visibility>1</visibility><styleUrl>#novaNgfPointStyle</styleUrl>");
                sb.AppendLine("      <ExtendedData>");
                Data(sb, "ID", ngf.Id);
                Data(sb, "Nom", ngf.Nom);
                Data(sb, "Altitude NGF", ngf.Altitude.HasValue ? F(ngf.Altitude.Value, 3) : "");
                Data(sb, "État", ngf.Etat ?? "");
                Data(sb, "Source", "IGN (repère de nivellement)");
                sb.AppendLine("      </ExtendedData>");
                sb.AppendLine($"      <Point><altitudeMode>clampToGround</altitudeMode><coordinates>{F(ngf.Lon, 12)},{F(ngf.Lat, 12)},0</coordinates></Point>");
                sb.AppendLine("    </Placemark>");
            }
            sb.AppendLine("  </Folder>");
        }

        sb.AppendLine("</Document>");
        sb.AppendLine("</kml>");
        return sb.ToString();
    }

    private static void Data(StringBuilder sb, string name, string value)
    {
        sb.AppendLine($"        <Data name=\"{Xml(name)}\"><value>{Xml(value)}</value></Data>");
    }

    private static void AppendPointStyles(StringBuilder sb)
    {
        const string circleIcon = "http://maps.google.com/mapfiles/kml/shapes/placemark_circle.png";
        sb.AppendLine("  <Style id=\"novaTxtPointStyle\">");
        sb.AppendLine("    <IconStyle><color>ffff6712</color><scale>0.85</scale>");
        sb.AppendLine($"      <Icon><href>{circleIcon}</href></Icon></IconStyle>");
        sb.AppendLine("    <LabelStyle><color>ffffffff</color><scale>0.82</scale></LabelStyle>");
        sb.AppendLine("  </Style>");
        sb.AppendLine("  <Style id=\"novaDxfPointStyle\">");
        sb.AppendLine("    <IconStyle><color>ff20a5ff</color><scale>0.85</scale>");
        sb.AppendLine($"      <Icon><href>{circleIcon}</href></Icon></IconStyle>");
        sb.AppendLine("    <LabelStyle><color>ffffffff</color><scale>0.82</scale></LabelStyle>");
        sb.AppendLine("  </Style>");
        sb.AppendLine("  <Style id=\"novaNgfPointStyle\">");
        sb.AppendLine("    <IconStyle><color>ff1c3fdc</color><scale>0.9</scale>");
        sb.AppendLine($"      <Icon><href>{circleIcon}</href></Icon></IconStyle>");
        sb.AppendLine("    <LabelStyle><color>ffffffff</color><scale>0.82</scale></LabelStyle>");
        sb.AppendLine("  </Style>");
    }

    private static (double Lon, double Lat) ToWgs84(double x, double y, string sourceCrs)
    {
        if (sourceCrs.Contains("4326", StringComparison.OrdinalIgnoreCase) ||
            sourceCrs.Contains("WGS84", StringComparison.OrdinalIgnoreCase))
            return (x, y);

        if (sourceCrs.Contains("2154", StringComparison.OrdinalIgnoreCase) ||
            sourceCrs.Contains("Lambert-93", StringComparison.OrdinalIgnoreCase))
            return InverseLambert2Sp(x, y, lat0Deg: 46.5, lat1Deg: 44.0, lat2Deg: 49.0, lon0Deg: 3.0, falseEasting: 700000, falseNorthing: 6600000);

        if (sourceCrs.Contains("27572", StringComparison.OrdinalIgnoreCase) ||
            sourceCrs.Contains("Lambert 2 étendu", StringComparison.OrdinalIgnoreCase) ||
            sourceCrs.Contains("Lambert 2 etendu", StringComparison.OrdinalIgnoreCase))
            return InverseNtfLambertZone(x, y, n: 0.7289686274, c: 11745793.39, xs: 600000.0, ys: 8199695.768);

        if (sourceCrs.Contains("27561", StringComparison.OrdinalIgnoreCase) ||
            sourceCrs.Contains("Lambert 1", StringComparison.OrdinalIgnoreCase))
            return InverseNtfLambertZone(x, y, n: 0.7604059656, c: 11603796.98, xs: 600000.0, ys: 5657616.674);

        if (sourceCrs.Contains("27562", StringComparison.OrdinalIgnoreCase) ||
            sourceCrs.Contains("Lambert 2", StringComparison.OrdinalIgnoreCase))
            return InverseNtfLambertZone(x, y, n: 0.7289686274, c: 11745793.39, xs: 600000.0, ys: 6199695.768);

        if (sourceCrs.Contains("27563", StringComparison.OrdinalIgnoreCase) ||
            sourceCrs.Contains("Lambert 3", StringComparison.OrdinalIgnoreCase))
            return InverseNtfLambertZone(x, y, n: 0.6959127966, c: 11947992.52, xs: 600000.0, ys: 6791905.085);

        for (int z = 42; z <= 50; z++)
        {
            if (sourceCrs.Contains($"CC{z}", StringComparison.OrdinalIgnoreCase) ||
                sourceCrs.Contains($"39{z}", StringComparison.OrdinalIgnoreCase))
            {
                double falseNorthing = (z - 41) * 1000000d + 200000d;
                return InverseLambert2Sp(x, y, lat0Deg: z, lat1Deg: z - 0.75, lat2Deg: z + 0.75, lon0Deg: 3.0, falseEasting: 1700000, falseNorthing: falseNorthing);
            }
        }

        throw new InvalidOperationException($"Systeme de coordonnees non pris en charge : {sourceCrs}");
    }

    private static (double Lon, double Lat) InverseLambert2Sp(
        double easting,
        double northing,
        double lat0Deg,
        double lat1Deg,
        double lat2Deg,
        double lon0Deg,
        double falseEasting,
        double falseNorthing)
    {
        const double a = 6378137.0;
        const double invF = 298.257222101;
        double f = 1.0 / invF;
        double eccentricity = Math.Sqrt(2.0 * f - f * f);
        double lat0 = DegToRad(lat0Deg);
        double lat1 = DegToRad(lat1Deg);
        double lat2 = DegToRad(lat2Deg);
        double lon0 = DegToRad(lon0Deg);

        double n = (Math.Log(M(lat1, eccentricity)) - Math.Log(M(lat2, eccentricity))) /
                   (Math.Log(T(lat1, eccentricity)) - Math.Log(T(lat2, eccentricity)));
        double bigF = M(lat1, eccentricity) / (n * Math.Pow(T(lat1, eccentricity), n));
        double rho0 = a * bigF * Math.Pow(T(lat0, eccentricity), n);

        double dx = easting - falseEasting;
        double dy = rho0 - (northing - falseNorthing);
        double rho = Math.Sqrt(dx * dx + dy * dy);
        double theta = Math.Atan2(dx, dy);
        double lon = lon0 + theta / n;

        double t = Math.Pow(rho / (a * bigF), 1.0 / n);
        double lat = Math.PI / 2.0 - 2.0 * Math.Atan(t);
        for (int i = 0; i < 10; i++)
        {
            double sin = Math.Sin(lat);
            lat = Math.PI / 2.0 - 2.0 * Math.Atan(t * Math.Pow((1.0 - eccentricity * sin) / (1.0 + eccentricity * sin), eccentricity / 2.0));
        }

        return (RadToDeg(lon), RadToDeg(lat));
    }

    private static (double Lon, double Lat) InverseNtfLambertZone(double easting, double northing, double n, double c, double xs, double ys)
    {
        const double clarkeA = 6378249.2;
        const double clarkeInvF = 293.4660212936269;
        const double parisMeridianDeg = 2.33722917;

        double eccentricity = Math.Sqrt(2.0 / clarkeInvF - 1.0 / (clarkeInvF * clarkeInvF));
        double dx = easting - xs;
        double dy = ys - northing;
        double r = Math.Sqrt(dx * dx + dy * dy);
        double gamma = Math.Atan2(dx, dy);
        double lonNtfGreenwich = parisMeridianDeg + RadToDeg(gamma / n);
        double latIso = Math.Log(c / r) / n;
        double latNtf = InverseIsometricLatitude(latIso, eccentricity);

        return NtfGeographicToWgs84(RadToDeg(latNtf), lonNtfGreenwich, clarkeA, clarkeInvF);
    }

    private static double InverseIsometricLatitude(double latIso, double e)
    {
        double lat = 2.0 * Math.Atan(Math.Exp(latIso)) - Math.PI / 2.0;
        for (int i = 0; i < 10; i++)
        {
            double sin = Math.Sin(lat);
            lat = 2.0 * Math.Atan(Math.Pow((1.0 + e * sin) / (1.0 - e * sin), e / 2.0) * Math.Exp(latIso)) - Math.PI / 2.0;
        }
        return lat;
    }

    private static (double Lon, double Lat) NtfGeographicToWgs84(double latDeg, double lonDeg, double sourceA, double sourceInvF)
    {
        var (x, y, z) = GeographicToEcef(latDeg, lonDeg, 0.0, sourceA, sourceInvF);

        x -= 168.0;
        y -= 60.0;
        z += 320.0;

        var (lat, lon) = EcefToGeographic(x, y, z, 6378137.0, 298.257223563);
        return (lon, lat);
    }

    private static (double X, double Y, double Z) GeographicToEcef(double latDeg, double lonDeg, double h, double a, double invF)
    {
        double f = 1.0 / invF;
        double e2 = 2.0 * f - f * f;
        double lat = DegToRad(latDeg);
        double lon = DegToRad(lonDeg);
        double sinLat = Math.Sin(lat);
        double cosLat = Math.Cos(lat);
        double n = a / Math.Sqrt(1.0 - e2 * sinLat * sinLat);
        return ((n + h) * cosLat * Math.Cos(lon), (n + h) * cosLat * Math.Sin(lon), (n * (1.0 - e2) + h) * sinLat);
    }

    private static (double Lat, double Lon) EcefToGeographic(double x, double y, double z, double a, double invF)
    {
        double f = 1.0 / invF;
        double e2 = 2.0 * f - f * f;
        double lon = Math.Atan2(y, x);
        double p = Math.Sqrt(x * x + y * y);
        double lat = Math.Atan2(z, p * (1.0 - e2));
        for (int i = 0; i < 10; i++)
        {
            double sinLat = Math.Sin(lat);
            double n = a / Math.Sqrt(1.0 - e2 * sinLat * sinLat);
            lat = Math.Atan2(z + e2 * n * sinLat, p);
        }
        return (RadToDeg(lat), RadToDeg(lon));
    }

    private static double M(double phi, double e)
        => Math.Cos(phi) / Math.Sqrt(1.0 - e * e * Math.Sin(phi) * Math.Sin(phi));

    private static double T(double phi, double e)
    {
        double sin = Math.Sin(phi);
        return Math.Tan(Math.PI / 4.0 - phi / 2.0) /
               Math.Pow((1.0 - e * sin) / (1.0 + e * sin), e / 2.0);
    }

    private static double DegToRad(double deg) => deg * Math.PI / 180.0;
    private static double RadToDeg(double rad) => rad * 180.0 / Math.PI;

    private static string F(double value, int decimals)
        => value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    private static string Xml(string value)
        => SecurityElement.Escape(value ?? "") ?? "";
}
