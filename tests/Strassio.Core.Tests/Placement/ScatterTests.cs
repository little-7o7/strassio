using Strassio.Core.Geometry;
using Strassio.Core.Methods;
using Strassio.Core.Placement;

namespace Strassio.Core.Tests.Placement;

/// <summary>«Растворение» (F14) и «Россыпь» вокруг дизайна (docs/SPEC.md, разделы 5 и 8).</summary>
public class ScatterTests
{
    private const double D = 2.4;
    private const double Gap = 0.2;

    private static Curve Rect(double w, double h) => Curve.FromPolyline(
        new List<Point2D> { new(0, 0), new(w, 0), new(w, h), new(0, h) }, true);

    private static Curve Circle(double r) => Curve.FromPolyline(
        Enumerable.Range(0, 120).Select(i => new Point2D(r * Math.Cos(2 * Math.PI * i / 120), r * Math.Sin(2 * Math.PI * i / 120))).ToList(), true);

    private static void AssertNoOverlaps(IReadOnlyList<PlacedStone> stones, double gap)
    {
        for (int i = 0; i < stones.Count; i++)
        {
            for (int j = i + 1; j < stones.Count; j++)
            {
                double need = (stones[i].DiameterMm + stones[j].DiameterMm) / 2 + gap - 0.01;
                Assert.True(Point2D.Distance(stones[i].Center, stones[j].Center) >= need, $"камни {i} и {j} налезают");
            }
        }
    }

    [Fact]
    public void Dissolve_IsFullHoneycombOnOneSide_AndSparseOnTheOther()
    {
        List<PlacedStone> stones = Scatter.Dissolve(new[] { Rect(60, 30) }, D, Gap, 5, FadeDirection.Right, 0);

        AssertNoOverlaps(stones, Gap);
        Assert.All(stones, s => Assert.InRange(s.Center.X, D / 2 - 0.01, 60 - D / 2 + 0.01));
        Assert.All(stones, s => Assert.InRange(s.Center.Y, D / 2 - 0.01, 30 - D / 2 + 0.01));

        // Слева — соты целиком: на полосе 10 мм площадь сот ≈ 10 × 28 / (2,6² · √3/2) ≈ 48 камней.
        int left = stones.Count(s => s.Center.X < 11.2);
        int right = stones.Count(s => s.Center.X > 48.8);
        Assert.InRange(left, 40, 60);
        Assert.True(right * 3 < left, $"справа {right}, слева {left}");
        Assert.True(right > 0);
    }

    [Fact]
    public void Dissolve_ToEdge_KeepsMiddleDense()
    {
        List<PlacedStone> stones = Scatter.Dissolve(new[] { Circle(20) }, D, Gap, 4, FadeDirection.Edge, 0);
        AssertNoOverlaps(stones, Gap);

        double Density(double from, double to)
        {
            int n = stones.Count(s => s.Center.Length >= from && s.Center.Length < to);
            return n / (Math.PI * (to * to - from * from));
        }

        Assert.True(Density(0, 8) > 2 * Density(14, 20));
    }

    [Fact]
    public void Dissolve_SameVariantSameLayout_OtherVariantDiffers()
    {
        var shape = new[] { Circle(15) };
        List<PlacedStone> a = Scatter.Dissolve(shape, D, Gap, 4, FadeDirection.Up, 0, 7);
        List<PlacedStone> b = Scatter.Dissolve(shape, D, Gap, 4, FadeDirection.Up, 0, 7);
        List<PlacedStone> c = Scatter.Dissolve(shape, D, Gap, 4, FadeDirection.Up, 0, 8);

        Assert.Equal(a.Select(s => s.Center), b.Select(s => s.Center));
        Assert.NotEqual(a.Select(s => s.Center), c.Select(s => s.Center));
    }

    [Fact]
    public void MethodF14_UsesFadeParameters()
    {
        var p = new MethodParameters { GapMm = Gap, FadeGapMm = 4, FadeTo = MethodChoices.FadeLeft };
        IReadOnlyList<PlacedStone> stones = MethodRunner.Run(MethodKind.F14, new[] { Rect(40, 20) }, D, p).Stones;

        Assert.NotEmpty(stones);
        Assert.True(stones.Count(s => s.Center.X > 30) > 2 * stones.Count(s => s.Center.X < 10));
    }

    [Fact]
    public void Splash_AroundStones_StaysOutside_AndGetsSmallerFurtherAway()
    {
        // «Дизайн» — кольцо из 24 страз радиусом 10 мм.
        List<PlacedStone> design = Enumerable.Range(0, 24)
            .Select(i => new PlacedStone(new Point2D(10 * Math.Cos(Math.PI * i / 12), 10 * Math.Sin(Math.PI * i / 12)), 2.8, false))
            .ToList();
        double[] sizes = { 2.8, 2.4, 2.0, 1.6 };
        List<PlacedStone> splash = Scatter.Splash(Array.Empty<Curve>(), design, Array.Empty<PlacedStone>(), sizes, 0.5, 12, 4);

        Assert.NotEmpty(splash);
        AssertNoOverlaps(splash, 0.5);

        // До края ближайшей стразы дизайна — не меньше просвета плюс радиус, и не дальше ширины россыпи.
        double Edge(PlacedStone s) => design.Min(t => Point2D.Distance(t.Center, s.Center) - t.DiameterMm / 2);
        Assert.All(splash, s => Assert.InRange(Edge(s), 0.5 + s.DiameterMm / 2 - 0.01, 12.01));

        List<PlacedStone> near = splash.Where(s => Edge(s) < 4).ToList();
        List<PlacedStone> far = splash.Where(s => Edge(s) > 8).ToList();
        Assert.NotEmpty(near);
        Assert.NotEmpty(far);
        Assert.True(near.Average(s => s.DiameterMm) > far.Average(s => s.DiameterMm));
    }

    [Fact]
    public void Splash_AroundShape_NeverGoesInside_AndAvoidsObstacles()
    {
        Curve square = Rect(20, 20);
        var obstacle = new PlacedStone(new Point2D(25, 10), 3.9, false);
        List<PlacedStone> splash = Scatter.Splash(new[] { square }, Array.Empty<PlacedStone>(), new[] { obstacle }, new[] { 2.4, 2.0 }, 0.4, 8, 3);

        Assert.NotEmpty(splash);
        Assert.All(splash, s => Assert.False(
            s.Center.X > -s.DiameterMm / 2 && s.Center.X < 20 + s.DiameterMm / 2 &&
            s.Center.Y > -s.DiameterMm / 2 && s.Center.Y < 20 + s.DiameterMm / 2, $"камень {s.Center} налезает на форму"));
        AssertNoOverlaps(splash.Append(obstacle).ToList(), 0.4);
    }
}
