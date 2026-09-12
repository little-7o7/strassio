using System;
using System.Collections.Generic;
using Strassio.Core.Geometry;

namespace Strassio.Core.Tests.Geometry;

public class PointInPolygonTests
{
    private static FlattenedCurve Square10mm() =>
        CurveFlattener.Flatten(Curve.FromPolyline(
            new[] { new Point2D(0, 0), new Point2D(10, 0), new Point2D(10, 10), new Point2D(0, 10) },
            isClosed: true));

    [Fact]
    public void PointInsideSquare_IsInside()
    {
        Assert.True(PointInPolygon.IsInside(Square10mm(), new Point2D(5, 5)));
    }

    [Fact]
    public void PointOutsideSquare_IsNotInside()
    {
        Assert.False(PointInPolygon.IsInside(Square10mm(), new Point2D(15, 5)));
        Assert.False(PointInPolygon.IsInside(Square10mm(), new Point2D(-1, 5)));
        Assert.False(PointInPolygon.IsInside(Square10mm(), new Point2D(5, -1)));
    }

    [Fact]
    public void DistanceToBoundary_CenterOfSquare_IsHalfSide()
    {
        double d = PointInPolygon.DistanceToBoundary(Square10mm(), new Point2D(5, 5));
        Assert.Equal(5, d, 6);
    }

    [Fact]
    public void DistanceToBoundary_OnEdge_IsZero()
    {
        double d = PointInPolygon.DistanceToBoundary(Square10mm(), new Point2D(5, 0));
        Assert.Equal(0, d, 6);
    }

    [Fact]
    public void StarShape_ConcavePointBeyondNotch_IsOutside()
    {
        // Пятиконечная звезда (та же формула, что и в Preview/LineScattererTests): точка на луче,
        // который проходит точно через внутреннюю вершину (самую глубокую точку выемки), но дальше
        // от центра, чем эта вершина, — лежит внутри выпуклой оболочки звезды, но уже снаружи
        // самой фигуры (выемка её не захватывает).
        const int spikes = 5;
        const double outerR = 40;
        const double innerR = 15;
        var points = new List<Point2D>();
        for (int i = 0; i < spikes * 2; i++)
        {
            double r = i % 2 == 0 ? outerR : innerR;
            double angle = Math.PI / 2 + i * Math.PI / spikes;
            points.Add(new Point2D(r * Math.Cos(angle), -r * Math.Sin(angle)));
        }

        FlattenedCurve star = CurveFlattener.Flatten(Curve.FromPolyline(points, isClosed: true));

        double notchAngle = Math.PI / 2 + Math.PI / spikes; // угол первой внутренней вершины (i=1)
        var beyondNotch = new Point2D(25 * Math.Cos(notchAngle), -25 * Math.Sin(notchAngle)); // 25 мм > innerR (15)

        Assert.True(PointInPolygon.IsInside(star, new Point2D(0, 0))); // центр звезды — внутри
        Assert.False(PointInPolygon.IsInside(star, beyondNotch));
    }
}
