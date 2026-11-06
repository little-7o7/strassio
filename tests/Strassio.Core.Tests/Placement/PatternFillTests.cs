using Strassio.Core.Geometry;
using Strassio.Core.Methods;
using Strassio.Core.Placement;

namespace Strassio.Core.Tests.Placement;

/// <summary>Узорные заливки для тканей: F15 «Лучи» и F16 «Штрихи» (docs/SPEC.md, раздел 5).</summary>
public class PatternFillTests
{
    private const double D = 2.0;
    private const double Gap = 0.3;

    private static Curve Circle(double r) => Curve.FromPolyline(
        Enumerable.Range(0, 160).Select(i => new Point2D(r * Math.Cos(2 * Math.PI * i / 160), r * Math.Sin(2 * Math.PI * i / 160))).ToList(), true);

    private static Curve Rect(double w, double h) => Curve.FromPolyline(
        new List<Point2D> { new(0, 0), new(w, 0), new(w, h), new(0, h) }, true);

    private static void AssertNoOverlaps(IReadOnlyList<PlacedStone> stones, double gap)
    {
        for (int i = 0; i < stones.Count; i++)
        {
            for (int j = i + 1; j < stones.Count; j++)
            {
                double need = (stones[i].DiameterMm + stones[j].DiameterMm) / 2 + gap - 0.03;
                Assert.True(Point2D.Distance(stones[i].Center, stones[j].Center) >= need, $"стразы {i} и {j} налезают");
            }
        }
    }

    [Fact]
    public void Rays_GoFromTheMiddle_AndMultiplyOutward()
    {
        List<PlacedStone> stones = PatternFills.Rays(new[] { Circle(20) }, D, Gap, 1.2, 3, 0);
        AssertNoOverlaps(stones, Gap);
        Assert.All(stones, s => Assert.InRange(s.Center.Length, 3 + D / 2 - 0.3, 20 - D / 2 + 0.01));

        // Пустая середина, а лучей на внешнем круге больше, чем на внутреннем.
        int inner = stones.Count(s => Math.Abs(s.Center.Length - (3 + D / 2)) < 0.5);
        int outer = stones.Count(s => s.Center.Length > 16);
        Assert.True(inner >= 6, $"у середины {inner} лучей");
        Assert.True(outer > 2 * inner, $"снаружи {outer}, у середины {inner}");

        // Стразы одного луча лежат на одной прямой.
        foreach (IGrouping<int, PlacedStone> ray in stones.GroupBy(s => s.RowId).Where(g => g.Count() > 2))
        {
            Point2D a = ray.First().Center, b = ray.Last().Center - a;
            Assert.All(ray, s => Assert.True(Math.Abs(b.X * (s.Center - a).Y - b.Y * (s.Center - a).X) < 1e-6));
        }
    }

    [Fact]
    public void Rays_NoHole_PutsOneStoneInTheMiddle()
    {
        List<PlacedStone> stones = PatternFills.Rays(new[] { Circle(12) }, D, Gap, 0.5, 0, 0);
        AssertNoOverlaps(stones, Gap);
        Assert.Contains(stones, s => s.Center.Length < 0.3);
    }

    [Fact]
    public void Streaks_LieOnVerticalLines_WithBreaks()
    {
        List<PlacedStone> stones = PatternFills.Streaks(new[] { Rect(40, 60) }, D, Gap, 0.8, 90, 8, 3, null, 1, 0);
        AssertNoOverlaps(stones, Gap);
        Assert.NotEmpty(stones);

        // Линии вертикальные: у страз одной линии одинаковый X; соседние линии — через камень + 0,8 мм.
        List<double> xs = stones.GroupBy(s => s.RowId).Select(g => g.Average(s => s.Center.X)).OrderBy(x => x).ToList();
        foreach (IGrouping<int, PlacedStone> line in stones.GroupBy(s => s.RowId))
        {
            Assert.All(line, s => Assert.Equal(line.First().Center.X, s.Center.X, 6));
        }

        for (int i = 1; i < xs.Count; i++)
        {
            Assert.Equal(D + 0.8, xs[i] - xs[i - 1], 3);
        }

        // Разрывы есть: стразы занимают заметно меньше мест, чем сплошные линии.
        double full = xs.Count * (60 - D) / (D + Gap);
        Assert.InRange(stones.Count, full * 0.4, full * 0.9);
    }

    [Fact]
    public void Streaks_FadeDown_SparserAtTheBottom()
    {
        List<PlacedStone> stones = PatternFills.Streaks(new[] { Rect(60, 60) }, D, Gap, 0.8, 90, 8, 3, FadeDirection.Down, 4, 0);
        int top = stones.Count(s => s.Center.Y > 45);
        int bottom = stones.Count(s => s.Center.Y < 15);
        Assert.True(bottom * 2 < top, $"сверху {top}, снизу {bottom}");
    }

    [Fact]
    public void Streaks_SameVariantSameLayout_OtherVariantDiffers()
    {
        var shape = new[] { Circle(15) };
        List<PlacedStone> a = PatternFills.Streaks(shape, D, Gap, 0.8, 90, 6, 3, null, 5, 0);
        List<PlacedStone> b = PatternFills.Streaks(shape, D, Gap, 0.8, 90, 6, 3, null, 5, 0);
        List<PlacedStone> c = PatternFills.Streaks(shape, D, Gap, 0.8, 90, 6, 3, null, 6, 0);
        Assert.Equal(a.Select(s => s.Center), b.Select(s => s.Center));
        Assert.NotEqual(a.Select(s => s.Center), c.Select(s => s.Center));
    }

    [Fact]
    public void Methods_F15_F16_UseTheirParameters()
    {
        var p = new MethodParameters { GapMm = Gap, CenterHoleMm = 5 };
        IReadOnlyList<PlacedStone> rays = MethodRunner.Run(MethodKind.F15, new[] { Circle(15) }, D, p).Stones;
        Assert.All(rays, s => Assert.True(s.Center.Length > 5));

        p = new MethodParameters { GapMm = Gap, StreakAngleDeg = 0 };
        IReadOnlyList<PlacedStone> streaks = MethodRunner.Run(MethodKind.F16, new[] { Rect(40, 30) }, D, p).Stones;
        foreach (IGrouping<int, PlacedStone> line in streaks.GroupBy(s => s.RowId))
        {
            Assert.All(line, s => Assert.Equal(line.First().Center.Y, s.Center.Y, 6));
        }
    }
}
