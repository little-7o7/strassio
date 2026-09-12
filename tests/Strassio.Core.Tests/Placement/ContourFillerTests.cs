using System;
using System.Linq;
using Strassio.Core.Geometry;
using Strassio.Core.Placement;

namespace Strassio.Core.Tests.Placement;

public class ContourFillerTests
{
    private static Curve Square(double side) => Curve.FromPolyline(
        new[] { new Point2D(0, 0), new Point2D(side, 0), new Point2D(side, side), new Point2D(0, side) },
        isClosed: true);

    private static bool HasAnyOverlap(System.Collections.Generic.IReadOnlyList<PlacedStone> stones)
    {
        for (int i = 0; i < stones.Count; i++)
        {
            for (int j = i + 1; j < stones.Count; j++)
            {
                double required = stones[i].DiameterMm / 2 + stones[j].DiameterMm / 2;
                if (Point2D.Distance(stones[i].Center, stones[j].Center) < required - 0.02)
                {
                    return true;
                }
            }
        }

        return false;
    }

    [Fact]
    public void F3_ContourFill_CoversWholeSquare_NoOverlaps_CenterIsFilled()
    {
        var options = new ContourFillOptions { StoneDiameterMm = 2.4, GapMm = 0.2 };

        var stones = ContourFiller.Fill(Square(40), options);

        Assert.True(stones.Count > 50, $"Ожидали много страз на квадрате 40x40, получили {stones.Count}.");
        Assert.False(HasAnyOverlap(stones));

        // Центр формы (20,20) должен быть недалеко от какой-то стразы — не должно остаться дыры
        // посередине из-за того, что кольца туда не дотянулись, а сетка не добила.
        var center = new Point2D(20, 20);
        double nearestToCenterDist = stones.Min(s => Point2D.Distance(s.Center, center));
        Assert.True(nearestToCenterDist < 3, $"Ближайшая к центру страза в {nearestToCenterDist:0.##} мм — похоже, в центре дыра.");
    }

    [Fact]
    public void F5_EdgeOnly_TwoRings_CenterStaysEmpty()
    {
        var options = new ContourFillOptions { StoneDiameterMm = 2.4, GapMm = 0.2, MaxRings = 2, FillCenter = false };

        var stones = ContourFiller.Fill(Square(40), options);

        Assert.True(stones.Count > 0);
        Assert.False(HasAnyOverlap(stones));

        var center = new Point2D(20, 20);
        Assert.DoesNotContain(stones, s => Point2D.Distance(s.Center, center) < 10);
    }

    [Fact]
    public void F4_EdgeRingsPlusGridInside_FillsWholeSquare()
    {
        var options = new ContourFillOptions { StoneDiameterMm = 2.4, GapMm = 0.2, MaxRings = 2, FillCenter = true };

        var stones = ContourFiller.Fill(Square(40), options);

        Assert.False(HasAnyOverlap(stones));

        var center = new Point2D(20, 20);
        double nearestToCenterDist = stones.Min(s => Point2D.Distance(s.Center, center));
        Assert.True(nearestToCenterDist < 3, "Сетка внутри должна добить центр даже при ограниченном числе рядов по краю.");
    }

    [Fact]
    public void MarginFromEdge_KeepsFirstRingAwayFromBoundary()
    {
        var options = new ContourFillOptions { StoneDiameterMm = 2.4, GapMm = 0.2, MarginFromEdgeMm = 3, MaxRings = 1, FillCenter = false };

        var square = Square(40);
        FlattenedCurve flat = CurveFlattener.Flatten(square);
        var stones = ContourFiller.Fill(square, options);

        double requiredClearance = options.StoneDiameterMm / 2 + options.MarginFromEdgeMm;
        foreach (PlacedStone s in stones)
        {
            double d = PointInPolygon.DistanceToBoundary(flat, s.Center);
            Assert.True(d >= requiredClearance - 0.05, $"Страза в {d:0.##} мм от края, нужно ≥ {requiredClearance}.");
        }
    }

    [Fact]
    public void StarShape_ContourFill_NoOverlapsAnywhere()
    {
        const int spikes = 5;
        const double outerR = 40;
        const double innerR = 15;
        var points = new System.Collections.Generic.List<Point2D>();
        for (int i = 0; i < spikes * 2; i++)
        {
            double r = i % 2 == 0 ? outerR : innerR;
            double angle = Math.PI / 2 + i * Math.PI / spikes;
            points.Add(new Point2D(r * Math.Cos(angle), -r * Math.Sin(angle)));
        }

        var star = Curve.FromPolyline(points, isClosed: true);
        var options = new ContourFillOptions { StoneDiameterMm = 2.4, GapMm = 0.2 };

        var stones = ContourFiller.Fill(star, options);

        Assert.True(stones.Count > 20);
        Assert.False(HasAnyOverlap(stones));
    }
}
