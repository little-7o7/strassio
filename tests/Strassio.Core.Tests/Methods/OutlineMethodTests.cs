using Strassio.Core.Geometry;
using Strassio.Core.Methods;
using Strassio.Core.Placement;

namespace Strassio.Core.Tests.Methods;

/// <summary>Обводка всего дизайна рядом страз (docs/SPEC.md, раздел 8).</summary>
public class OutlineMethodTests
{
    private const double D = 2.4;

    private static Curve Circle(double cx, double cy, double r)
    {
        var pts = new List<Point2D>();
        for (int i = 0; i < 96; i++)
        {
            double a = 2 * Math.PI * i / 96;
            pts.Add(new Point2D(cx + r * Math.Cos(a), cy + r * Math.Sin(a)));
        }

        return Curve.FromPolyline(pts, true);
    }

    private static IReadOnlyList<PlacedStone> Run(IReadOnlyList<Curve> design, double margin = 1) =>
        MethodRunner.Run(MethodKind.Outline, design, D, new MethodParameters { EdgeMarginMm = margin }).Stones;

    /// <summary>Расстояние от центра стразы до ближайшего края дизайна.</summary>
    private static double DistanceToDesign(IReadOnlyList<Curve> design, Point2D p) =>
        design.Select(c => CurveFlattener.Flatten(c)).Min(f => PointInPolygon.DistanceToBoundary(f, p));

    [Fact]
    public void TwoOverlappingCircles_OneCommonOutline()
    {
        Curve[] design = { Circle(0, 0, 10), Circle(14, 0, 10) };
        IReadOnlyList<PlacedStone> stones = Run(design);

        Assert.NotEmpty(stones);
        Assert.All(stones, s => Assert.InRange(DistanceToDesign(design, s.Center), 1 + D / 2 - 0.15, 1 + D / 2 + 0.15));
        Assert.All(stones, s => Assert.DoesNotContain(design, c => PointInPolygon.IsInside(CurveFlattener.Flatten(c), s.Center)));
        Assert.DoesNotContain(true, IntersectionFixer.FindIndicesToRemove(stones, 0.05, 0.01));
    }

    [Fact]
    public void RingOfShapes_OutlinesOnlyOuterSilhouette()
    {
        // Четыре круга по кругу, между ними в середине — пустота шире обводки.
        var design = new List<Curve>();
        for (int k = 0; k < 6; k++)
        {
            double a = 2 * Math.PI * k / 6;
            design.Add(Circle(20 * Math.Cos(a), 20 * Math.Sin(a), 11));
        }

        IReadOnlyList<PlacedStone> stones = Run(design);
        Assert.NotEmpty(stones);
        Assert.DoesNotContain(stones, s => Point2D.Distance(s.Center, Point2D.Zero) < 15);
    }

    [Fact]
    public void LetterO_OnlyOuterContour()
    {
        Curve[] letter = { Circle(0, 0, 15), Circle(0, 0, 8) };
        IReadOnlyList<PlacedStone> stones = Run(letter);
        Assert.NotEmpty(stones);
        Assert.All(stones, s => Assert.True(Point2D.Distance(s.Center, Point2D.Zero) > 15));
    }
}
