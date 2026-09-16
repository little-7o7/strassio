using System.Linq;
using Strassio.Core.Geometry;

namespace Strassio.Core.Tests.Geometry;

public class CurveFlattenerTests
{
    [Fact]
    public void Line_FlattensToItsTwoEndpoints()
    {
        var curve = Curve.FromPolyline(new[] { new Point2D(0, 0), new Point2D(10, 0) });

        FlattenedCurve flat = CurveFlattener.Flatten(curve);

        Assert.Equal(2, flat.Points.Count);
        Assert.Equal(10, flat.TotalLength, 6);
    }

    [Fact]
    public void Polyline_TotalLengthIsSumOfSegments()
    {
        var curve = Curve.FromPolyline(new[]
        {
            new Point2D(0, 0),
            new Point2D(10, 0),
            new Point2D(10, 10),
        });

        FlattenedCurve flat = CurveFlattener.Flatten(curve);

        Assert.Equal(20, flat.TotalLength, 6);
        Assert.Equal(3, flat.Points.Count);
        Assert.True(flat.Points[1].IsSegmentJoint);
    }

    [Fact]
    public void CubicBezier_StaysWithinTolerance()
    {
        // Четверть окружности радиуса 10, приближенная кубическим Безье (стандартная константа k).
        const double r = 10;
        const double k = 0.5522847498;
        var seg = CurveSegment.Cubic(
            new Point2D(r, 0),
            new Point2D(r, r * k),
            new Point2D(r * k, r),
            new Point2D(0, r));
        var curve = new Curve(new[] { seg }, isClosed: false);

        FlattenedCurve flat = CurveFlattener.Flatten(curve, toleranceMm: 0.01);

        Assert.True(flat.Points.Count > 2);

        foreach (FlattenedPoint p in flat.Points)
        {
            double distanceFromCenter = p.Position.Length;
            Assert.InRange(distanceFromCenter, r - 0.05, r + 0.05);
        }
    }

    [Fact]
    public void PointAtDistance_ReturnsMidpointOfLine()
    {
        var curve = Curve.FromPolyline(new[] { new Point2D(0, 0), new Point2D(10, 0) });
        FlattenedCurve flat = CurveFlattener.Flatten(curve);

        Point2D mid = flat.PointAtDistance(5);

        Assert.Equal(5, mid.X, 6);
        Assert.Equal(0, mid.Y, 6);
    }

    [Fact]
    public void ClosedPolyline_DuplicatesFirstPointAtEnd()
    {
        var square = Curve.FromPolyline(
            new[] { new Point2D(0, 0), new Point2D(10, 0), new Point2D(10, 10), new Point2D(0, 10) },
            isClosed: true);

        FlattenedCurve flat = CurveFlattener.Flatten(square);

        Assert.Equal(flat.Points[0].Position, flat.Points[flat.Points.Count - 1].Position);
        Assert.Equal(40, flat.TotalLength, 6);
    }

    [Fact]
    public void ClosedCurve_RepeatedLastNode_IsNotCountedTwice()
    {
        // Замкнутая кривая, у которой последний узел совпадает с первым с точностью до 16-го знака —
        // ровно так CorelDRAW отдаёт замкнутые кривые. Стык должен остаться ОДНОЙ точкой.
        var curve = Curve.FromPolyline(
            new[]
            {
                new Point2D(0, 0),
                new Point2D(20, 0),
                new Point2D(20, 20),
                new Point2D(1e-16, -1e-16),
            },
            isClosed: true);

        FlattenedCurve flat = CurveFlattener.Flatten(curve);

        // Точки: стык, (20,0), (20,20) и точка замыкания — ровно та же, что стык.
        Assert.Equal(4, flat.Points.Count);
        Assert.Equal(flat.Points[0].Position, flat.Points[flat.Points.Count - 1].Position);

        // И сам стык считается острым углом только один раз, а не дважды (в начале и в конце).
        System.Collections.Generic.List<double> corners =
            CornerDetector.FindSharpCornerDistances(flat, thresholdDeg: 20);
        Assert.Single(corners.Where(d => d <= 1e-9 || System.Math.Abs(d - flat.TotalLength) <= 1e-9));
    }

    [Fact]
    public void ClosedCurve_WithDuplicateNodeInTheMiddle_DropsIt()
    {
        var curve = Curve.FromPolyline(
            new[]
            {
                new Point2D(0, 0),
                new Point2D(20, 0),
                new Point2D(20, 0),
                new Point2D(20, 20),
            },
            isClosed: true);

        FlattenedCurve flat = CurveFlattener.Flatten(curve);

        Assert.Equal(4, flat.Points.Count);
        Assert.Equal(20 + 20 + System.Math.Sqrt(20 * 20 + 20 * 20), flat.TotalLength, 6);
    }
}
