using System;
using System.Collections.Generic;
using System.Linq;
using Strassio.Core.Geometry;

namespace Strassio.Core.Placement
{
    /// <summary>
    /// Методы F1 «сетка» и F2 «соты» (docs/SPEC.md, раздел 5): заливка формы стразами по правильной
    /// сетке с заданным углом поворота. Форма может состоять из нескольких контуров — внешняя граница
    /// и отверстия (буквы «О», «А», кольца) — считается по чётно-нечётному правилу: точка внутри
    /// формы, если она внутри нечётного числа контуров.
    /// </summary>
    public static class GridFiller
    {
        public static List<PlacedStone> Fill(Curve boundary, GridFillOptions options) =>
            Fill(new[] { boundary }, options);

        public static List<PlacedStone> Fill(IReadOnlyList<Curve> boundaries, GridFillOptions options)
        {
            List<FlattenedCurve> flats = boundaries
                .Select(b => CurveFlattener.Flatten(b, options.FlattenToleranceMm))
                .ToList();

            bool IsInsideShape(Point2D p)
            {
                int count = 0;
                foreach (FlattenedCurve f in flats)
                {
                    if (PointInPolygon.IsInside(f, p))
                    {
                        count++;
                    }
                }

                return count % 2 == 1;
            }

            double DistanceToNearestBoundary(Point2D p)
            {
                double min = double.MaxValue;
                foreach (FlattenedCurve f in flats)
                {
                    double d = PointInPolygon.DistanceToBoundary(f, p);
                    if (d < min)
                    {
                        min = d;
                    }
                }

                return min;
            }

            double step = options.StoneDiameterMm + options.GapMm;
            double radius = options.StoneDiameterMm / 2;
            double requiredClearance = radius + options.MarginFromEdgeMm;
            double rowSpacing = options.Pattern == GridPattern.Honeycomb ? step * Math.Sqrt(3) / 2 : step;

            double angleRad = options.AngleDeg * Math.PI / 180;
            double cos = Math.Cos(angleRad);
            double sin = Math.Sin(angleRad);

            Point2D ToWorld(double lx, double ly) => new Point2D(lx * cos - ly * sin, lx * sin + ly * cos);
            Point2D ToLocal(Point2D w) => new Point2D(w.X * cos + w.Y * sin, -w.X * sin + w.Y * cos);

            double minLX = double.MaxValue, maxLX = double.MinValue, minLY = double.MaxValue, maxLY = double.MinValue;
            foreach (FlattenedCurve f in flats)
            {
                foreach (FlattenedPoint p in f.Points)
                {
                    Point2D local = ToLocal(p.Position);
                    if (local.X < minLX) minLX = local.X;
                    if (local.X > maxLX) maxLX = local.X;
                    if (local.Y < minLY) minLY = local.Y;
                    if (local.Y > maxLY) maxLY = local.Y;
                }
            }

            var result = new List<PlacedStone>();
            if (flats.Count == 0 || minLX > maxLX)
            {
                return result;
            }

            minLX -= step;
            maxLX += step;
            minLY -= rowSpacing;
            maxLY += rowSpacing;

            int rowStart = (int)Math.Floor(minLY / rowSpacing);
            int rowEnd = (int)Math.Ceiling(maxLY / rowSpacing);

            for (int row = rowStart; row <= rowEnd; row++)
            {
                double ly = row * rowSpacing;
                double xOffset = options.Pattern == GridPattern.Honeycomb && (row & 1) != 0 ? step / 2 : 0;

                int colStart = (int)Math.Floor((minLX - xOffset) / step);
                int colEnd = (int)Math.Ceiling((maxLX - xOffset) / step);

                for (int col = colStart; col <= colEnd; col++)
                {
                    double lx = col * step + xOffset;
                    Point2D world = ToWorld(lx, ly);

                    if (!IsInsideShape(world))
                    {
                        continue;
                    }

                    if (DistanceToNearestBoundary(world) < requiredClearance)
                    {
                        continue;
                    }

                    // rowId: -1 — сетка не ряд вдоль кривой, а плоская 2D-раскладка; «одиночек»
                    // (IntersectionFixer.RemoveLonelySurvivors) для неё считать не нужно и нельзя —
                    // соседство по индексу в списке ничего не говорит о соседстве на сетке.
                    result.Add(new PlacedStone(world, options.StoneDiameterMm, false, rowId: -1));
                }
            }

            return result;
        }
    }
}
