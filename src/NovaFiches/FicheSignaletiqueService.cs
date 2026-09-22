using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TopoRapportWin;

// Import CSV/TXT du module "Fiches signalétiques" : format à en-têtes (29 colonnes), distinct
// du format simple "Id X Y Z Code" du module KMZ (voir MainForm.ParseTxtPoints). Miroir de
// KmzExportService.cs pour la forme (record + méthode statique de parsing + rejets par ligne).
public static class FicheSignaletiqueService
{
    public sealed record FicheSignaletiqueRow(
        string? IdFiche,
        string? TypeFiche,
        string? Dossier,
        string? Chantier,
        string? Reference,
        string? Indice,
        string? DateEdition,
        string? DateDetermination,
        string? Client,
        string? Prestataire,
        string? Commune,
        string? Departement,
        string? Adresse,
        string? Site,
        string? Point,
        string? Nature,
        string? SystemePlani,
        string? SystemeAlti,
        string? EpsgSource,
        string? Ppm,
        double? X,
        double? Y,
        double? Z,
        double? Latitude,
        double? Longitude,
        double? Zoom,
        string? Observations,
        string? Photo,
        IReadOnlyList<string> RawHeaders,
        IReadOnlyDictionary<string, string> RawColumns);

    public static (List<FicheSignaletiqueRow> rows, List<(int lineNo, string reason)> rejects) ParseCsv(string text)
    {
        var rows = new List<FicheSignaletiqueRow>();
        var rejects = new List<(int, string)>();

        if (string.IsNullOrWhiteSpace(text))
            return (rows, rejects);

        var lines = text.Replace("﻿", "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        int headerLineIndex = -1;
        string firstLine = "";
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim().Length > 0) { headerLineIndex = i; firstLine = lines[i]; break; }
        }
        if (headerLineIndex < 0)
            return (rows, rejects);

        char delimiter = firstLine.Contains(';') ? ';' : firstLine.Contains('\t') ? '\t' : ',';
        var normalizedHeaders = SplitCsvLine(firstLine, delimiter).Select(NormalizeKey).ToList();

        for (int i = headerLineIndex + 1; i < lines.Length; i++)
        {
            var raw = lines[i];
            if (raw.Trim().Length == 0) continue;

            var fields = SplitCsvLine(raw, delimiter);
            var columns = new Dictionary<string, string>(StringComparer.Ordinal);
            var orderedHeaders = new List<string>();
            for (int c = 0; c < normalizedHeaders.Count; c++)
            {
                var header = normalizedHeaders[c];
                if (header.Length == 0) continue;
                var value = c < fields.Count ? fields[c].Trim() : "";
                columns[header] = value;
                orderedHeaders.Add(header);
            }

            string? Get(string key) => columns.TryGetValue(key, out var v) && v.Length > 0 ? v : null;

            var idFiche = Get("id_fiche");
            var point = Get("point");
            if (idFiche is null && point is null)
            {
                rejects.Add((i + 1, "Identifiant de fiche manquant (id_fiche/point)"));
                continue;
            }

            rows.Add(new FicheSignaletiqueRow(
                IdFiche: idFiche,
                TypeFiche: Get("type_fiche"),
                Dossier: Get("dossier"),
                Chantier: Get("chantier"),
                Reference: Get("reference"),
                Indice: Get("indice"),
                DateEdition: Get("date_edition"),
                DateDetermination: Get("date_determination"),
                Client: Get("client"),
                Prestataire: Get("prestataire"),
                Commune: Get("commune"),
                Departement: Get("departement"),
                Adresse: Get("adresse"),
                Site: Get("site"),
                Point: point,
                Nature: Get("nature"),
                SystemePlani: Get("systeme_plani"),
                SystemeAlti: Get("systeme_alti"),
                EpsgSource: Get("epsg_source"),
                Ppm: Get("ppm"),
                X: ToNumber(Get("x")),
                Y: ToNumber(Get("y")),
                Z: ToNumber(Get("z")),
                Latitude: ToNumber(Get("latitude")),
                Longitude: ToNumber(Get("longitude")),
                Zoom: ToNumber(Get("zoom")),
                Observations: Get("observations"),
                Photo: Get("photo"),
                RawHeaders: orderedHeaders,
                RawColumns: columns));
        }

        return (rows, rejects);
    }

    // Colonnes "revision_1", "revision_2", ... (nombre non borné) : chacune au format
    // "indice|date|description|auteur". Parcourt RawHeaders (ordre d'origine du CSV) plutôt que
    // RawColumns directement, pour un ordre de sortie déterministe indépendant de l'implémentation
    // d'énumération de Dictionary<>.
    public static List<(string Indice, string Date, string Description, string Auteur)> CollectRevisions(FicheSignaletiqueRow row)
    {
        var revisions = new List<(string, string, string, string)>();
        foreach (var header in row.RawHeaders)
        {
            if (!header.StartsWith("revision_", StringComparison.Ordinal)) continue;
            if (!row.RawColumns.TryGetValue(header, out var raw) || string.IsNullOrWhiteSpace(raw)) continue;

            var parts = raw.Split('|').Select(p => p.Trim()).ToArray();
            string indice = parts.Length > 0 ? parts[0] : "";
            string date = parts.Length > 1 ? parts[1] : "";
            string description = parts.Length > 2 ? parts[2] : "";
            string auteur = parts.Length > 3 ? parts[3] : "";
            revisions.Add((indice, date, description, auteur));
        }
        return revisions;
    }

    // Minuscule, accents supprimés, tout non-alphanumérique -> "_" : même normalisation que
    // l'outil autonome d'origine (normalizeKey), pour accepter les en-têtes Excel accentués /
    // en casse variable sans configuration.
    private static string NormalizeKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        var decomposed = key.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }
        return Regex.Replace(sb.ToString(), "[^a-z0-9]+", "_").Trim('_');
    }

    private static double? ToNumber(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var normalized = raw.Replace(" ", "").Replace(",", ".");
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
            ? value
            : null;
    }

    // Tokenizer CSV minimal : gère les champs entre guillemets (délimiteur/retours à la ligne
    // encodés dans le champ, échappement "" -> ") sur UNE seule ligne physique. Ne gère pas les
    // champs contenant un vrai saut de ligne au milieu d'une cellule (RFC4180 complet) - cas rare
    // pour ce type de fichier, non nécessaire pour cette passe.
    private static List<string> SplitCsvLine(string line, char delimiter)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else
            {
                if (c == '"' && sb.Length == 0) inQuotes = true;
                else if (c == delimiter) { fields.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
        }
        fields.Add(sb.ToString());
        return fields;
    }
}
