using Strassio.Core.Geometry;
using Strassio.Core.Placement;
using Strassio.Core.Vector;

namespace Strassio.Core.Tests.Vector;

/// <summary>Векторные инструменты (docs/SPEC.md, раздел 10).</summary>
public class VectorToolsTests
{
    private static Curve Square(double size, bool ccw = true)
    {
        var pts = new List<Point2D> { new(0, 0), new(size, 0), new(size, size), new(0, size) };
        if (!ccw)
        {
            pts.Reverse();
        }

        return Curve.FromPolyline(pts, true);
    }

    private static Curve Line(params (double X, double Y)[] pts) =>
        Curve.FromPolyline(pts.Select(p => new Point2D(p.X, p.Y)).ToList(), false);

    private static Curve Arc() => new(new[]
    {
        CurveSegment.Cubic(new Point2D(0, 0), new Point2D(0, 10), new Point2D(20, 10), new Point2D(20, 0)),
        CurveSegment.Line(new Point2D(20, 0), new Point2D(40, 0)),
    }, false);

    private static double Length(Curve c) => c.Segments.Sum(VectorTools.SegmentLength);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Offset_PositiveIsOutside_WhateverDirection(bool ccw)
    {
        Curve outer = VectorTools.Offset(Square(20, ccw), 3, roundCorners: false);
        (double w, double h) = CurveMetrics.BoundingSize(CurveFlattener.Flatten(outer));
        Assert.Equal(26, w, 1);
        Assert.Equal(26, h, 1);

        Curve inner = VectorTools.Offset(Square(20, ccw), -3, roundCorners: false);
        (w, h) = CurveMetrics.BoundingSize(CurveFlattener.Flatten(inner));
        Assert.Equal(14, w, 1);
    }

    [Fact]
    public void Offset_RoundCorners_AreCloserThanSharp()
    {
        Curve round = VectorTools.Offset(Square(20), 3, roundCorners: true);
        FlattenedCurve f = CurveFlattener.Flatten(round);
        double farthest = f.Points.Max(p => Point2D.Distance(p.Position, new Point2D(10, 10)));
        Assert.True(farthest < Math.Sqrt(2) * 13 - 0.5, "у круглых углов нет «шипа» митра");
    }

    [Fact]
    public void Parallel_BothSides_MakesTwicePerCount()
    {
        List<Curve> lines = VectorTools.Parallel(Line((0, 0), (50, 0)), 3, 2.6, bothSides: true, roundCorners: true);
        Assert.Equal(6, lines.Count);
        double[] ys = lines.Select(c => Math.Round(c.Segments[0].Start.Y, 2)).OrderBy(y => y).ToArray();
        Assert.Equal(new[] { -7.8, -5.2, -2.6, 2.6, 5.2, 7.8 }, ys);
    }

    [Fact]
    public void Parallel_InsideTooFar_SkipsImpossibleLines()
    {
        // Квадрат 10: внутрь на 2, 4 — да; на 6 — уже не бывает.
        List<Curve> lines = VectorTools.Parallel(Square(10), 3, -2, bothSides: false, roundCorners: false);
        Assert.True(lines.Count <= 2);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    public void SplitEqual_PartsHaveEqualLength_AndShapeIsKept(int parts)
    {
        Curve arc = Arc();
        List<Curve> pieces = VectorTools.SplitEqual(arc, parts);
        Assert.Equal(parts, pieces.Count);

        double each = Length(arc) / parts;
        Assert.All(pieces, p => Assert.Equal(each, Length(p), 1));

        // Концы кусков стыкуются и лежат на исходной кривой.
        for (int i = 1; i < pieces.Count; i++)
        {
            Assert.True(Point2D.Distance(pieces[i - 1].Segments[^1].End, pieces[i].Segments[0].Start) < 1e-9);
        }

        FlattenedCurve original = CurveFlattener.Flatten(arc, 0.005);
        foreach (Curve piece in pieces)
        {
            Point2D mid = CurveFlattener.Flatten(piece).PointAtDistance(Length(piece) / 2);
            double dist = Strassio.Core.Editing.StoneEditor.DistanceToPolyline(original, mid);
            Assert.True(dist < 0.05, $"точка куска в {dist:0.000} мм от исходной линии");
        }
    }

    [Fact]
    public void JoinEnds_ConnectsNearEnds_ReversingIfNeeded_AndClosesLoop()
    {
        var a = Line((0, 0), (10, 0));
        var b = Line((10, 10), (10.05, 0.02)); // нарисована «к» концу a
        var c = Line((10, 10), (0, 10.03));
        var d = Line((0, 9.98), (0.02, 0.01));
        List<Curve> joined = VectorTools.JoinEnds(new[] { a, b, c, d }, toleranceMm: 0.1);

        Curve loop = Assert.Single(joined);
        Assert.True(loop.IsClosed);
        Assert.Equal(40, Length(loop), 0);
    }

    [Fact]
    public void JoinEnds_LeavesFarEndsAlone()
    {
        List<Curve> joined = VectorTools.JoinEnds(new[] { Line((0, 0), (10, 0)), Line((12, 0), (20, 0)) }, toleranceMm: 0.5);
        Assert.Equal(2, joined.Count);
        Assert.All(joined, c => Assert.False(c.IsClosed));
    }

    [Fact]
    public void FindGaps_ReportsSmallGapsOnly()
    {
        var curves = new[] { Line((0, 0), (10, 0)), Line((10.4, 0), (20, 0)), Line((40, 0), (50, 0)) };
        List<Point2D> gaps = VectorTools.FindGaps(curves, 0.01, 1.0);
        Point2D gap = Assert.Single(gaps);
        Assert.Equal(10.2, gap.X, 6);
    }

    [Fact]
    public void Reverse_TwiceIsSame()
    {
        Curve arc = Arc();
        Curve back = VectorTools.ReverseDirection(arc);
        Assert.Equal(arc.Segments[^1].End, back.Segments[0].Start);
        Assert.True(VectorTools.AreSame(arc, VectorTools.ReverseDirection(back)));
        Assert.True(VectorTools.AreSame(arc, back)); // дубль и в обратную сторону — всё равно дубль
    }

    [Fact]
    public void SimplifyAndSmooth_RemovesJitter()
    {
        var rnd = new Random(3);
        var pts = Enumerable.Range(0, 200).Select(i => new Point2D(i * 0.25, Math.Sin(i * 0.05) * 5 + (rnd.NextDouble() - 0.5) * 0.08)).ToList();
        Curve shaky = Curve.FromPolyline(pts, false);
        Curve smooth = VectorTools.SimplifyAndSmooth(shaky, 0.1, smooth: true);

        Assert.True(smooth.Segments.Count < 40, $"{smooth.Segments.Count} сегментов");
        Assert.All(smooth.Segments, s => Assert.False(s.IsLine));
        Assert.Equal(pts[0], smooth.Segments[0].Start);
        Assert.Equal(pts[^1], smooth.Segments[^1].End);
    }

    [Fact]
    public void SelfIntersections_FigureEight()
    {
        var eight = Curve.FromPolyline(new List<Point2D> { new(0, 0), new(10, 10), new(10, 0), new(0, 10) }, true);
        Point2D x = Assert.Single(VectorTools.SelfIntersections(eight));
        Assert.Equal(5, x.X, 6);
        Assert.Equal(5, x.Y, 6);
        Assert.Empty(VectorTools.SelfIntersections(Square(10)));
    }

    [Fact]
    public void MicroSegments_Found()
    {
        var line = Line((0, 0), (10, 0), (10.01, 0), (20, 0));
        Assert.Single(VectorTools.MicroSegments(line, 0.05));
    }

    [Fact]
    public void Centerline_OfStrip_IsOneLineAlongMiddle()
    {
        var strip = Curve.FromPolyline(new List<Point2D> { new(0, 0), new(60, 0), new(60, 8), new(0, 8) }, true);
        List<Curve> lines = Centerline.Build(new[] { strip });

        Curve line = Assert.Single(lines);
        FlattenedCurve f = CurveFlattener.Flatten(line);
        Assert.All(f.Points.Where(p => p.Position.X > 6 && p.Position.X < 54), p => Assert.InRange(p.Position.Y, 3.5, 4.5));
        Assert.True(f.TotalLength > 45, $"длина {f.TotalLength:0.0}");
    }

    [Fact]
    public void Centerline_OfRing_IsClosedLoop()
    {
        Curve Circle(double r)
        {
            var pts = new List<Point2D>();
            for (int i = 0; i < 120; i++)
            {
                double a = 2 * Math.PI * i / 120;
                pts.Add(new Point2D(r * Math.Cos(a), r * Math.Sin(a)));
            }

            return Curve.FromPolyline(pts, true);
        }

        List<Curve> lines = Centerline.Build(new[] { Circle(20), Circle(14) });
        Curve loop = Assert.Single(lines, l => CurveFlattener.Flatten(l).TotalLength > 50);
        Assert.True(loop.IsClosed);
        Assert.All(CurveFlattener.Flatten(loop).Points, p => Assert.InRange(Point2D.Distance(p.Position, Point2D.Zero), 16, 18));
    }

    [Fact]
    public void Centerline_OfLetterL_CoversBothArms()
    {
        var letterL = Curve.FromPolyline(new List<Point2D> { new(0, 0), new(40, 0), new(40, 8), new(8, 8), new(8, 40), new(0, 40) }, true);
        List<Curve> lines = Centerline.Build(new[] { letterL });
        List<Point2D> all = lines.SelectMany(l => CurveFlattener.Flatten(l).Points.Select(p => p.Position)).ToList();

        Assert.Contains(all, p => p.X > 30 && Math.Abs(p.Y - 4) < 1);  // нижняя перекладина
        Assert.Contains(all, p => p.Y > 30 && Math.Abs(p.X - 4) < 1);  // вертикаль
        Assert.True(lines.Count <= 2, $"{lines.Count} линий");
    }
}
