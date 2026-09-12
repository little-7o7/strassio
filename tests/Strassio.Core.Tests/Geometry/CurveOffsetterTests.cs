using System;
using System.Collections.Generic;
using System.Linq;
using Strassio.Core.Geometry;

namespace Strassio.Core.Tests.Geometry;

public class CurveOffsetterTests
{
    [Fact]
    public void StraightLine_OffsetLeft_ShiftsPerpendicular()
    {
        var curve = Curve.FromPolyline(new[] { new Point2D(0, 0), new Point2D(10, 0) });
        FlattenedCurve flat = CurveFlattener.Flatten(curve);

        List<Point2D> offset = CurveOffsetter.Offset(flat, distance: 2);

        Assert.Equal(2, offset.Count);
        Assert.Equal(0, offset[0].X, 6);
        Assert.Equal(2, offset[0].Y, 6); // левая нормаль движения вправо (+X) — это +Y
        Assert.Equal(10, offset[1].X, 6);
        Assert.Equal(2, offset[1].Y, 6);
    }

    [Fact]
    public void SquareCorner_OuterSide_GetsRoundedWithPointsAtOffsetRadius()
    {
        // Квадрат обходится по часовой стрелке (вниз, значит смещение вправо от хода = наружу вниз-вправо
        // уходит от центра квадрата). Проверяем, что смещение НАРУЖУ (в сторону, где отрезки расходятся)
        // даёт дугу: несколько точек на одинаковом расстоянии radius от исходной вершины.
        var square = Curve.FromPolyline(
            new[] { new Point2D(0, 0), new Point2D(10, 0), new Point2D(10, 10), new Point2D(0, 10) },
            isClosed: true);
        FlattenedCurve flat = CurveFlattener.Flatten(square);

        const double distance = 3;
        List<Point2D> offsetA = CurveOffsetter.Offset(flat, distance);
        List<Point2D> offsetB = CurveOffsetter.Offset(flat, -distance);

        double areaA = Math.Abs(PolygonArea(offsetA));
        double areaB = Math.Abs(PolygonArea(offsetB));

        // Одна сторона смещения расширяет квадрат (площадь заметно больше 100), другая — сужает
        // (заметно меньше 100). Само направление зависит от порядка обхода контура, поэтому здесь
        // не гадаем какое именно — просто проверяем, что стороны действительно разные.
        Assert.True(Math.Abs(areaA - areaB) > 50, $"Площади смещений должны заметно различаться: {areaA} vs {areaB}");

        // На стороне, где контур расширяется, у каждой исходной вершины должна появиться дуга —
        // то есть хотя бы одна дополнительная точка рядом со срезом угла на правильном расстоянии.
        List<Point2D> biggerOffset = areaA > areaB ? offsetA : offsetB;
        Point2D corner = new Point2D(10, 0);
        var nearCorner = biggerOffset.Where(p => Point2D.Distance(p, corner) < distance * 1.5).ToList();
        Assert.True(nearCorner.Count >= 2, "У внешнего угла должно быть больше одной точки (дуга).");
        foreach (Point2D p in nearCorner)
        {
            Assert.Equal(distance, Point2D.Distance(p, corner), 2);
        }
    }

    [Fact]
    public void SquareCorner_InnerSide_TrimsToSinglePoint_NoLoop()
    {
        var square = Curve.FromPolyline(
            new[] { new Point2D(0, 0), new Point2D(10, 0), new Point2D(10, 10), new Point2D(0, 10) },
            isClosed: true);
        FlattenedCurve flat = CurveFlattener.Flatten(square);

        const double distance = 3;
        List<Point2D> outward = CurveOffsetter.Offset(flat, distance);
        List<Point2D> inward = CurveOffsetter.Offset(flat, -distance);
        List<Point2D> smallerOffset = Math.Abs(PolygonArea(outward)) < Math.Abs(PolygonArea(inward)) ? outward : inward;

        // Внутренний (обрезанный) угол — ровно одна точка на пересечении биссектрисы, а не две
        // близко расположенные точки, которые образовали бы петлю/самопересечение.
        Point2D corner = new Point2D(10, 0);
        var nearCorner = smallerOffset.Where(p => Point2D.Distance(p, corner) < distance * 1.5).ToList();
        Assert.Single(nearCorner);
    }

    private static double PolygonArea(IReadOnlyList<Point2D> pts)
    {
        double sum = 0;
        for (int i = 0; i < pts.Count - 1; i++)
        {
            sum += pts[i].X * pts[i + 1].Y - pts[i + 1].X * pts[i].Y;
        }

        return sum / 2;
    }
}
