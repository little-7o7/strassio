using Strassio.Core.Geometry;
using Strassio.Core.Methods;
using Strassio.Core.Vector;

namespace Strassio.Core.Tests.Vector;

/// <summary>Надпись стразами: деление на буквы, тонкие буквы, тесный интервал (docs/SPEC.md, раздел 10.1).</summary>
public class TextCheckTests
{
    private static Curve Rect(double x, double y, double w, double h) => Curve.FromPolyline(
        new List<Point2D> { new(x, y), new(x + w, y), new(x + w, y + h), new(x, y + h) }, true);

    private static Curve Ellipse(double cx, double cy, double rx, double ry)
    {
        var pts = new List<Point2D>();
        for (int i = 0; i < 96; i++)
        {
            double a = 2 * Math.PI * i / 96;
            pts.Add(new Point2D(cx + rx * Math.Cos(a), cy + ry * Math.Sin(a)));
        }

        return Curve.FromPolyline(pts, true);
    }

    [Fact]
    public void Letters_KeepsHolesWithTheirLetter()
    {
        // «IO»: палочка и буква О с дыркой.
        var contours = new[] { Rect(0, 0, 4, 30), Ellipse(20, 15, 10, 15), Ellipse(20, 15, 6, 11) };
        List<List<Curve>> letters = TextCheck.Letters(contours);

        Assert.Equal(2, letters.Count);
        Assert.Contains(letters, l => l.Count == 2);
        Assert.Contains(letters, l => l.Count == 1);
    }

    [Fact]
    public void ThinLetters_NeedBiggerText_ForOutline_ButFitFill()
    {
        // Палочки толщиной 3 мм — как буквы «III».
        var contours = new[] { Rect(0, 0, 3, 30), Rect(10, 0, 3, 30), Rect(20, 0, 3, 30) };

        TextReport outline = TextCheck.Analyze(contours, TextMode.Outline, 2.8, 2.0, 0.2, 0);
        Assert.Equal(3, outline.LetterCount);
        Assert.InRange(outline.StrokeMm, 2.7, 3.1);
        Assert.True(outline.IsThin);
        Assert.InRange(outline.Scale, 1.8, 2.1);  // нужно 5,8 мм, есть 3

        TextReport fill = TextCheck.Analyze(contours, TextMode.Fill, 2.8, 2.0, 0.2, 0);
        Assert.False(fill.IsThin);
        Assert.Empty(fill.ThinSpots);

        TextReport middle = TextCheck.Analyze(contours, TextMode.Middle, 3.9, 2.0, 0.2, 0);
        Assert.False(middle.IsThin);
    }

    [Fact]
    public void ThinNeck_IsMarked()
    {
        // Перекладина толщиной 5 мм с перетяжкой 1,5 мм посередине.
        var shape = Curve.FromPolyline(new List<Point2D>
        {
            new(0, 0), new(18, 0), new(18, 1.75), new(24, 1.75), new(24, 0), new(42, 0),
            new(42, 5), new(24, 5), new(24, 3.25), new(18, 3.25), new(18, 5), new(0, 5),
        }, true);
        TextReport report = TextCheck.Analyze(new[] { shape }, TextMode.Fill, 2.8, 2.0, 0.2, 0);

        Point2D spot = Assert.Single(report.ThinSpots);
        Assert.InRange(spot.X, 17, 25);
        Assert.False(report.IsThin);  // в основном буква толстая — увеличивать не надо
    }

    [Fact]
    public void LettersTooClose_AskForMoreSpacing()
    {
        var contours = new[] { Rect(0, 0, 6, 30), Rect(6.5, 0, 6, 30), Rect(30, 0, 6, 30) };
        TextReport report = TextCheck.Analyze(contours, TextMode.Fill, 2.8, 2.0, 0.2, 0);

        Assert.True(report.IsTight);
        Assert.InRange(report.ClosestMm, 0.4, 0.6);
        Assert.InRange(report.ExtraSpacingMm, 2.2, 2.4);
        Point2D spot = Assert.Single(report.CloseSpots);
        Assert.InRange(spot.X, 6, 6.5);
        Assert.Equal(0, report.Overlaps);
    }

    [Fact]
    public void WellSpacedLetters_AreFine()
    {
        var contours = new[] { Rect(0, 0, 6, 30), Rect(10, 0, 6, 30) };
        TextReport report = TextCheck.Analyze(contours, TextMode.Fill, 2.8, 2.0, 0.2, 0);

        Assert.False(report.IsTight);
        Assert.False(report.IsThin);
        Assert.Empty(report.CloseSpots);
    }

    [Fact]
    public void OverlappingLetters_AreReported()
    {
        // Рукописный шрифт: соседние буквы заходят друг на друга.
        var contours = new[] { Rect(0, 0, 8, 30), Rect(6, 10, 8, 4) };
        TextReport report = TextCheck.Analyze(contours, TextMode.Fill, 2.8, 2.0, 0.2, 0);

        Assert.Equal(1, report.Overlaps);
        Assert.Equal(0, report.ClosestMm);
        Assert.True(report.IsTight);
    }

    [Fact]
    public void Modes_UseTheirMethods()
    {
        Assert.Equal(MethodKind.F5, TextCheck.MethodFor(TextMode.Outline));
        Assert.Equal(MethodKind.F13, TextCheck.MethodFor(TextMode.Middle));
        Assert.Equal(MethodKind.F3, TextCheck.MethodFor(TextMode.Fill));

        var user = new MethodParameters { Rings = 4, GapMm = 0.4 };
        MethodParameters outline = TextCheck.ParametersFor(TextMode.Outline, user);
        Assert.Equal(1, outline.Rings);
        Assert.Equal(0.4, outline.GapMm);
        Assert.Equal(4, user.Rings);
    }
}
