using Strassio.Core.Editing;
using Strassio.Core.Geometry;

namespace Strassio.Core.Tests.Editing;

/// <summary>Симметрия (пункт 8): половина или четверть → целое, камни на оси не двоятся.</summary>
public class SymmetryTests
{
    private static DocStone S(double x, double y, bool locked = false) => new(new Point2D(x, y), 2.4, 0, locked);

    private static List<Point2D> All(IReadOnlyList<DocStone> stones, MirrorResult result)
    {
        List<Point2D> all = stones.Select(s => s.Center).ToList();
        foreach (StoneMove m in result.Snapped)
        {
            all[m.Index] = m.NewCenter;
        }

        all.AddRange(result.Added.Select(a => a.Center));
        return all;
    }

    private static void AssertNoOverlaps(List<Point2D> all, double minGap)
    {
        for (int i = 0; i < all.Count; i++)
        {
            for (int j = i + 1; j < all.Count; j++)
            {
                Assert.True(Point2D.Distance(all[i], all[j]) - 2.4 >= minGap - 0.051, $"{all[i]} и {all[j]}");
            }
        }
    }

    [Fact]
    public void HalfHeart_MirroredRight_AxisStonesNotDoubled()
    {
        // Левая половина: ряд по горизонтали до оси x = 10.4 (последняя страза на оси) и ещё ряд выше.
        var stones = new List<DocStone>();
        for (int i = 0; i < 5; i++)
        {
            stones.Add(S(i * 2.6, 0));
            if (i < 4)
            {
                stones.Add(S(i * 2.6 + 1.3, 2.25));
            }
        }

        MirrorResult result = Symmetry.Mirror(stones, new Point2D(10.4, 0), new Point2D(0, 1), 0.2);
        List<Point2D> all = All(stones, result);

        Assert.Equal(4 + 4, result.Added.Count); // страза на оси (x=10.4) не копируется
        Assert.All(result.Added, a => Assert.True(a.Center.X > 10.4));
        AssertNoOverlaps(all, 0.2);

        // Симметрично: у каждой стразы есть зеркальная.
        foreach (Point2D p in all)
        {
            var mirror = new Point2D(20.8 - p.X, p.Y);
            Assert.Contains(all, q => Point2D.Distance(q, mirror) < 1e-6);
        }
    }

    [Fact]
    public void StoneJustBesideAxis_SnapsOntoIt()
    {
        var stones = new List<DocStone> { S(0, 0), S(2.6, 0), S(4.9, 0) };
        MirrorResult result = Symmetry.Mirror(stones, new Point2D(5.2, 0), new Point2D(0, 1), 0.2);

        StoneMove snap = Assert.Single(result.Snapped);
        Assert.Equal(2, snap.Index);
        Assert.Equal(5.2, snap.NewCenter.X, 9);
        Assert.Equal(2, result.Added.Count);
    }

    [Fact]
    public void Quarter_BecomesFourfold()
    {
        // Четверть: квадрат 3×3 (правый столбец и нижний ряд — на осях).
        var stones = new List<DocStone>();
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                stones.Add(S(c * 2.6, r * 2.6));
            }
        }

        MirrorResult result = Symmetry.Quarter(stones, 0.2);

        Assert.Equal(25 - 9, result.Added.Count); // целое 5×5 без повторов
        AssertNoOverlaps(All(stones, result), 0.2);
    }

    [Fact]
    public void SlantedAxis_ReflectsCorrectly()
    {
        Point2D p = Symmetry.Reflect(new Point2D(2, 0), new Point2D(0, 0), new Point2D(1, 1));
        Assert.Equal(0, p.X, 9);
        Assert.Equal(2, p.Y, 9);
    }

    [Fact]
    public void CopyThatWouldOverlap_IsSkipped()
    {
        // Страза по ту сторону оси: копия соседней налезла бы на неё — копия не ставится.
        var stones = new List<DocStone> { S(0, 0), S(3.7, 0), S(7.4, 2.0) };
        MirrorResult result = Symmetry.Mirror(stones, new Point2D(5.2, 0), new Point2D(0, 1), 0.2);

        AssertNoOverlaps(All(stones, result), 0.2);
        Assert.True(result.Skipped >= 1);
    }

    [Fact]
    public void AxisAt_UsesExtremeCenters()
    {
        var stones = new List<DocStone> { S(1, 5), S(7, -2) };
        Assert.Equal(7, Symmetry.AxisAt(stones, MirrorSide.Right).Point.X);
        Assert.Equal(1, Symmetry.AxisAt(stones, MirrorSide.Left).Point.X);
        Assert.Equal(-2, Symmetry.AxisAt(stones, MirrorSide.Down).Point.Y);
        Assert.Equal(5, Symmetry.AxisAt(stones, MirrorSide.Up).Point.Y);
    }
}
