using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace TopoRapportWin;

/// <summary>
/// Interroge le portail open data de la Ville de Paris (opendata.paris.fr, plateforme
/// OpenDataSoft) pour récupérer les repères de nivellement du "Plan de voirie" dans une emprise
/// géographique - même principe que IgnGeodesyService pour les repères NGF, mais une source
/// distincte : système altimétrique propre à Paris (NVP = Nivellement Ville de Paris, orthométrique
/// NGF-Lallemand), décalé d'environ -33 cm par rapport au NGF-IGN69 national. Chaque point renvoie
/// les deux altitudes (NVP et NGF-IGN69), pas de fiche PDF associée (contrairement aux repères IGN).
/// Gratuit, sans clé API ; comme pour les tuiles OSM/le flux IGN déjà utilisés, cet appel nécessite
/// une connexion Internet et échoue simplement en son absence.
/// </summary>
internal static class ParisNvpGeodesyService
{
    private const string ApiUrl = "https://opendata.paris.fr/api/explore/v2.1/catalog/datasets/plan-de-voirie-repere-de-nivellement/records";
    // Plafond de l'API (confirmé en direct : limit>100 renvoie une erreur "InvalidRESTParameterError").
    private const int PageSize = 100;
    // Garde-fou : une grande emprise en plein Paris peut compter plusieurs milliers de repères
    // (densité bien supérieure au réseau NGF national) - au-delà, on s'arrête plutôt que de
    // multiplier les requêtes indéfiniment ou de charger une carte illisible.
    private const int MaxRecords = 500;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public sealed record NvpBenchmark(string Numero, string Denomination, string? Typ, double? AltitudeNvp, double? AltitudeIgn69, double Lon, double Lat);

    public static async Task<List<NvpBenchmark>> FetchBenchmarksAsync(double minLon, double minLat, double maxLon, double maxLat)
    {
        var result = new List<NvpBenchmark>();
        string whereClause = $"in_bbox(geo_point_2d, {F(minLat)}, {F(minLon)}, {F(maxLat)}, {F(maxLon)})";
        int offset = 0;

        while (result.Count < MaxRecords)
        {
            var url = $"{ApiUrl}?where={Uri.EscapeDataString(whereClause)}&limit={PageSize}&offset={offset}";
            var json = await Http.GetStringAsync(url).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("results", out var records) || records.ValueKind != JsonValueKind.Array)
                break;

            int countThisPage = 0;
            foreach (var rec in records.EnumerateArray())
            {
                countThisPage++;
                if (!rec.TryGetProperty("geo_point_2d", out var geoPt) || geoPt.ValueKind != JsonValueKind.Object)
                    continue;
                double? lon = GetNullableDouble(geoPt, "lon");
                double? lat = GetNullableDouble(geoPt, "lat");
                if (lon == null || lat == null) continue;

                string numero = GetString(rec, "numero") ?? "";
                string denomination = GetString(rec, "denomination") ?? "";
                string? typ = GetString(rec, "typ");
                double? altNvp = GetNullableDouble(rec, "altitude_nvp");
                double? altIgn69 = GetNullableDouble(rec, "altitude_ign69");

                result.Add(new NvpBenchmark(numero, denomination, typ, altNvp, altIgn69, lon.Value, lat.Value));
                if (result.Count >= MaxRecords) break;
            }

            if (countThisPage < PageSize) break; // dernière page (moins de résultats que la taille de page demandée)
            offset += PageSize;
        }

        return result;
    }

    private static string? GetString(JsonElement el, string key)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static double? GetNullableDouble(JsonElement el, string key)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(key, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return d;
        if (v.ValueKind == JsonValueKind.String &&
            double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return null;
    }

    private static string F(double value) => value.ToString(CultureInfo.InvariantCulture);
}
