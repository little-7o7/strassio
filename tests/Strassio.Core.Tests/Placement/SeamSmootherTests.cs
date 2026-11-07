using System.Diagnostics;
using Strassio.Core.Geometry;
using Strassio.Core.Methods;
using Strassio.Core.Placement;

namespace Strassio.Core.Tests.Placement;

/// <summary>Шов в контурной заливке: цепочка дырок там, где середина упирается в кольцо, расходится.</summary>
public class SeamSmootherTests
{
    private const double D = 2.4;
    private const double Gap = 0.2;

    /// <summary>Изогнутая лента шириной 14 мм — у неё кольца сходятся посередине длинным швом.</summary>
    private static Curve Ribbon()
    {
        var left = new List<Point2D>();
        var right = new List<Point2D>();
        for (int i = 0; i <= 80; i++)
        {
            double t = i / 80.0;
            var c = new Point2D(70 * t, 12 * Math.Sin(2 * Math.PI * t * 0.75));
            var c2 = new Point2D(70 * (t + 1e-3), 12 * Math.Sin(2 * Math.PI * (t + 1e-3) * 0.75));
            Point2D dir = (c2 - c).Normalized();
            var n = new Point2D(-dir.Y, dir.X);
            left.Add(c + n * 7);
            right.Add(c - n * 7);
        }

        right.Reverse();
        return Curve.FromPolyline(left.Concat(right).ToList(), true);
    }

    private static Curve Leaf()
    {
        var pts = new List<Point2D>();
        for (int i = 0; i <= 60; i++)
        {
            pts.Add(new Point2D(i, 12 * Math.Sin(Math.PI * i / 60)));
        }

        for (int i = 59; i > 0; i--)
        {
            pts.Add(new Point2D(i, -12 * Math.Sin(Math.PI * i / 60)));
        }

        return Curve.FromPolyline(pts, true);
    }

    private static List<PlacedStone> Raw(Curve shape) =>
        AdvancedFillers.Lengthwise(new[] { shape }, D, Gap, 0)
        ?? ContourFiller.Fill(new[] { shape }, new ContourFillOptions { StoneDiameterMm = D, GapMm = Gap, FillCenter = true });

    [Theory]
    [InlineData("ribbon")]
    [InlineData("leaf")]
    public void Seam_HasFewerHoles_NoOverlaps_StaysInside(string name)
    {
        Curve shape = name == "ribbon" ? Ribbon() : Leaf();
        Curve[] contours = { shape };
        List<PlacedStone> before = Raw(shape);
        List<PlacedStone> after = SeamSmoother.Smooth(before, contours, Gap, 0);

        int holesBefore = SeamSmoother.CountHoles(before, contours, Gap, 0);
        int holesAfter = SeamSmoother.CountHoles(after, contours, Gap, 0);
        Assert.True(holesBefore >= 5, $"до: {holesBefore} дырок — пример не показывает шов");
        Assert.True(holesAfter <= holesBefore / 2, $"дырок {holesBefore} → {holesAfter}");
        Assert.True(after.Count >= before.Count);

        FlattenedCurve flat = CurveFlattener.Flatten(shape);
        foreach (PlacedStone s in after)
        {
            Assert.True(PointInPolygon.DistanceToBoundary(flat, s.Center) >= s.DiameterMm / 2 - 0.05, $"камень {s.Center} вылез за край");
        }

        bool[] overlaps = IntersectionFixer.FindIndicesToRemove(after, Gap / 2 - 0.02, 0.01);  // заливка держит не меньше половины зазора
        Assert.DoesNotContain(true, overlaps);
    }

    [Fact]
    public void EdgeRing_DoesNotMove()
    {
        Curve[] contours = { Ribbon() };
        List<PlacedStone> before = Raw(contours[0]);
        List<PlacedStone> after = SeamSmoother.Smooth(before, contours, Gap, 0);
        FlattenedCurve flat = CurveFlattener.Flatten(contours[0]);

        // Крайний ряд (касается края) остался на месте.
        for (int i = 0; i < before.Count; i++)
        {
            if (PointInPolygon.DistanceToBoundary(flat, before[i].Center) < D / 2 + 0.2)
            {
                Assert.Equal(before[i].Center, after[i].Center);
            }
        }
    }

    [Fact]
    public void EvenHoneycomb_IsLeftAlone()
    {
        Curve[] rect = { Curve.FromPolyline(new List<Point2D> { new(0, 0), new(40, 0), new(40, 30), new(0, 30) }, true) };
        List<PlacedStone> grid = GridFiller.Fill(rect, new GridFillOptions { StoneDiameterMm = D, GapMm = Gap, Pattern = GridPattern.Honeycomb });
        List<PlacedStone> after = SeamSmoother.Smooth(grid, rect, Gap, 0);

        Assert.Equal(grid.Count, after.Count);
        Assert.Equal(grid.Select(s => s.Center), after.Select(s => s.Center));
    }

    [Fact]
    public void ContourFill_ThroughRunner_IsSmoothed_AndFast()
    {
        Curve[] big = { Curve.FromPolyline(Enumerable.Range(0, 240).Select(i => new Point2D(80 * Math.Cos(i * Math.PI / 120), 45 * Math.Sin(i * Math.PI / 120))).ToList(), true) };
        int raw = SeamSmoother.CountHoles(Raw(big[0]), big, Gap, 0);
        var watch = Stopwatch.StartNew();
        IReadOnlyList<PlacedStone> stones = MethodRunner.Run(MethodKind.F3, big, D, new MethodParameters { GapMm = Gap }).Stones;
        Assert.True(watch.Elapsed.TotalSeconds < 10, $"{watch.Elapsed.TotalSeconds:0.0} с");
        Assert.True(stones.Count > 1500);
        int smooth = SeamSmoother.CountHoles(stones, big, Gap, 0);
        Assert.True(smooth <= raw / 2, $"дырок {raw} → {smooth}");
    }
}
