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
}
