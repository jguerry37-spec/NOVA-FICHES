using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace TopoRapportWin;

/// <summary>
/// Vérification non bloquante de disponibilité d'une nouvelle version. Le code source du
/// dépôt principal (NOVA-FICHES) reste privé ; un second dépôt GitHub public minimal
/// (NOVA-FICHES-releases) héberge uniquement un fichier version.json, mis à jour manuellement
/// à chaque release, lu ici en anonyme. Aucun token, aucune donnée métier transmise.
/// Échec (réseau absent, timeout, JSON invalide, etc.) : toujours silencieux, jamais de
/// MessageBox — cohérent avec un usage terrain sans réseau fiable.
/// </summary>
internal static class UpdateCheck
{
    private const string VersionJsonUrl =
        "https://raw.githubusercontent.com/jguerry37-spec/NOVA-FICHES-releases/master/version.json";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

    public static async Task<(string LatestVersion, string DownloadUrl)?> CheckAsync()
    {
        try
        {
            var json = await Http.GetStringAsync(VersionJsonUrl);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var latestVersion = root.GetProperty("version").GetString();
            var downloadUrl = root.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null;

            if (string.IsNullOrWhiteSpace(latestVersion)) return null;
            if (!Version.TryParse(latestVersion, out var latest)) return null;

            var currentVersion = System.Windows.Forms.Application.ProductVersion ?? "0.0.0.0";
            if (!Version.TryParse(currentVersion, out var current)) return null;

            if (latest <= current) return null;

            return (latestVersion, downloadUrl ?? "");
        }
        catch (Exception ex)
        {
            AppLog.Info($"UpdateCheck: vérification impossible (ignorée) — {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }
}
