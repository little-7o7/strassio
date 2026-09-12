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
    public void VerySharpCorner_NoTwoStonesCloserThanStoneStep()
    {
        // Шип с углом ~35° (как острие пятиконечной звезды) — без учёта остроты угла стразы слева
        // и справа от вершины физически перекрывались бы, хотя каждая на своём отрезке стоит верно.
        var spike = Curve.FromPolyline(new[]
        {
            new Point2D(-30, 0),
            new Point2D(0, 0),
            new Point2D(0, 40),
            new Point2D(30, 0),
        });
        var options = new LineScatterOptions
        {
            StoneDiameterMm = 2.4,
            GapMm = 0.2,
            Mode = StepMode.FitEven,
            CornerAngleThresholdDeg = 20,
        };

        var stones = LineScatterer.Scatter(spike, options);

        // Общая физическая граница: круги вообще не должны накладываться нигде на кривой
        // (FitEven на обычной прямой может слегка ужать шаг ради ровной подгонки — это нормально,
        // лишь бы не доходило до пересечения самих кругов).
        for (int i = 0; i < stones.Count; i++)
        {
            for (int j = i + 1; j < stones.Count; j++)
            {
                double d = Point2D.Distance(stones[i].Center, stones[j].Center);
                Assert.True(d >= options.StoneDiameterMm - 1e-6,
                    $"Стразы {i} и {j} накладываются: {d:0.###} мм (диаметр {options.StoneDiameterMm} мм).");
            }
        }

        // Прицельная проверка самого шипа: две ближайшие к вершине стразы (по разные стороны угла)
        // должны стоять хотя бы на маленький зазор CornerMinGapMm друг от друга. Ищем по фактическому
        // расстоянию до вершины, а не по координате X — плавный сдвиг (раздел 6.1 ТЗ) специально
        // немного уводит несколько ближайших страз в сторону от исходной линии.
        var tip = new Point2D(0, 40);
        Assert.Contains(stones, s => s.IsCorner && Point2D.Distance(s.Center, tip) < 1e-6);
        var nearestTwo = stones
            .Where(s => !s.IsCorner)
            .OrderBy(s => Point2D.Distance(s.Center, tip))
            .Take(2)
            .ToList();

        double targetMinDistance = options.StoneDiameterMm + options.CornerMinGapMm;
        double armDistance = Point2D.Distance(nearestTwo[0].Center, nearestTwo[1].Center);
        Assert.True(armDistance >= targetMinDistance - 1e-6,
            $"Ближайшие стразы по разные стороны шипа слишком близко: {armDistance:0.###} мм (нужно ≥ {targetMinDistance} мм).");
    }

    [Fact]
    public void VerySharpCorner_NudgeIsSmoothAcrossSeveralStones()
    {
        // По замечанию автора: сдвиг у угла не должен выглядеть как один резкий скачок — несколько
        // страз подряд должны сдвигаться на постепенно уменьшающуюся величину.
        var spike = Curve.FromPolyline(new[]
        {
            new Point2D(-30, 0),
            new Point2D(0, 0),
            new Point2D(0, 40),
            new Point2D(30, 0),
        });
        var options = new LineScatterOptions
        {
            StoneDiameterMm = 2.4,
            GapMm = 0.2,
            Mode = StepMode.FitEven,
            CornerAngleThresholdDeg = 20,
        };

        var stones = new List<PlacedStone>(LineScatterer.Scatter(spike, options));
        var tip = new Point2D(0, 40);

        // "Естественные" (без острого угла на конце) позиции того же вертикального отрезка —
        // считаем отдельно на прямой той же длины, без соседнего шипа. Сравниваем по порядку
        // добавления (не по координатам — плавный сдвиг у угла специально немного уводит стразу
        // не только вдоль ряда, но и в сторону, так что сортировка по Y после сдвига ненадёжна).
        var straightArm = Curve.FromPolyline(new[] { new Point2D(0, 0), new Point2D(0, 40) });
        var straightNatural = LineScatterer.Scatter(straightArm, options);
        int n = straightNatural.Count;

        int tipIndex = stones.FindIndex(s => s.IsCorner && Point2D.Distance(s.Center, tip) < 1e-6);
        Assert.True(tipIndex > 0, "Не нашли угловую стразу в вершине шипа.");

        var displacements = new List<double>();
        for (int k = 0; k < options.CornerTaperCount; k++)
        {
            int actualIdx = tipIndex - 1 - k;
            int naturalIdx = n - 2 - k; // n-1 у прямой — сама конечная точка (в шипе это угол, не сравниваем)
            if (actualIdx < 0 || naturalIdx < 0)
            {
                break;
            }

            displacements.Add(Point2D.Distance(stones[actualIdx].Center, straightNatural[naturalIdx].Center));
        }

        Assert.True(displacements[0] > 1e-6, "Ближайшая к углу страза должна быть сдвинута.");
        for (int i = 1; i < displacements.Count; i++)
        {
            Assert.True(displacements[i] <= displacements[i - 1] + 1e-6,
                $"Сдвиг должен плавно убывать по мере удаления от угла: " +
                $"{string.Join(", ", displacements.Select(o => o.ToString("0.###")))}");
        }
    }

    [Fact]
    public void FivePointStar_NoOverlapsAnywhereDespiteSharpTips()
    {
        const int spikes = 5;
        const double outerR = 40;
        const double innerR = 15;
        var points = new List<Point2D>();
        for (int i = 0; i < spikes * 2; i++)
        {
            double r = i % 2 == 0 ? outerR : innerR;
            double angle = Math.PI / 2 + i * Math.PI / spikes;
            points.Add(new Point2D(r * Math.Cos(angle), -r * Math.Sin(angle)));
        }

        var star = Curve.FromPolyline(points, isClosed: true);
        var options = new LineScatterOptions
        {
            StoneDiameterMm = 2.4,
            GapMm = 0.2,
            Mode = StepMode.FitEven,
            CornerAngleThresholdDeg = 20,
        };

        var stones = LineScatterer.Scatter(star, options);

        for (int i = 0; i < stones.Count; i++)
        {
            for (int j = i + 1; j < stones.Count; j++)
            {
                double d = Point2D.Distance(stones[i].Center, stones[j].Center);
                Assert.True(d >= options.StoneDiameterMm - 1e-6,
                    $"Стразы {i} и {j} накладываются: {d:0.###} мм (диаметр {options.StoneDiameterMm} мм).");
            }
        }

        Assert.Equal(10, stones.Count(s => s.IsCorner));
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
