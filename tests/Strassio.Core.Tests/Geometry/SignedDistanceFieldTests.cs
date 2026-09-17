using System;
using System.Collections.Generic;
using System.Linq;
using Strassio.Core.Geometry;

namespace Strassio.Core.Tests.Geometry;

public class SignedDistanceFieldTests
{
    private static FlattenedCurve Circle(Point2D center, double radius, int points = 360)
    {
        var polyline = new List<Point2D>();
        for (int i = 0; i <= points; i++)
        {
            double t = 2 * Math.PI * i / points;
            polyline.Add(new Point2D(center.X + radius * Math.Cos(t), center.Y + radius * Math.Sin(t)));
        }

        return CurveFlattener.Flatten(Curve.FromPolyline(polyline, isClosed: true));
    }

    private static FlattenedCurve Square(double side) => CurveFlattener.Flatten(
        Curve.FromPolyline(
            new[] { new Point2D(0, 0), new Point2D(side, 0), new Point2D(side, side), new Point2D(0, side) },
            isClosed: true));

    [Fact]
    public void InsideIsPositive_OutsideIsNegative()
    {
        SignedDistanceField field = SignedDistanceField.Build(new[] { Square(40) }, 0.25);

        Assert.True(field.ValueAt(new Point2D(20, 20)) > 0);
        Assert.True(field.ValueAt(new Point2D(1, 20)) > 0);
        Assert.True(field.ValueAt(new Point2D(-3, 20)) < 0);
        Assert.True(field.ValueAt(new Point2D(20, 45)) < 0);
    }

    [Fact]
    public void DistanceInsideSquare_MatchesDistanceToNearestSide()
    {
        SignedDistanceField field = SignedDistanceField.Build(new[] { Square(40) }, 0.2);

        // В середине квадрата 40×40 до ближайшей стороны ровно 20 мм.
        Assert.Equal(20, field.ValueAt(new Point2D(20, 20)), 1);

        // В 3 мм от левой стороны — 3 мм.
        Assert.Equal(3, field.ValueAt(new Point2D(3, 20)), 1);

        // Снаружи напротив угла ближайшая точка — сама вершина: (−0,3; −0,4) → 0,5 мм, со знаком минус.
        Assert.Equal(-0.5, field.ValueAt(new Point2D(-0.3, -0.4)), 1);
    }

    [Fact]
    public void Hole_CountsAsOutside()
    {
        // Кольцо: большой квадрат с квадратной дыркой внутри (буква «О», чётно-нечётное правило).
        FlattenedCurve outer = Square(40);
        FlattenedCurve hole = CurveFlattener.Flatten(
            Curve.FromPolyline(
                new[] { new Point2D(15, 15), new Point2D(25, 15), new Point2D(25, 25), new Point2D(15, 25) },
                isClosed: true));

        SignedDistanceField field = SignedDistanceField.Build(new[] { outer, hole }, 0.2);

        Assert.True(field.ValueAt(new Point2D(20, 20)) < 0); // середина дырки — снаружи формы
        Assert.True(field.ValueAt(new Point2D(5, 20)) > 0);  // стенка кольца — внутри формы
    }

    [Fact]
    public void IsoContourOfCircle_IsSmallerCircle()
    {
        var center = new Point2D(30, 30);
        SignedDistanceField field = SignedDistanceField.Build(new[] { Circle(center, 20) }, 0.2);

        List<List<Point2D>> loops = IsoContour.Trace(field, 6);

        Assert.Single(loops);
        foreach (Point2D p in loops[0])
        {
            Assert.Equal(14, Point2D.Distance(p, center), 1); // 20 − 6
        }
    }

    [Fact]
    public void IsoContour_DeepInsideShape_IsEmpty()
    {
        SignedDistanceField field = SignedDistanceField.Build(new[] { Square(40) }, 0.25);

        Assert.NotEmpty(IsoContour.Trace(field, 19));  // ещё есть маленький квадратик
        Assert.Empty(IsoContour.Trace(field, 21));     // глубже середины квадрата уже ничего нет
    }

    [Fact]
    public void IsoContour_SplitsIntoSeveralLoops_WhenShapeSplits()
    {
        // Гантель: два квадрата 20×20, соединённых тонкой перемычкой шириной 2 мм. Стоит уйти
        // от края глубже 1 мм — перемычка исчезает, и одно кольцо распадается на два.
        // Ровно это и не умело смещение контура: оно выворачивалось наизнанку вместо распада.
        var dumbbell = Curve.FromPolyline(
            new[]
            {
                new Point2D(0, 0), new Point2D(20, 0), new Point2D(20, 9), new Point2D(40, 9),
                new Point2D(40, 0), new Point2D(60, 0), new Point2D(60, 20), new Point2D(40, 20),
                new Point2D(40, 11), new Point2D(20, 11), new Point2D(20, 20), new Point2D(0, 20),
            },
            isClosed: true);

        SignedDistanceField field = SignedDistanceField.Build(
            new[] { CurveFlattener.Flatten(dumbbell) }, 0.2);

        Assert.Single(IsoContour.Trace(field, 0.5));   // перемычка ещё держит форму единой
        Assert.Equal(2, IsoContour.Trace(field, 3).Count); // глубже — две отдельные головы гантели
    }

    [Fact]
    public void BigShape_BuildsQuickly()
    {
        // Сетка ограничена сверху, поэтому даже дизайн размером с лист A1 считается быстро.
        var watch = System.Diagnostics.Stopwatch.StartNew();
        SignedDistanceField field = SignedDistanceField.Build(new[] { Square(600) }, 0.3);
        watch.Stop();

        Assert.Equal(300, field.ValueAt(new Point2D(300, 300)), 0);
        Assert.True(watch.ElapsedMilliseconds < 5000, $"Слишком долго: {watch.ElapsedMilliseconds} мс");
    }
}
