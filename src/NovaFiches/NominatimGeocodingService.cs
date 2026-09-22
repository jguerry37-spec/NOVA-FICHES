using System.Globalization;
using System.Text.Json;

namespace TopoRapportWin;

// Géocodage inverse (coordonnées -> adresse) via l'API publique Nominatim/OpenStreetMap, pour
// remplir automatiquement adresse/commune/département d'une fiche signalétique quand le CSV ne
// les fournit pas. Même service que celui utilisé par l'outil autonome d'origine ; portée côté
// C# ici (au lieu d'un fetch() JS) pour respecter le rythme d'appel imposé par Nominatim (voir
// MainForm.GeocodeFichesSignaletiquesAsync, qui espace les appels de 1.1s) sans bloquer le
// thread UI WinForms. Suit le même pattern que IgnGeodesyService : HttpClient statique, timeout
// court, jamais d'exception qui remonte à l'appelant (une fiche en échec ne bloque pas les autres).
public static class NominatimGeocodingService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    static NominatimGeocodingService()
    {
        // Nominatim exige un User-Agent identifiable (politique d'usage officielle) sous peine
        // de blocage de l'IP - même exigence que MapTileFetcher/StationPlanRenderer côté PDF.
        try { Http.DefaultRequestHeaders.UserAgent.ParseAdd("Nova-Fiches/GeocodingClient (+https://novatlas.fr)"); } catch { }
        try { Http.DefaultRequestHeaders.Accept.ParseAdd("application/json"); } catch { }
    }

    public sealed record GeocodeResult(string? Adresse, string? Commune, string? Departement);

    public static async Task<GeocodeResult?> ReverseGeocodeAsync(double lat, double lon)
    {
        try
        {
            var url = "https://nominatim.openstreetmap.org/reverse?format=jsonv2&addressdetails=1"
                     + "&lat=" + lat.ToString(CultureInfo.InvariantCulture)
                     + "&lon=" + lon.ToString(CultureInfo.InvariantCulture);
            var json = await Http.GetStringAsync(url).ConfigureAwait(false);
            return ParseGeocodeResponse(json);
        }
        catch
        {
            return null;
        }
    }

    // Logique de parsing/formatage isolée du fetch réseau pour être testable sans appel réseau
    // (voir NominatimGeocodingServiceTests - même discipline que le reste de l'appli : pas
    // d'appel réseau réel dans les tests unitaires).
    internal static GeocodeResult? ParseGeocodeResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var addr = root.TryGetProperty("address", out var a) && a.ValueKind == JsonValueKind.Object ? a : default;

            string? road = First(Str(addr, "road"), Str(addr, "pedestrian"), Str(addr, "footway"), Str(addr, "cycleway"), Str(addr, "path"));
            string? house = Str(addr, "house_number");
            string? streetLine = string.Join(' ', new[] { house, road }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
            string? postcode = Str(addr, "postcode");
            string? city = First(Str(addr, "city"), Str(addr, "town"), Str(addr, "village"), Str(addr, "municipality"), Str(addr, "hamlet"));
            string? county = First(Str(addr, "county"), Str(addr, "state_district"), Str(addr, "department"));
            string? name = Str(root, "name");
            string? displayName = Str(root, "display_name");

            string? adresse = !string.IsNullOrWhiteSpace(streetLine) ? streetLine
                : !string.IsNullOrWhiteSpace(name) ? name
                : !string.IsNullOrWhiteSpace(displayName) ? string.Join(", ", displayName!.Split(',').Take(2).Select(s => s.Trim()))
                : null;

            // Même logique de suffixe que l'outil d'origine (reverseGeocodeCurrentRow) : ajoute
            // code postal + commune à l'adresse si pas déjà présents, pour un rendu cohérent
            // avec les fiches déjà produites (ex. "Port de Tolbiac, 75013, Paris").
            if (!string.IsNullOrWhiteSpace(adresse))
            {
                if (!string.IsNullOrWhiteSpace(postcode) && !string.IsNullOrWhiteSpace(city) && !adresse!.Contains(postcode!, StringComparison.OrdinalIgnoreCase))
                    adresse = string.Join(", ", new[] { adresse, postcode, city });
                else if (!string.IsNullOrWhiteSpace(city) && !adresse!.Contains(city!, StringComparison.OrdinalIgnoreCase))
                    adresse = string.Join(", ", new[] { adresse, city });
            }

            if (string.IsNullOrWhiteSpace(adresse) && string.IsNullOrWhiteSpace(city) && string.IsNullOrWhiteSpace(county))
                return null;

            return new GeocodeResult(adresse, city, county);
        }
        catch
        {
            return null;
        }
    }

    private static string? Str(JsonElement el, string key) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? First(params string?[] vals) => vals.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
