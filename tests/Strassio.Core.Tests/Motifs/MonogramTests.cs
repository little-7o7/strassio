using Strassio.Core.Geometry;
using Strassio.Core.Motifs;

namespace Strassio.Core.Tests.Motifs;

/// <summary>Монограмма: 1–3 буквы в ряд и рамка ровно на заданном расстоянии от букв.</summary>
public class MonogramTests
{
    private static Curve Rect(double x, double y, double w, double h) => Curve.FromPolyline(
        new[] { new Point2D(x, y), new Point2D(x + w, y), new Point2D(x + w, y + h), new Point2D(x, y + h) }, true);

    private static Curve Ring(double cx, double cy, double r)
    {
        var pts = Enumerable.Range(0, 72).Select(i => new Point2D(cx + r * Math.Cos(i * Math.PI / 36), cy + r * Math.Sin(i * Math.PI / 36))).ToList();
        return Curve.FromPolyline(pts, true);
    }

    /// <summary>«Буквы» как из шрифта: в своих координатах, с разной шириной; «О» — кольцо с дыркой.</summary>
    private static IReadOnlyList<IReadOnlyList<Curve>> Letters(int count) => new IReadOnlyList<Curve>[]
    {
        new[] { Rect(100, 50, 6, 30) },               // I
        new[] { Ring(15, 15, 15), Ring(15, 15, 10) }, // O
        new[] { Rect(-40, -10, 22, 30), Rect(-35, -5, 12, 20) }, // квадрат с дыркой
    }.Take(count).ToList();

    private static (double MinX, double MinY, double MaxX, double MaxY) Box(IEnumerable<Curve> curves)
    {
        List<Point2D> pts = curves.SelectMany(c => CurveFlattener.Flatten(c).Points.Select(p => p.Position)).ToList();
        return (pts.Min(p => p.X), pts.Min(p => p.Y), pts.Max(p => p.X), pts.Max(p => p.Y));
    }

    [Fact]
    public void ThreeLetters_SitInARow_MiddleIsLarger_AllCentred()
    {
        var options = new MonogramOptions { Frame = MonogramFrame.None, LetterHeightMm = 40, LetterGapMm = 3, MiddleScale = 1.4 };
        var center = new Point2D(200, -50);

        MonogramLayout m = Monogram.Arrange(Letters(3), options, center);

        Assert.Null(m.Frame);
        var boxes = m.Letters.Select(Box).ToList();
        // Буквы в исходнике разной высоты (30, 30, 30) — все крайние высотой 40, средняя 56.
        Assert.Equal(40, boxes[0].MaxY - boxes[0].MinY, 1);
        Assert.Equal(56, boxes[1].MaxY - boxes[1].MinY, 1);
        Assert.Equal(40, boxes[2].MaxY - boxes[2].MinY, 1);
        foreach (var b in boxes)
        {
            Assert.Equal(center.Y, (b.MinY + b.MaxY) / 2, 1);
        }

        // Слева направо, между габаритами ровно LetterGapMm.
        Assert.Equal(3, boxes[1].MinX - boxes[0].MaxX, 1);
        Assert.Equal(3, boxes[2].MinX - boxes[1].MaxX, 1);
        Assert.Equal(center.X, (boxes[0].MinX + boxes[2].MaxX) / 2, 1);
        Assert.Equal(boxes[2].MaxX - boxes[0].MinX, m.WidthMm, 1);
        // Дырки остались дырками: у «О» по-прежнему два контура.
        Assert.Equal(2, m.Letters[1].Count);
    }

    [Theory]
    [InlineData(MonogramFrame.Circle, 1)]
    [InlineData(MonogramFrame.Circle, 3)]
    [InlineData(MonogramFrame.Oval, 2)]
    [InlineData(MonogramFrame.Oval, 1)]
    [InlineData(MonogramFrame.Diamond, 3)]
    [InlineData(MonogramFrame.Rounded, 3)]
    [InlineData(MonogramFrame.Shield, 2)]
    [InlineData(MonogramFrame.Shield, 3)]
    public void Frame_HoldsLetters_ExactlyAtTheGap(MonogramFrame frame, int letters)
    {
        var options = new MonogramOptions { Frame = frame, LetterHeightMm = 30, FrameGapMm = 5 };

        MonogramLayout m = Monogram.Arrange(Letters(letters), options, new Point2D(10, 20));

        Assert.NotNull(m.Frame);
        Assert.True(m.Frame!.IsClosed);
        FlattenedCurve f = CurveFlattener.Flatten(m.Frame);
        List<Point2D> pts = m.Letters.SelectMany(l => l).SelectMany(c => CurveFlattener.Flatten(c).Points.Select(p => p.Position)).ToList();
        Assert.All(pts, p => Assert.True(PointInPolygon.IsInside(f, p), $"{p} снаружи рамки"));
        double closest = pts.Min(p => PointInPolygon.DistanceToBoundary(f, p));
        // Не ближе заданного и не с лишним запасом: рамка обтягивает буквы.
        Assert.InRange(closest, 4.98, 5.1);
    }

    [Fact]
    public void Circle_IsRound_AndOval_IsReallyOval()
    {
        MonogramLayout circle = Monogram.Arrange(Letters(3), new MonogramOptions { Frame = MonogramFrame.Circle }, Point2D.Zero);
        MonogramLayout oval = Monogram.Arrange(Letters(1), new MonogramOptions { Frame = MonogramFrame.Oval }, Point2D.Zero);

        Assert.Equal(circle.WidthMm, circle.HeightMm, 1);
        // Одна узкая буква «I» — овал стоит вертикально и заметно вытянут.
        Assert.True(oval.HeightMm >= oval.WidthMm * 1.24, $"{oval.WidthMm} × {oval.HeightMm}");
    }

    [Fact]
    public void NoLetters_IsAnError()
    {
        Assert.Throws<ArgumentException>(() => Monogram.Arrange(new List<IReadOnlyList<Curve>> { new List<Curve>() }, new MonogramOptions(), Point2D.Zero));
    }
}
