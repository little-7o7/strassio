using System.Diagnostics;
using Strassio.Core.Editing;
using Strassio.Core.Geometry;

namespace Strassio.Core.Tests.Editing;

/// <summary>Правка страз в документе (docs/SPEC.md, разделы 7.1 и 7.2).</summary>
public class StoneEditorTests
{
    private const double D = 2.4;

    private static DocStone S(double x, double y, int order, bool locked = false, double d = D) =>
        new(new Point2D(x, y), d, order, locked);

    private static Curve Square(double x0, double y0, double size) => Curve.FromPolyline(
        new List<Point2D> { new(x0, y0), new(x0 + size, y0), new(x0 + size, y0 + size), new(x0, y0 + size) }, true);

    private static void AssertNoOverlaps(IReadOnlyList<DocStone> stones, EditResult r)
    {
        var alive = new List<(Point2D C, double R)>();
        for (int i = 0; i < stones.Count; i++)
        {
            if (r.Deleted.Contains(i))
            {
                continue;
            }

            Point2D c = r.Moved.Where(m => m.Index == i).Select(m => m.NewCenter).DefaultIfEmpty(stones[i].Center).First();
            alive.Add((c, stones[i].Radius));
        }

        for (int i = 0; i < alive.Count; i++)
        {
            for (int j = i + 1; j < alive.Count; j++)
            {
                double gap = Point2D.Distance(alive[i].C, alive[j].C) - alive[i].R - alive[j].R;
                Assert.True(gap >= StoneEditor.DefaultMinGapMm - 1e-6, $"наложение {gap:0.###} мм");
            }
        }
    }

    [Fact]
    public void KeepTop_RemovesLowerOfOverlappingPair()
    {
        var stones = new[] { S(0, 0, order: 1), S(1, 0, order: 2), S(10, 0, order: 3) };
        EditResult r = StoneEditor.ResolveOverlaps(stones, keepTop: true);
        Assert.Equal(new[] { 0 }, r.Deleted);
    }

    [Fact]
    public void KeepBottom_RemovesUpperOfOverlappingPair()
    {
        var stones = new[] { S(0, 0, order: 1), S(1, 0, order: 2), S(10, 0, order: 3) };
        EditResult r = StoneEditor.ResolveOverlaps(stones, keepTop: false);
        Assert.Equal(new[] { 1 }, r.Deleted);
    }

    [Fact]
    public void LockedStone_IsNeverRemoved_AndWinsOverlaps()
    {
        var stones = new[] { S(0, 0, order: 1, locked: true), S(1, 0, order: 2) };
        Assert.Equal(new[] { 1 }, StoneEditor.ResolveOverlaps(stones, keepTop: true).Deleted);
        Assert.Empty(StoneEditor.DeleteAlongLines(
            new[] { S(0, 0, 1, locked: true) },
            new[] { Curve.FromPolyline(new List<Point2D> { new(-5, 0), new(5, 0) }, false) }).Deleted);
    }

    [Fact]
    public void Overlaps_ChainOfThree_KeepsTopAndBottomOfChain()
    {
        // 0 снизу, 2 сверху, 1 в середине налезает на обоих. «Оставить верхние»: 2 остаётся, 1 уходит,
        // 0 больше ни на кого не налезает — остаётся.
        var stones = new[] { S(0, 0, 1), S(2, 0, 2), S(4, 0, 3) };
        EditResult r = StoneEditor.ResolveOverlaps(stones, keepTop: true);
        Assert.Equal(new[] { 1 }, r.Deleted);
        AssertNoOverlaps(stones, r);
    }

    [Fact]
    public void Shift_MovesStoneThatFitsNearby()
    {
        // Две стразы налезают на 0,3 мм; вокруг свободно — нижнюю можно сдвинуть.
        var stones = new[] { S(0, 0, 1), S(D - 0.3, 0, 2) };
        EditResult r = StoneEditor.ShiftApart(stones);
        Assert.Empty(r.Deleted);
        StoneMove move = Assert.Single(r.Moved);
        Assert.Equal(0, move.Index);
        Assert.True(Point2D.Distance(move.NewCenter, stones[0].Center) <= D * StoneEditor.DefaultMaxShiftFraction + 1e-6);
        AssertNoOverlaps(stones, r);
    }

    [Fact]
    public void Shift_DeletesWhenNoRoom()
    {
        // Плотный ряд, и точно на середине — лишняя страза: её некуда сдвинуть.
        var stones = new List<DocStone>();
        for (int i = 0; i < 5; i++)
        {
            stones.Add(S(i * (D + 0.1), 0, order: 10 + i));
            stones.Add(S(i * (D + 0.1), D + 0.1, order: 20 + i));
            stones.Add(S(i * (D + 0.1), -(D + 0.1), order: 30 + i));
        }

        stones.Add(S(2 * (D + 0.1), 0.2, order: 1));
        EditResult r = StoneEditor.ShiftApart(stones);
        Assert.Contains(stones.Count - 1, r.Deleted);
        AssertNoOverlaps(stones, r);
    }

    [Fact]
    public void Shift_NeverMovesLocked()
    {
        var stones = new[] { S(0, 0, 2, locked: true), S(1, 0, 1, locked: true) };
        EditResult r = StoneEditor.ShiftApart(stones);
        Assert.True(r.IsEmpty);
    }

    [Fact]
    public void AlongLine_RemovesOnlyStonesTheLineTouches()
    {
        var stones = new[] { S(0, 0, 1), S(0, 1.1, 2), S(0, 1.3, 3), S(0, -5, 4) };
        var cutter = Curve.FromPolyline(new List<Point2D> { new(-10, 0), new(10, 0) }, false);
        EditResult r = StoneEditor.DeleteAlongLines(stones, new[] { cutter });
        Assert.Equal(new[] { 0, 1 }, r.Deleted);
    }

    [Fact]
    public void InsideShape_RespectsHoles()
    {
        var stones = new[] { S(5, 5, 1), S(15, 15, 2), S(50, 50, 3) };
        Curve[] ringShape = { Square(0, 0, 30), Square(10, 10, 10) };

        Assert.Equal(new[] { 0 }, StoneEditor.DeleteByShape(stones, ringShape, inside: true).Deleted);
        Assert.Equal(new[] { 1, 2 }, StoneEditor.DeleteByShape(stones, ringShape, inside: false).Deleted);
    }

    [Fact]
    public void InsideShape_TouchingToo_TakesStonesOnTheEdge()
    {
        var stones = new[] { S(-0.5, 5, 1), S(-5, 5, 2) };
        Curve[] shape = { Square(0, 0, 10) };
        Assert.Empty(StoneEditor.DeleteByShape(stones, shape, inside: true).Deleted);
        Assert.Equal(new[] { 0 }, StoneEditor.DeleteByShape(stones, shape, inside: true, touchingToo: true).Deleted);
    }

    [Fact]
    public void Duplicates_KeepOneOfEachPile_TopmostOrLocked()
    {
        var stones = new[]
        {
            S(0, 0, 1), S(0.01, 0, 5), S(0, 0.02, 3),
            S(10, 0, 1, locked: true), S(10, 0, 9),
            S(20, 0, 1), S(20, 0, 2, d: 3.9),
        };

        EditResult r = StoneEditor.FindDuplicates(stones);
        Assert.Equal(new[] { 0, 2, 4 }, r.Deleted);
    }

    [Fact]
    public void Fast_On50000Stones()
    {
        var stones = new List<DocStone>();
        var rnd = new Random(1);
        for (int i = 0; i < 50_000; i++)
        {
            stones.Add(S(rnd.NextDouble() * 600, rnd.NextDouble() * 600, i));
        }

        var sw = Stopwatch.StartNew();
        StoneEditor.ResolveOverlaps(stones, keepTop: true);
        StoneEditor.FindDuplicates(stones);
        Assert.True(sw.Elapsed.TotalSeconds < 3, $"{sw.Elapsed.TotalSeconds:0.00} с");
    }
}
