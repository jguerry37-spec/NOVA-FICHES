using NovaFiches.PdfSharpEngine;
using Xunit;

namespace NovaFiches.Tests;

public class UnitsTests
{
    [Fact]
    public void MmToPt_OneInch_Returns72Points()
    {
        Assert.Equal(72.0, Units.MmToPt(25.4), precision: 6);
    }

    [Fact]
    public void MmToPt_Zero_ReturnsZero()
    {
        Assert.Equal(0.0, Units.MmToPt(0));
    }

    [Fact]
    public void MmToPt_IsLinear()
    {
        var single = Units.MmToPt(10);
        var doubled = Units.MmToPt(20);
        Assert.Equal(single * 2, doubled, precision: 9);
    }

    [Fact]
    public void MmToPt_NegativeValue_PreservesSign()
    {
        Assert.True(Units.MmToPt(-5) < 0);
    }
}
