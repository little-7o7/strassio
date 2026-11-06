using Strassio.Core.Editing;
using Strassio.Core.Geometry;

namespace Strassio.Core.Tests.Editing;

/// <summary>«Выровнять просветы» (docs/SPEC.md, 7.2): ряд остаётся на линии, концы, углы и закреплённые стоят.</summary>
public class GapEvenerTests
{
    private const double D = 2.4;

    private static DocStone S(double x, double y, bool locked = false, double d = D) => new(new Point2D(x, y), d, 0, locked);

    private static Point2D[] After(IReadOnlyList<DocStone> stones, GapEvenResult result)
    {
        Point2D[] centers = stones.Select(s => s.Center).ToArray();
        foreach (StoneMove move in result.Edit.Moved)
        {
            centers[move.Index] = move.NewCenter;
        }

        return centers;
    }

    private static double[] Gaps(Point2D[] line) =>
        Enumerable.Range(0, line.Length - 1).Select(i => Point2D.Distance(line[i], line[i + 1]) - D).ToArray();

    /// <summary>Ряд с просветом 0,2 мм и одним большим, 1,4 мм, посередине.</summary>
    private static List<DocStone> LineWithBigGap(int count = 9, double gap = 0.2, double big = 1.4)
    {
        var stones = new List<DocStone>();
        double x = 0;
        for (int i = 0; i < count; i++)
        {
            stones.Add(S(x, 0));
            x += D + (i == count / 2 ? big : gap);
        }

        return stones;
    }

    [Fact]
    public void BigGapInLine_IsSpreadOut_EndsStay_LineStays()
    {
        List<DocStone> stones = LineWithBigGap();
        GapEvenResult result = GapEvener.Even(stones, maxShiftMm: 1.0, minGapMm: 0.1);

        Point2D[] after = After(stones, result);
        double[] gaps = Gaps(after);
        Assert.True(gaps.Max() - gaps.Min() < 0.25, string.Join(" ", gaps.Select(g => g.ToString("0.00"))));
        Assert.Equal(stones[0].Center, after[0]);
        Assert.Equal(stones[^1].Center, after[^1]);
        Assert.All(after, p => Assert.Equal(0, p.Y, 9));
        Assert.True(result.WorstAfterMm < result.WorstBeforeMm / 2, $"{result.WorstBeforeMm} → {result.WorstAfterMm}");
        Assert.Empty(result.Edit.Deleted);
    }

    [Fact]
    public void FindUneven_PointsAtTheBigGap()
    {
        List<DocStone> stones = LineWithBigGap();
        IReadOnlyList<int> uneven = GapEvener.FindUneven(stones, 0.2, out double worst);

        Assert.Equal(new[] { 4, 5 }, uneven);
        Assert.Equal(1.2, worst, 6);
    }

    [Fact]
    public void EvenLine_NothingToDo()
    {
        var stones = Enumerable.Range(0, 20).Select(i => S(i * 2.7, 0)).ToList();
        GapEvenResult result = GapEvener.Even(stones, 0.5, 0.1);

        Assert.Empty(result.UnevenBefore);
        Assert.Empty(result.Edit.Moved);
    }

    [Fact]
    public void LockedStones_DoNotMove()
    {
        List<DocStone> stones = LineWithBigGap();
        stones[4] = S(stones[4].Center.X, 0, locked: true);
        stones[5] = S(stones[5].Center.X, 0, locked: true);

        GapEvenResult result = GapEvener.Even(stones, 1.0, 0.1);

        Assert.DoesNotContain(result.Edit.Moved, m => m.Index == 4 || m.Index == 5);
    }

    [Fact]
    public void MaxShift_IsRespected()
    {
        List<DocStone> stones = LineWithBigGap(big: 2.0);
        GapEvenResult result = GapEvener.Even(stones, maxShiftMm: 0.3, minGapMm: 0.1);

        Assert.NotEmpty(result.Edit.Moved);
        Assert.All(result.Edit.Moved, m => Assert.True(Point2D.Distance(m.NewCenter, stones[m.Index].Center) <= 0.3 + 1e-9));
        Assert.NotEmpty(result.UnevenAfter); // разница была большая, за 0,3 мм не исправить
    }

    [Fact]
    public void CornerStone_StaysPut()
    {
        // Угол буквы «Г»: страза в углу — соседи под прямым углом, пары нет.
        var stones = new List<DocStone>();
        for (int i = 0; i < 5; i++)
        {
            stones.Add(S(i * 2.6, 0));
        }

        stones.Add(S(0, 3.5)); // большой просвет вверх от угла
        stones.Add(S(0, 3.5 + 2.6));
        stones.Add(S(0, 3.5 + 5.2));

        GapEvenResult result = GapEvener.Even(stones, 1.0, 0.1);

        Assert.DoesNotContain(result.Edit.Moved, m => m.Index == 0);
        Assert.Contains(result.Edit.Moved, m => m.Index == 5); // а вот сосед угла выравнивается
        Assert.Equal(0, After(stones, result)[5].X, 9);
    }

    [Fact]
    public void Arc_StonesStayCloseToTheCurve()
    {
        // Дуга окружности радиусом 20 мм, один просвет большой.
        const double radius = 20;
        var stones = new List<DocStone>();
        double angle = 0;
        for (int i = 0; i < 12; i++)
        {
            stones.Add(S(radius * Math.Cos(angle), radius * Math.Sin(angle)));
            angle += (D + (i == 6 ? 1.2 : 0.25)) / radius;
        }

        GapEvenResult result = GapEvener.Even(stones, 0.8, 0.1);

        Point2D[] after = After(stones, result);
        Assert.All(after, p => Assert.InRange(p.Length, radius - 0.1, radius + 0.02));
        Assert.True(result.WorstAfterMm < 0.3, result.WorstAfterMm.ToString());
    }

    [Fact]
    public void JitteredFill_NoNewTightSpots()
    {
        var random = new Random(7);
        var stones = new List<DocStone>();
        for (int row = 0; row < 25; row++)
        {
            for (int col = 0; col < 25; col++)
            {
                double x = col * 2.7 + (row % 2) * 1.35 + (random.NextDouble() - 0.5) * 0.5;
                double y = row * 2.7 * Math.Sqrt(3) / 2 + (random.NextDouble() - 0.5) * 0.5;
                stones.Add(S(x, y));
            }
        }

        const double minGap = 0.15;
        GapEvenResult result = GapEvener.Even(stones, 0.5, minGap);
        Point2D[] after = After(stones, result);

        Assert.NotEmpty(result.Edit.Moved);
        Assert.True(result.WorstAfterMm < result.WorstBeforeMm, $"{result.WorstBeforeMm} → {result.WorstAfterMm}");
        Assert.True(result.UnevenAfter.Count < result.UnevenBefore.Count, $"{result.UnevenBefore.Count} → {result.UnevenAfter.Count}");
        for (int i = 0; i < stones.Count; i++)
        {
            for (int j = i + 1; j < stones.Count; j++)
            {
                double was = Point2D.Distance(stones[i].Center, stones[j].Center) - D;
                double now = Point2D.Distance(after[i], after[j]) - D;
                Assert.True(now >= Math.Min(minGap, was) - 1e-6, $"{i}-{j}: было {was:0.000}, стало {now:0.000}");
            }
        }
    }

    [Fact]
    public void TenThousandStones_Fast()
    {
        var random = new Random(3);
        var stones = new List<DocStone>();
        for (int row = 0; row < 100; row++)
        {
            for (int col = 0; col < 100; col++)
            {
                stones.Add(S(col * 2.7 + (row % 2) * 1.35 + random.NextDouble() * 0.3, row * 2.34 + random.NextDouble() * 0.3));
            }
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        GapEvener.Even(stones, 0.5, 0.15);
        // Отладочная сборка и тесты параллельно — с запасом; в CorelDRAW (Release) втрое быстрее.
        Assert.True(watch.ElapsedMilliseconds < 2000, $"{watch.ElapsedMilliseconds} мс");
    }
}
