using System.Diagnostics;
using Strassio.Core.Geometry;
using Strassio.Core.Methods;
using Strassio.Core.Placement;

namespace Strassio.Core.Tests.Placement;

/// <summary>F13 «По толщине штриха»: один ряд по середине, размер камня следует ширине (docs/SPEC.md, раздел 5).</summary>
public class StrokeRowTests
{
    private static readonly double[] Sizes = { 2.0, 2.8, 3.9 };
    private const double Gap = 0.3;

    private static Curve Polygon(params double[] xy) => Curve.FromPolyline(
        Enumerable.Range(0, xy.Length / 2).Select(i => new Point2D(xy[2 * i], xy[2 * i + 1])).ToList(), true);

    private static Curve Ellipse(double cx, double cy, double rx, double ry, int n = 96)
    {
        var pts = new List<Point2D>();
        for (int i = 0; i < n; i++)
        {
            double a = 2 * Math.PI * i / n;
            pts.Add(new Point2D(cx + rx * Math.Cos(a), cy + ry * Math.Sin(a)));
        }

        return Curve.FromPolyline(pts, true);
    }

    /// <summary>Прямой мазок вдоль X: кончики тонкие, середина толщиной <paramref name="maxWidth"/>.</summary>
    private static Curve Taper(double length, double maxWidth, double tipWidth = 0.6)
    {
        var top = new List<Point2D>();
        var bottom = new List<Point2D>();
        for (int i = 0; i <= 60; i++)
        {
            double t = i / 60.0;
            double half = (tipWidth + (maxWidth - tipWidth) * Math.Sin(Math.PI * t)) / 2;
            top.Add(new Point2D(length * t, half));
            bottom.Add(new Point2D(length * t, -half));
        }

        bottom.Reverse();
        return Curve.FromPolyline(top.Concat(bottom).ToList(), true);
    }

    private static void AssertClean(IReadOnlyList<PlacedStone> stones, IReadOnlyList<Curve> shape)
    {
        Assert.NotEmpty(stones);
        List<FlattenedCurve> flats = shape.Select(c => CurveFlattener.Flatten(c)).ToList();
        foreach (PlacedStone s in stones)
        {
            int inside = flats.Count(f => PointInPolygon.IsInside(f, s.Center));
            Assert.True(inside % 2 == 1, $"страза {s.Center} вне формы");
            double edge = flats.Min(f => PointInPolygon.DistanceToBoundary(f, s.Center));
            double allowed = s.DiameterMm == Sizes[0] ? s.DiameterMm / 4 : s.DiameterMm / 2;
            Assert.True(edge >= allowed - 0.05, $"страза {s.DiameterMm} в {s.Center} вылезает за край ({edge:0.00})");
        }

        for (int i = 0; i < stones.Count; i++)
        {
            for (int j = i + 1; j < stones.Count; j++)
            {
                double need = (stones[i].DiameterMm + stones[j].DiameterMm) / 2 + Gap - 0.05;
                Assert.True(Point2D.Distance(stones[i].Center, stones[j].Center) >= need, $"накладываются {stones[i].Center} и {stones[j].Center}");
            }
        }
    }

    /// <summary>Просветы между соседями по порядку вдоль X.</summary>
    private static List<double> GapsAlongX(IReadOnlyList<PlacedStone> stones)
    {
        List<PlacedStone> row = stones.OrderBy(s => s.Center.X).ToList();
        return Enumerable.Range(1, row.Count - 1)
            .Select(i => Point2D.Distance(row[i - 1].Center, row[i].Center) - (row[i - 1].DiameterMm + row[i].DiameterMm) / 2)
            .ToList();
    }

    [Fact]
    public void EvenStrip_OneSize_EvenGaps_ReachesEnds()
    {
        Curve[] strip = { Polygon(0, 0, 40, 0, 40, 3, 0, 3) };
        List<PlacedStone> stones = StrokeRow.Place(strip, Sizes, Gap, 0);

        AssertClean(stones, strip);
        Assert.All(stones, s => Assert.Equal(2.8, s.DiameterMm));
        Assert.All(stones, s => Assert.InRange(s.Center.Y, 1.3, 1.7));
        List<double> gaps = GapsAlongX(stones);
        Assert.True(gaps.Max() - gaps.Min() < 0.1, $"просветы {gaps.Min():0.00}–{gaps.Max():0.00}");
        Assert.True(gaps.Min() >= Gap - 0.02);
        Assert.InRange(stones.Min(s => s.Center.X), 0, 2.2);
        Assert.InRange(stones.Max(s => s.Center.X), 37.8, 40);
    }

    [Fact]
    public void TaperedStroke_SmallAtTips_BigInTheMiddle()
    {
        Curve[] stroke = { Taper(50, 5) };
        List<PlacedStone> stones = StrokeRow.Place(stroke, Sizes, Gap, 0);

        AssertClean(stones, stroke);
        List<PlacedStone> row = stones.OrderBy(s => s.Center.X).ToList();
        Assert.Equal(2.0, row[0].DiameterMm);
        Assert.Equal(2.0, row[row.Count - 1].DiameterMm);
        Assert.Contains(row, s => s.DiameterMm == 3.9 && Math.Abs(s.Center.X - 25) < 4);
        Assert.Contains(row, s => s.DiameterMm == 2.8);

        // Размер растёт к середине и падает к концу — без случайных «бусин» другого размера.
        int peak = row.FindIndex(s => s.DiameterMm == 3.9);
        for (int i = 1; i <= peak; i++)
        {
            Assert.True(row[i].DiameterMm >= row[i - 1].DiameterMm, $"камень {i} меньше предыдущего");
        }

        int lastPeak = row.FindLastIndex(s => s.DiameterMm == 3.9);
        for (int i = lastPeak + 1; i < row.Count; i++)
        {
            Assert.True(row[i].DiameterMm <= row[i - 1].DiameterMm, $"камень {i} больше предыдущего");
        }

        // Кончики заняты: до острия не больше камня.
        Assert.True(row[0].Center.X < 3.5, $"первый камень в {row[0].Center.X:0.0}");
        Assert.True(row[row.Count - 1].Center.X > 46.5, $"последний камень в {row[row.Count - 1].Center.X:0.0}");
        List<double> gaps = GapsAlongX(stones);
        Assert.True(gaps.Max() - gaps.Min() < 0.25, $"просветы {gaps.Min():0.00}–{gaps.Max():0.00}");
    }

    [Fact]
    public void Margin_KeepsStonesAwayFromEdge()
    {
        Curve[] stroke = { Taper(50, 5) };
        List<PlacedStone> plain = StrokeRow.Place(stroke, Sizes, Gap, 0);
        List<PlacedStone> withMargin = StrokeRow.Place(stroke, Sizes, Gap, 0.6);

        Assert.Contains(plain, s => s.DiameterMm == 3.9);
        Assert.DoesNotContain(withMargin, s => s.DiameterMm == 3.9);
    }

    [Fact]
    public void LetterT_BothStrokesFilled_NoOverlaps()
    {
        Curve[] t = { Polygon(0, 36, 13, 36, 13, 0, 17, 0, 17, 36, 30, 36, 30, 40, 0, 40) };
        List<PlacedStone> stones = StrokeRow.Place(t, Sizes, Gap, 0);

        AssertClean(stones, t);
        Assert.Contains(stones, s => s.Center.Y < 3);
        Assert.Contains(stones, s => s.Center.X < 3 && s.Center.Y > 36);
        Assert.Contains(stones, s => s.Center.X > 27 && s.Center.Y > 36);

        // Ножка заполнена без дыр: ни один промежуток по высоте не больше камня.
        List<PlacedStone> stem = stones.Where(s => Math.Abs(s.Center.X - 15) < 0.5 && s.Center.Y < 34).OrderBy(s => s.Center.Y).ToList();
        Assert.True(stem.Count >= 8);
        for (int i = 1; i < stem.Count; i++)
        {
            Assert.True(stem[i].Center.Y - stem[i - 1].Center.Y < 4.5);
        }
    }

    [Fact]
    public void LetterO_ClosedRow_EvenAllAround()
    {
        Curve[] o = { Ellipse(0, 0, 15, 20), Ellipse(0, 0, 11.5, 16.5) };
        List<PlacedStone> stones = StrokeRow.Place(o, Sizes, Gap, 0);

        AssertClean(stones, o);
        Assert.True(stones.Count > 25, $"{stones.Count} страз");

        // Соседи по кругу: ближайший камень у каждого — с одинаковым просветом.
        List<double> nearest = stones.Select(s => stones.Where(o2 => !o2.Center.Equals(s.Center))
            .Min(o2 => Point2D.Distance(o2.Center, s.Center) - (o2.DiameterMm + s.DiameterMm) / 2)).ToList();
        Assert.True(nearest.Max() - nearest.Min() < 0.3, $"просветы {nearest.Min():0.00}–{nearest.Max():0.00}");
    }

    [Fact]
    public void ThroughMethodRunner_UsesSizesFromThinToMain()
    {
        var table = new Dictionary<string, double> { ["ss3"] = 1.4, ["ss6"] = 2.0, ["ss10"] = 2.8, ["ss16"] = 3.9, ["ss20"] = 4.7 };
        Curve[] stroke = { Taper(60, 6) };
        IReadOnlyList<PlacedStone> stones = MethodRunner.Run(
            MethodKind.F13, stroke, 3.9, new MethodParameters { GapMm = Gap, ThinSize = "ss6" }, table).Stones;

        Assert.NotEmpty(stones);
        Assert.All(stones, s => Assert.Contains(s.DiameterMm, new[] { 2.0, 2.8, 3.9 }));
        Assert.Contains(stones, s => s.DiameterMm == 2.0);
        Assert.Contains(stones, s => s.DiameterMm == 3.9);
    }

    [Fact]
    public void LongStroke_IsFast()
    {
        Curve[] stroke = { Taper(400, 8) };
        var watch = Stopwatch.StartNew();
        List<PlacedStone> stones = StrokeRow.Place(stroke, Sizes, Gap, 0);
        Assert.True(stones.Count > 80);
        Assert.True(watch.ElapsedMilliseconds < 3000, $"{watch.ElapsedMilliseconds} мс");
    }
}
