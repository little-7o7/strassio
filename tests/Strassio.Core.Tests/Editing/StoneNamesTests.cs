using Strassio.Core.Editing;

namespace Strassio.Core.Tests.Editing;

public class StoneNamesTests
{
    private static readonly string[] Sizes = { "ss6", "ss6 big", "SS10" };

    [Theory]
    [InlineData("ss6 Красный", "ss6", "Красный")]
    [InlineData("ss6 big Золото", "ss6 big", "Золото")]
    [InlineData("ss10 Светло-розовый АБ", "SS10", "Светло-розовый АБ")]
    [InlineData("ss16 Кристалл", "ss16", "Кристалл")]
    [InlineData("  ss6  ", "ss6", "")]
    public void Parse(string name, string size, string color)
    {
        Assert.True(StoneNames.TryParse(name, Sizes, out string s, out string c));
        Assert.Equal(size, s);
        Assert.Equal(color, c);
    }

    [Fact]
    public void EmptyName_IsNotAStone() => Assert.False(StoneNames.TryParse("  ", Sizes, out _, out _));

    [Fact]
    public void Matches_EmptyMeansAny()
    {
        Assert.True(StoneNames.Matches("ss6", "Красный", "SS6", null));
        Assert.True(StoneNames.Matches("ss6", "Красный", null, "красный"));
        Assert.False(StoneNames.Matches("ss6", "Красный", "ss6", "Золото"));
    }

    [Fact]
    public void Compose_IsInverseOfParse()
    {
        Assert.True(StoneNames.TryParse(StoneNames.Compose("ss6 big", "Золото"), Sizes, out string s, out string c));
        Assert.Equal(("ss6 big", "Золото"), (s, c));
    }
}
