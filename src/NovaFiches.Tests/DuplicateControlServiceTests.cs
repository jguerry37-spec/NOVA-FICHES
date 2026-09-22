using TopoRapportWin.DuplicateControl;
using Xunit;

namespace NovaFiches.Tests;

public class DuplicateControlServiceTests
{
    [Fact]
    public void ParseTxtPoints_ParsesTabSeparatedRows()
    {
        var text = "P1\t100.000\t200.000\t10.000\tPT\nP2\t101.000\t201.000\t11.000\tPT";

        var (points, rejects) = DuplicateControlService.ParseTxtPoints(text, "fichier1.txt", "input");

        Assert.Empty(rejects);
        Assert.Equal(2, points.Count);
        Assert.Equal("P1", points[0].Id);
        Assert.Equal(100.000, points[0].X);
        Assert.Equal(200.000, points[0].Y);
        Assert.Equal(10.000, points[0].Z);
        Assert.True(points[0].HasZ);
        Assert.Equal("PT", points[0].Code);
        Assert.Equal("fichier1.txt", points[0].SourceFile);
        Assert.Equal("input", points[0].Role);
    }

    [Fact]
    public void ParseTxtPoints_SkipsHeaderAndComments()
    {
        var text = "ID\tX\tY\tZ\n# commentaire\n// autre commentaire\nP1\t100\t200\t10";

        var (points, rejects) = DuplicateControlService.ParseTxtPoints(text, "f.txt", "input");

        Assert.Empty(rejects);
        var p = Assert.Single(points);
        Assert.Equal("P1", p.Id);
    }

    [Fact]
    public void ParseTxtPoints_FirstPointIdContainingHeaderLetter_IsNotMistakenForHeader()
    {
        // Bug réel constaté (audit) : l'ancienne détection d'en-tête cherchait les lettres
        // X/Y/Z/N n'importe où dans la ligne brute - un ID alphanumérique comme "PTX12" (contient
        // "X") ou "BORNE-N2" (contient "N") sur la toute première ligne du fichier la faisait
        // passer à tort pour un en-tête, et ce tout premier point disparaissait silencieusement,
        // sans rejet ni avertissement.
        var text = "PTX12\t100\t200\nBORNE-N2\t101\t201";

        var (points, rejects) = DuplicateControlService.ParseTxtPoints(text, "f.txt", "input");

        Assert.Empty(rejects);
        Assert.Equal(2, points.Count);
        Assert.Equal("PTX12", points[0].Id);
        Assert.Equal("BORNE-N2", points[1].Id);
    }

    [Fact]
    public void ParseTxtPoints_SingleMalformedDataLine_IsRejectedNotSwallowedAsHeader()
    {
        // Non-régression : une ligne de données unique et malformée (sans vrai en-tête) doit
        // rester rejetée avec un motif visible, pas avalée silencieusement comme un en-tête.
        var text = "P1\tabc\t200";

        var (points, rejects) = DuplicateControlService.ParseTxtPoints(text, "f.txt", "input");

        Assert.Empty(points);
        var reject = Assert.Single(rejects);
        Assert.Equal("X/Y non numériques", reject.Reason);
    }

    [Fact]
    public void ParseTxtPoints_AcceptsSemicolonAndCommaDecimal()
    {
        var text = "P1;100,500;200,250;10,000";

        var (points, rejects) = DuplicateControlService.ParseTxtPoints(text, "f.txt", "input");

        Assert.Empty(rejects);
        var p = Assert.Single(points);
        Assert.Equal(100.500, p.X);
        Assert.Equal(200.250, p.Y);
    }

    [Fact]
    public void ParseTxtPoints_WithoutZ_HasZFalse()
    {
        var text = "P1\t100\t200";

        var (points, _) = DuplicateControlService.ParseTxtPoints(text, "f.txt", "input");

        var p = Assert.Single(points);
        Assert.False(p.HasZ);
    }

    [Fact]
    public void ParseTxtPoints_RejectsNonNumericCoordinates()
    {
        var text = "P1\tabc\t200";

        var (points, rejects) = DuplicateControlService.ParseTxtPoints(text, "f.txt", "input");

        Assert.Empty(points);
        var reject = Assert.Single(rejects);
        Assert.Equal("X/Y non numériques", reject.Reason);
    }

    [Fact]
    public void Analyze_PointInBothInputAndControl_IsDuplicateGroup()
    {
        var inputs = new[] { new DcPoint("P1", 100, 200, 10, true, null, "entree.txt", "input") };
        var controls = new[] { new DcPoint("P1", 100.1, 200.1, 10.1, true, null, "controle.txt", "control") };

        var result = DuplicateControlService.Analyze(inputs, controls);

        var group = Assert.Single(result.Duplicates);
        Assert.Equal("P1", group.Id);
        Assert.Equal(2, group.Occurrences.Count);
        Assert.Empty(result.OrphanInputs);
        Assert.Empty(result.OrphanControls);
    }

    [Fact]
    public void Analyze_IsCaseInsensitive()
    {
        var inputs = new[] { new DcPoint("p1", 100, 200, 10, true, null, "entree.txt", "input") };
        var controls = new[] { new DcPoint("P1", 100, 200, 10, true, null, "controle.txt", "control") };

        var result = DuplicateControlService.Analyze(inputs, controls);

        Assert.Single(result.Duplicates);
    }

    [Fact]
    public void Analyze_SameIdInMultipleInputFiles_AllOccurrencesInOneGroup()
    {
        var inputs = new[]
        {
            new DcPoint("P1", 100, 200, 10, true, null, "entree1.txt", "input"),
            new DcPoint("P1", 100.2, 200.2, 10.2, true, null, "entree2.txt", "input"),
        };
        var controls = new[] { new DcPoint("P1", 100.1, 200.1, 10.1, true, null, "controle.txt", "control") };

        var result = DuplicateControlService.Analyze(inputs, controls);

        var group = Assert.Single(result.Duplicates);
        Assert.Equal(3, group.Occurrences.Count);
    }

    [Fact]
    public void Analyze_SameIdInTwoInputFilesWithoutControl_NotGroupedAsDuplicate()
    {
        // Limite V1 actée : un ID partagé par 2 fichiers d'entrée sans être dans le contrôle
        // n'est pas un doublon détecté - chaque occurrence atterrit dans OrphanInputs.
        var inputs = new[]
        {
            new DcPoint("P1", 100, 200, 10, true, null, "entree1.txt", "input"),
            new DcPoint("P1", 999, 999, 999, true, null, "entree2.txt", "input"),
        };
        var controls = Array.Empty<DcPoint>();

        var result = DuplicateControlService.Analyze(inputs, controls);

        Assert.Empty(result.Duplicates);
        Assert.Equal(2, result.OrphanInputs.Count);
    }

    [Fact]
    public void Analyze_InputOnly_IsOrphanInput()
    {
        var inputs = new[] { new DcPoint("P1", 100, 200, 10, true, null, "entree.txt", "input") };
        var controls = Array.Empty<DcPoint>();

        var result = DuplicateControlService.Analyze(inputs, controls);

        Assert.Empty(result.Duplicates);
        var orphan = Assert.Single(result.OrphanInputs);
        Assert.Equal("P1", orphan.Id);
        Assert.Empty(result.OrphanControls);
    }

    [Fact]
    public void Analyze_ControlOnly_IsOrphanControl()
    {
        var inputs = Array.Empty<DcPoint>();
        var controls = new[] { new DcPoint("P1", 100, 200, 10, true, null, "controle.txt", "control") };

        var result = DuplicateControlService.Analyze(inputs, controls);

        Assert.Empty(result.Duplicates);
        Assert.Empty(result.OrphanInputs);
        var orphan = Assert.Single(result.OrphanControls);
        Assert.Equal("P1", orphan.Id);
    }

    [Fact]
    public void Average_ComputesMeanOfXYZ()
    {
        var points = new[]
        {
            new DcPoint("P1", 100, 200, 10, true, null, "a.txt", "input"),
            new DcPoint("P1", 102, 202, 12, true, null, "b.txt", "control"),
        };

        var (x, y, z) = DuplicateControlService.Average(points);

        Assert.Equal(101, x);
        Assert.Equal(201, y);
        Assert.Equal(11, z);
    }

    [Fact]
    public void Average_ZNullWhenNoOccurrenceHasZ()
    {
        var points = new[]
        {
            new DcPoint("P1", 100, 200, 0, false, null, "a.txt", "input"),
            new DcPoint("P1", 102, 202, 0, false, null, "b.txt", "control"),
        };

        var (_, _, z) = DuplicateControlService.Average(points);

        Assert.Null(z);
    }

    [Fact]
    public void Average_ZAveragedOnlyOverOccurrencesThatHaveIt()
    {
        var points = new[]
        {
            new DcPoint("P1", 100, 200, 10, true, null, "a.txt", "input"),
            new DcPoint("P1", 100, 200, 0, false, null, "b.txt", "control"),
        };

        var (_, _, z) = DuplicateControlService.Average(points);

        Assert.Equal(10, z);
    }

    [Fact]
    public void FormatOutputTxt_RoundTripsThroughParser()
    {
        var points = new List<DcPoint>
        {
            new("P1", 100.123, 200.456, 10.789, true, "PT", "src", "input"),
            new("P2", 101.000, 201.000, 0, false, null, "src", "input"),
        };

        var text = DuplicateControlService.FormatOutputTxt(points);
        var (reparsed, rejects) = DuplicateControlService.ParseTxtPoints(text, "roundtrip.txt", "input");

        Assert.Empty(rejects);
        Assert.Equal(2, reparsed.Count);
        Assert.Equal("P1", reparsed[0].Id);
        Assert.Equal(100.123, reparsed[0].X);
        Assert.Equal("PT", reparsed[0].Code);
        Assert.Equal("P2", reparsed[1].Id);
        Assert.False(reparsed[1].HasZ);
    }

    [Fact]
    public void FormatReport_IncludesCountsAndResolutions()
    {
        var analysis = new DcAnalysisResult(
            Duplicates: Array.Empty<DcDuplicateGroup>(),
            OrphanInputs: new[] { new DcPoint("P9", 1, 2, 3, true, null, "in.txt", "input") },
            OrphanControls: new[] { new DcPoint("P8", 1, 2, 3, true, null, "ctrl.txt", "control") });
        var resolutions = new[] { ("P1", "Moyenne", "2 occurrences moyennées") };

        var report = DuplicateControlService.FormatReport(analysis, resolutions, inputPointCount: 5, controlPointCount: 3, inputFileCount: 2);

        Assert.Contains("P1 : Moyenne", report);
        Assert.Contains("P9", report);
        Assert.Contains("P8", report);
        Assert.Contains("Fichiers d'entrée : 2 (5 points)", report);
    }
}
