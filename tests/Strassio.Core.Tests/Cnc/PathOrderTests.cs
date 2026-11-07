using System.Diagnostics;
using Strassio.Core.Cnc;
using Strassio.Core.Geometry;

namespace Strassio.Core.Tests.Cnc;

/// <summary>Порядок обхода отверстий: каждое ровно один раз, короткий путь, быстро на 10 000.</summary>
public class PathOrderTests
{
    private static List<Point2D> Random(int n, double w, double h, int seed)
    {
        var rnd = new Random(seed);
        return Enumerable.Range(0, n).Select(_ => new Point2D(rnd.NextDouble() * w, rnd.NextDouble() * h)).ToList();
    }

    private static void AssertPermutation(int[] order, int n)
    {
        Assert.Equal(n, order.Length);
        Assert.Equal(Enumerable.Range(0, n), order.OrderBy(i => i));
    }

    [Fact]
    public void Empty_AndSingle()
    {
        Assert.Empty(PathOrder.Order(new List<Point2D>(), Point2D.Zero));
        Assert.Equal(new[] { 0 }, PathOrder.Order(new[] { new Point2D(5, 5) }, Point2D.Zero));
    }

    [Fact]
    public void PointsOnALine_AreVisitedInOrderFromStart()
    {
        var points = new[] { 30, 10, 50, 20, 40 }.Select(x => new Point2D(x, 0)).ToList();
        int[] order = PathOrder.Order(points, Point2D.Zero);
        Assert.Equal(new[] { 10.0, 20, 30, 40, 50 }, order.Select(i => points[i].X));
    }

    [Fact]
    public void Grid_PathIsNearlyOptimal()
    {
        // Сетка 40 × 25 с шагом 3 мм: лучший путь — змейка, 999 переходов по 3 мм.
        var points = new List<Point2D>();
        for (int y = 0; y < 25; y++)
        {
            for (int x = 0; x < 40; x++)
            {
                points.Add(new Point2D(5 + x * 3, 5 + y * 3));
            }
        }

        var rnd = new Random(11);
        var shuffled = points.OrderBy(_ => rnd.Next()).ToList();
        int[] order = PathOrder.Order(shuffled, Point2D.Zero);
        AssertPermutation(order, shuffled.Count);
        double length = PathOrder.Length(shuffled, order, Point2D.Zero, returnToStart: false);
        double best = 999 * 3 + Math.Sqrt(50);
        Assert.True(length < best * 1.12, $"путь {length:0} мм, лучший около {best:0} мм");
    }

    [Fact]
    public void Improvement_NeverMakesPathLonger()
    {
        List<Point2D> points = Random(2000, 300, 200, 7);
        int[] plain = PathOrder.Order(points, Point2D.Zero, improve: false);
        int[] better = PathOrder.Order(points, Point2D.Zero, improve: true);
        AssertPermutation(plain, points.Count);
        AssertPermutation(better, points.Count);
        double a = PathOrder.Length(points, plain, Point2D.Zero, false);
        double b = PathOrder.Length(points, better, Point2D.Zero, false);
        Assert.True(b <= a + 1e-6);
        Assert.True(b < a * 0.95, $"улучшение слабое: {a:0} → {b:0}");
    }

    [Fact]
    public void TenThousandHoles_FastAndComplete()
    {
        List<Point2D> points = Random(10000, 600, 400, 3);
        var clock = Stopwatch.StartNew();
        int[] order = PathOrder.Order(points, Point2D.Zero);
        clock.Stop();
        AssertPermutation(order, points.Count);
        Assert.True(clock.ElapsedMilliseconds < 4000, $"{clock.ElapsedMilliseconds} мс");
    }

    [Fact]
    public void StartFarOutside_StillFindsNearest()
    {
        var points = new[] { new Point2D(100, 100), new Point2D(101, 100), new Point2D(200, 100) }.ToList();
        int[] order = PathOrder.Order(points, new Point2D(-500, 100));
        Assert.Equal(0, order[0]);
        Assert.Equal(1, order[1]);
        Assert.Equal(2, order[2]);
    }

    [Fact]
    public void DuplicatePoints_AreAllVisited()
    {
        var points = Enumerable.Repeat(new Point2D(10, 10), 5).Concat(Enumerable.Repeat(new Point2D(20, 10), 5)).ToList();
        int[] order = PathOrder.Order(points, Point2D.Zero);
        AssertPermutation(order, points.Count);
    }
}
