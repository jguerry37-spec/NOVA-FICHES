using System.Globalization;
using System.Text;
using System.Text.Json;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace TopoRapportWin;

public static class AutoCadExportService
{
    public static void ExportFromNovaState(string stateJson, string payloadJson, string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            throw new InvalidOperationException("Dossier d'export invalide.");

        Directory.CreateDirectory(folderPath);

        using var stateDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(stateJson) ? "{}" : stateJson);
        using var payloadDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson);

        var state = stateDoc.RootElement;
        var payload = payloadDoc.RootElement;

        var cha = FirstNonEmpty(
            GetStringByPaths(state, "infosDossier.repCHA"),
            GetStringByPaths(state, "numeroCha", "numCha", "cha", "CHA", "codeChantier"),
            "NO_CHA");

        var safeCha = Sanitize(cha);

        ExportPoints(GetArray(payload, "Implantation"), Path.Combine(folderPath, $"NOVA_{safeCha}_IMPLANTATION.txt"));
        ExportPoints(GetArray(payload, "Ligne"), Path.Combine(folderPath, $"NOVA_{safeCha}_LIGNE.txt"));
        ExportPoints(GetArray(payload, "Leve"), Path.Combine(folderPath, $"NOVA_{safeCha}_LEVE.txt"));

        ExportCartouche(state, Path.Combine(folderPath, $"NOVA_{safeCha}_CARTOUCHE.txt"));
    }

    private static void ExportPoints(JsonElement array, string path)
    {
        var sb = new StringBuilder();

        if (array.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in array.EnumerateArray())
            {
                var id = FirstNonEmpty(
                    GetString(item, "Id"),
                    GetString(item, "ID"),
                    GetString(item, "id"),
                    GetString(item, "Name"),
                    GetString(item, "name"),
                    GetString(item, "pointName"));

                if (string.IsNullOrWhiteSpace(id))
                    continue;

                var x = GetDouble(item, "X");
                var y = GetDouble(item, "Y");
                var z = GetDouble(item, "Z");

                if (Math.Abs(x) < double.Epsilon && Math.Abs(y) < double.Epsilon && Math.Abs(z) < double.Epsilon)
                    continue;

                sb.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}\t{1:0.000}\t{2:0.000}\t{3:0.000}",
                    id, x, y, z));
            }
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static void ExportCartouche(JsonElement state, string path)
    {
        var intervention = FirstNonEmpty(
            GetStringByPaths(state, "infosDossier.repElements"),
            GetStringByPaths(state, "nomIntervention", "interventionName", "operationName", "projectName"));

        var city = FirstNonEmpty(
            GetStringByPaths(state, "infosDossier.repZone"),
            GetStringByPaths(state, "ville", "city"));

        var address = FirstNonEmpty(
            GetStringByPaths(state, "infosDossier.repSiteAddress"),
            GetStringByPaths(state, "adresseChantier", "address"));

        var contact = FirstNonEmpty(
            GetStringByPaths(state, "infosDossier.repSiteContact"),
            GetStringByPaths(state, "contactChantier", "contact"));

        var cha = FirstNonEmpty(
            GetStringByPaths(state, "infosDossier.repCHA"),
            GetStringByPaths(state, "numeroCha", "numCha", "cha", "CHA", "codeChantier"));

        var type = FirstNonEmpty(
            GetStringByPaths(state, "infosDossier.repType"),
            GetStringByPaths(state, "type"));

        var cartoucheZone = FirstNonEmpty(
            GetStringByPaths(state, "infosDossier.repCartoucheZone"),
            GetStringByPaths(state, "infosDossier.cartoucheZone", "infosDossier.zoneCartouche"),
            GetStringByPaths(state, "cartoucheZone", "zoneCartouche"));

        var indice = FirstNonEmpty(
            GetStringByPaths(state, "infosDossier.repIndice"),
            GetStringByPaths(state, "indice", "index"),
            "A");

        var date = NormalizeDate(FirstNonEmpty(
            GetStringByPaths(state, "infosDossier.repDate"),
            GetStringByPaths(state, "dateIntervention", "date"),
            DateTime.Now.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)));

        var ppm = FirstNonEmpty(
            GetStringByPaths(state, "infosDossier.metaPPM"),
            GetStringByPaths(state, "ppm", "PPM"));

        if (!string.IsNullOrWhiteSpace(ppm) &&
            !ppm.Contains("mm/km", StringComparison.OrdinalIgnoreCase))
        {
            ppm += " mm/km";
        }

        var plani = FirstNonEmpty(
            GetStringByPaths(state, "infosDossier.metaCoordSys"),
            GetStringByPaths(state, "systemeCoordonnees", "coordinateSystem"));

        var altimetrie = FirstNonEmpty(
            GetStringByPaths(state, "infosDossier.metaAltSys"),
            GetStringByPaths(state, "systemeAltimetrique", "verticalSystem"));

        var echelle = FirstNonEmpty(
            GetStringByPaths(state, "echelle", "scale"),
            "1/200");

        var xref = FirstNonEmpty(
            GetStringByPaths(state, "infosDossier.repDwg"),
            GetStringByPaths(state, "planReferenceDwg", "referencePlanDwg", "referencePlan"));

        var entreprise = FirstNonEmpty(
            GetStringByPaths(state, "infosDossier.repClient"),
            GetStringByPaths(state, "entreprise", "company"));

        var operatorName = FirstNonEmpty(
            GetStringByPaths(state, "infosDossier.metaIntervenant"),
            GetStringByPaths(state, "intervenant", "operator"));

        var phase = FirstNonEmpty(
            GetStringByPaths(state, "infosDossier.repPhase"),
            GetStringByPaths(state, "phase"),
            "EXE");

        var site = BuildSiteCode(city);

        var typeDePlan = FirstNonEmpty(
            GetStringByPaths(state, "infosDossier.repPlanType"),
            GetStringByPaths(state, "infosDossier.planType", "infosDossier.typePlan"),
            GetStringByPaths(state, "typeDePlan", "planType", "typePlan"));

        var lines = new List<string>
        {
            $"NOM-DU-PROJET=",
            $"TYPE_DE_PLAN={typeDePlan}",

            $"ADRESSE-PROJET={address}",
            $"ADRESSE-PROJET-2={city}",

            $"CODE_CHANTIER={cha}",
            $"PHASE={phase}",
            $"PRESTA={phase}",
            $"TYPE={type}",
            $"IND={indice}",
            $"DATE={date}",
            $"INTERVENANT={operatorName}",

            $"CONTACT={contact}",

            $"ALTERATION={ppm}",
            $"PLANI={plani}",
            $"ALTIMETRIE={altimetrie}",

            $"XREF={xref}",
            $"INFO={entreprise}",
            $"SITE={site}",
            $"ZONE={cartoucheZone}",
            $"VILLE={city}"
        };

        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    private static string BuildSiteCode(string value)
    {
        var text = RemoveDiacritics(value ?? string.Empty)
            .ToUpperInvariant();

        var sb = new StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(ch);
            if (sb.Length >= 4)
                break;
        }

        return sb.ToString();
    }

    private static string RemoveDiacritics(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string NormalizeDate(string value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0)
            return DateTime.Now.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

        string[] formats =
        {
            "yyyy-MM-dd",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-ddTHH:mm:ss.FFFFFFFK",
            "dd/MM/yyyy",
            "d/M/yyyy",
            "dd-MM-yyyy",
            "d-M-yyyy"
        };

        if (DateTime.TryParseExact(
                text,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                out var parsed))
        {
            return parsed.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        }

        return text;
    }

    private static JsonElement GetArray(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            TryGetPropertyIgnoreCase(element, propertyName, out var value) &&
            value.ValueKind == JsonValueKind.Array)
        {
            return value;
        }

        return default;
    }

    private static string GetStringByPaths(JsonElement root, params string[] paths)
    {
        foreach (var path in paths)
        {
            if (TryGetByPath(root, path, out var value))
            {
                var s = JsonToString(value);
                if (!string.IsNullOrWhiteSpace(s))
                    return s;
            }
        }

        return string.Empty;
    }

    private static bool TryGetByPath(JsonElement root, string path, out JsonElement value)
    {
        value = root;
        foreach (var part in path.Split('.'))
        {
            if (value.ValueKind != JsonValueKind.Object || !TryGetPropertyIgnoreCase(value, part, out value))
                return false;
        }
        return true;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        return TryGetPropertyIgnoreCase(element, propertyName, out var value)
            ? JsonToString(value)
            : string.Empty;
    }

    private static string JsonToString(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
    }

    private static double GetDouble(JsonElement element, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(element, propertyName, out var value))
            return 0d;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var d))
            return d;

        if (value.ValueKind == JsonValueKind.String &&
            double.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out d))
            return d;

        return 0d;
    }

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }
        return string.Empty;
    }

    private static string Sanitize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "NO_CHA";

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(input.Trim()
            .Select(c => invalid.Contains(c) ? '_' : c)
            .ToArray());

        cleaned = string.Join("_", cleaned.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        cleaned = cleaned.Trim('_').ToUpperInvariant();
        return string.IsNullOrWhiteSpace(cleaned) ? "NO_CHA" : cleaned;
    }


public static void MergePlanAndFiches(string planPdfPath, string fichesPdfPath, string outputPdfPath)
{
    if (string.IsNullOrWhiteSpace(planPdfPath) || !File.Exists(planPdfPath))
        throw new FileNotFoundException("PDF plan introuvable.", planPdfPath);

    if (string.IsNullOrWhiteSpace(fichesPdfPath) || !File.Exists(fichesPdfPath))
        throw new FileNotFoundException("PDF fiches introuvable.", fichesPdfPath);

    var outDir = Path.GetDirectoryName(outputPdfPath);
    if (!string.IsNullOrWhiteSpace(outDir))
        Directory.CreateDirectory(outDir);

    using var output = new PdfDocument();

    AppendPdf(output, fichesPdfPath);
    AppendPdf(output, planPdfPath);

    output.Save(outputPdfPath);
}

private static void AppendPdf(PdfDocument output, string path)
{
    using var input = PdfReader.Open(path, PdfDocumentOpenMode.Import);
    for (int i = 0; i < input.PageCount; i++)
        output.AddPage(input.Pages[i]);
}

}
