using System.Globalization;
using System.Text.Json;

namespace TopoRapportWin.Licensing;

// IMPORTANT : ce fichier a un jumeau strictement identique dans tools/license-gen/
// (l'outil de génération ne référence pas ce projet, pour rester un simple CLI sans
// dépendance WinForms/WebView2). Le format canonique du payload (ordre des champs,
// format de date) doit rester identique des deux côtés, sinon une licence générée
// par l'outil ne validera plus ici.
internal static class LicensePayloadFormat
{
    private const string DateFormat = "yyyy-MM-ddTHH:mm:ssZ";

    public static string BuildCanonicalPayload(string licensedTo, DateTime issuedAtUtc, DateTime? expiresAtUtc, string? machineId)
    {
        var exp = expiresAtUtc.HasValue
            ? $"\"{expiresAtUtc.Value.ToString(DateFormat, CultureInfo.InvariantCulture)}\""
            : "null";
        var mid = machineId is null ? "null" : $"\"{EscapeJson(machineId)}\"";

        return "{\"licensedTo\":\"" + EscapeJson(licensedTo) + "\","
             + "\"issuedAtUtc\":\"" + issuedAtUtc.ToString(DateFormat, CultureInfo.InvariantCulture) + "\","
             + "\"expiresAtUtc\":" + exp + ","
             + "\"machineId\":" + mid + "}";
    }

    public static LicensePayload ParseCanonicalPayload(string canonicalJson)
    {
        using var doc = JsonDocument.Parse(canonicalJson);
        var root = doc.RootElement;

        var licensedTo = root.GetProperty("licensedTo").GetString() ?? "";
        var issuedAtUtc = DateTime.Parse(root.GetProperty("issuedAtUtc").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

        DateTime? expiresAtUtc = null;
        var expEl = root.GetProperty("expiresAtUtc");
        if (expEl.ValueKind != JsonValueKind.Null)
        {
            expiresAtUtc = DateTime.Parse(expEl.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        }

        string? machineId = null;
        var midEl = root.GetProperty("machineId");
        if (midEl.ValueKind != JsonValueKind.Null)
        {
            machineId = midEl.GetString();
        }

        return new LicensePayload(licensedTo, issuedAtUtc, expiresAtUtc, machineId);
    }

    private static string EscapeJson(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
