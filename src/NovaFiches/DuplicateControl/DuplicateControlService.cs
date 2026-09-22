using System.Globalization;
using System.Text;

namespace TopoRapportWin.DuplicateControl;

// Module manager "Contrôle de doublons" : fusionne des fichiers de points TXT (format déjà
// utilisé par Échanges/KMZ - ID/X/Y/Z/Code tabulé) contre un fichier de contrôle unique, détecte
// les n° de points présents à la fois dans les entrées et le contrôle, et laisse l'utilisateur
// résoudre chaque conflit (moyenne d'une sélection, ou suppression) avant export. Parseur dédié
// (pas de dépendance à MainForm.ParseTxtPoints, qui est privé et sert Échanges/KMZ) - même
// tolérance de format, testable sur chaînes.
public static class DuplicateControlService
{
    public static (List<DcPoint> Points, List<(int LineNo, string Reason)> Rejects) ParseTxtPoints(
        string text, string sourceFile, string role)
    {
        var points = new List<DcPoint>();
        var rejects = new List<(int, string)>();
        if (string.IsNullOrWhiteSpace(text)) return (points, rejects);

        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        bool headerSkipped = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var raw = lines[i].Trim();
            if (raw.Length == 0) continue;
            if (raw.StartsWith("#") || raw.StartsWith("//")) continue;

            var norm = raw.Replace(';', ' ').Replace(',', '.');
            var parts = SplitByWhitespace(norm);

            if (!headerSkipped)
            {
                headerSkipped = true;
                // Colonne EXACTEMENT "X"/"Y"/"Z"/"N" (pas une simple sous-chaîne n'importe où dans
                // la ligne) : bug réel constaté lors d'un audit - l'ancienne version
                // (raw.Contains('X') etc. sur la ligne brute entière) déclenchait à tort sur un ID
                // de point alphanumérique comme "PTX12" ou "BORNE-N2" et faisait disparaître
                // silencieusement le tout premier point du fichier, sans rejet ni avertissement.
                // Comparer les colonnes une par une (et pas juste chercher les lettres dans la
                // ligne) préserve aussi le cas d'une ligne de données malformée unique (sans vrai
                // en-tête) : elle continue d'être rejetée avec un motif plutôt qu'avalée en silence.
                if (raw.IndexOf('°') >= 0 || parts.Any(p =>
                        p.Equals("X", StringComparison.OrdinalIgnoreCase) ||
                        p.Equals("Y", StringComparison.OrdinalIgnoreCase) ||
                        p.Equals("Z", StringComparison.OrdinalIgnoreCase) ||
                        p.Equals("N", StringComparison.OrdinalIgnoreCase)))
                    continue;
            }

            if (parts.Count < 3)
            {
                rejects.Add((i + 1, "Colonnes insuffisantes"));
                continue;
            }

            string id = parts[0].Trim();
            if (string.IsNullOrWhiteSpace(id))
            {
                rejects.Add((i + 1, "Id point vide"));
                continue;
            }

            if (!TryParseDouble(parts[1], out double x) || !TryParseDouble(parts[2], out double y))
            {
                rejects.Add((i + 1, "X/Y non numériques"));
                continue;
            }

            double z = 0d;
            bool hasZ = parts.Count >= 4 && TryParseDouble(parts[3], out z);

            string? code = null;
            int codeIndex = hasZ ? 4 : 3;
            if (parts.Count > codeIndex) code = parts[codeIndex].Trim();

            points.Add(new DcPoint(id, x, y, z, hasZ, string.IsNullOrWhiteSpace(code) ? null : code, sourceFile, role));
        }

        return (points, rejects);
    }

    private static List<string> SplitByWhitespace(string s) =>
        s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();

    private static bool TryParseDouble(string s, out double value) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static string NormalizeId(string id) => id.Trim().ToUpperInvariant();

    // Regroupe par ID normalisé (trim + casse insensible). Un ID présent à la fois dans le
    // contrôle et dans au moins une entrée devient un DcDuplicateGroup (toutes ses occurrences,
    // y compris si plusieurs fichiers d'entrée partagent ce même ID). Sinon : orphelin d'un
    // côté ou de l'autre. Un même ID présent dans 2 fichiers d'entrée sans être dans le
    // contrôle n'est PAS regroupé (limite V1 actée avec l'utilisateur) - chaque occurrence
    // atterrit telle quelle dans OrphanInputs.
    public static DcAnalysisResult Analyze(IReadOnlyList<DcPoint> inputPoints, IReadOnlyList<DcPoint> controlPoints)
    {
        var controlByNormId = new Dictionary<string, List<DcPoint>>();
        foreach (var p in controlPoints)
        {
            var key = NormalizeId(p.Id);
            if (!controlByNormId.TryGetValue(key, out var list)) controlByNormId[key] = list = new List<DcPoint>();
            list.Add(p);
        }

        var inputsByNormId = new Dictionary<string, List<DcPoint>>();
        foreach (var p in inputPoints)
        {
            var key = NormalizeId(p.Id);
            if (!inputsByNormId.TryGetValue(key, out var list)) inputsByNormId[key] = list = new List<DcPoint>();
            list.Add(p);
        }

        var duplicates = new List<DcDuplicateGroup>();
        var orphanInputs = new List<DcPoint>();
        var orphanControls = new List<DcPoint>();

        foreach (var (key, inputs) in inputsByNormId)
        {
            if (controlByNormId.TryGetValue(key, out var controls))
            {
                var occurrences = new List<DcPoint>(controls);
                occurrences.AddRange(inputs);
                duplicates.Add(new DcDuplicateGroup(inputs[0].Id, occurrences));
            }
            else
            {
                orphanInputs.AddRange(inputs);
            }
        }

        foreach (var (key, controls) in controlByNormId)
        {
            if (!inputsByNormId.ContainsKey(key))
                orphanControls.AddRange(controls);
        }

        return new DcAnalysisResult(duplicates, orphanInputs, orphanControls);
    }

    // Moyenne simple X/Y ; Z moyenné uniquement sur les occurrences qui en ont un (null sinon).
    public static (double X, double Y, double? Z) Average(IReadOnlyList<DcPoint> selected)
    {
        if (selected.Count == 0) throw new ArgumentException("Aucune occurrence sélectionnée.", nameof(selected));
        double x = selected.Average(p => p.X);
        double y = selected.Average(p => p.Y);
        var withZ = selected.Where(p => p.HasZ).ToList();
        double? z = withZ.Count > 0 ? withZ.Average(p => p.Z) : null;
        return (x, y, z);
    }

    // Ré-écrit au même format que l'entrée (ID/X/Y/Z/Code, tabulation), sans ligne d'en-tête
    // pour rester ré-importable tel quel par ce module ou par Échanges/KMZ.
    public static string FormatOutputTxt(IReadOnlyList<DcPoint> finalPoints)
    {
        var sb = new StringBuilder();
        foreach (var p in finalPoints)
        {
            sb.Append(p.Id).Append('\t')
              .Append(p.X.ToString("F3", CultureInfo.InvariantCulture)).Append('\t')
              .Append(p.Y.ToString("F3", CultureInfo.InvariantCulture));
            if (p.HasZ) sb.Append('\t').Append(p.Z.ToString("F3", CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(p.Code)) sb.Append('\t').Append(p.Code);
            sb.Append('\n');
        }
        return sb.ToString();
    }

    public static string FormatReport(
        DcAnalysisResult analysis,
        IReadOnlyList<(string Id, string Action, string Detail)> resolutions,
        int inputPointCount,
        int controlPointCount,
        int inputFileCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine("RAPPORT DE CONTRÔLE DE DOUBLONS");
        sb.AppendLine($"Généré le {DateTime.Now:dd/MM/yyyy HH:mm}");
        sb.AppendLine();
        sb.AppendLine($"Fichiers d'entrée : {inputFileCount} ({inputPointCount} points)");
        sb.AppendLine($"Fichier de contrôle : {controlPointCount} points");
        sb.AppendLine($"Doublons résolus : {resolutions.Count}");
        sb.AppendLine($"Points en entrée sans correspondance au contrôle : {analysis.OrphanInputs.Count}");
        sb.AppendLine($"Points au contrôle sans correspondance en entrée : {analysis.OrphanControls.Count}");
        sb.AppendLine();

        if (resolutions.Count > 0)
        {
            sb.AppendLine("--- Doublons résolus ---");
            foreach (var r in resolutions)
                sb.AppendLine($"  {r.Id} : {r.Action} - {r.Detail}");
            sb.AppendLine();
        }

        if (analysis.OrphanInputs.Count > 0)
        {
            sb.AppendLine("--- Points en entrée sans correspondance au contrôle ---");
            foreach (var p in analysis.OrphanInputs)
                sb.AppendLine($"  {p.Id} (source : {p.SourceFile})");
            sb.AppendLine();
        }

        if (analysis.OrphanControls.Count > 0)
        {
            sb.AppendLine("--- Points au contrôle sans correspondance en entrée ---");
            foreach (var p in analysis.OrphanControls)
                sb.AppendLine($"  {p.Id}");
        }

        return sb.ToString();
    }
}
