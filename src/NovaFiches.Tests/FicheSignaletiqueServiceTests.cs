using TopoRapportWin;
using Xunit;

namespace NovaFiches.Tests;

public class FicheSignaletiqueServiceTests
{
    [Fact]
    public void ParseCsv_SemicolonDelimited_ParsesRow()
    {
        var csv = "id_fiche;point;x;y;z\nFS-001;GEO-ST101;1653717.673;8168127.579;86.437";
        var (rows, rejects) = FicheSignaletiqueService.ParseCsv(csv);

        Assert.Empty(rejects);
        var row = Assert.Single(rows);
        Assert.Equal("FS-001", row.IdFiche);
        Assert.Equal("GEO-ST101", row.Point);
        Assert.Equal(1653717.673, row.X);
        Assert.Equal(8168127.579, row.Y);
        Assert.Equal(86.437, row.Z);
    }

    [Fact]
    public void ParseCsv_CommaDelimited_ParsesRow()
    {
        var csv = "id_fiche,point,x,y\nFS-002,S.50,1641847.080,8195685.860";
        var (rows, rejects) = FicheSignaletiqueService.ParseCsv(csv);

        Assert.Empty(rejects);
        var row = Assert.Single(rows);
        Assert.Equal("FS-002", row.IdFiche);
        Assert.Equal(1641847.080, row.X);
    }

    [Fact]
    public void ParseCsv_TabDelimited_ParsesRow()
    {
        var csv = "id_fiche\tpoint\tx\nFS-003\tPT-1\t100.5";
        var (rows, rejects) = FicheSignaletiqueService.ParseCsv(csv);

        Assert.Empty(rejects);
        var row = Assert.Single(rows);
        Assert.Equal("PT-1", row.Point);
        Assert.Equal(100.5, row.X);
    }

    [Fact]
    public void ParseCsv_QuotedFieldContainingDelimiter_IsPreserved()
    {
        var csv = "id_fiche;point;observations\nFS-004;PT-2;\"Implante; controle visuel OK\"";
        var (rows, rejects) = FicheSignaletiqueService.ParseCsv(csv);

        Assert.Empty(rejects);
        var row = Assert.Single(rows);
        Assert.Equal("Implante; controle visuel OK", row.Observations);
    }

    [Theory]
    [InlineData("ID_FICHE")]
    [InlineData("Id Fiche")]
    [InlineData(" id_fiche ")]
    public void ParseCsv_HeaderCaseAccentVariants_StillMatch(string header)
    {
        var csv = $"{header};point\nFS-005;PT-3";
        var (rows, _) = FicheSignaletiqueService.ParseCsv(csv);

        var row = Assert.Single(rows);
        Assert.Equal("FS-005", row.IdFiche);
    }

    [Fact]
    public void ParseCsv_AccentedHeader_NormalizesAndMatches()
    {
        var csv = "id_fiche;département\nFS-006;Essonne";
        var (rows, _) = FicheSignaletiqueService.ParseCsv(csv);

        var row = Assert.Single(rows);
        Assert.Equal("Essonne", row.Departement);
    }

    [Fact]
    public void ParseCsv_RowMissingIdFicheAndPoint_IsRejected()
    {
        var csv = "id_fiche;point;x\n;;100.5";
        var (rows, rejects) = FicheSignaletiqueService.ParseCsv(csv);

        Assert.Empty(rows);
        var reject = Assert.Single(rejects);
        Assert.Equal(2, reject.lineNo);
    }

    [Fact]
    public void ParseCsv_PointWithoutIdFiche_IsAccepted()
    {
        var csv = "id_fiche;point\n;PT-only";
        var (rows, rejects) = FicheSignaletiqueService.ParseCsv(csv);

        Assert.Empty(rejects);
        var row = Assert.Single(rows);
        Assert.Null(row.IdFiche);
        Assert.Equal("PT-only", row.Point);
    }

    [Fact]
    public void ParseCsv_BlankLinesAreSkipped()
    {
        var csv = "id_fiche;point\nFS-007;PT-4\n\n\nFS-008;PT-5";
        var (rows, rejects) = FicheSignaletiqueService.ParseCsv(csv);

        Assert.Empty(rejects);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void ParseCsv_CommaDecimal_ParsesAsFloat()
    {
        var csv = "id_fiche;x\nFS-009;100,012";
        var (rows, _) = FicheSignaletiqueService.ParseCsv(csv);

        var row = Assert.Single(rows);
        Assert.Equal(100.012, row.X);
    }

    [Fact]
    public void CollectRevisions_TwoStandardColumns_ParsesBoth()
    {
        var csv = "id_fiche;revision_1;revision_2\n"
                 + "FS-010;A | 30/03/2026 | Creation fiche | NVT;B | 20/02/2026 | Mise a jour | N.E.";
        var (rows, _) = FicheSignaletiqueService.ParseCsv(csv);
        var row = Assert.Single(rows);

        var revisions = FicheSignaletiqueService.CollectRevisions(row);

        Assert.Equal(2, revisions.Count);
        Assert.Equal("A", revisions[0].Indice);
        Assert.Equal("30/03/2026", revisions[0].Date);
        Assert.Equal("Creation fiche", revisions[0].Description);
        Assert.Equal("NVT", revisions[0].Auteur);
        Assert.Equal("B", revisions[1].Indice);
    }

    [Fact]
    public void CollectRevisions_DynamicColumnsBeyondTwo_AreAllCaptured()
    {
        var csv = "id_fiche;revision_1;revision_2;revision_3\n"
                 + "FS-011;A|2026-01-01|first|X;B|2026-02-01|second|Y;C|2026-03-01|third|Z";
        var (rows, _) = FicheSignaletiqueService.ParseCsv(csv);
        var row = Assert.Single(rows);

        var revisions = FicheSignaletiqueService.CollectRevisions(row);

        Assert.Equal(3, revisions.Count);
        Assert.Equal("C", revisions[2].Indice);
        Assert.Equal("third", revisions[2].Description);
    }

    [Fact]
    public void CollectRevisions_EmptyRevisionColumn_IsSkipped()
    {
        var csv = "id_fiche;revision_1;revision_2\nFS-012;;B|2026-02-01|second|Y";
        var (rows, _) = FicheSignaletiqueService.ParseCsv(csv);
        var row = Assert.Single(rows);

        var revisions = FicheSignaletiqueService.CollectRevisions(row);

        var revision = Assert.Single(revisions);
        Assert.Equal("B", revision.Indice);
    }

    [Fact]
    public void ParseCsv_EmptyText_ReturnsEmptyResult()
    {
        var (rows, rejects) = FicheSignaletiqueService.ParseCsv("");
        Assert.Empty(rows);
        Assert.Empty(rejects);
    }
}
