using System.Globalization;

namespace TopoRapportWin.ControlePrecision;

// Module manager "Contrôle classe de précision" : compare un fichier Levé à un fichier Contrôle
// (même ID de part et d'autre) et vérifie la conformité selon l'arrêté du 16 septembre 2003.
// Réimplémentation fidèle du moteur de l'outil de référence "NOVA_RAPPORT 2003_V7.html" (parsing,
// appariement, statistiques, conditions réglementaires) - voir le plan pour le détail des
// formules. Contrairement à DuplicateControlService (Contrôle de doublons), volontairement :
//   - les ID sont sensibles à la casse (l'outil de référence fait un lookup d'objet JS brut,
//     jamais de normalisation) ;
//   - Z n'est jamais "absent" : forcé à 0 si la colonne manque ou n'est pas numérique.
public static class ControlePrecisionService
{
    // Réplique exactement parsePointFile() de l'outil de référence : remplace virgule/point-virgule/
    // tabulation par un espace AVANT de découper les colonnes (donc un décimal à virgule dans un
    // fichier espace-séparé sera lui aussi cassé en 2 tokens - comportement d'origine reproduit
    // tel quel, y compris cette limite, pour garantir un résultat identique à l'outil de référence).
    // ID dupliqué au sein d'un même fichier : avertissement + dernière occurrence gagne.
    //
    // Détection automatique du format "brut" (non traité) en plus du format "traité" habituel :
    // certains fichiers de contrôle terrain ne sont pas encore réduits à ID/Xmes/Ymes/Zmes - ils
    // sortent directement d'un rattachement/comparaison théo-mesuré avec les colonnes
    // ID Xthéo Ythéo Zthéo Xmes Ymes Zmes DX DY DZ (10 colonnes, ID souvent préfixé d'une lettre
    // comme "I3365"). Auparavant l'utilisateur devait produire lui-même le fichier réduit en dehors
    // de l'appli avant de l'importer ; on détecte maintenant ce format ligne par ligne (>= 8 tokens)
    // et on n'y prend que Xmes/Ymes/Zmes (colonnes 5-7) en ignorant le théorique et les deltas, avec
    // le préfixe non numérique de l'ID retiré pour qu'il s'apparie avec le fichier Levé (IDs
    // numériques). Le format déjà traité (3-4 tokens) continue de fonctionner à l'identique.
    public static (Dictionary<string, CpPoint> Points, List<string> Warnings) ParseTxtPoints(string text)
    {
        var points = new Dictionary<string, CpPoint>();
        var warnings = new List<string>();
        if (string.IsNullOrEmpty(text)) return (points, warnings);

        var seenIds = new HashSet<string>();
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        for (int i = 0; i < lines.Length; i++)
        {
            var normalized = lines[i].Trim().Replace(',', ' ').Replace(';', ' ').Replace('\t', ' ');
            var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3 || parts[0].Length == 0) continue;

            string id;
            double x, y;
            double z = 0d;

            // Le seul comptage de tokens ne suffit pas à distinguer les 2 formats : un fichier
            // "traité" (ID X Y Z Code) dont le Code est un texte libre à plusieurs mots (ex. "Point
            // de construction 1") atteint aussi 8 tokens après normalisation, ce qui le faisait
            // passer à tort pour le format "brut" - puis échouer à parser "Point" comme Xmes et
            // rejeter silencieusement toute la ligne (point valide perdu, cassant l'appariement sur
            // des ID par ailleurs présents dans les deux fichiers). Le format brut n'est retenu que
            // si les colonnes Xmes/Ymes (indices 4-5) sont bien numériques.
            if (parts.Length >= 8 && TryParseDouble(parts[4], out x) && TryParseDouble(parts[5], out y))
            {
                // Format brut ID Xthéo Ythéo Zthéo Xmes Ymes Zmes [DX DY DZ] : on ne garde que le
                // mesuré (indices 4-6), pas le théorique ni les deltas déjà calculés.
                if (TryParseDouble(parts[6], out var zBrut)) z = zBrut;
                id = StripNonNumericIdPrefix(parts[0]);
            }
            else
            {
                if (!TryParseDouble(parts[1], out x) || !TryParseDouble(parts[2], out y)) continue;
                if (parts.Length > 3 && TryParseDouble(parts[3], out var zParsed)) z = zParsed;
                id = parts[0];
            }

            if (!seenIds.Add(id))
                warnings.Add($"ID dupliqué '{id}' (ligne {i + 1}).");

            points[id] = new CpPoint(id, x, y, z);
        }

        return (points, warnings);
    }

    // "I3365" -> "3365" ; "IHMAL.458" -> "HMAL.458" : ne retire qu'UNE seule lettre de préfixe (pas
    // une boucle jusqu'au premier chiffre), car l'ID sous-jacent peut lui-même être alphanumérique
    // (ex. "HMAL.458"). Une boucle jusqu'au premier chiffre rongeait aussi ce préfixe alphanumérique
    // ("IHMAL.458" -> "458" au lieu de "HMAL.458"), cassant l'appariement avec le fichier Contrôle
    // pour tout ID non purement numérique (bug réel constaté : 1 point apparié sur 9).
    private static string StripNonNumericIdPrefix(string rawId) =>
        rawId.Length > 1 && !char.IsDigit(rawId[0]) ? rawId[1..] : rawId;

    private static bool TryParseDouble(string s, out double value) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    // "I378" -> "378" : repli d'appariement dédié au seul préfixe de rappel/implantation terrain
    // "I" (Leica Captivate), volontairement restreint à cette lettre précise - contrairement à
    // StripNonNumericIdPrefix (retrait générique d'UNE lettre quelconque, réservé au parsing du
    // format "brut"), un retrait générique ici entrerait en collision avec de vrais ID Levé/Contrôle
    // commençant par une autre lettre (ex. "P1"/"p2"), qui doivent rester sensibles à la casse et
    // non normalisés.
    private static string StripRecallPrefix(string id) => id.Length > 1 && id[0] == 'I' ? id[1..] : id;

    // Appariement par ID exact en priorité (pas de normalisation de casse) - un point Levé sans
    // correspondance Contrôle (ou inversement) n'apparaît simplement pas dans le résultat.
    //
    // Repli : certains exports terrain (Leica Captivate, points rappelés/implantés) gardent le
    // préfixe "I" même sur le format déjà réduit ID/Xmes/Ymes/Zmes (ex. "I378"), qui n'est censé
    // être ajouté que sur le format "brut" auto-détecté par ParseTxtPoints. Résultat réel constaté :
    // un fichier préfixé "I" (Levé OU Contrôle, selon celui des 2 fichiers que l'utilisateur charge
    // dans quel champ) contre un fichier en IDs bruts ne produisait alors AUCUN appariement (rapport
    // vide, "rien ne se passe"). Le repli ne s'applique qu'après l'échec de l'appariement exact, donc
    // ne peut jamais dégrader un appariement qui fonctionnait déjà - et couvre les 2 sens (index de
    // repli construit sur les ID Contrôle eux-mêmes préfixés) pour ne pas dépendre de quel fichier
    // l'utilisateur a chargé comme Levé ou comme Contrôle.
    public static List<CpMatchedPoint> MatchPoints(
        IReadOnlyDictionary<string, CpPoint> levePoints, IReadOnlyDictionary<string, CpPoint> controlePoints)
    {
        Dictionary<string, CpPoint>? controleByStrippedId = null;
        foreach (var (cid, cp) in controlePoints)
        {
            var stripped = StripRecallPrefix(cid);
            if (stripped == cid) continue;
            controleByStrippedId ??= new Dictionary<string, CpPoint>();
            controleByStrippedId[stripped] = cp;
        }

        var matches = new List<CpMatchedPoint>();
        foreach (var (id, s) in levePoints)
        {
            if (!controlePoints.TryGetValue(id, out var c) &&
                !controlePoints.TryGetValue(StripRecallPrefix(id), out c) &&
                !(controleByStrippedId?.TryGetValue(StripRecallPrefix(id), out c) ?? false)) continue;
            double dx = s.X - c.X, dy = s.Y - c.Y, dz = s.Z - c.Z;
            double plani = Math.Sqrt(dx * dx + dy * dy);
            double alti = Math.Abs(dz);
            double threeD = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            matches.Add(new CpMatchedPoint(id, s, c, dx, dy, dz, plani, alti, threeD));
        }
        return matches;
    }

    public static CpStats CalculateStats(IReadOnlyList<double> values, double zScore)
    {
        if (values.Count == 0)
            return new CpStats(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        var sorted = values.OrderBy(v => v).ToList();
        double mean = values.Average();
        double variance = values.Count < 2 ? 0 : values.Sum(v => Math.Pow(v - mean, 2)) / (values.Count - 1);
        double stdDev = Math.Sqrt(variance);
        double median = Median(sorted);
        double mad = values.Average(v => Math.Abs(v - mean));
        double sem = stdDev / Math.Sqrt(values.Count);

        return new CpStats(
            N: values.Count,
            Min: sorted[0],
            Max: sorted[^1],
            Mean: mean,
            MeanAbs: values.Average(Math.Abs),
            Variance: variance,
            StdDev: stdDev,
            Median: median,
            Mad: mad,
            Q1: Quantile(sorted, 0.25),
            P33: Quantile(sorted, 0.33),
            P67: Quantile(sorted, 0.67),
            Q3: Quantile(sorted, 0.75),
            CiLower: mean - zScore * sem,
            CiUpper: mean + zScore * sem);
    }

    private static double Median(IReadOnlyList<double> sorted)
    {
        if (sorted.Count == 0) return 0;
        int m = sorted.Count / 2;
        return sorted.Count % 2 != 0 ? sorted[m] : (sorted[m - 1] + sorted[m]) / 2.0;
    }

    // Interpolation linéaire identique à quantile() de l'outil de référence.
    private static double Quantile(IReadOnlyList<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        double pos = (sorted.Count - 1) * p;
        int baseIdx = (int)Math.Floor(pos);
        double rest = pos - baseIdx;
        return baseIdx + 1 < sorted.Count
            ? sorted[baseIdx] + rest * (sorted[baseIdx + 1] - sorted[baseIdx])
            : sorted[baseIdx];
    }

    // N' : nombre limite d'écarts admis au-delà de T1 (table de l'annexe de l'arrêté). N<5 -> 0.
    private static int NPrimeThreshold(int n) =>
        n < 5 ? 0 : (int)Math.Floor(0.01 * n + 0.232 * Math.Sqrt(n)) + 1;

    // f = 1 + 1/(2*C²) ; S1 = baseTol*f ; T1 = k*baseTol*f ; T2 = 1.5*T1.
    // Retourne null si N == 0 (équivalent du cas "details: Aucun point." de l'outil de référence).
    public static CpConditionSet? CheckConditions(IReadOnlyList<double> devs, double baseTol, double k, int n, double c)
    {
        if (n == 0) return null;

        double f = 1 + 1.0 / (2 * c * c);
        double meanDev = devs.Average();
        double s1 = baseTol * f;
        bool c1Ok = meanDev <= s1;

        double t1 = k * baseTol * f;
        int nPrime = NPrimeThreshold(n);
        int countOverT1 = devs.Count(d => d > t1);
        bool c2Ok = countOverT1 <= nPrime;

        double t2 = 1.5 * t1;
        int countOverT2 = devs.Count(d => d > t2);
        bool c3Ok = countOverT2 == 0;

        return new CpConditionSet(
            new CpCond1(c1Ok, meanDev, s1),
            new CpCond2(c2Ok, countOverT1, t1, nPrime),
            new CpCond3(c3Ok, countOverT2, t2),
            c1Ok && c2Ok && c3Ok);
    }

    public static (double X, double Y, double Z)? Barycenter(IReadOnlyList<CpPoint> pts) =>
        pts.Count == 0 ? null : (pts.Average(p => p.X), pts.Average(p => p.Y), pts.Average(p => p.Z));

    // 24 plages en mm, identiques à histogramRanges de l'outil de référence. La dernière plage
    // (100-300) est inclusive à ses deux bornes ; les autres sont inclusives en bas, exclusives en
    // haut - un écart au-delà de 300mm n'est compté dans aucune plage (comportement de référence).
    public static readonly (double MinMm, double MaxMm, string Label)[] HistogramRanges =
    {
        (0, 2, "0-2"), (2, 4, "2-4"), (4, 6, "4-6"), (6, 8, "6-8"), (8, 10, "8-10"),
        (10, 15, "10-15"), (15, 20, "15-20"), (20, 25, "20-25"), (25, 30, "25-30"),
        (30, 35, "30-35"), (35, 40, "35-40"), (40, 45, "40-45"), (45, 50, "45-50"),
        (50, 55, "50-55"), (55, 60, "55-60"), (60, 65, "60-65"), (65, 70, "65-70"),
        (70, 75, "70-75"), (75, 80, "75-80"), (80, 85, "80-85"), (85, 90, "85-90"),
        (90, 95, "90-95"), (95, 100, "95-100"), (100, 300, "100-300"),
    };

    public static int[] CalculateHistogram(IReadOnlyList<double> devsM)
    {
        var counts = new int[HistogramRanges.Length];
        foreach (var d in devsM)
        {
            double mm = d * 1000;
            for (int i = 0; i < HistogramRanges.Length; i++)
            {
                var (min, max, _) = HistogramRanges[i];
                bool isLast = i == HistogramRanges.Length - 1;
                bool inRange = isLast ? (mm >= min && mm <= max) : (mm >= min && mm < max);
                if (inRange) { counts[i]++; break; }
            }
        }
        return counts;
    }

    // Coefficients k de l'annexe, indexés par type de contrôle (1D/2D/3D).
    public const double KAlti1D = 3.23;
    public const double KPlani2D = 2.42;
    public const double K3D = 2.11;

    public static CpAnalysisResult Analyze(
        IReadOnlyList<CpMatchedPoint> included,
        double tolXYm, double tolZm, double securityCoefficient, double zScore, string controlType)
    {
        var fDx = included.Select(m => m.DX).ToList();
        var fDy = included.Select(m => m.DY).ToList();
        var fDz = included.Select(m => m.DZ).ToList();
        var fPlani = included.Select(m => m.PlaniDev).ToList();
        var fAlti = included.Select(m => m.AltiDev).ToList();
        var f3D = included.Select(m => m.ThreeDDev).ToList();
        int n = included.Count;

        var altiConditions = CheckConditions(fAlti, tolZm, KAlti1D, n, securityCoefficient);
        var planiConditions = CheckConditions(fPlani, tolXYm, KPlani2D, n, securityCoefficient);
        var threeDConditions = CheckConditions(f3D, Math.Sqrt(tolXYm * tolXYm + tolZm * tolZm), K3D, n, securityCoefficient);

        bool overall = controlType switch
        {
            "1D" => altiConditions?.OverallOk ?? false,
            "2D" => planiConditions?.OverallOk ?? false,
            "3D" => threeDConditions?.OverallOk ?? false,
            _ => (altiConditions?.OverallOk ?? false) && (planiConditions?.OverallOk ?? false), // "2D+1D"
        };

        return new CpAnalysisResult(
            CalculateStats(fDx, zScore), CalculateStats(fDy, zScore), CalculateStats(fDz, zScore),
            CalculateStats(fPlani, zScore), CalculateStats(fAlti, zScore), CalculateStats(f3D, zScore),
            altiConditions, planiConditions, threeDConditions, overall,
            Barycenter(included.Select(m => m.Leve).ToList()),
            Barycenter(included.Select(m => m.Controle).ToList()));
    }
}
