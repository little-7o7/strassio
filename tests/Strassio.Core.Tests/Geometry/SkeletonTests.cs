using System;
using System.Collections.Generic;
using Strassio.Core.Geometry;

namespace Strassio.Core.Tests.Geometry;

/// <summary>
/// Срединная линия фигуры — основа для рядов, которые идут вдоль формы и встречаются посередине
/// (сравнение автора: ручная работа против нашей заливки).
/// </summary>
public class SkeletonTests
{
    private static SignedDistanceField Field(Curve shape, double cellMm = 0.3) =>
        SignedDistanceField.Build(new[] { CurveFlattener.Flatten(shape) }, cellMm);

    private static Curve Rectangle(double width, double height) => Curve.FromPolyline(
        new[] { new Point2D(0, 0), new Point2D(width, 0), new Point2D(width, height), new Point2D(0, height) },
        isClosed: true);

    [Fact]
    public void LongRectangle_SkeletonRunsAlongTheMiddle()
    {
        // Полоса 40 × 10 мм: середина — линия y = 5.
        SignedDistanceField field = Field(Rectangle(40, 10));
        Skeleton skeleton = Skeleton.Build(field);

        int cells = 0;
        double worstOffset = 0;

        for (int iy = 0; iy < skeleton.Height; iy++)
        {
            for (int ix = 0; ix < skeleton.Width; ix++)
            {
                if (!skeleton.Cells[iy * skeleton.Width + ix])
                {
                    continue;
                }

                Point2D p = field.PositionOf(ix, iy);

                // Скелет прямоугольника — линия по середине плюс короткие «усы» к углам;
                // усы живут у самых торцов, поэтому смотрим только среднюю часть полосы.
                if (p.X < 8 || p.X > 32)
                {
                    continue;
                }

                cells++;
                worstOffset = Math.Max(worstOffset, Math.Abs(p.Y - 5));
            }
        }

        Assert.True(cells > 50, $"срединная линия слишком короткая: {cells} клеток");
        Assert.True(worstOffset < 0.7, $"срединная линия ушла от середины на {worstOffset:0.00} мм");
    }

    [Fact]
    public void DistanceToSkeleton_AtTheEdge_IsHalfOfTheWidth()
    {
        SignedDistanceField field = Field(Rectangle(40, 10));
        Skeleton skeleton = Skeleton.Build(field);

        // Точка у самого края полосы: до середины 5 мм, значит и до скелета столько же.
        int ix = (int)Math.Round((20 - field.Origin.X) / field.CellSizeMm);
        int iyEdge = (int)Math.Round((0.3 - field.Origin.Y) / field.CellSizeMm);
        int iyMiddle = (int)Math.Round((5 - field.Origin.Y) / field.CellSizeMm);

        Assert.InRange(skeleton.DistanceAt(ix, iyEdge), 4.0, 5.5);
        Assert.InRange(skeleton.DistanceAt(ix, iyMiddle), 0, 0.5);
    }

    [Fact]
    public void Circle_SkeletonShrinksToTheCentre()
    {
        var points = new List<Point2D>();
        for (int i = 0; i < 180; i++)
        {
            double a = i * 2 * Math.PI / 180;
            points.Add(new Point2D(20 + 12 * Math.Cos(a), 20 + 12 * Math.Sin(a)));
        }

        SignedDistanceField field = Field(Curve.FromPolyline(points, isClosed: true));
        Skeleton skeleton = Skeleton.Build(field);

        // У круга середина — точка: все клетки скелета должны лежать рядом с центром.
        var centre = new Point2D(20, 20);
        for (int iy = 0; iy < skeleton.Height; iy++)
        {
            for (int ix = 0; ix < skeleton.Width; ix++)
            {
                if (skeleton.Cells[iy * skeleton.Width + ix])
                {
                    double away = Point2D.Distance(field.PositionOf(ix, iy), centre);
                    Assert.True(away < 3.5, $"клетка скелета круга далеко от центра: {away:0.0} мм");
                }
            }
        }
    }
}
