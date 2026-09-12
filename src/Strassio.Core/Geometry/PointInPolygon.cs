using System;
using System.Collections.Generic;

namespace Strassio.Core.Geometry
{
    /// <summary>Проверки «точка относительно замкнутого контура» — нужны заливке формы (docs/SPEC.md, раздел 5).</summary>
    public static class PointInPolygon
    {
        /// <summary>Точка внутри замкнутой (разбитой в полилинию) кривой — луч вправо, чётность пересечений.</summary>
        public static bool IsInside(FlattenedCurve polygon, Point2D point)
        {
            IReadOnlyList<FlattenedPoint> pts = polygon.Points;
            bool inside = false;

            for (int i = 0; i < pts.Count - 1; i++)
            {
                Point2D a = pts[i].Position;
                Point2D b = pts[i + 1].Position;

                bool crosses = (a.Y > point.Y) != (b.Y > point.Y);
                if (!crosses)
                {
                    continue;
                }

                double xAtY = a.X + (point.Y - a.Y) / (b.Y - a.Y) * (b.X - a.X);
                if (point.X < xAtY)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        /// <summary>Кратчайшее расстояние от точки до самого контура (до ближайшего отрезка полилинии).</summary>
        public static double DistanceToBoundary(FlattenedCurve polygon, Point2D point)
        {
            IReadOnlyList<FlattenedPoint> pts = polygon.Points;
            double min = double.MaxValue;

            for (int i = 0; i < pts.Count - 1; i++)
            {
                double d = DistanceToSegment(point, pts[i].Position, pts[i + 1].Position);
                if (d < min)
                {
                    min = d;
                }
            }

            return min;
        }

        private static double DistanceToSegment(Point2D p, Point2D a, Point2D b)
        {
            Point2D ab = b - a;
            double lenSq = ab.Dot(ab);
            if (lenSq < 1e-12)
            {
                return Point2D.Distance(p, a);
            }

            double t = Math.Max(0, Math.Min(1, (p - a).Dot(ab) / lenSq));
            Point2D projection = a + ab * t;
            return Point2D.Distance(p, projection);
        }
    }
}
