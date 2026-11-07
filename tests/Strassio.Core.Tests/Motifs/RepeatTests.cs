using Strassio.Core.Geometry;
using Strassio.Core.Motifs;
using Strassio.Core.Placement;

namespace Strassio.Core.Tests.Motifs;

/// <summary>Раппорт: повтор плитки по ткани без наложений на стыках (docs/SPEC.md, раздел 9).</summary>
public class RepeatTests
{
    private const double D = 2.4;
    private const double Gap = 0.2;

    /// <summary>Соты на прямоугольнике w × h — плитка, у которой стразы лежат вплотную к краю.</summary>
    private static List<PlacedStone> Honeycomb(double w, double h)
    {
        var stones = new List<PlacedStone>();
        double step = D + Gap, rowH = step * Math.Sqrt(3) / 2;
        for (int j = 0; j * rowH <= h; j++)
        {
            for (int i = 0; i * step + (j % 2) * step / 2 <= w; i++)
            {
                stones.Add(new PlacedStone(new Point2D(i * step + (j % 2) * step / 2, j * rowH), D, false));
            }
        }

        return stones;
    }

    private static void AssertNoOverlaps(IReadOnlyList<PlacedStone> stones)
    {
        var sorted = stones.OrderBy(s => s.Center.X).ToList();
        for (int i = 0; i < sorted.Count; i++)
        {
            for (int j = i + 1; j < sorted.Count && sorted[j].Center.X - sorted[i].Center.X < D + Gap; j++)
            {
                double need = (sorted[i].DiameterMm + sorted[j].DiameterMm) / 2 + Gap - 0.02;
                Assert.True(Point2D.Distance(sorted[i].Center, sorted[j].Center) >= need,
                    $"стразы {sorted[i].Center} и {sorted[j].Center} налезают");
            }
        }
    }

    [Fact]
    public void Offsets_Straight_HalfDrop_Brick()
    {
        var o = new RepeatOptions { StepXMm = 10, StepYMm = 20, Columns = 2, Rows = 2 };
        Assert.Equal(new[] { new Point2D(0, 0), new Point2D(10, 0), new Point2D(0, -20), new Point2D(10, -20) }, Repeat.Offsets(o));

        o.Shift = RepeatShift.HalfDrop;
        Assert.Equal(new[] { new Point2D(0, 0), new Point2D(10, -10), new Point2D(0, -20), new Point2D(10, -30) }, Repeat.Offsets(o));

        o.Shift = RepeatShift.Brick;
        Assert.Equal(new[] { new Point2D(0, 0), new Point2D(10, 0), new Point2D(5, -20), new Point2D(15, -20) }, Repeat.Offsets(o));
    }

    [Fact]
    public void TileThatFitsItsStep_LosesNothing()
    {
        // Плитка 20 × 10 мм, шаг с запасом в камень: стыки и так чистые.
        List<PlacedStone> tile = Honeycomb(20, 10);
        var o = new RepeatOptions { StepXMm = 20 + D + Gap, StepYMm = 10 + D + Gap, Columns = 3, Rows = 3, GapMm = Gap };
        Assert.Empty(Repeat.SeamConflicts(tile, o));
        List<PlacedStone> all = Repeat.Tile(tile, o, out int removed);
        Assert.Equal(0, removed);
        Assert.Equal(tile.Count * 9, all.Count);
        AssertNoOverlaps(all);
    }

    [Theory]
    [InlineData(RepeatShift.None)]
    [InlineData(RepeatShift.HalfDrop)]
    [InlineData(RepeatShift.Brick)]
    public void TightStep_RemovesSeamStones_AndWholeFabricIsClean(RepeatShift shift)
    {
        // Шаг меньше плитки: копии заходят друг на друга, на стыках надо убрать стразы.
        List<PlacedStone> tile = Honeycomb(20, 12);
        var o = new RepeatOptions { StepXMm = 19, StepYMm = 11, Shift = shift, Columns = 4, Rows = 4, GapMm = Gap };
        List<int> conflicts = Repeat.SeamConflicts(tile, o);
        Assert.NotEmpty(conflicts);
        Assert.True(conflicts.Count < tile.Count / 2, $"убрано {conflicts.Count} из {tile.Count}");

        List<PlacedStone> all = Repeat.Tile(tile, o, out int removed);
        Assert.Equal(conflicts.Count, removed);
        AssertNoOverlaps(all);
    }

    [Fact]
    public void SeamStones_AreTakenFromTheEdge_NotFromTheMiddle()
    {
        List<PlacedStone> tile = Honeycomb(20, 12);
        var o = new RepeatOptions { StepXMm = 19, StepYMm = 11, Columns = 2, Rows = 2, GapMm = Gap };
        foreach (int i in Repeat.SeamConflicts(tile, o))
        {
            Point2D p = tile[i].Center;
            bool nearEdge = p.X < 3 || p.X > 17 || p.Y < 3 || p.Y > 9;
            Assert.True(nearEdge, $"убрана страза из середины плитки: {p}");
        }
    }

    [Fact]
    public void MixedSizes_UseTheirOwnDiameters()
    {
        // Крупная страза у правого края и мелкая у левого: при шаге 10 они сходятся на стыке.
        var tile = new List<PlacedStone>
        {
            new(new Point2D(0, 0), 2, false),
            new(new Point2D(5, 0), 2, false),
            new(new Point2D(8.5, 0), 4, false),
        };
        var o = new RepeatOptions { StepXMm = 10, StepYMm = 50, Columns = 3, Rows = 1, GapMm = Gap };

        // До соседней мелкой — 1,5 мм между центрами, а надо (4 + 2) / 2 + 0,2 = 3,2.
        Assert.Single(Repeat.SeamConflicts(tile, o));
        Assert.Equal(2, Repeat.Tile(tile, o, out _).Count / 3);
    }

    [Theory]
    [InlineData(RepeatShift.None, RepeatAlternate.Rotate180)]
    [InlineData(RepeatShift.HalfDrop, RepeatAlternate.MirrorX)]
    [InlineData(RepeatShift.Brick, RepeatAlternate.MirrorY)]
    public void SecondCopyTurned_WholeFabricIsClean(RepeatShift shift, RepeatAlternate alternate)
    {
        // Несимметричная плитка: соты, к которым справа сверху добавлен «хвостик». У повёрнутой копии
        // хвостик оказывается с другой стороны, и стыки другие, чем у обычного повтора.
        List<PlacedStone> tile = Honeycomb(20, 12);
        tile.Add(new PlacedStone(new Point2D(22.5, 13.5), D, false));
        var o = new RepeatOptions { StepXMm = 20, StepYMm = 12, Shift = shift, Columns = 4, Rows = 4, GapMm = Gap, Alternate = alternate };

        List<PlacedStone> all = Repeat.Tile(tile, o, out int removed);
        Assert.True(removed > 0 && removed < tile.Count / 2, $"убрано {removed} из {tile.Count}");
        Assert.Equal((tile.Count - removed) * 16, all.Count);
        AssertNoOverlaps(all);
    }

    [Fact]
    public void SecondCopy_IsTurnedAroundTheTileMiddle_InCheckerboardOrder()
    {
        var tile = new List<PlacedStone> { new(new Point2D(1, 1), D, false) };
        var o = new RepeatOptions
        {
            StepXMm = 10, StepYMm = 10, Columns = 2, Rows = 2, Alternate = RepeatAlternate.Rotate180, Pivot = new Point2D(5, 5),
        };

        Assert.False(Repeat.IsAlternate(o, 0));
        Assert.True(Repeat.IsAlternate(o, 1));
        Assert.True(Repeat.IsAlternate(o, 2));
        Assert.False(Repeat.IsAlternate(o, 3));

        List<PlacedStone> all = Repeat.Tile(tile, o, out _);
        Assert.Equal(new Point2D(1, 1), all[0].Center);
        Assert.Equal(new Point2D(19, 9), all[1].Center);
        Assert.Equal(new Point2D(9, -1), all[2].Center);
        Assert.Equal(new Point2D(11, -9), all[3].Center);
    }

    [Fact]
    public void SeamlessSheet_IsAWholeNumberOfPatternPeriods()
    {
        var o = new RepeatOptions { StepXMm = 70, StepYMm = 90 };
        RepeatSheet straight = Repeat.SeamlessSheet(o, 300, 400);
        Assert.Equal((280.0, 360.0, 4, 4), (straight.WidthMm, straight.HeightMm, straight.Across, straight.Down));

        // Полшага: по ширине период — два шага (через столбец всё повторяется как было).
        o.Shift = RepeatShift.HalfDrop;
        RepeatSheet halfDrop = Repeat.SeamlessSheet(o, 300, 400);
        Assert.Equal((280.0, 360.0), (halfDrop.WidthMm, halfDrop.HeightMm));
        Assert.Equal(2, halfDrop.Across);

        o.Shift = RepeatShift.Brick;
        Assert.Equal((280.0, 360.0), (Repeat.SeamlessSheet(o, 300, 400).WidthMm, Repeat.SeamlessSheet(o, 300, 400).HeightMm));

        o.Shift = RepeatShift.None;
        o.Alternate = RepeatAlternate.MirrorX;
        Assert.Equal((280.0, 360.0), (Repeat.SeamlessSheet(o, 300, 400).WidthMm, Repeat.SeamlessSheet(o, 300, 400).HeightMm));
        Assert.False(Repeat.SeamlessSheet(o, 100, 400).IsValid);
    }

    [Theory]
    [InlineData(RepeatShift.None, RepeatAlternate.None)]
    [InlineData(RepeatShift.HalfDrop, RepeatAlternate.None)]
    [InlineData(RepeatShift.Brick, RepeatAlternate.None)]
    [InlineData(RepeatShift.None, RepeatAlternate.Rotate180)]
    [InlineData(RepeatShift.HalfDrop, RepeatAlternate.MirrorX)]
    [InlineData(RepeatShift.Brick, RepeatAlternate.MirrorY)]
    public void FillSheet_SheetsSideBySide_AreTheSameFabric_WithoutSeams(RepeatShift shift, RepeatAlternate alternate)
    {
        List<PlacedStone> tile = Honeycomb(20, 12);
        tile.Add(new PlacedStone(new Point2D(22.5, 13.5), D, false));
        var o = new RepeatOptions { StepXMm = 20, StepYMm = 12, Shift = shift, GapMm = Gap, Alternate = alternate };
        RepeatSheet sheet = Repeat.SeamlessSheet(o, 130, 100);
        Assert.True(sheet.IsValid);
        var topLeft = new Point2D(-3.7, 8.2);

        List<SheetStone> first = Repeat.FillSheet(tile, o, sheet, topLeft, out int removed);
        Assert.True(first.Count > 100, $"страз на листе {first.Count}");
        Assert.True(removed > 0);
        Assert.All(first, s => Assert.True(
            s.Center.X >= topLeft.X - 1e-6 && s.Center.X < topLeft.X + sheet.WidthMm && s.Center.Y <= topLeft.Y + 1e-6 && s.Center.Y > topLeft.Y - sheet.HeightMm,
            $"страза {s.Center} вне листа"));

        // Соседний лист справа и снизу — тот же лист, сдвинутый на его ширину и высоту: шва нет.
        foreach (Point2D shiftBy in new[] { new Point2D(sheet.WidthMm, 0), new Point2D(0, -sheet.HeightMm) })
        {
            List<SheetStone> next = Repeat.FillSheet(tile, o, sheet, topLeft + shiftBy, out _);
            Assert.Equal(first.Count, next.Count);
            var expected = first.Select(s => (s.Source, s.Center + shiftBy)).OrderBy(t => Math.Round(t.Item2.X, 4)).ThenBy(t => Math.Round(t.Item2.Y, 4)).ToList();
            var actual = next.Select(s => (s.Source, s.Center)).OrderBy(t => Math.Round(t.Center.X, 4)).ThenBy(t => Math.Round(t.Center.Y, 4)).ToList();
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.Equal(expected[i].Item1, actual[i].Source);
                Assert.True(Point2D.Distance(expected[i].Item2, actual[i].Center) < 1e-6, $"{expected[i].Item2} и {actual[i].Center}");
            }

            // И два листа рядом — без наложений на стыке.
            AssertNoOverlaps(first.Concat(next).Select(s => new PlacedStone(s.Center, D, false)).ToList());
        }
    }

    [Fact]
    public void TilesPerRunningMetre_IsFabricAreaOverTileArea()
    {
        var o = new RepeatOptions { StepXMm = 100, StepYMm = 125 };
        Assert.Equal(120, Repeat.TilesPerRunningMetre(o, 1500), 6);
        o.Shift = RepeatShift.HalfDrop;
        Assert.Equal(120, Repeat.TilesPerRunningMetre(o, 1500), 6);
    }

    [Theory]
    [InlineData(FrameTrimMode.WholeStone, 3)]
    [InlineData(FrameTrimMode.Center, 2)]
    public void FrameTrim_RemovesStonesOutsideTheFrame(FrameTrimMode mode, int removed)
    {
        var frame = Curve.FromPolyline(new List<Point2D> { new(0, 0), new(20, 0), new(20, 10), new(0, 10) }, true);
        var stones = new List<PlacedStone>
        {
            new(new Point2D(5, 5), D, false),     // внутри
            new(new Point2D(19.5, 5), D, false),  // середина внутри, край снаружи
            new(new Point2D(21, 5), D, false),    // снаружи
            new(new Point2D(10, -3), D, false),   // снаружи
        };

        List<int> outside = FrameTrim.Outside(stones, new[] { frame }, mode);
        Assert.Equal(removed, outside.Count);
        Assert.DoesNotContain(0, outside);
        Assert.Contains(2, outside);
        Assert.Contains(3, outside);
    }
}
