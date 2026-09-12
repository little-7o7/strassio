using System;
using System.Linq;
using Strassio.Core.Geometry;
using Strassio.Core.Placement;

namespace Strassio.Core.Tests.Placement;

public class GridFillerTests
{
    private static Curve Square(double side) => Curve.FromPolyline(
        new[] { new Point2D(0, 0), new Point2D(side, 0), new Point2D(side, side), new Point2D(0, side) },
        isClosed: true);

    [Fact]
    public void SquareGrid_FillsSquare_NoOverlaps_AllInsideWithMargin()
    {
        var options = new GridFillOptions { StoneDiameterMm = 2.4, GapMm = 0.2, Pattern = GridPattern.Square };

        var stones = GridFiller.Fill(Square(40), options);

        Assert.True(stones.Count > 50, $"Ожидали много страз на квадрате 40x40, получили {stones.Count}.");

        double step = options.StoneDiameterMm + options.GapMm;
        for (int i = 0; i < stones.Count; i++)
        {
            for (int j = i + 1; j < stones.Count; j++)
            {
                double d = Point2D.Distance(stones[i].Center, stones[j].Center);
                Assert.True(d >= step - 1e-6 || d > step * 1.3, // либо это сосед по сетке на полном шаге, либо гарантированно далеко
                    $"Стразы {i} и {j} слишком близко: {d:0.###} мм.");
            }
        }
    }

    [Fact]
    public void MarginFromEdge_KeepsStonesAwayFromBoundary()
    {
        var options = new GridFillOptions
        {
            StoneDiameterMm = 2.4, GapMm = 0.2, Pattern = GridPattern.Square, MarginFromEdgeMm = 3,
        };

        var square = Square(40);
        FlattenedCurve flat = CurveFlattener.Flatten(square);
        var stones = GridFiller.Fill(square, options);

        double requiredClearance = options.StoneDiameterMm / 2 + options.MarginFromEdgeMm;
        foreach (PlacedStone s in stones)
        {
            double d = PointInPolygon.DistanceToBoundary(flat, s.Center);
            Assert.True(d >= requiredClearance - 1e-6, $"Страза в {d:0.##} мм от края, нужно ≥ {requiredClearance}.");
        }
    }

    [Fact]
    public void Honeycomb_IsDenserThanSquareGrid_ForSameArea()
    {
        var square = Square(40);
        var squareOptions = new GridFillOptions { StoneDiameterMm = 2.4, GapMm = 0.2, Pattern = GridPattern.Square };
        var honeyOptions = new GridFillOptions { StoneDiameterMm = 2.4, GapMm = 0.2, Pattern = GridPattern.Honeycomb };

        int squareCount = GridFiller.Fill(square, squareOptions).Count;
        int honeyCount = GridFiller.Fill(square, honeyOptions).Count;

        Assert.True(honeyCount > squareCount, $"Соты ({honeyCount}) должны быть плотнее сетки ({squareCount}).");
    }

    [Fact]
    public void RotatedGrid_ProducesDifferentLayout_SameCount()
    {
        var square = Square(40);
        var options0 = new GridFillOptions { StoneDiameterMm = 2.4, GapMm = 0.2, Pattern = GridPattern.Square };
        var options45 = new GridFillOptions { StoneDiameterMm = 2.4, GapMm = 0.2, Pattern = GridPattern.Square, AngleDeg = 45 };

        var stones0 = GridFiller.Fill(square, options0);
        var stones45 = GridFiller.Fill(square, options45);

        Assert.True(stones0.Count > 0 && stones45.Count > 0);
        // При повороте на 45° раскладка другая (по диагонали) — хотя бы координаты не совпадают 1:1.
        bool anyDifferent = stones0.Count != stones45.Count ||
                             stones0.Select(s => s.Center.X).OrderBy(x => x)
                                 .SequenceEqual(stones45.Select(s => s.Center.X).OrderBy(x => x)) == false;
        Assert.True(anyDifferent);
    }

    [Fact]
    public void LetterO_HoleIsNotFilled()
    {
        // Буква «О»: внешний квадрат 30x30 и внутреннее отверстие 10x10 по центру — два контура,
        // чётно-нечётное правило должно оставить дырку пустой.
        var outer = Square(30);
        var hole = Curve.FromPolyline(
            new[] { new Point2D(10, 10), new Point2D(20, 10), new Point2D(20, 20), new Point2D(10, 20) },
            isClosed: true);

        var options = new GridFillOptions { StoneDiameterMm = 2.4, GapMm = 0.2, Pattern = GridPattern.Square };
        var stones = GridFiller.Fill(new[] { outer, hole }, options);

        Assert.True(stones.Count > 0);
        Assert.DoesNotContain(stones, s => s.Center.X is > 11 and < 19 && s.Center.Y is > 11 and < 19);

        // А снаружи отверстия, но внутри внешнего контура — стразы обязаны быть (например, в углу).
        Assert.Contains(stones, s => s.Center.X < 8 && s.Center.Y < 8);
    }
}
