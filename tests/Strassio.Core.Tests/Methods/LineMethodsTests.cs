using Strassio.Core.Geometry;
using Strassio.Core.Methods;
using Strassio.Core.Placement;

namespace Strassio.Core.Tests.Methods;

/// <summary>Методы по линии Этапа 4: L4–L8 и L2 с другим размером крайних рядов (docs/SPEC.md, раздел 4).</summary>
public class LineMethodsTests
{
    private const double D = 2.4;

    private static readonly Dictionary<string, double> Sizes = new()
    {
        ["ss5"] = 2.1, ["ss6"] = 2.4, ["ss8"] = 2.5, ["ss10"] = 2.9, ["ss16"] = 3.9,
    };

    private static Curve Line(double length) =>
        Curve.FromPolyline(new List<Point2D> { new(0, 0), new(length, 0) }, false);

    private static Curve Zigzag() => Curve.FromPolyline(
        new List<Point2D> { new(0, 0), new(20, 30), new(40, 0), new(60, 30) }, false);

    private static MethodResult Run(MethodKind kind, Curve curve, MethodParameters p) =>
        MethodRunner.Run(kind, new[] { curve }, D, p, Sizes);

    private static double MinGap(IReadOnlyList<PlacedStone> stones)
    {
        double min = double.MaxValue;
        for (int i = 0; i < stones.Count; i++)
        {
            for (int j = i + 1; j < stones.Count; j++)
            {
                min = Math.Min(min, Point2D.Distance(stones[i].Center, stones[j].Center) - (stones[i].DiameterMm + stones[j].DiameterMm) / 2);
            }
        }

        return min;
    }

    [Fact]
    public void L5_SizeGoesFromBigToSmall_ThroughTableSizes()
    {
        var p = new MethodParameters { FromSize = "ss16", ToSize = "ss5" };
        IReadOnlyList<PlacedStone> stones = Run(MethodKind.L5, Line(120), p).Stones.OrderBy(s => s.Center.X).ToList();

        Assert.Equal(3.9, stones.First().DiameterMm, 6);
        Assert.Equal(2.1, stones.Last().DiameterMm, 6);
        for (int i = 1; i < stones.Count; i++)
        {
            Assert.True(stones[i].DiameterMm <= stones[i - 1].DiameterMm + 1e-9, "размер должен только уменьшаться");
        }

        Assert.Equal(new[] { 2.1, 2.4, 2.5, 2.9, 3.9 }, stones.Select(s => s.DiameterMm).Distinct().OrderBy(x => x));
        Assert.True(MinGap(stones) > 0.1);
    }

    [Fact]
    public void L5_EndsExactlyAtLineEnds()
    {
        var p = new MethodParameters { FromSize = "ss10", ToSize = "ss6" };
        IReadOnlyList<PlacedStone> stones = Run(MethodKind.L5, Line(50), p).Stones;
        Assert.InRange(stones.Min(s => s.Center.X), -0.01, 0.01);
        Assert.InRange(stones.Max(s => s.Center.X), 49.99, 50.01);
    }

    [Fact]
    public void L6_PatternRepeats()
    {
        var p = new MethodParameters { SizePattern = "ss6, ss6, ss16" };
        List<double> ds = Run(MethodKind.L6, Line(100), p).Stones.OrderBy(s => s.Center.X).Select(s => s.DiameterMm).ToList();
        for (int i = 0; i + 2 < ds.Count; i += 3)
        {
            Assert.Equal(new[] { 2.4, 2.4, 3.9 }, ds.Skip(i).Take(3));
        }
    }

    [Fact]
    public void L6_UnknownNames_Ignored_EmptyPatternUsesMainStone()
    {
        Assert.All(Run(MethodKind.L6, Line(30), new MethodParameters { SizePattern = "нет такого" }).Stones, s => Assert.Equal(D, s.DiameterMm));
        Assert.Equal(new[] { "ss99" }, SizePatterns.Unknown("ss6, ss99", Sizes.Keys));
    }

    [Fact]
    public void L7_DashesAndSkips()
    {
        MethodResult full = Run(MethodKind.L1, Line(100), new MethodParameters());
        MethodResult dashed = Run(MethodKind.L7, Line(100), new MethodParameters { DashCount = 3, SkipCount = 2 });
        int expected = Enumerable.Range(0, full.Stones.Count).Count(i => i % 5 < 3);
        Assert.Equal(expected, dashed.Stones.Count);
    }

    [Fact]
    public void L8_AccentsAtEndsAndCorners_NoOverlaps()
    {
        var p = new MethodParameters { AccentSize = "ss16", AccentWhere = MethodChoices.AccentBoth };
        IReadOnlyList<PlacedStone> stones = Run(MethodKind.L8, Zigzag(), p).Stones;

        int big = stones.Count(s => Math.Abs(s.DiameterMm - 3.9) < 1e-9);
        Assert.Equal(4, big); // два конца + два угла
        Assert.Contains(stones, s => Math.Abs(s.DiameterMm - 3.9) < 1e-9 && Point2D.Distance(s.Center, new Point2D(0, 0)) < 0.01);
        Assert.True(MinGap(stones) > 0.05);
    }

    [Fact]
    public void L8_OnlyEnds()
    {
        var p = new MethodParameters { AccentSize = "ss16", AccentWhere = MethodChoices.AccentEnds };
        Assert.Equal(2, Run(MethodKind.L8, Zigzag(), p).Stones.Count(s => s.DiameterMm > 3));
    }

    [Theory]
    [InlineData(MethodChoices.ProfileMiddle)]
    [InlineData(MethodChoices.ProfileGrow)]
    public void L4_RowsChangeAlongLine(string profile)
    {
        var p = new MethodParameters { RowCount = 5, RowGapMm = 0.3, WidthProfile = profile };
        IReadOnlyList<PlacedStone> stones = Run(MethodKind.L4, Line(150), p).Stones;

        int RowsNear(double x) => stones.Where(s => Math.Abs(s.Center.X - x) < 2).Select(s => Math.Round(s.Center.Y, 1)).Distinct().Count();

        Assert.Equal(1, RowsNear(0));
        if (profile == MethodChoices.ProfileMiddle)
        {
            Assert.Equal(5, RowsNear(75));
            Assert.Equal(1, RowsNear(150));
        }
        else
        {
            Assert.Equal(5, RowsNear(148));
        }

        Assert.True(MinGap(stones) > 0.05);
    }

    [Fact]
    public void L4_WidthGrowsOneRowAtATime_AndStaysWithinLineEnds()
    {
        // Замечание автора «каллиграфию не понял»: ширина прыгала 1 → 3 → 5 рядов, а на широком
        // конце боковые ряды загибались вокруг конца линии. Теперь ряды прибавляются по одному.
        var p = new MethodParameters { RowCount = 5, RowGapMm = 0.3, WidthProfile = MethodChoices.ProfileGrow };
        IReadOnlyList<PlacedStone> stones = Run(MethodKind.L4, Line(150), p).Stones;

        var seen = new SortedSet<int>();
        for (double x = 0; x <= 150; x += 2)
        {
            seen.Add(stones.Where(s => Math.Abs(s.Center.X - x) < 2).Select(s => Math.Round(s.Center.Y, 1)).Distinct().Count());
        }

        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, seen.Where(n => n > 0).ToArray());
        Assert.All(stones, s => Assert.InRange(s.Center.X, -0.01, 150.01));
    }

    [Fact]
    public void L2_EdgeRows_UseOtherSize_AndDoNotOverlap()
    {
        var p = new MethodParameters { RowCount = 3, RowGapMm = 0.3, EdgeSize = "ss5" };
        IReadOnlyList<PlacedStone> stones = Run(MethodKind.L2, Line(80), p).Stones;

        Assert.All(stones.Where(s => Math.Abs(s.Center.Y) < 0.01), s => Assert.Equal(D, s.DiameterMm));
        Assert.All(stones.Where(s => Math.Abs(s.Center.Y) > 0.5), s => Assert.Equal(2.1, s.DiameterMm));

        double edgeY = stones.Where(s => s.Center.Y > 0.5).Select(s => s.Center.Y).First();
        Assert.Equal((2.4 + 2.1) / 2 + 0.3, edgeY, 2);
        Assert.True(MinGap(stones) > 0.1);
    }

    [Fact]
    public void Variable_ClosedCurve_HasNoSeam()
    {
        var pts = new List<Point2D>();
        for (int i = 0; i < 120; i++)
        {
            double a = 2 * Math.PI * i / 120;
            pts.Add(new Point2D(20 * Math.Cos(a), 20 * Math.Sin(a)));
        }

        IReadOnlyList<PlacedStone> stones = VariableLineScatterer.Scatter(
            Curve.FromPolyline(pts, true), (i, t) => i % 2 == 0 ? 2.4 : 3.9, 0.3);

        var gaps = new List<double>();
        for (int i = 0; i < stones.Count; i++)
        {
            PlacedStone a = stones[i];
            PlacedStone b = stones[(i + 1) % stones.Count];
            gaps.Add(Point2D.Distance(a.Center, b.Center) - (a.DiameterMm + b.DiameterMm) / 2);
        }

        Assert.True(gaps.Max() - gaps.Min() < 0.25, $"зазоры от {gaps.Min():0.00} до {gaps.Max():0.00}");
    }
}
