using Strassio.Core.Editing;
using Strassio.Core.Geometry;
using Strassio.Core.Methods;

namespace Strassio.Core.Tests.Editing;

/// <summary>Этап 4: смешение цветов, поиск дырок, «живые» стразы (docs/SPEC.md, разделы 3.4, 7.2, 8).</summary>
public class Stage4EditingTests
{
    [Fact]
    public void Random_KeepsExactProportions()
    {
        int[] colors = ColorMixer.Random(100, new[] { 70.0, 30.0 }, seed: 5);
        Assert.Equal(70, colors.Count(c => c == 0));
        Assert.Equal(30, colors.Count(c => c == 1));
        Assert.NotEqual(Enumerable.Range(0, 100).Select(i => i < 70 ? 0 : 1), colors); // перемешано
        Assert.Equal(colors, ColorMixer.Random(100, new[] { 70.0, 30.0 }, seed: 5));
    }

    [Theory]
    [InlineData(10, new[] { 50.0, 30, 20 }, new[] { 5, 3, 2 })]
    [InlineData(7, new[] { 1.0, 1, 1 }, new[] { 3, 2, 2 })]
    [InlineData(5, new[] { 0.0, 0 }, new[] { 5, 0 })]
    public void Quotas_SumToCount(int count, double[] weights, int[] expected) =>
        Assert.Equal(expected, ColorMixer.Quotas(count, weights));

    [Fact]
    public void Sequence_RepeatsPattern()
    {
        Assert.Equal(new[] { 0, 0, 1, 0, 0, 1, 0 }, ColorMixer.Sequence(7, new[] { 2, 1 }));
    }

    private static List<DocStone> Honeycomb(int cols, int rows, double d, double gap, out List<Point2D> all)
    {
        all = new List<Point2D>();
        double step = d + gap;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                all.Add(new Point2D(c * step + (r % 2) * step / 2, r * step * Math.Sqrt(3) / 2));
            }
        }

        return all.Select((p, i) => new DocStone(p, d, i)).ToList();
    }

    [Fact]
    public void Holes_FoundInsideFill_NotOutside()
    {
        List<DocStone> stones = Honeycomb(12, 10, 2.4, 0.2, out List<Point2D> all);
        Point2D hole1 = all[5 * 12 + 5];
        Point2D hole2 = all[3 * 12 + 8];
        stones.RemoveAll(s => s.Center.Equals(hole1) || s.Center.Equals(hole2));

        List<Point2D> holes = HoleFinder.Find(stones, 2.4, 0.2);
        Assert.Equal(2, holes.Count);
        Assert.Contains(holes, h => Point2D.Distance(h, hole1) < 0.3);
        Assert.Contains(holes, h => Point2D.Distance(h, hole2) < 0.3);
    }

    [Fact]
    public void Holes_NoneInCompleteFill()
    {
        List<DocStone> stones = Honeycomb(10, 8, 2.4, 0.2, out _);
        Assert.Empty(HoleFinder.Find(stones, 2.4, 0.2));
    }

    [Fact]
    public void Holes_SmallerStone_FitsGapThatBigDoesNot()
    {
        // Сетка с шагом 3,4 мм у камней 2,4: между четырьмя соседями влезает камень ~1,9 мм.
        var stones = new List<DocStone>();
        int i = 0;
        for (int x = 0; x < 6; x++)
        {
            for (int y = 0; y < 6; y++)
            {
                stones.Add(new DocStone(new Point2D(x * 3.4, y * 3.4), 2.4, i++));
            }
        }

        Assert.Empty(HoleFinder.Find(stones, 2.4, 0.1));
        Assert.NotEmpty(HoleFinder.Find(stones, 1.8, 0.1));
    }

    [Fact]
    public void LiveRecipe_RoundTrip()
    {
        var recipe = new LiveRecipe
        {
            Method = "l2", Size = "ss6", Color = "Красный", Sources = { 12345, 777 },
            Parameters = new MethodParameters { RowCount = 5, EdgeSize = "ss5" },
        };

        Assert.True(LiveRecipe.TryParse(recipe.ToText(), out LiveRecipe? back));
        Assert.Equal("l2", back!.Method);
        Assert.Equal(new[] { 12345, 777 }, back.Sources);
        Assert.Equal(5, back.Parameters.RowCount);
        Assert.Equal("ss5", back.Parameters.EdgeSize);
        Assert.True(back.TryGetKind(out MethodKind kind));
        Assert.Equal(MethodKind.L2, kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("не json")]
    [InlineData("{\"method\":\"l1\"}")]
    public void LiveRecipe_RejectsGarbage(string? text) => Assert.False(LiveRecipe.TryParse(text, out _));

    [Fact]
    public void LiveRecipe_UnknownMethod()
    {
        var recipe = new LiveRecipe { Method = "z9", Sources = { 1 } };
        Assert.True(LiveRecipe.TryParse(recipe.ToText(), out LiveRecipe? back));
        Assert.False(back!.TryGetKind(out _));
    }
}
