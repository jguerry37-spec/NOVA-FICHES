using System.IO;
using ClosedXML.Excel;
using TopoRapportWin.ControlePolygonale;
using Xunit;

namespace NovaFiches.Tests;

public class ControlePolygonaleServiceTests
{
    private static Stream BuildWorkbook(Action<XLWorkbook> fill)
    {
        using var wb = new XLWorkbook();
        fill(wb);
        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void ReadWorkbook_SheetNameContainsGnss_ClassifiedAsXy()
    {
        using var stream = BuildWorkbook(wb =>
        {
            var ws = wb.Worksheets.Add("Comparaison GNSS");
            // 2 header rows, data starts row 3.
            ws.Cell(3, 1).Value = "P1";
            ws.Cell(3, 2).Value = 100.000; ws.Cell(3, 3).Value = 200.000;
            ws.Cell(3, 4).Value = 100.010; ws.Cell(3, 5).Value = 200.005;
        });

        var workbook = ControlePolygonaleService.ReadWorkbook(stream);

        Assert.Single(workbook.XySheets);
        Assert.Empty(workbook.ZSheets);
        var row = workbook.XySheets[0].Rows[0];
        Assert.Equal("P1", row.Point);
        Assert.True(row.Complete);
        // Dx/Dy recomputed from théo/contrôle since absent from the sheet.
        Assert.Equal(0.010, row.Dx!.Value, 3);
        Assert.Equal(0.005, row.Dy!.Value, 3);
        Assert.Equal(Math.Sqrt(0.010 * 0.010 + 0.005 * 0.005), row.DeltaXy!.Value, 6);
    }

    [Fact]
    public void ReadWorkbook_SheetNameContainsNiv_ClassifiedAsZ()
    {
        using var stream = BuildWorkbook(wb =>
        {
            var ws = wb.Worksheets.Add("Nivellement");
            // 3 header rows, data starts row 4.
            ws.Cell(4, 1).Value = "P1";
            ws.Cell(4, 2).Value = 50.000; ws.Cell(4, 3).Value = 50.009;
        });

        var workbook = ControlePolygonaleService.ReadWorkbook(stream);

        Assert.Empty(workbook.XySheets);
        Assert.Single(workbook.ZSheets);
        var row = workbook.ZSheets[0].Rows[0];
        Assert.Equal("P1", row.Point);
        Assert.True(row.Complete);
        Assert.Equal(0.009, row.Dz!.Value, 3);
    }

    [Fact]
    public void ReadWorkbook_UnrelatedSheetName_Ignored()
    {
        using var stream = BuildWorkbook(wb =>
        {
            wb.Worksheets.Add("Export");
        });

        var workbook = ControlePolygonaleService.ReadWorkbook(stream);

        Assert.Empty(workbook.XySheets);
        Assert.Empty(workbook.ZSheets);
    }

    [Fact]
    public void ReadWorkbook_XyStats_ComputesMeanAndMax()
    {
        using var stream = BuildWorkbook(wb =>
        {
            var ws = wb.Worksheets.Add("GNSS");
            // Complete requires théo + contrôle on both axes ; Dx/Dy are then derived (cols 6/7 left blank).
            ws.Cell(3, 1).Value = "P1";
            ws.Cell(3, 2).Value = 100.000; ws.Cell(3, 3).Value = 200.000;
            ws.Cell(3, 4).Value = 100.010; ws.Cell(3, 5).Value = 200.000;
            ws.Cell(4, 1).Value = "P2";
            ws.Cell(4, 2).Value = 100.000; ws.Cell(4, 3).Value = 200.000;
            ws.Cell(4, 4).Value = 100.030; ws.Cell(4, 5).Value = 200.040;
        });

        var stats = ControlePolygonaleService.ReadWorkbook(stream).XySheets[0].Stats;

        Assert.Equal(2, stats.Count);
        Assert.Equal(2, stats.Complete);
        Assert.Equal(0, stats.Missing);
        Assert.Equal("P2", stats.MaxDeltaXyPoint);
        Assert.Equal(0.05, stats.MaxDeltaXy!.Value, 6); // sqrt(0.03^2+0.04^2) = 0.05
    }

    [Fact]
    public void ReadWorkbook_MissingPoint_CountedAsIncomplete()
    {
        using var stream = BuildWorkbook(wb =>
        {
            var ws = wb.Worksheets.Add("GNSS");
            ws.Cell(3, 1).Value = "P1"; ws.Cell(3, 2).Value = 100.0; ws.Cell(3, 3).Value = 200.0;
            // No X ctrl / Y ctrl -> incomplete.
        });

        var stats = ControlePolygonaleService.ReadWorkbook(stream).XySheets[0].Stats;

        Assert.Equal(1, stats.Count);
        Assert.Equal(0, stats.Complete);
        Assert.Equal(1, stats.Missing);
    }

    [Fact]
    public void ReadGeoBase_ParsesSpaceSeparatedRows()
    {
        var rows = ControlePolygonaleService.ReadGeoBase("P1 100.000 200.000 10.000 extra\nP2 101.5 201.5 11.5");

        Assert.Equal(2, rows.Count);
        Assert.Equal("P1", rows[0].Point);
        Assert.Equal(100.000, rows[0].X);
        Assert.Equal(200.000, rows[0].Y);
        Assert.Equal(10.000, rows[0].Z);
    }

    [Fact]
    public void ReadGeoBase_CommaDecimalSeparator_IsAccepted()
    {
        var rows = ControlePolygonaleService.ReadGeoBase("P1 100,500 200,250 10,000");

        Assert.Single(rows);
        Assert.Equal(100.5, rows[0].X);
        Assert.Equal(200.25, rows[0].Y);
    }

    [Fact]
    public void ReadGeoBase_MalformedLine_IsSkipped()
    {
        var rows = ControlePolygonaleService.ReadGeoBase("P1 100.000 200.000 10.000\nP2 not-a-number 200.000 10.000\nshort line");

        Assert.Single(rows);
        Assert.Equal("P1", rows[0].Point);
    }

    [Fact]
    public void ReadGeoBase_EmptyInput_ReturnsEmptyList()
    {
        Assert.Empty(ControlePolygonaleService.ReadGeoBase(""));
        Assert.Empty(ControlePolygonaleService.ReadGeoBase(null!));
    }
}
