using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace TopoRapportWin;

/// <summary>
/// Interroge le flux WFS public de la Géoplateforme IGN (data.geopf.fr) pour récupérer les
/// repères de nivellement (NGF) présents dans une emprise géographique. Service gratuit, sans
/// clé API. Comme pour les tuiles OpenStreetMap déjà utilisées dans le module Export KMZ, cet
/// appel nécessite une connexion Internet ; en son absence il échoue simplement (timeout/erreur
/// réseau), à charge de l'appelant de traduire ça en message d'erreur pour l'UI.
/// </summary>
internal static class IgnGeodesyService
{
    private const string WfsBaseUrl = "https://data.geopf.fr/wfs";
    private const string TypeName = "IGNF_GEODESIE:rn";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public sealed record NgfBenchmark(string Id, string Nom, string? Etat, double? Altitude, double Lon, double Lat);

    public static async Task<List<NgfBenchmark>> FetchBenchmarksAsync(double minLon, double minLat, double maxLon, double maxLat)
    {
        var url = $"{WfsBaseUrl}?SERVICE=WFS&VERSION=2.0.0&REQUEST=GetFeature&TYPENAMES={TypeName}" +
                   $"&OUTPUTFORMAT=application/json&COUNT=500" +
                   $"&BBOX={F(minLon)},{F(minLat)},{F(maxLon)},{F(maxLat)},EPSG:4326";

        var json = await Http.GetStringAsync(url);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var result = new List<NgfBenchmark>();
        if (!root.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var feature in features.EnumerateArray())
        {
            if (!feature.TryGetProperty("geometry", out var geometry) ||
                !geometry.TryGetProperty("coordinates", out var coords) ||
                coords.ValueKind != JsonValueKind.Array ||
                coords.GetArrayLength() < 2)
                continue;

            double lon = coords[0].GetDouble();
            double lat = coords[1].GetDouble();

            var props = feature.TryGetProperty("properties", out var p) ? p : default;
            string id = GetString(props, "id") ?? "";
            string nom = GetString(props, "nom") ?? id;
            string? etat = GetString(props, "etat");
            double? altitude = GetNullableDouble(props, "altitude");

            result.Add(new NgfBenchmark(id, nom, etat, altitude, lon, lat));
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
        return v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : null;
    }

    private static string F(double value) => value.ToString(CultureInfo.InvariantCulture);
}
