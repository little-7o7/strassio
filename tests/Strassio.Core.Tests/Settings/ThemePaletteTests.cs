using Strassio.Core.Settings;

namespace Strassio.Core.Tests.Settings;

/// <summary>Тема докера как у CorelDRAW (docs/SPEC.md, раздел 12).</summary>
public class ThemePaletteTests
{
    [Theory]
    [InlineData("LightestGrey", "LightestGrey")]
    [InlineData("MediumGrey", "MediumGrey")]
    [InlineData("DarkGrey", "DarkGrey")]
    [InlineData("Black", "Black")]
    [InlineData("ColorScheme_DarkGrey", "DarkGrey")]
    [InlineData("  scheme_black ", "Black")]
    [InlineData("Scheme_11_ModernUI", "ModernUI")]
    [InlineData("Scheme_12_ModernUIDark", "DarkGrey")]
    public void Auto_FollowsCorelScheme(string corelScheme, string expected)
    {
        Assert.Equal(expected, ThemePalette.Resolve(ThemeMode.Auto, corelScheme, windowsIsDark: false).Name);
    }

    [Theory]
    [InlineData(null, false, "LightestGrey")]
    [InlineData("", true, "DarkGrey")]
    [InlineData("SomethingNew", true, "DarkGrey")]
    public void Auto_UnknownCorelScheme_FollowsWindows(string? corelScheme, bool windowsIsDark, string expected)
    {
        Assert.Equal(expected, ThemePalette.Resolve(ThemeMode.Auto, corelScheme, windowsIsDark).Name);
    }

    [Fact]
    public void ManualChoice_IgnoresCorel()
    {
        Assert.False(ThemePalette.Resolve(ThemeMode.Light, "Black", windowsIsDark: true).IsDark);
        Assert.True(ThemePalette.Resolve(ThemeMode.Dark, "LightestGrey", windowsIsDark: false).IsDark);
    }
}
