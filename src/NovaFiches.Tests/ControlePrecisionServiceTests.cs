using TopoRapportWin.ControlePrecision;
using Xunit;

namespace NovaFiches.Tests;

public class ControlePrecisionServiceTests
{
    [Fact]
    public void ParseTxtPoints_ParsesSpaceSeparatedRows()
    {
        var (points, warnings) = ControlePrecisionService.ParseTxtPoints("P1 100.000 200.000 10.000\nP2 101.000 201.000 11.000");

        Assert.Empty(warnings);
        Assert.Equal(2, points.Count);
        Assert.Equal(100.000, points["P1"].X);
        Assert.Equal(200.000, points["P1"].Y);
        Assert.Equal(10.000, points["P1"].Z);
    }

    [Fact]
    public void ParseTxtPoints_ZMissing_DefaultsToZero()
    {
        var (points, _) = ControlePrecisionService.ParseTxtPoints("P1 100.000 200.000");

        Assert.Equal(0, points["P1"].Z);
    }

    [Fact]
    public void ParseTxtPoints_ZNonNumeric_DefaultsToZero()
    {
        var (points, _) = ControlePrecisionService.ParseTxtPoints("P1 100.000 200.000 ABC");

        Assert.Equal(0, points["P1"].Z);
    }

    [Fact]
    public void ParseTxtPoints_IsCaseSensitive()
    {
        // Contrairement à Contrôle de doublons, "p1" et "P1" sont ici 2 points distincts -
        // comportement fidèle à l'outil de référence (lookup d'objet JS brut).
        var (points, _) = ControlePrecisionService.ParseTxtPoints("p1 100 200 10\nP1 999 999 999");

        Assert.Equal(2, points.Count);
        Assert.True(points.ContainsKey("p1"));
        Assert.True(points.ContainsKey("P1"));
    }

    [Fact]
    public void ParseTxtPoints_DuplicateId_WarnsAndLastOccurrenceWins()
    {
        var (points, warnings) = ControlePrecisionService.ParseTxtPoints("P1 100 200 10\nP1 999 999 999");

        var warning = Assert.Single(warnings);
        Assert.Contains("P1", warning);
        Assert.Equal(999, points["P1"].X);
    }

    [Fact]
    public void ParseTxtPoints_CommaDecimal_SplitsIntoExtraTokens_LikeReferenceTool()
    {
        // Reproduction fidèle (bug inclus) de l'outil de référence : la virgule est remplacée par
        // une espace AVANT le découpage en colonnes, donc "100,500" devient 2 tokens ("100" "500")
        // et non un nombre à virgule - un fichier à décimales virgule et colonnes espace n'est
        // donc PAS interprété comme prévu par l'UI d'origine. On verrouille ce comportement pour
        // garantir un résultat identique à l'outil de référence plutôt que de le "corriger".
        var (points, _) = ControlePrecisionService.ParseTxtPoints("P1 100,500 200,250 10,000");

        // "P1 100,500 200,250 10,000" -> après remplacement des virgules par des espaces :
        // "P1 100 500 200 250 10 000" -> X=100 (2e token), Y=500 (3e token), et non 100.5/200.25.
        Assert.Equal(100, points["P1"].X);
        Assert.Equal(500, points["P1"].Y);
    }

    [Fact]
    public void ParseTxtPoints_SkipsNonNumericHeaderLine()
    {
        var (points, _) = ControlePrecisionService.ParseTxtPoints("ID X Y Z\nP1 100 200 10");

        var p = Assert.Single(points.Values);
        Assert.Equal("P1", p.Id);
    }

    [Fact]
    public void ParseTxtPoints_RawTheoMesFormat_UsesMesColumnsAndStripsIdPrefix()
    {
        // Format "brut" non traité (ID Xthéo Ythéo Zthéo Xmes Ymes Zmes DX DY DZ) tel que produit
        // directement par un rattachement terrain, sans réduction manuelle externe préalable -
        // seules les colonnes Xmes/Ymes/Zmes (indices 4-6) doivent être retenues, pas le théorique
        // ni les deltas, et le préfixe non numérique de l'ID ("I") doit être retiré pour s'apparier
        // avec le fichier Levé (IDs numériques).
        var line = "I3365 1707815.5290 9279967.0650 51.0160 1707815.5635 9279967.0603 51.0082 -0.0345 0.0047 0.0078";
        var (points, warnings) = ControlePrecisionService.ParseTxtPoints(line);

        Assert.Empty(warnings);
        var p = Assert.Single(points.Values);
        Assert.Equal("3365", p.Id);
        Assert.Equal(1707815.5635, p.X);
        Assert.Equal(9279967.0603, p.Y);
        Assert.Equal(51.0082, p.Z);
    }

    [Fact]
    public void ParseTxtPoints_RawTheoMesFormat_SkipsHeaderLine()
    {
        var text = "N° Xthéo Ythéo Zthéo Xmes Ymes Zmes DX DY DZ\n" +
                   "I3365 1707815.5290 9279967.0650 51.0160 1707815.5635 9279967.0603 51.0082 -0.0345 0.0047 0.0078";
        var (points, _) = ControlePrecisionService.ParseTxtPoints(text);

        var p = Assert.Single(points.Values);
        Assert.Equal("3365", p.Id);
    }

    [Fact]
    public void ParseTxtPoints_RawTheoMesFormat_AlphanumericIdPrefix_OnlyStripsTheSingleLetter()
    {
        // Bug réel constaté sur chantier : IDs de type "IHMAL.458" (préfixe "I" + ID alphanumérique
        // sous-jacent "HMAL.458", pas purement numérique). L'ancienne implémentation bouclait
        // jusqu'au premier CHIFFRE du champ, rongeant "HMAL." au passage ("IHMAL.458" -> "458" au
        // lieu de "HMAL.458"), ce qui cassait l'appariement avec le fichier Contrôle (résultat :
        // 1 point apparié sur 9 attendus). Seule la lettre de préfixe doit être retirée.
        var line = "IHMAL.458 1702767.1240 9270695.6820 23.7680 1702767.1192 9270695.6977 23.7742 0.0048 -0.0157 -0.0062";
        var (points, _) = ControlePrecisionService.ParseTxtPoints(line);

        var p = Assert.Single(points.Values);
        Assert.Equal("HMAL.458", p.Id);
    }

    [Fact]
    public void ParseTxtPoints_RawTheoMesFormat_IdWithoutPrefix_UnchangedAndMatchesTraiteFormat()
    {
        // Un ID déjà purement numérique en format brut (pas de lettre préfixe) doit rester
        // identique - et donner exactement le même point que le format "traité" équivalent, pour
        // que Levé et Contrôle continuent de s'apparier quel que soit le format d'origine.
        var raw = "3365 1707815.5290 9279967.0650 51.0160 1707815.5635 9279967.0603 51.0082 -0.0345 0.0047 0.0078";
        var traite = "3365 1707815.5635 9279967.0603 51.0082";
        var (rawPoints, _) = ControlePrecisionService.ParseTxtPoints(raw);
        var (traitePoints, _) = ControlePrecisionService.ParseTxtPoints(traite);

        Assert.Equal(traitePoints["3365"].X, rawPoints["3365"].X);
        Assert.Equal(traitePoints["3365"].Y, rawPoints["3365"].Y);
        Assert.Equal(traitePoints["3365"].Z, rawPoints["3365"].Z);
    }

    [Fact]
    public void MatchPoints_MatchesOnlyCommonIds_CaseSensitive()
    {
        var (leve, _) = ControlePrecisionService.ParseTxtPoints("P1 100 200 10\np2 50 60 5");
        var (controle, _) = ControlePrecisionService.ParseTxtPoints("P1 100.1 200.1 10.1\nP2 50 60 5");

        var matches = ControlePrecisionService.MatchPoints(leve, controle);

        var m = Assert.Single(matches);
        Assert.Equal("P1", m.Id);
    }

    [Fact]
    public void MatchPoints_ReducedFormat_LeveIdKeepsRecallPrefix_FallsBackToStrippedId()
    {
        // Bug réel constaté sur chantier : fichier Levé au format déjà réduit (ID Xmes Ymes Zmes,
        // 4 tokens) exporté avec le préfixe "I" de rappel/implantation conservé sur chaque ID
        // ("I378"), face à un fichier Contrôle en IDs bruts ("378"). Comme ce format ne passe pas
        // par la détection "brut" de ParseTxtPoints (qui retire ce préfixe), l'appariement exact
        // échouait pour la totalité des points (résultat : 0 point apparié, rapport vide - "rien ne
        // se passe" côté utilisateur). Le repli de MatchPoints doit rattraper ce cas.
        var (leve, _) = ControlePrecisionService.ParseTxtPoints("I378 1713884.8046 9276791.1052 24.0967");
        var (controle, _) = ControlePrecisionService.ParseTxtPoints("378 1713884.776 9276791.122 24.133");

        var m = Assert.Single(ControlePrecisionService.MatchPoints(leve, controle));

        Assert.Equal("I378", m.Id);
        Assert.Equal(1713884.8046, m.Leve.X);
        Assert.Equal(1713884.776, m.Controle.X);
    }

    [Fact]
    public void MatchPoints_ReducedFormat_ControleIdKeepsRecallPrefix_FallsBackToStrippedId()
    {
        // Sens inverse du bug précédent : cette fois c'est le fichier chargé comme CONTRÔLE qui
        // garde le préfixe "I" ("I378") et le Levé qui est en IDs bruts ("378") - cas réel constaté
        // quand l'utilisateur charge le fichier terrain dans le champ "Contrôle" et le fichier de
        // référence CC50 dans le champ "Levé" (l'inverse de l'autre test). Sans repli symétrique,
        // aucun appariement ne se produisait dans ce sens non plus.
        var (leve, _) = ControlePrecisionService.ParseTxtPoints("378 1713884.776 9276791.122 24.133");
        var (controle, _) = ControlePrecisionService.ParseTxtPoints("I378 1713884.8046 9276791.1052 24.0967");

        var m = Assert.Single(ControlePrecisionService.MatchPoints(leve, controle));

        Assert.Equal("378", m.Id);
        Assert.Equal(1713884.776, m.Leve.X);
        Assert.Equal(1713884.8046, m.Controle.X);
    }

    [Fact]
    public void MatchPoints_ComputesDeviationsCorrectly()
    {
        var leve = new Dictionary<string, CpPoint> { ["P1"] = new("P1", 103, 204, 13) };
        var controle = new Dictionary<string, CpPoint> { ["P1"] = new("P1", 100, 200, 10) };

        var m = Assert.Single(ControlePrecisionService.MatchPoints(leve, controle));

        Assert.Equal(3, m.DX);
        Assert.Equal(4, m.DY);
        Assert.Equal(3, m.DZ);
        Assert.Equal(5, m.PlaniDev, 3); // sqrt(3^2+4^2) = 5
        Assert.Equal(3, m.AltiDev);
        Assert.Equal(Math.Sqrt(3 * 3 + 4 * 4 + 3 * 3), m.ThreeDDev, 3);
    }

    [Fact]
    public void CalculateStats_ComputesMeanMedianStdDev()
    {
        var stats = ControlePrecisionService.CalculateStats(new List<double> { 1, 2, 3, 4, 5 }, 1.96);

        Assert.Equal(5, stats.N);
        Assert.Equal(3, stats.Mean);
        Assert.Equal(3, stats.Median);
        Assert.Equal(1, stats.Min);
        Assert.Equal(5, stats.Max);
        Assert.Equal(Math.Sqrt(2.5), stats.StdDev, 6); // variance (n-1) = 2.5
    }

    [Fact]
    public void CalculateStats_EmptyArray_ReturnsZeroedStats()
    {
        var stats = ControlePrecisionService.CalculateStats(new List<double>(), 1.96);

        Assert.Equal(0, stats.N);
        Assert.Equal(0, stats.Mean);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(4, 0)]
    [InlineData(5, 1)]
    [InlineData(13, 1)]
    [InlineData(14, 2)]
    [InlineData(44, 2)]
    [InlineData(45, 3)]
    [InlineData(85, 3)]
    [InlineData(86, 4)]
    [InlineData(132, 4)]
    [InlineData(133, 5)]
    [InlineData(184, 5)]
    [InlineData(185, 6)]
    [InlineData(240, 6)]
    [InlineData(241, 7)]
    [InlineData(298, 7)]
    [InlineData(299, 8)]
    [InlineData(359, 8)]
    [InlineData(360, 9)]
    [InlineData(422, 9)]
    [InlineData(423, 10)]
    [InlineData(487, 10)]
    public void CheckConditions_NPrimeThreshold_MatchesAnnexTable(int n, int expectedNPrime)
    {
        // Un seul écart nul pour isoler le seuil N' (Cond1/Cond3 toujours OK dans ce cas).
        var devs = Enumerable.Repeat(0.0, n).ToList();

        var result = ControlePrecisionService.CheckConditions(devs, baseTol: 0.04, k: 2.42, n: n, c: 2);

        Assert.NotNull(result);
        Assert.Equal(expectedNPrime, result!.Cond2.NPrimeMax);
    }

    [Fact]
    public void CheckConditions_ZeroPoints_ReturnsNull()
    {
        var result = ControlePrecisionService.CheckConditions(new List<double>(), 0.04, 2.42, 0, 2);

        Assert.Null(result);
    }

    [Fact]
    public void CheckConditions_AllDeviationsWithinTolerance_IsConforme()
    {
        // 10 écarts très faibles (1mm), tolérance 4cm -> largement conforme sur les 3 conditions.
        var devs = Enumerable.Repeat(0.001, 10).ToList();

        var result = ControlePrecisionService.CheckConditions(devs, baseTol: 0.04, k: 2.42, n: 10, c: 2);

        Assert.NotNull(result);
        Assert.True(result!.Cond1.Ok);
        Assert.True(result.Cond2.Ok);
        Assert.True(result.Cond3.Ok);
        Assert.True(result.OverallOk);
    }

    [Fact]
    public void CheckConditions_OneOutlierBeyondT2_FailsCond3()
    {
        var devs = new List<double> { 0.001, 0.001, 0.001, 0.001, 0.001, 0.001, 0.001, 0.001, 0.001, 1.0 };

        var result = ControlePrecisionService.CheckConditions(devs, baseTol: 0.04, k: 2.42, n: 10, c: 2);

        Assert.NotNull(result);
        Assert.False(result!.Cond3.Ok);
        Assert.False(result.OverallOk);
    }

    [Fact]
    public void CalculateHistogram_BucketsByMillimeterRange()
    {
        // 1mm -> "0-2", 3mm -> "2-4", 300mm -> dernière plage (bornée aux 2 extrémités).
        var counts = ControlePrecisionService.CalculateHistogram(new List<double> { 0.001, 0.003, 0.300 });

        Assert.Equal(1, counts[0]);  // 0-2
        Assert.Equal(1, counts[1]);  // 2-4
        Assert.Equal(1, counts[^1]); // 100-300
    }

    [Fact]
    public void CalculateHistogram_LowerBoundInclusive_UpperBoundExclusive()
    {
        // Exactement 2mm doit tomber dans "2-4", pas "0-2" (borne basse incluse, borne haute exclue).
        var counts = ControlePrecisionService.CalculateHistogram(new List<double> { 0.002 });

        Assert.Equal(0, counts[0]);
        Assert.Equal(1, counts[1]);
    }

    [Fact]
    public void CalculateHistogram_LastRangeInclusiveOfBothBounds()
    {
        var counts = ControlePrecisionService.CalculateHistogram(new List<double> { 0.100, 0.300 });

        Assert.Equal(2, counts[^1]);
    }

    [Fact]
    public void CalculateHistogram_BeyondLastRange_CountedNowhere()
    {
        var counts = ControlePrecisionService.CalculateHistogram(new List<double> { 0.301 });

        Assert.Equal(0, counts.Sum());
    }

    [Fact]
    public void Analyze_CombinedControlType_RequiresBothAltiAndPlaniConforme()
    {
        var included = new List<CpMatchedPoint>();
        for (int i = 0; i < 10; i++)
        {
            var leve = new CpPoint($"P{i}", i, i, i);
            var controle = new CpPoint($"P{i}", i + 0.001, i + 0.001, i + 0.001);
            included.Add(new CpMatchedPoint($"P{i}", leve, controle, 0.001, 0.001, 0.001, 0.0014, 0.001, 0.0017));
        }

        var result = ControlePrecisionService.Analyze(included, tolXYm: 0.04, tolZm: 0.04, securityCoefficient: 2, zScore: 1.96, controlType: "2D+1D");

        Assert.True(result.OverallConformity);
        Assert.NotNull(result.AltiConditions);
        Assert.NotNull(result.PlaniConditions);
        Assert.True(result.AltiConditions!.OverallOk);
        Assert.True(result.PlaniConditions!.OverallOk);
    }

    [Fact]
    public void Analyze_Barycenters_AverageIncludedPointsOnly()
    {
        var leve1 = new CpPoint("P1", 0, 0, 0);
        var controle1 = new CpPoint("P1", 10, 10, 10);
        var leve2 = new CpPoint("P2", 4, 4, 4);
        var controle2 = new CpPoint("P2", 14, 14, 14);
        var included = new List<CpMatchedPoint>
        {
            new("P1", leve1, controle1, -10, -10, -10, Math.Sqrt(200), 10, Math.Sqrt(300)),
            new("P2", leve2, controle2, -10, -10, -10, Math.Sqrt(200), 10, Math.Sqrt(300)),
        };

        var result = ControlePrecisionService.Analyze(included, 0.04, 0.04, 2, 1.96, "2D+1D");

        Assert.Equal((2, 2, 2), result.BarycentreLeve);
        Assert.Equal((12, 12, 12), result.BarycentreControle);
    }
}
