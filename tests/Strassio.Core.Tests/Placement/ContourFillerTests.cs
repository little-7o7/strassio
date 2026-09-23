using System;
using System.Linq;
using Strassio.Core.Geometry;
using Strassio.Core.Placement;

namespace Strassio.Core.Tests.Placement;

public class ContourFillerTests
{
    [Fact]
    public void Kant_OnSquare_RowsAreRectanglesNotRoundedCorners()
    {
        // Замечание автора «пропускает стразы»: ряды канта строились по линии равного расстояния,
        // у которой углы тем круглее, чем дальше ряд от края. Ряды переставали быть параллельными
        // и в углах разъезжались. Теперь кольцо — смещённая копия контура с острым углом, поэтому
        // каждый камень стоит ровно на своей глубине: 1,2 / 3,6 / 6,0 мм от края.
        const double D = 2.4;
        var stones = ContourFiller.Fill(new[] { Square(40) }, new ContourFillOptions
        {
            StoneDiameterMm = D,
            GapMm = 0,
            MaxRings = 3,
            FillCenter = false,
        });

        Assert.NotEmpty(stones);

        double[] levels = { D / 2, D / 2 + D, D / 2 + 2 * D };
        foreach (PlacedStone s in stones)
        {
            double depth = DepthInSquare(s.Center, 40);
            double nearest = levels.Select(l => Math.Abs(depth - l)).Min();
            Assert.True(nearest < 0.25, $"камень стоит на глубине {depth:0.00} мм — это не ряд канта");
        }

        // И все три ряда на месте.
        foreach (double level in levels)
        {
            Assert.Contains(stones, s => Math.Abs(DepthInSquare(s.Center, 40) - level) < 0.25);
        }
    }

    /// <summary>Насколько точка внутри квадрата удалена от ближайшей стороны.</summary>
    private static double DepthInSquare(Point2D p, double side) =>
        Math.Min(Math.Min(p.X, side - p.X), Math.Min(p.Y, side - p.Y));

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
    [Fact]
    public void LetterO_RingsGoAroundHole_HoleStaysEmpty()
    {
        // Квадрат 40×40 с квадратной дыркой 16×16 посередине — как буква «О».
        var outer = Square(40);
        var hole = Curve.FromPolyline(
            new[] { new Point2D(12, 12), new Point2D(28, 12), new Point2D(28, 28), new Point2D(12, 28) },
            isClosed: true);
        var options = new ContourFillOptions { StoneDiameterMm = 2.4, GapMm = 0.2 };

        var stones = ContourFiller.Fill(new[] { outer, hole }, options);

        Assert.False(HasAnyOverlap(stones));

        FlattenedCurve holeFlat = CurveFlattener.Flatten(hole);
        FlattenedCurve outerFlat = CurveFlattener.Flatten(outer);
        foreach (PlacedStone s in stones)
        {
            // Ни одна страза не залезает в дырку и не вылезает за внешний край.
            Assert.False(PointInPolygon.IsInside(holeFlat, s.Center), $"Страза в дырке: {s.Center}.");
            Assert.True(PointInPolygon.DistanceToBoundary(holeFlat, s.Center) >= 1.2 - 0.05);
            Assert.True(PointInPolygon.DistanceToBoundary(outerFlat, s.Center) >= 1.2 - 0.05);
        }

        // Первый ряд вокруг дырки есть: вдоль её края стоят стразы (у стороны y=12 снизу).
        Assert.Contains(stones, s => Math.Abs(s.Center.Y - (12 - 1.2)) < 0.3 && s.Center.X > 14 && s.Center.X < 26);
    }

    [Fact]
    public void StarShape_ContourFill_LeavesNoBigGaps()
    {
        // Раньше ряды на звезде выворачивались наизнанку и оставляли дыры. Проверяем: в любой
        // точке звезды, где страза поместилась бы, до ближайшей стразы недалеко.
        const int spikes = 5;
        var points = new System.Collections.Generic.List<Point2D>();
        for (int i = 0; i < spikes * 2; i++)
        {
            double r = i % 2 == 0 ? 40 : 15;
            double angle = Math.PI / 2 + i * Math.PI / spikes;
            points.Add(new Point2D(r * Math.Cos(angle), -r * Math.Sin(angle)));
        }

        var star = Curve.FromPolyline(points, isClosed: true);
        FlattenedCurve flat = CurveFlattener.Flatten(star);
        var options = new ContourFillOptions { StoneDiameterMm = 2.4, GapMm = 0.2 };

        var stones = ContourFiller.Fill(star, options);

        Assert.False(HasAnyOverlap(stones));

        for (double x = -40; x <= 40; x += 0.5)
        {
            for (double y = -40; y <= 40; y += 0.5)
            {
                var p = new Point2D(x, y);
                if (!PointInPolygon.IsInside(flat, p) || PointInPolygon.DistanceToBoundary(flat, p) < 1.2)
                {
                    continue;
                }

                double nearest = stones.Min(s => Point2D.Distance(s.Center, p));
                // Шаг ряда 2,6 мм: в плотной расстановке любая точка ближе ~2,6 мм к центру стразы.
                Assert.True(nearest < 2.9, $"Дыра около ({x}; {y}): ближайшая страза в {nearest:0.##} мм.");
            }
        }
    }
    [Fact]
    public void BigSquare_TenThousandStones_IsFast()
    {
        // Цель из CLAUDE.md: 10 000 камней, расчёт в Core — доли секунды (с запасом на медленный компьютер).
        var options = new ContourFillOptions { StoneDiameterMm = 2.4, GapMm = 0.2 };

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var stones = ContourFiller.Fill(Square(260), options);
        watch.Stop();

        Assert.True(stones.Count > 9000, $"Ожидали около 10 000 страз, получили {stones.Count}.");
        Assert.True(watch.ElapsedMilliseconds < 3000, $"Слишком долго: {watch.ElapsedMilliseconds} мс на {stones.Count} страз.");
    }
}
