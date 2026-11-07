using Strassio.Core.Editing;
using Strassio.Core.Geometry;
using Strassio.Core.Stones;

namespace Strassio.Core.Tests.Editing;

/// <summary>«Перекрасить по рисунку»: какая фигура под стразой и какой камень ближе по цвету.</summary>
public class ArtworkColorsTests
{
    private static Curve Square(double x, double y, double size) => Curve.FromPolyline(
        new[] { new Point2D(x, y), new Point2D(x + size, y), new Point2D(x + size, y + size), new Point2D(x, y + size) },
        isClosed: true);

    private static List<StoneColor> Palette() => new()
    {
        new StoneColor { Name = "Кристалл", Rgb = "#E8EEF5" },
        new StoneColor { Name = "Красный", Rgb = "#E53935" },
        new StoneColor { Name = "Зелёный", Rgb = "#43A047" },
        new StoneColor { Name = "Золото", Rgb = "#D4AF37" },
        new StoneColor { Name = "Чёрный", Rgb = "#202020" },
    };

    [Fact]
    public void StoneTakesShapeUnderItsCenter()
    {
        var regions = new List<ColorRegion>
        {
            new(new[] { Square(0, 0, 10) }, 255, 0, 0, order: 0),
            new(new[] { Square(10, 0, 10) }, 0, 0, 255, order: 1),
        };

        int[] result = ArtworkColors.RegionOf(
            new[] { new Point2D(3, 3), new Point2D(17, 5), new Point2D(30, 5) }, regions);

        Assert.Equal(new[] { 0, 1, -1 }, result);
    }

    [Fact]
    public void OverlappingShapes_TopOneWins()
    {
        Curve big = Square(0, 0, 20);
        Curve spot = Square(5, 5, 4);
        Point2D inSpot = new(7, 7);
        Point2D onlyBig = new(15, 15);

        // Пятно нарисовано поверх фона — стразы на пятне берут его цвет.
        int[] spotOnTop = ArtworkColors.RegionOf(new[] { inSpot, onlyBig }, new List<ColorRegion>
        {
            new(new[] { big }, 255, 255, 255, order: 0),
            new(new[] { spot }, 0, 0, 0, order: 5),
        });
        Assert.Equal(new[] { 1, 0 }, spotOnTop);

        // Фон поверх пятна закрывает его целиком — пятна не видно, и стразы его не замечают.
        int[] bigOnTop = ArtworkColors.RegionOf(new[] { inSpot, onlyBig }, new List<ColorRegion>
        {
            new(new[] { big }, 255, 255, 255, order: 9),
            new(new[] { spot }, 0, 0, 0, order: 5),
        });
        Assert.Equal(new[] { 0, 0 }, bigOnTop);
    }

    [Fact]
    public void HoleOfShape_IsNotPartOfIt()
    {
        // Кольцо: внешний квадрат и дырка — центр в дырке не берёт цвет кольца.
        var ring = new ColorRegion(new[] { Square(0, 0, 20), Square(6, 6, 8) }, 255, 0, 0, order: 0);

        Assert.True(ring.Contains(new Point2D(2, 2)));
        Assert.False(ring.Contains(new Point2D(10, 10)));
        Assert.Equal(new[] { -1 }, ArtworkColors.RegionOf(new[] { new Point2D(10, 10) }, new[] { ring }));
    }

    [Fact]
    public void OpenLine_ColorsNothing()
    {
        Curve line = Curve.FromPolyline(new[] { new Point2D(0, 0), new Point2D(10, 0), new Point2D(10, 10) }, isClosed: false);
        var region = new ColorRegion(new[] { line }, 255, 0, 0, order: 0);

        Assert.True(region.IsEmpty);
        Assert.False(region.Contains(new Point2D(8, 2)));
    }

    [Theory]
    [InlineData(250, 0, 0, "Красный")]
    [InlineData(140, 20, 20, "Красный")] // тёмно-красный — всё равно красный, а не чёрный
    [InlineData(255, 215, 0, "Золото")]
    [InlineData(255, 255, 255, "Кристалл")]
    [InlineData(0, 128, 0, "Зелёный")]
    [InlineData(0, 0, 0, "Чёрный")]
    public void Nearest_PicksColorAsEyeSeesIt(byte r, byte g, byte b, string expected)
    {
        List<StoneColor> palette = Palette();
        Assert.Equal(expected, palette[ArtworkColors.Nearest(r, g, b, palette)].Name);
    }

    [Fact]
    public void Nearest_EmptyOrBrokenPalette_ReturnsMinusOne()
    {
        Assert.Equal(-1, ArtworkColors.Nearest(10, 20, 30, new List<StoneColor>()));
        Assert.Equal(-1, ArtworkColors.Nearest(10, 20, 30, new List<StoneColor> { new() { Name = "?", Rgb = "zzz" } }));
    }

    [Fact]
    public void Difference_SameColorIsZero()
    {
        Assert.Equal(0, ArtworkColors.Difference(229, 57, 53, 229, 57, 53), 6);
        Assert.True(ArtworkColors.Difference(255, 255, 255, 0, 0, 0) > 90);
    }

    [Fact]
    public void TenThousandStones_FiftyShapes_Fast()
    {
        var regions = new List<ColorRegion>();
        for (int i = 0; i < 50; i++)
        {
            regions.Add(new ColorRegion(new[] { Square(i % 10 * 20, i / 10 * 20, 18) }, (byte)i, 0, 0, i));
        }

        var centers = new List<Point2D>();
        for (int i = 0; i < 10000; i++)
        {
            centers.Add(new Point2D(i % 100 * 2.0, i / 100 * 1.0));
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        int[] result = ArtworkColors.RegionOf(centers, regions);
        Assert.True(watch.ElapsedMilliseconds < 1000, $"{watch.ElapsedMilliseconds} мс");
        Assert.Contains(result, r => r >= 0);
        Assert.Contains(result, r => r < 0);
    }
}
