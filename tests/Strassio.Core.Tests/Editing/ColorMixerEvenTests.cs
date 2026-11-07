using Strassio.Core.Editing;
using Strassio.Core.Geometry;

namespace Strassio.Core.Tests.Editing;

/// <summary>«Равномерно» (пункт 5 прокачки): доли точные, одинаковые камни не слипаются.</summary>
public class ColorMixerEvenTests
{
    private static (List<Point2D> Centers, List<double> Diameters) Honeycomb(int rows, int cols, double d = 2.4, double gap = 0.2)
    {
        var centers = new List<Point2D>();
        double pitch = d + gap;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                centers.Add(new Point2D(c * pitch + (r % 2) * pitch / 2, r * pitch * Math.Sqrt(3) / 2));
            }
        }

        return (centers, Enumerable.Repeat(d, centers.Count).ToList());
    }

    [Fact]
    public void KeepsExactShares()
    {
        (List<Point2D> centers, List<double> d) = Honeycomb(20, 20);
        int[] colors = ColorMixer.Even(centers, d, new[] { 70.0, 30.0 }, seed: 1);

        Assert.Equal(280, colors.Count(c => c == 0));
        Assert.Equal(120, colors.Count(c => c == 1));
    }

    [Fact]
    public void RareColor_AlmostNeverTouchesItself_UnlikeRandom()
    {
        (List<Point2D> centers, List<double> d) = Honeycomb(30, 30);
        var weights = new[] { 70.0, 30.0 };

        int random = ColorMixer.SameColorTouches(centers, d, ColorMixer.Random(centers.Count, weights, 3), 1);
        int even = ColorMixer.SameColorTouches(centers, d, ColorMixer.Even(centers, d, weights, 3), 1);

        // В сотах 30% можно разложить совсем без касаний (каждый третий камень) — почти так и выходит.
        Assert.True(random > 200, random.ToString());
        Assert.True(even < random / 10, $"случайно {random}, равномерно {even}");
    }

    [Fact]
    public void StonesInAnyOrder_StillSpreadOut()
    {
        (List<Point2D> centers, List<double> d) = Honeycomb(30, 30);
        var rnd = new Random(2);
        List<int> order = Enumerable.Range(0, centers.Count).OrderBy(_ => rnd.Next()).ToList();
        List<Point2D> shuffled = order.Select(i => centers[i]).ToList();
        var weights = new[] { 70.0, 30.0 };

        int random = ColorMixer.SameColorTouches(shuffled, d, ColorMixer.Random(shuffled.Count, weights, 3), 1);
        int even = ColorMixer.SameColorTouches(shuffled, d, ColorMixer.Even(shuffled, d, weights, 3), 1);

        Assert.True(even < random / 6, $"случайно {random}, равномерно {even}");
    }

    [Fact]
    public void ThreeColors_EachSpreadOut()
    {
        (List<Point2D> centers, List<double> d) = Honeycomb(24, 24);
        var weights = new[] { 50.0, 30.0, 20.0 };
        int[] even = ColorMixer.Even(centers, d, weights, 9);
        int[] random = ColorMixer.Random(centers.Count, weights, 9);

        for (int c = 1; c < 3; c++)
        {
            int e = ColorMixer.SameColorTouches(centers, d, even, c);
            int r = ColorMixer.SameColorTouches(centers, d, random, c);
            Assert.True(e < r / 4, $"цвет {c}: случайно {r}, равномерно {e}");
        }
    }

    [Fact]
    public void Line_Alternates()
    {
        List<Point2D> centers = Enumerable.Range(0, 40).Select(i => new Point2D(i * 2.6, 0)).ToList();
        List<double> d = Enumerable.Repeat(2.4, 40).ToList();
        int[] colors = ColorMixer.Even(centers, d, new[] { 50.0, 50.0 }, 4);

        Assert.True(
            ColorMixer.SameColorTouches(centers, d, colors, 0) + ColorMixer.SameColorTouches(centers, d, colors, 1) == 0,
            string.Concat(colors));
    }

    [Fact]
    public void SameSeed_SameResult_AndFastOnTenThousand()
    {
        (List<Point2D> centers, List<double> d) = Honeycomb(100, 100);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        int[] a = ColorMixer.Even(centers, d, new[] { 70.0, 30.0 }, 11);
        long ms = watch.ElapsedMilliseconds;

        Assert.Equal(a, ColorMixer.Even(centers, d, new[] { 70.0, 30.0 }, 11));
        Assert.True(ms < 1500, $"{ms} мс");
    }

    [Fact]
    public void OneColor_OrTiny_Works()
    {
        (List<Point2D> centers, List<double> d) = Honeycomb(3, 3);
        Assert.All(ColorMixer.Even(centers, d, new[] { 100.0, 0.0 }, 1), c => Assert.Equal(0, c));
        Assert.Empty(ColorMixer.Even(new List<Point2D>(), new List<double>(), new[] { 50.0, 50.0 }, 1));
    }
}
