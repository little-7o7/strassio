using System;
using System.Collections.Generic;
using System.Linq;
using Strassio.Core.Geometry;
using Strassio.Core.Placement;

namespace Strassio.Core.Tests.Placement;

/// <summary>Действия при пересечении: «Удалить», «Сдвинуть», «Только показать» (docs/SPEC.md, раздел 6.4).</summary>
public class IntersectionActionTests
{
    // Ряд 0: три стразы ss6 на оси X с большим запасом места между ними. Угловая страза чужого
    // ряда стоит почти над средней — средняя мешает, но её можно отодвинуть вдоль своего ряда.
    private static List<PlacedStone> RowWithIntruderAboveMiddle(double rowSpacing = 5)
    {
        return new List<PlacedStone>
        {
            new PlacedStone(new Point2D(0, 0), 2.4, false, rowId: 0),
            new PlacedStone(new Point2D(rowSpacing, 0), 2.4, false, rowId: 0),
            new PlacedStone(new Point2D(rowSpacing * 2, 0), 2.4, false, rowId: 0),
            new PlacedStone(new Point2D(rowSpacing, 2.2), 2.4, true, rowId: 1),
        };
    }

    [Fact]
    public void Shift_MovesStoneAlongItsRow_InsteadOfRemoving()
    {
        var stones = RowWithIntruderAboveMiddle();

        var result = IntersectionFixer.Fix(stones, new IntersectionFixOptions
        {
            Action = IntersectionAction.Shift, MaxShiftMm = 1.5,
        });

        Assert.Equal(4, result.Stones.Count);
        Assert.Empty(result.RemovedIndices);
        Assert.Equal(new[] { 1 }, result.ShiftedIndices);

        PlacedStone moved = result.Stones[1];
        Assert.Equal(0, moved.Center.Y, 6); // осталась на своей линии
        double shift = Math.Abs(moved.Center.X - 5);
        Assert.InRange(shift, 0.01, 1.5);
        Assert.False(HasAnyOverlap(result.Stones, 0.1));
    }

    [Fact]
    public void Shift_BeyondLimit_RemovesStone()
    {
        var stones = RowWithIntruderAboveMiddle();

        var result = IntersectionFixer.Fix(stones, new IntersectionFixOptions
        {
            Action = IntersectionAction.Shift, MaxShiftMm = 0.5,
        });

        Assert.Equal(new[] { 1 }, result.RemovedIndices);
        Assert.Empty(result.ShiftedIndices);
        Assert.Equal(3, result.Stones.Count);
    }

    [Fact]
    public void Shift_BlockedByOwnRowNeighbours_RemovesStone()
    {
        // Соседи по ряду стоят вплотную — двигаться некуда, сдвиг ударил бы в соседа.
        var stones = RowWithIntruderAboveMiddle(rowSpacing: 2.6);

        var result = IntersectionFixer.Fix(stones, new IntersectionFixOptions
        {
            Action = IntersectionAction.Shift, MaxShiftMm = 1.5,
        });

        Assert.Contains(1, result.RemovedIndices);
        Assert.False(HasAnyOverlap(result.Stones, 0.1));
    }

    [Fact]
    public void Shift_NeverMovesCornerStone()
    {
        // Две угловые стразы одного ряда накладываются: угловая должна стоять точно в вершине
        // (раздел 4 ТЗ), поэтому её не двигаем, а убираем.
        var stones = new List<PlacedStone>
        {
            new PlacedStone(new Point2D(-5, 0), 2.4, false, rowId: 0),
            new PlacedStone(new Point2D(0, 0), 2.4, true, rowId: 0),
            new PlacedStone(new Point2D(5, 0), 2.4, false, rowId: 0),
            new PlacedStone(new Point2D(0, 1), 2.4, true, rowId: 1),
        };

        var result = IntersectionFixer.Fix(stones, new IntersectionFixOptions
        {
            Action = IntersectionAction.Shift, MaxShiftMm = 3,
        });

        Assert.Empty(result.ShiftedIndices);
        Assert.Single(result.RemovedIndices);
    }

    [Fact]
    public void Shift_GridStoneWithoutRow_IsRemovedNotShifted()
    {
        // RowId = -1 — страза плоской сетки, у неё нет «своей линии», вдоль которой двигаться.
        var stones = new List<PlacedStone>
        {
            new PlacedStone(new Point2D(0, 0), 2.4, false, rowId: -1),
            new PlacedStone(new Point2D(0.5, 0), 2.4, false, rowId: -1),
        };

        var result = IntersectionFixer.Fix(stones, new IntersectionFixOptions
        {
            Action = IntersectionAction.Shift, MaxShiftMm = 3,
        });

        Assert.Empty(result.ShiftedIndices);
        Assert.Equal(new[] { 1 }, result.RemovedIndices);
    }

    [Fact]
    public void ShowOnly_ChangesNothing_ReportsBothStonesOfEachConflict()
    {
        var stones = RowWithIntruderAboveMiddle();

        var result = IntersectionFixer.Fix(stones, new IntersectionFixOptions { Action = IntersectionAction.ShowOnly });

        Assert.Equal(stones, result.Stones);
        Assert.Empty(result.RemovedIndices);
        Assert.Empty(result.ShiftedIndices);
        Assert.Equal(new[] { 1, 3 }, result.ConflictIndices);
    }

    [Fact]
    public void Remove_SameAsRemoveOverlaps()
    {
        var rnd = new Random(7);
        var stones = new List<PlacedStone>();
        for (int i = 0; i < 400; i++)
        {
            stones.Add(new PlacedStone(new Point2D(rnd.NextDouble() * 40, rnd.NextDouble() * 40), 2.4, i % 17 == 0, rowId: i % 5));
        }

        var viaFix = IntersectionFixer.Fix(stones, new IntersectionFixOptions { Action = IntersectionAction.Remove });
        var viaOld = IntersectionFixer.RemoveOverlaps(stones);

        Assert.Equal(viaOld, viaFix.Stones);
        Assert.Empty(viaFix.ShiftedIndices);
    }

    [Fact]
    public void Shift_OnRingStar_NoOverlaps_AndKeepsAtLeastAsManyAsRemove()
    {
        var rows = new[]
        {
            new RowSpec { OffsetMm = -3.4, ScatterOptions = new LineScatterOptions { StoneDiameterMm = 2.4, GapMm = 0.2, CornerAngleThresholdDeg = 20 } },
            new RowSpec { OffsetMm = 0, ScatterOptions = new LineScatterOptions { StoneDiameterMm = 3.2, GapMm = 0.2, CornerAngleThresholdDeg = 20 } },
            new RowSpec { OffsetMm = 3.4, ScatterOptions = new LineScatterOptions { StoneDiameterMm = 2.4, GapMm = 0.2, CornerAngleThresholdDeg = 20 } },
        };
        var stones = RingScatterer.Scatter(StarCurve(), rows);

        var removed = IntersectionFixer.Fix(stones, new IntersectionFixOptions { Action = IntersectionAction.Remove });
        var shifted = IntersectionFixer.Fix(stones, new IntersectionFixOptions { Action = IntersectionAction.Shift });

        Assert.False(HasAnyOverlap(shifted.Stones, 0.1));
        Assert.True(shifted.Stones.Count >= removed.Stones.Count,
            $"Сдвиг сохранил {shifted.Stones.Count} страз, удаление — {removed.Stones.Count}: сдвиг не должен терять больше.");
        Assert.NotEmpty(shifted.ShiftedIndices);
    }

    private static Curve StarCurve()
    {
        var points = new List<Point2D>();
        for (int i = 0; i < 10; i++)
        {
            double r = i % 2 == 0 ? 40 : 15;
            double angle = Math.PI / 2 + i * Math.PI / 5;
            points.Add(new Point2D(r * Math.Cos(angle), -r * Math.Sin(angle)));
        }

        return Curve.FromPolyline(points, isClosed: true);
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
