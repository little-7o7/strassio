using Strassio.Core.Editing;
using Strassio.Core.Geometry;

namespace Strassio.Core.Tests.Editing;

/// <summary>Омбре (пункт 7): цвета плавно перетекают, без полосы и без кучек.</summary>
public class OmbreTests
{
    private static (List<Point2D> C, List<double> D) Honeycomb(int rows, int cols)
    {
        var c = new List<Point2D>();
        for (int r = 0; r < rows; r++)
        {
            for (int k = 0; k < cols; k++)
            {
                c.Add(new Point2D(k * 2.6 + (r % 2) * 1.3, r * 2.25));
            }
        }

        return (c, Enumerable.Repeat(2.4, c.Count).ToList());
    }

    /// <summary>Доля цвета <paramref name="color"/> в полосе x ∈ [from, to).</summary>
    private static double ShareIn(List<Point2D> c, int[] colors, int color, double from, double to)
    {
        int[] inBand = Enumerable.Range(0, c.Count).Where(i => c[i].X >= from && c[i].X < to).ToArray();
        return inBand.Count(i => colors[i] == color) / (double)inBand.Length;
    }

    [Fact]
    public void TwoColors_LeftToRight_ShareGrowsSmoothly()
    {
        (List<Point2D> c, List<double> d) = Honeycomb(20, 40);
        int[] colors = ColorMixer.Ombre(c, d, 2, ColorMixer.OmbreDirection.LeftToRight, 3);
        double width = c.Max(p => p.X);

        double previous = -1;
        for (int band = 0; band < 5; band++)
        {
            double share = ShareIn(c, colors, 1, band * width / 5, (band + 1) * width / 5 + (band == 4 ? 1 : 0));
            double expected = (band + 0.5) / 5;
            Assert.InRange(share, expected - 0.12, expected + 0.12);
            Assert.True(share > previous, $"полоса {band}: {share:0.00}");
            previous = share;
        }
    }

    [Fact]
    public void ThreeColors_MiddleColorInTheMiddle()
    {
        (List<Point2D> c, List<double> d) = Honeycomb(20, 45);
        int[] colors = ColorMixer.Ombre(c, d, 3, ColorMixer.OmbreDirection.LeftToRight, 5);
        double width = c.Max(p => p.X);

        Assert.True(ShareIn(c, colors, 0, 0, width * 0.1) > 0.7);
        Assert.True(ShareIn(c, colors, 1, width * 0.45, width * 0.55) > 0.7);
        Assert.True(ShareIn(c, colors, 2, width * 0.9, width + 1) > 0.7);
        Assert.Equal(0, ShareIn(c, colors, 2, 0, width * 0.4));
    }

    [Fact]
    public void FewerClumps_ThanRandomDither()
    {
        (List<Point2D> c, List<double> d) = Honeycomb(30, 30);
        int[] ombre = ColorMixer.Ombre(c, d, 2, ColorMixer.OmbreDirection.CenterOut, 7);

        // Тот же переход, но цвет каждого камня — просто случайный по доле.
        var rnd = new Random(7);
        var center = new Point2D(c.Average(p => p.X), c.Average(p => p.Y));
        double maxR = c.Max(p => Point2D.Distance(p, center));
        int[] dither = c.Select(p => rnd.NextDouble() < Point2D.Distance(p, center) / maxR ? 1 : 0).ToArray();

        int Clumps(int[] colors) => ColorMixer.SameColorTouches(c, d, colors, 0) + ColorMixer.SameColorTouches(c, d, colors, 1);
        Assert.True(Clumps(ombre) < Clumps(dither), $"омбре {Clumps(ombre)}, случайно {Clumps(dither)}");
        Assert.Equal(0, ombre[Enumerable.Range(0, c.Count).OrderBy(i => Point2D.Distance(c[i], center)).First()]);
    }

    [Fact]
    public void AlongLine_FollowsStoneOrder()
    {
        List<Point2D> c = Enumerable.Range(0, 60).Select(i => new Point2D(30 * Math.Cos(i * 0.1), 30 * Math.Sin(i * 0.1))).ToList();
        List<double> d = Enumerable.Repeat(2.4, c.Count).ToList();
        int[] colors = ColorMixer.Ombre(c, d, 2, ColorMixer.OmbreDirection.AlongLine, 1);

        Assert.True(colors.Take(10).Count(x => x == 0) >= 8);
        Assert.True(colors.Skip(50).Count(x => x == 1) >= 8);
    }

    [Fact]
    public void TopToBottom_TopIsFirstColor()
    {
        (List<Point2D> c, List<double> d) = Honeycomb(30, 10);
        int[] colors = ColorMixer.Ombre(c, d, 2, ColorMixer.OmbreDirection.TopToBottom, 2);
        double top = c.Max(p => p.Y);

        int[] topRow = Enumerable.Range(0, c.Count).Where(i => c[i].Y > top - 0.1).ToArray();
        Assert.All(topRow, i => Assert.Equal(0, colors[i]));
    }
}
