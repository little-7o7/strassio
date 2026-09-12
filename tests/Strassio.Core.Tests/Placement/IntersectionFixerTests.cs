using System;
using System.Collections.Generic;
using System.Linq;
using Strassio.Core.Geometry;
using Strassio.Core.Placement;

namespace Strassio.Core.Tests.Placement;

public class IntersectionFixerTests
{
    [Fact]
    public void NonOverlappingStones_AllSurvive()
    {
        var stones = new List<PlacedStone>
        {
            new PlacedStone(new Point2D(0, 0), 2.4, false),
            new PlacedStone(new Point2D(10, 0), 2.4, false),
            new PlacedStone(new Point2D(20, 0), 2.4, false),
        };

        var result = IntersectionFixer.RemoveOverlaps(stones);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void TwoOverlappingStones_LowerPriorityRemoved_SameDiameter_FirstInListWins()
    {
        var stones = new List<PlacedStone>
        {
            new PlacedStone(new Point2D(0, 0), 2.4, false),
            new PlacedStone(new Point2D(0.5, 0), 2.4, false), // явно ближе, чем диаметр — конфликт
        };

        var result = IntersectionFixer.RemoveOverlaps(stones);

        Assert.Single(result);
        Assert.Equal(0, result[0].Center.X, 6); // выжила первая (при равенстве остального — раньше в списке)
    }

    [Fact]
    public void CornerStone_AlwaysWinsOverNonCornerEvenIfSmaller()
    {
        var stones = new List<PlacedStone>
        {
            new PlacedStone(new Point2D(0, 0), 3.2, false), // крупнее, но не угловая
            new PlacedStone(new Point2D(0.5, 0), 2.4, true), // угловая — приоритет выше диаметра
        };

        var result = IntersectionFixer.RemoveOverlaps(stones);

        Assert.Single(result);
        Assert.True(result[0].IsCorner);
    }

    [Fact]
    public void BiggerStone_WinsOverSmaller_WhenNeitherIsCorner()
    {
        var stones = new List<PlacedStone>
        {
            new PlacedStone(new Point2D(0.5, 0), 2.4, false),
            new PlacedStone(new Point2D(0, 0), 3.2, false),
        };

        var result = IntersectionFixer.RemoveOverlaps(stones);

        Assert.Single(result);
        Assert.Equal(3.2, result[0].DiameterMm, 6);
    }

    [Fact]
    public void MinGap_IsRespected()
    {
        // Диаметр 2.4, значит центры на 2.4 мм друг от друга — круги ровно касаются (зазор 0).
        // При minGap 0.5 это уже конфликт, при minGap 0 — нет.
        var stones = new List<PlacedStone>
        {
            new PlacedStone(new Point2D(0, 0), 2.4, false),
            new PlacedStone(new Point2D(2.4, 0), 2.4, false),
        };

        Assert.Equal(2, IntersectionFixer.RemoveOverlaps(stones, minGapMm: 0).Count);
        Assert.Single(IntersectionFixer.RemoveOverlaps(stones, minGapMm: 0.5));
    }

    [Fact]
    public void RingStar_ThreeRows_NoOverlapsAnywhereAfterFix()
    {
        // Тот самый сценарий из Preview (ring-star), где автор заметил наложение рядов у острых
        // концов звезды — три ряда вокруг пятиконечной звезды, независимо расставленные L1.
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

        var rows = new[]
        {
            new RowSpec
            {
                OffsetMm = -3.4,
                ScatterOptions = new LineScatterOptions { StoneDiameterMm = 2.4, GapMm = 0.2, Mode = StepMode.FitEven, CornerAngleThresholdDeg = 20 },
            },
            new RowSpec
            {
                OffsetMm = 0,
                ScatterOptions = new LineScatterOptions { StoneDiameterMm = 3.2, GapMm = 0.2, Mode = StepMode.FitEven, CornerAngleThresholdDeg = 20 },
            },
            new RowSpec
            {
                OffsetMm = 3.4,
                ScatterOptions = new LineScatterOptions { StoneDiameterMm = 2.4, GapMm = 0.2, Mode = StepMode.FitEven, CornerAngleThresholdDeg = 20 },
            },
        };

        var beforeFix = RingScatterer.Scatter(star, rows);
        Assert.True(HasAnyOverlap(beforeFix), "Сценарий должен воспроизводить исходную проблему — иначе тест ничего не проверяет.");

        var afterFix = IntersectionFixer.RemoveOverlaps(beforeFix);

        Assert.False(HasAnyOverlap(afterFix), "После исправления пересечений наложений быть не должно.");
        // У этой звезды угол в острие ~37°, три ряда с шагом 3,4 мм — три ряда физически не
        // помещаются различимо у самых острых концов, так что заметная доля страз там неизбежно
        // убирается (проверено визуально на Preview — выглядит чисто). Проверяем только, что вообще
        // что-то осталось и хоть что-то убралось, без произвольного точного порога.
        Assert.True(afterFix.Count < beforeFix.Count, "Часть страз должна была быть убрана.");
        Assert.True(afterFix.Count > beforeFix.Count / 2, "Убрано подозрительно много страз — больше половины.");
    }

    [Fact]
    public void ManyStones_ResolvesQuickly()
    {
        // Грубая проверка производительности сеточного поиска (раздел 6.2: 50 000 страз < 1 с) —
        // не гоняем полные 50 000 в юнит-тесте, но 5 000 с гарантированными пересечениями должны
        // решаться быстро, а не зависать в переборе всех пар.
        var rnd = new Random(42);
        var stones = new List<PlacedStone>();
        for (int i = 0; i < 5000; i++)
        {
            stones.Add(new PlacedStone(new Point2D(rnd.NextDouble() * 200, rnd.NextDouble() * 200), 2.4, false));
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = IntersectionFixer.RemoveOverlaps(stones);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 3000, $"Слишком медленно: {sw.ElapsedMilliseconds} мс на 5000 страз.");
        Assert.False(HasAnyOverlap(result));
    }

    private static bool HasAnyOverlap(IReadOnlyList<PlacedStone> stones)
    {
        for (int i = 0; i < stones.Count; i++)
        {
            for (int j = i + 1; j < stones.Count; j++)
            {
                double required = stones[i].DiameterMm / 2 + stones[j].DiameterMm / 2;
                if (Point2D.Distance(stones[i].Center, stones[j].Center) < required - 0.01)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
