using System;
using System.Collections.Generic;
using System.Linq;
using Strassio.Core.Geometry;
using Strassio.Core.Placement;

namespace Strassio.Core.Tests.Placement;

/// <summary>После удаления соседи того же ряда раздвигаются, чтобы не осталось дырки (docs/SPEC.md, раздел 6.4).</summary>
public class GapClosingTests
{
    private const double Diameter = 2.4;
    private const double Step = 3.0;

    // Прямой ряд 0 из 11 страз с шагом 3 мм (x = 0…30) и угловая страза ряда 1 чуть над серединой:
    // она мешает только средней стразе ряда (x = 15), соседям — нет.
    private static List<PlacedStone> RowWithIntruder(double intruderY = 2.3, double intruderDiameter = Diameter)
    {
        var stones = new List<PlacedStone>();
        for (int k = 0; k <= 10; k++)
        {
            stones.Add(new PlacedStone(new Point2D(k * Step, 0), Diameter, false, rowId: 0));
        }

        stones.Add(new PlacedStone(new Point2D(15, intruderY), intruderDiameter, true, rowId: 1));
        return stones;
    }

    [Fact]
    public void Remove_SpreadsRowNeighbours_SoNoHoleRemains()
    {
        var result = IntersectionFixer.Fix(RowWithIntruder(), new IntersectionFixOptions { Action = IntersectionAction.Remove });

        Assert.Equal(new[] { 5 }, result.RemovedIndices);
        Assert.NotEmpty(result.ShiftedIndices);

        List<PlacedStone> row = result.Stones.Where(s => s.RowId == 0).OrderBy(s => s.Center.X).ToList();
        Assert.Equal(10, row.Count);
        Assert.All(row, s => Assert.Equal(0, s.Center.Y, 6)); // остались на своей линии

        // Концы ряда на месте, а самый большой шаг — не больше обычного плюс четверть диаметра.
        Assert.Equal(0, row[0].Center.X, 6);
        Assert.Equal(30, row[row.Count - 1].Center.X, 6);
        double maxStep = Enumerable.Range(1, row.Count - 1).Max(k => row[k].Center.X - row[k - 1].Center.X);
        Assert.InRange(maxStep, Step, Step + Diameter / 4 + 1e-6);

        Assert.False(HasAnyOverlap(result.Stones, 0.1));
    }

    [Fact]
    public void CloseGapsOff_LeavesHole()
    {
        var result = IntersectionFixer.Fix(RowWithIntruder(), new IntersectionFixOptions
        {
            Action = IntersectionAction.Remove, CloseGaps = false,
        });

        Assert.Empty(result.ShiftedIndices);
        Assert.Equal(IntersectionFixer.RemoveOverlaps(RowWithIntruder()), result.Stones);
    }

    [Fact]
    public void WideCut_WhereOtherRowSits_IsNotFilled()
    {
        // Большая угловая страза лежит прямо на ряду и выбивает три стразы подряд. Это не «дырка»,
        // а место, где ряды сходятся: раздвинуть соседей можно только слишком сильно — не трогаем.
        List<PlacedStone> stones = RowWithIntruder(intruderY: 0, intruderDiameter: 4);

        var result = IntersectionFixer.Fix(stones, new IntersectionFixOptions { Action = IntersectionAction.Remove });

        Assert.Equal(new[] { 4, 5, 6 }, result.RemovedIndices);
        Assert.Empty(result.ShiftedIndices);
        Assert.False(HasAnyOverlap(result.Stones, 0.1));
    }

    [Fact]
    public void CornerStone_StaysPut_WhileNeighboursSpread()
    {
        // Страза x = 12 угловая: она не двигается, раздвигаются только стразы по другую сторону дырки
        // и между угловой и дыркой двигать некого.
        List<PlacedStone> stones = RowWithIntruder();
        stones[4] = new PlacedStone(stones[4].Center, Diameter, true, rowId: 0);

        var result = IntersectionFixer.Fix(stones, new IntersectionFixOptions { Action = IntersectionAction.Remove });

        Assert.Equal(new[] { 5 }, result.RemovedIndices);
        Assert.DoesNotContain(4, result.ShiftedIndices);
        Assert.Contains(result.Stones, s => s.RowId == 0 && s.Center == new Point2D(12, 0));
        Assert.False(HasAnyOverlap(result.Stones, 0.1));
    }

    [Fact]
    public void ClosedRing_HoleAtSeam_IsClosed()
    {
        // Кольцо из 20 страз с шагом ~3 мм; мешающая страза стоит у стразы №0 — дырка приходится
        // на «шов» замкнутого ряда (между последней и первой).
        const int count = 20;
        double radius = count * Step / (2 * Math.PI);
        var stones = new List<PlacedStone>();
        for (int k = 0; k < count; k++)
        {
            double a = 2 * Math.PI * k / count;
            stones.Add(new PlacedStone(new Point2D(radius * Math.Cos(a), radius * Math.Sin(a)), Diameter, false, rowId: 0));
        }

        stones.Add(new PlacedStone(new Point2D(radius + 2.3, 0), Diameter, true, rowId: 1));

        var result = IntersectionFixer.Fix(stones, new IntersectionFixOptions { Action = IntersectionAction.Remove });

        Assert.Equal(new[] { 0 }, result.RemovedIndices);
        Assert.Contains(count - 1, result.ShiftedIndices);
        Assert.Contains(1, result.ShiftedIndices);
        Assert.False(HasAnyOverlap(result.Stones, 0.1));

        // Самый большой зазор между соседями по кольцу — уже не «две ступеньки», а почти обычный шаг.
        List<Point2D> ring = result.Stones.Where(s => s.RowId == 0)
            .Select(s => s.Center).OrderBy(p => Math.Atan2(p.Y, p.X)).ToList();
        double maxStep = Enumerable.Range(0, ring.Count).Max(k => Point2D.Distance(ring[k], ring[(k + 1) % ring.Count]));
        Assert.True(maxStep < Step + Diameter / 4 + 0.05, $"Самый большой шаг {maxStep:0.###} мм — дырка не закрыта.");
    }

    [Fact]
    public void Shift_AlsoClosesHolesLeftByRemoval()
    {
        // Соседи стоят вплотную, сдвиг невозможен — страза убирается, а дырка закрывается.
        List<PlacedStone> stones = RowWithIntruder();

        var result = IntersectionFixer.Fix(stones, new IntersectionFixOptions
        {
            Action = IntersectionAction.Shift, MaxShiftMm = 0.2,
        });

        Assert.Equal(new[] { 5 }, result.RemovedIndices);
        Assert.NotEmpty(result.ShiftedIndices);
        Assert.False(HasAnyOverlap(result.Stones, 0.1));
    }

    private static bool HasAnyOverlap(IReadOnlyList<PlacedStone> stones, double minGapMm)
    {
        for (int i = 0; i < stones.Count; i++)
        {
            for (int j = i + 1; j < stones.Count; j++)
            {
                double required = stones[i].DiameterMm / 2 + stones[j].DiameterMm / 2 + minGapMm;
                if (Point2D.Distance(stones[i].Center, stones[j].Center) < required - 0.011)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
