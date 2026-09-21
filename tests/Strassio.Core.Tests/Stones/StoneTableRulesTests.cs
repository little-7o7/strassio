using Strassio.Core.Stones;

namespace Strassio.Core.Tests.Stones;

/// <summary>Редактор таблицы камней (docs/SPEC.md, раздел 3.1).</summary>
public class StoneTableRulesTests
{
    private static StoneSet Set() => new()
    {
        Name = "Основной",
        Sizes =
        {
            new StoneSize { Name = "ss6", DiameterMm = 2.4, Colors = { new StoneColor { Name = "Красный", Rgb = "#E53935" } } },
            new StoneSize { Name = "ss10", DiameterMm = 2.9, Colors = { new StoneColor { Name = "Золото", Rgb = "#D4A017" } } },
        },
    };

    private static string[] Keys(StoneSet set) => StoneTableRules.Validate(set).Select(p => p.Key).ToArray();

    [Fact]
    public void GoodSet_HasNoProblems() => Assert.Empty(StoneTableRules.Validate(Set()));

    [Fact]
    public void EmptySet_IsAProblem() => Assert.Equal(new[] { "stones.error.noSizes" }, Keys(new StoneSet()));

    [Fact]
    public void DuplicateSizeName_IgnoringCaseAndSpaces()
    {
        StoneSet set = Set();
        set.Sizes[1].Name = " SS6 ";
        Assert.Contains("stones.error.sizeDuplicate", Keys(set));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.4)]
    [InlineData(31)]
    [InlineData(double.NaN)]
    public void BadDiameter_IsAProblem(double diameter)
    {
        StoneSet set = Set();
        set.Sizes[0].DiameterMm = diameter;
        Assert.Contains("stones.error.diameter", Keys(set));
    }

    [Fact]
    public void EmptyNames_NoColors_BadRgb_DuplicateColor_AllReported()
    {
        StoneSet set = Set();
        set.Sizes[0].Name = "  ";
        set.Sizes[1].Colors.Clear();
        set.Sizes[0].Colors.Add(new StoneColor { Name = "красный", Rgb = "#E53935" });
        set.Sizes[0].Colors.Add(new StoneColor { Name = "", Rgb = "nope" });

        string[] keys = Keys(set);
        Assert.Contains("stones.error.sizeName", keys);
        Assert.Contains("stones.error.noColors", keys);
        Assert.Contains("stones.error.colorDuplicate", keys);
        Assert.Contains("stones.error.colorName", keys);
        Assert.Contains("stones.error.rgb", keys);
    }

    [Theory]
    [InlineData("#E53935", "#E53935")]
    [InlineData("e53935", "#E53935")]
    [InlineData("  #e53 ", "#EE5533")]
    [InlineData("229, 57, 53", "#E53935")]
    [InlineData("229 57 53", "#E53935")]
    public void Rgb_UnderstoodInCommonForms(string text, string expected)
    {
        Assert.True(StoneTableRules.TryNormalizeRgb(text, out string rgb));
        Assert.Equal(expected, rgb);
    }

    [Theory]
    [InlineData("")]
    [InlineData("red")]
    [InlineData("#E5393")]
    [InlineData("256, 0, 0")]
    [InlineData("1,2")]
    public void Rgb_RejectsNonsense(string text) => Assert.False(StoneTableRules.TryNormalizeRgb(text, out _));

    [Fact]
    public void Clone_IsIndependent()
    {
        var table = new StoneTable { Sets = { Set() } };
        StoneTable copy = StoneTableRules.Clone(table);
        copy.Sets[0].Sizes[0].Name = "ss99";
        copy.Sets[0].Sizes[0].Colors[0].Rgb = "#000000";
        Assert.Equal("ss6", table.Sets[0].Sizes[0].Name);
        Assert.Equal("#E53935", table.Sets[0].Sizes[0].Colors[0].Rgb);
    }

    [Fact]
    public void AddSize_AfterSelected_CopiesColors_UniqueName()
    {
        StoneSet set = Set();
        StoneSize added = StoneTableRules.AddSize(set, set.Sizes[0], "ss");
        StoneSize second = StoneTableRules.AddSize(set, added, "ss");

        Assert.Same(added, set.Sizes[1]);
        Assert.Same(second, set.Sizes[2]);
        Assert.Equal("ss", added.Name);
        Assert.Equal("ss 2", second.Name);
        Assert.Equal(2.5, added.DiameterMm, 6);
        Assert.Equal("Красный", added.Colors.Single().Name);
        Assert.NotSame(set.Sizes[0].Colors[0], added.Colors[0]);
    }

    [Fact]
    public void AddSize_ToEmptySet_HasDefaultDiameter()
    {
        var set = new StoneSet();
        StoneSize size = StoneTableRules.AddSize(set, null, "ss");
        Assert.Equal(2.4, size.DiameterMm);
        Assert.Empty(size.Colors);
    }

    [Fact]
    public void AddColor_GetsUniqueName()
    {
        StoneSize size = Set().Sizes[0];
        StoneTableRules.AddColor(size, "Цвет");
        StoneColor second = StoneTableRules.AddColor(size, "Цвет");
        Assert.Equal("Цвет 2", second.Name);
        Assert.True(second.TryGetRgb(out _, out _, out _));
    }

    [Fact]
    public void CopyColorsToAllSizes_UpdatesExisting_AddsMissing()
    {
        StoneSet set = Set();
        set.Sizes[1].Colors.Add(new StoneColor { Name = "красный", Rgb = "#000000" });
        set.Sizes[0].Colors.Add(new StoneColor { Name = "Кристалл", Rgb = "#E8EEF5" });

        StoneTableRules.CopyColorsToAllSizes(set, set.Sizes[0]);

        List<StoneColor> target = set.Sizes[1].Colors;
        Assert.Equal(new[] { "Золото", "красный", "Кристалл" }, target.Select(c => c.Name));
        Assert.Equal("#E53935", target[1].Rgb);
    }

    [Fact]
    public void Move_StaysInsideList()
    {
        var list = new List<string> { "a", "b", "c" };
        Assert.Equal(0, StoneTableRules.Move(list, 1, -1));
        Assert.Equal(new[] { "b", "a", "c" }, list);
        Assert.Equal(0, StoneTableRules.Move(list, 0, -1));
        Assert.Equal(2, StoneTableRules.Move(list, 0, 5));
        Assert.Equal(new[] { "a", "c", "b" }, list);
    }

    [Fact]
    public void SortByDiameter_Ascending()
    {
        StoneSet set = Set();
        set.Sizes.Insert(0, new StoneSize { Name = "ss16", DiameterMm = 3.9 });
        StoneTableRules.SortByDiameter(set);
        Assert.Equal(new[] { "ss6", "ss10", "ss16" }, set.Sizes.Select(s => s.Name));
    }

    [Fact]
    public void Tidy_TrimsNames_NormalizesRgb()
    {
        StoneSet set = Set();
        set.Sizes[0].Name = " ss6 ";
        set.Sizes[0].Colors[0].Name = " Красный ";
        set.Sizes[0].Colors[0].Rgb = "229,57,53";
        StoneTableRules.Tidy(set);
        Assert.Equal("ss6", set.Sizes[0].Name);
        Assert.Equal("Красный", set.Sizes[0].Colors[0].Name);
        Assert.Equal("#E53935", set.Sizes[0].Colors[0].Rgb);
    }
}
