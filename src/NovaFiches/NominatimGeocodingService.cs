using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

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

    public sealed record ForwardGeocodeResult(double Lat, double Lon, string DisplayName);

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

    // Géocodage direct (adresse texte -> coordonnées), pour le bouton "Rechercher une adresse"
    // de l'export KMZ : permet de localiser une zone à interroger (repères NGF/NVP) sans avoir
    // besoin d'importer un fichier de points au préalable. "countrycodes=fr" biaise les résultats
    // vers la France (usage quasi-exclusif de l'appli) sans empêcher une recherche hors France si
    // vraiment aucun résultat français ne correspond (Nominatim ignore alors ce filtre que si on
    // ne le passe pas du tout - donc pas de repli automatique ici, l'utilisateur peut préciser un
    // pays dans sa saisie si besoin).
    public static async Task<ForwardGeocodeResult?> ForwardGeocodeAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;

        var result = await ForwardGeocodeRawAsync(query).ConfigureAwait(false);
        if (result is not null) return result;

        // Repli : Nominatim ne comprend pas le raccourci francais "Paris 01"/"Lyon 3"/"Marseille
        // 8e" (son parseur attend soit un nom de quartier reel, soit un code postal) - une saisie
        // frequente sur ces 3 villes a arrondissements. On la retraduit en "<code postal> <ville>"
        // (ex. "75001 Paris"), format que Nominatim resout de maniere fiable, et on retente une
        // seule fois. Ne declenche jamais de requete reseau supplementaire si la saisie ne
        // correspond pas a ce pattern precis (TryNormalizeArrondissementQuery renvoie null).
        var normalized = TryNormalizeArrondissementQuery(query);
        if (normalized is null) return null;
        return await ForwardGeocodeRawAsync(normalized).ConfigureAwait(false);
    }

    // "countrycodes=fr" biaise les résultats vers la France (usage quasi-exclusif de l'appli)
    // sans empêcher une recherche hors France si vraiment aucun résultat français ne correspond
    // (Nominatim ignore alors ce filtre que si on ne le passe pas du tout - donc pas de repli
    // automatique ici, l'utilisateur peut préciser un pays dans sa saisie si besoin).
    private static async Task<ForwardGeocodeResult?> ForwardGeocodeRawAsync(string query)
    {
        try
        {
            var url = "https://nominatim.openstreetmap.org/search?format=jsonv2&limit=1&countrycodes=fr"
                     + "&q=" + Uri.EscapeDataString(query);
            var json = await Http.GetStringAsync(url).ConfigureAwait(false);
            return ParseForwardGeocodeResponse(json);
        }
        catch
        {
            return null;
        }
    }

    private static readonly Regex ArrondissementPattern = new(
        @"^(Paris|Lyon|Marseille)\s*0*(\d{1,2})\s*(?:e|er|ère|ème|eme|ieme|ième)?\s*(?:arrondissement)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Interne pour être testable sans appel réseau (même discipline que ParseForwardGeocodeResponse).
    internal static string? TryNormalizeArrondissementQuery(string query)
    {
        var match = ArrondissementPattern.Match(query.Trim());
        if (!match.Success) return null;
        string city = match.Groups[1].Value;
        if (!int.TryParse(match.Groups[2].Value, out var arr) || arr < 1 || arr > 20) return null;

        string prefix = city.Equals("Paris", StringComparison.OrdinalIgnoreCase) ? "750"
                      : city.Equals("Lyon", StringComparison.OrdinalIgnoreCase) ? "690"
                      : "130"; // Marseille
        return $"{prefix}{arr:00} {city}";
    }

    internal static ForwardGeocodeResult? ParseForwardGeocodeResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                return null;
            var first = doc.RootElement[0];
            string? latStr = Str(first, "lat");
            string? lonStr = Str(first, "lon");
            if (latStr is null || lonStr is null) return null;
            if (!double.TryParse(latStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)) return null;
            if (!double.TryParse(lonStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)) return null;
            string displayName = Str(first, "display_name")
                ?? $"{lat.ToString(CultureInfo.InvariantCulture)}, {lon.ToString(CultureInfo.InvariantCulture)}";
            return new ForwardGeocodeResult(lat, lon, displayName);
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
