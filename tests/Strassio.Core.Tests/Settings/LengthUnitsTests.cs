using System.Globalization;
using Strassio.Core.Settings;

namespace Strassio.Core.Tests.Settings;

/// <summary>Внутри всё в мм; дюймы — только при показе и вводе (CLAUDE.md, правило 6).</summary>
public class LengthUnitsTests
{
    [Fact]
    public void Format_Millimeters_And_Inches()
    {
        var ru = CultureInfo.GetCultureInfo("ru-RU");

        Assert.Equal("2,4", LengthUnits.Format(2.4, LengthUnit.Millimeter, 2, ru));
        Assert.Equal("0,094", LengthUnits.Format(2.4, LengthUnit.Inch, 3, ru));
        Assert.Equal("25.4", LengthUnits.Format(25.4, LengthUnit.Millimeter, 2, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("2,4", LengthUnit.Millimeter, 2.4)]
    [InlineData("2.4", LengthUnit.Millimeter, 2.4)]
    [InlineData(" 1 ", LengthUnit.Inch, 25.4)]
    [InlineData("0,5", LengthUnit.Inch, 12.7)]
    public void TryParse_AcceptsCommaOrDot_ReturnsMillimeters(string text, LengthUnit unit, double expectedMm)
    {
        Assert.True(LengthUnits.TryParse(text, unit, out double mm));
        Assert.Equal(expectedMm, mm, 9);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1,2,3")]
    public void TryParse_RejectsGarbage(string text)
    {
        Assert.False(LengthUnits.TryParse(text, LengthUnit.Millimeter, out _));
    }
}
