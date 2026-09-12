using Strassio.Core.Geometry;

namespace Strassio.Core.Tests.Geometry;

public class CornerDetectorTests
{
    [Fact]
    public void StraightLine_HasNoCorners()
    {
        var curve = Curve.FromPolyline(new[] { new Point2D(0, 0), new Point2D(5, 0), new Point2D(10, 0) });
        FlattenedCurve flat = CurveFlattener.Flatten(curve);

        var corners = CornerDetector.FindSharpCornerDistances(flat, thresholdDeg: 20);

        Assert.Empty(corners);
    }

    [Fact]
    public void RightAngle_IsDetectedAsCorner()
    {
        // Зигзаг: прямой угол (90°) посередине — заведомо острее порога 20° из SPEC 6.5.
        var curve = Curve.FromPolyline(new[] { new Point2D(0, 0), new Point2D(10, 0), new Point2D(10, 10) });
        FlattenedCurve flat = CurveFlattener.Flatten(curve);

        var corners = CornerDetector.FindSharpCornerDistances(flat, thresholdDeg: 20);

        Assert.Single(corners);
        Assert.Equal(10, corners[0], 6);
    }

    [Fact]
    public void SlightBend_BelowThreshold_IsNotACorner()
    {
        // Точки почти на одной прямой — угол поворота около 5°, порог 20° не должен сработать.
        var curve = Curve.FromPolyline(new[] { new Point2D(0, 0), new Point2D(10, 0), new Point2D(20, 0.9) });
        FlattenedCurve flat = CurveFlattener.Flatten(curve);

        var corners = CornerDetector.FindSharpCornerDistances(flat, thresholdDeg: 20);

        Assert.Empty(corners);
    }

    [Fact]
    public void ClosedSquare_AllFourCornersDetected_SeamCountedOnce()
    {
        var square = Curve.FromPolyline(
            new[] { new Point2D(0, 0), new Point2D(10, 0), new Point2D(10, 10), new Point2D(0, 10) },
            isClosed: true);
        FlattenedCurve flat = CurveFlattener.Flatten(square);

        var corners = CornerDetector.FindSharpCornerDistances(flat, thresholdDeg: 20);

        Assert.Equal(4, corners.Count);
        Assert.Contains(corners, d => d == 0);
    }
}
