using System.Globalization;
using System.Text;
using System.Text.Json;

namespace LicenseGen;

// IMPORTANT : ce fichier a un jumeau strictement identique dans
// src/NovaFiches/Licensing/LicensePayloadFormat.cs (l'app ne référence pas cet outil,
// pour éviter d'embarquer WinForms/WebView2 dans un simple CLI). Le format canonique
// du payload (ordre des champs, format de date) doit rester identique des deux côtés,
// sinon la signature générée ici ne validera plus côté application.
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

    public static string BuildLicenseFile(byte[] payloadUtf8, byte[] signature)
    {
        var doc = new
        {
            payload = Convert.ToBase64String(payloadUtf8),
            signature = Convert.ToBase64String(signature)
        };
        return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string EscapeJson(string value)
    {
        // Échappement minimal : les valeurs attendues (nom client, hash machine) ne
        // contiennent normalement ni guillemet ni antislash, mais on reste défensif.
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
