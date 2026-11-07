using Strassio.Core.Editing;
using Strassio.Core.Geometry;
using Strassio.Core.Methods;
using Strassio.Core.Placement;

namespace Strassio.Core.Tests.Editing;

/// <summary>Крайние ряды (пункт 6): по самим стразам, у внешнего края и у отверстий.</summary>
public class EdgeRowsTests
{
    private static Curve Circle(double r, int n = 120) => Curve.FromPolyline(
        Enumerable.Range(0, n).Select(i => new Point2D(r * Math.Cos(i * 2 * Math.PI / n), r * Math.Sin(i * 2 * Math.PI / n))).ToList(),
        isClosed: true);

    private static (List<Point2D> C, List<double> D) Of(IReadOnlyList<PlacedStone> stones) =>
        (stones.Select(s => s.Center).ToList(), stones.Select(s => s.DiameterMm).ToList());

    [Fact]
    public void Honeycomb_OuterRingFound_InnerNot()
    {
        MethodResult made = MethodRunner.Run(MethodKind.F2, new[] { Circle(15) }, 2.4, new MethodParameters { GapMm = 0.2 });
        (List<Point2D> c, List<double> d) = Of(made.Stones);

        int[] row = EdgeRows.Find(c, d, 1);

        double maxR = c.Max(p => p.Length);
        for (int i = 0; i < c.Count; i++)
        {
            if (c[i].Length > maxR - 1.0)
            {
                Assert.True(row[i] == 1, $"камень на {c[i].Length:0.0} мм от центра должен быть крайним");
            }

            if (c[i].Length < maxR - 5)
            {
                Assert.True(row[i] == 0, $"камень на {c[i].Length:0.0} мм от центра — не край");
            }
        }
    }

    [Fact]
    public void TwoRows_SecondRowInsideFirst()
    {
        MethodResult made = MethodRunner.Run(MethodKind.F4, new[] { Circle(15) }, 2.4, new MethodParameters { GapMm = 0.2, Rings = 3 });
        (List<Point2D> c, List<double> d) = Of(made.Stones);

        int[] row = EdgeRows.Find(c, d, 2);

        Assert.Contains(1, row);
        Assert.Contains(2, row);
        Assert.Contains(0, row);
        double first = Enumerable.Range(0, c.Count).Where(i => row[i] == 1).Average(i => c[i].Length);
        double second = Enumerable.Range(0, c.Count).Where(i => row[i] == 2).Average(i => c[i].Length);
        Assert.True(second < first - 1.5, $"{first:0.0} / {second:0.0}");
    }

    [Fact]
    public void RingWithHole_BothEdges()
    {
        // Кольцо: снаружи 15 мм, внутри отверстие 7 мм — край есть и у отверстия.
        MethodResult made = MethodRunner.Run(MethodKind.F2, new[] { Circle(15), Circle(7) }, 2.4, new MethodParameters { GapMm = 0.2 });
        (List<Point2D> c, List<double> d) = Of(made.Stones);

        int[] row = EdgeRows.Find(c, d, 1);

        double minR = c.Min(p => p.Length);
        Assert.All(Enumerable.Range(0, c.Count).Where(i => c[i].Length < minR + 0.8), i => Assert.Equal(1, row[i]));
    }

    [Fact]
    public void SingleMissingStone_IsNotAnEdge()
    {
        var c = new List<Point2D>();
        for (int r = 0; r < 12; r++)
        {
            for (int k = 0; k < 12; k++)
            {
                if (r == 6 && k == 6)
                {
                    continue; // дырка в один камень посередине
                }

                c.Add(new Point2D(k * 2.6 + (r % 2) * 1.3, r * 2.6 * Math.Sqrt(3) / 2));
            }
        }

        List<double> d = Enumerable.Repeat(2.4, c.Count).ToList();
        int[] row = EdgeRows.Find(c, d, 1);

        var hole = new Point2D(6 * 2.6, 6 * 2.6 * Math.Sqrt(3) / 2);
        Assert.All(Enumerable.Range(0, c.Count).Where(i => Point2D.Distance(c[i], hole) < 3), i => Assert.Equal(0, row[i]));
    }

    [Fact]
    public void BandAroundLine_OuterRowsOnBothSides()
    {
        // Три ряда вдоль прямой: крайние — верхний и нижний, средний — нет.
        var c = new List<Point2D>();
        for (int r = -1; r <= 1; r++)
        {
            for (int k = 0; k < 20; k++)
            {
                c.Add(new Point2D(k * 2.6, r * 2.8));
            }
        }

        List<double> d = Enumerable.Repeat(2.4, c.Count).ToList();
        int[] row = EdgeRows.Find(c, d, 1);

        for (int i = 0; i < c.Count; i++)
        {
            bool middleRowInside = Math.Abs(c[i].Y) < 0.1 && c[i].X > 1 && c[i].X < 19 * 2.6 - 1;
            Assert.Equal(middleRowInside ? 0 : 1, row[i]);
        }
    }

    [Fact]
    public void TenThousand_Fast()
    {
        var c = new List<Point2D>();
        for (int r = 0; r < 100; r++)
        {
            for (int k = 0; k < 100; k++)
            {
                c.Add(new Point2D(k * 2.6 + (r % 2) * 1.3, r * 2.25));
            }
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        int[] row = EdgeRows.Find(c, Enumerable.Repeat(2.4, c.Count).ToList(), 2);
        Assert.True(watch.ElapsedMilliseconds < 1500, $"{watch.ElapsedMilliseconds} мс");
        Assert.Equal(4 * 99, row.Count(x => x == 1));
    }
}
