using System;
using System.Linq;
using Strassio.Core.Geometry;
using Strassio.Core.Placement;

namespace Strassio.Core.Tests.Placement;

public class LineScattererTests
{
    private static Curve StraightLine10mm() =>
        Curve.FromPolyline(new[] { new Point2D(0, 0), new Point2D(10, 0) });

    [Fact]
    public void FitEven_OnStraightLine_FirstAndLastStoneAreAtTheEnds()
    {
        var options = new LineScatterOptions { StoneDiameterMm = 2, GapMm = 0, Mode = StepMode.FitEven };

        var stones = LineScatterer.Scatter(StraightLine10mm(), options);

        Assert.True(stones.Count >= 2);
        Assert.Equal(0, stones[0].Center.X, 6);
        Assert.Equal(10, stones[stones.Count - 1].Center.X, 6);

        // Подгонка: расстояние между соседними центрами одинаковое по всей линии.
        double step = stones[1].Center.X - stones[0].Center.X;
        for (int i = 1; i < stones.Count; i++)
        {
            double d = stones[i].Center.X - stones[i - 1].Center.X;
            Assert.Equal(step, d, 6);
        }
    }

    [Fact]
    public void ExactStep_LeavesRemainderGapAtTheEnd_NoStoneForcedThere()
    {
        // 10 мм линия, шаг 3 мм: стразы на 0, 3, 6, 9 — а не ровно на 10.
        var options = new LineScatterOptions
        {
            StoneDiameterMm = 2,
            GapMm = 0,
            Mode = StepMode.ExactStep,
            ExactStepMm = 3,
        };

        var stones = LineScatterer.Scatter(StraightLine10mm(), options);

        Assert.Equal(4, stones.Count);
        Assert.Equal(9, stones[stones.Count - 1].Center.X, 6);
    }

    [Fact]
    public void ExactCount_PlacesExactlyRequestedNumberOfStones()
    {
        var options = new LineScatterOptions
        {
            StoneDiameterMm = 2,
            GapMm = 0,
            Mode = StepMode.ExactCount,
            ExactCount = 6,
        };

        var stones = LineScatterer.Scatter(StraightLine10mm(), options);

        Assert.Equal(6, stones.Count);
        Assert.Equal(0, stones[0].Center.X, 6);
        Assert.Equal(10, stones[stones.Count - 1].Center.X, 6);
    }

    [Fact]
    public void StartOffsetAndEndMargin_ShrinkTheActiveRange()
    {
        var options = new LineScatterOptions
        {
            StoneDiameterMm = 2,
            GapMm = 0,
            Mode = StepMode.FitEven,
            StartOffsetMm = 2,
            EndMarginMm = 3,
        };

        var stones = LineScatterer.Scatter(StraightLine10mm(), options);

        Assert.Equal(2, stones[0].Center.X, 6);
        Assert.Equal(7, stones[stones.Count - 1].Center.X, 6);
    }

    [Fact]
    public void Reverse_StartsFromTheOtherEnd()
    {
        var options = new LineScatterOptions
        {
            StoneDiameterMm = 2,
            GapMm = 0,
            Mode = StepMode.FitEven,
            StartOffsetMm = 2,
            Reverse = true,
        };

        var stones = LineScatterer.Scatter(StraightLine10mm(), options);

        // С учётом Reverse отступ 2 мм откладывается от точки (10,0), а не от (0,0).
        Assert.Equal(8, stones[0].Center.X, 6);
    }

    [Fact]
    public void SharpCorner_GetsStoneExactlyAtVertex_AndSegmentsFillIndependently()
    {
        // Зигзаг с прямым углом 90° в точке (10,0) — заведомо острее порога 20°.
        var zigzag = Curve.FromPolyline(new[] { new Point2D(0, 0), new Point2D(10, 0), new Point2D(10, 10) });
        var options = new LineScatterOptions
        {
            StoneDiameterMm = 2,
            GapMm = 0,
            Mode = StepMode.FitEven,
            CornerAngleThresholdDeg = 20,
        };

        var stones = LineScatterer.Scatter(zigzag, options);

        PlacedStone corner = Assert.Single(stones, s => s.IsCorner);
        Assert.Equal(10, corner.Center.X, 6);
        Assert.Equal(0, corner.Center.Y, 6);

        // Ровно одна страза в вершине угла — не наложение с обеих сторон.
        Assert.Single(stones, s => s.Center.X == 10 && s.Center.Y == 0);
    }

    [Fact]
    public void ClosedSquare_NoDoubleStoneAtSeam_AllCornersPresent()
    {
        var square = Curve.FromPolyline(
            new[] { new Point2D(0, 0), new Point2D(10, 0), new Point2D(10, 10), new Point2D(0, 10) },
            isClosed: true);
        var options = new LineScatterOptions
        {
            StoneDiameterMm = 2,
            GapMm = 0,
            Mode = StepMode.FitEven,
            CornerAngleThresholdDeg = 20,
        };

        var stones = LineScatterer.Scatter(square, options);

        Assert.Equal(4, stones.Count(s => s.IsCorner));

        // Нет дублей: каждая точка встречается только один раз.
        var distinctPositions = stones.Select(s => (Math.Round(s.Center.X, 6), Math.Round(s.Center.Y, 6))).Distinct().Count();
        Assert.Equal(stones.Count, distinctPositions);
    }
}
