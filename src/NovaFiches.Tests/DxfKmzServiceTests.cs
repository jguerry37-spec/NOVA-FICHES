using System.Text;
using TopoRapportWin;
using Xunit;

namespace NovaFiches.Tests;

public class DxfKmzServiceTests
{
    private const string MinimalDxf =
        "0\nSECTION\n2\nENTITIES\n" +
        "0\nPOINT\n8\nTOPO\n10\n100.5\n20\n200.25\n30\n50.0\n" +
        "0\nLINE\n8\nAXES\n10\n0.0\n20\n0.0\n30\n0.0\n11\n10.0\n21\n10.0\n31\n0.0\n" +
        "0\nENDSEC\n0\nEOF\n";

    private static string WriteTempDxf(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"nf-test-{Guid.NewGuid():N}.dxf");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void Load_ParsesPointAndLineFromMinimalDxf_Windows1252()
    {
        var path = WriteTempDxf(Encoding.GetEncoding(1252).GetBytes(MinimalDxf));
        try
        {
            var doc = DxfKmzService.Load(path);

            var point = Assert.Single(doc.Points);
            Assert.Equal("TOPO", point.Layer);
            Assert.Equal(100.5, point.X);
            Assert.Equal(200.25, point.Y);
            Assert.Equal(50.0, point.Z);
            Assert.True(point.HasZ);

            var line = Assert.Single(doc.Lines);
            Assert.Equal("AXES", line.Layer);
            Assert.Equal(0.0, line.X1);
            Assert.Equal(10.0, line.X2);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_Utf8BomWithAccents_DecodesLayerNameCorrectly()
    {
        // Reproduit le cas signalé par l'audit : un DXF ré-exporté en UTF-8 avec BOM
        // (ex. via QGIS/LibreCAD) contenant un calque accentué.
        const string dxfWithAccents =
            "0\nSECTION\n2\nENTITIES\n" +
            "0\nPOINT\n8\nSondé\n10\n1.0\n20\n2.0\n30\n0.0\n" +
            "0\nENDSEC\n0\nEOF\n";

        var utf8Bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var bytes = utf8Bom.Concat(Encoding.UTF8.GetBytes(dxfWithAccents)).ToArray();
        var path = WriteTempDxf(bytes);
        try
        {
            var doc = DxfKmzService.Load(path);

            var point = Assert.Single(doc.Points);
            Assert.Equal("Sondé", point.Layer);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_ProducesLayerSummaryWithCorrectCounts()
    {
        var path = WriteTempDxf(Encoding.GetEncoding(1252).GetBytes(MinimalDxf));
        try
        {
            var doc = DxfKmzService.Load(path);

            var topoLayer = doc.Layers.Single(l => l.Name == "TOPO");
            Assert.Equal(1, topoLayer.PointCount);
            Assert.Equal(0, topoLayer.LineCount);

            var axesLayer = doc.Layers.Single(l => l.Name == "AXES");
            Assert.Equal(0, axesLayer.PointCount);
            Assert.Equal(1, axesLayer.LineCount);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
