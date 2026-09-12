using System;
using System.Collections.Generic;

namespace Strassio.Core.Geometry
{
    /// <summary>Одна точка разбитой в полилинию кривой.</summary>
    public readonly struct FlattenedPoint
    {
        public Point2D Position { get; }

        /// <summary>
        /// true, если точка — граница исходного сегмента (место возможного острого угла),
        /// а не просто внутренняя точка, добавленная при разбивке кривой Безье.
        /// </summary>
        public bool IsSegmentJoint { get; }

        public FlattenedPoint(Point2D position, bool isSegmentJoint)
        {
            Position = position;
            IsSegmentJoint = isSegmentJoint;
        }
    }

    /// <summary>
    /// Кривая, разбитая в полилинию, плюс таблица длины дуги для перевода
    /// «расстояние от начала» в точку и обратно.
    /// </summary>
    public sealed class FlattenedCurve
    {
        public IReadOnlyList<FlattenedPoint> Points { get; }
        public bool IsClosed { get; }

        /// <summary>Накопленная длина от начала кривой до каждой точки Points (тот же индекс).</summary>
        public IReadOnlyList<double> ArcLengths { get; }

        public double TotalLength => ArcLengths[ArcLengths.Count - 1];

        public FlattenedCurve(IReadOnlyList<FlattenedPoint> points, bool isClosed)
        {
            if (points == null || points.Count < 2)
            {
                throw new ArgumentException("Нужно минимум 2 точки.", nameof(points));
            }

            Points = points;
            IsClosed = isClosed;

            var arcLengths = new double[points.Count];
            arcLengths[0] = 0;
            for (int i = 1; i < points.Count; i++)
            {
                arcLengths[i] = arcLengths[i - 1] + Point2D.Distance(points[i - 1].Position, points[i].Position);
            }

            ArcLengths = arcLengths;
        }

        /// <summary>Точка на кривой на расстоянии distance от начала (зажимается в [0, TotalLength]).</summary>
        public Point2D PointAtDistance(double distance)
        {
            distance = Clamp(distance, 0, TotalLength);

            int i = FindSegmentIndex(distance);
            double segStart = ArcLengths[i];
            double segLen = ArcLengths[i + 1] - segStart;
            double t = segLen < 1e-12 ? 0 : (distance - segStart) / segLen;
            return Point2D.Lerp(Points[i].Position, Points[i + 1].Position, t);
        }

        /// <summary>Направление движения вдоль кривой в точке на расстоянии distance (единичный вектор).</summary>
        public Point2D TangentAtDistance(double distance)
        {
            distance = Clamp(distance, 0, TotalLength);
            int i = FindSegmentIndex(distance);
            return (Points[i + 1].Position - Points[i].Position).Normalized();
        }

        private int FindSegmentIndex(double distance)
        {
            for (int i = 0; i < ArcLengths.Count - 1; i++)
            {
                if (distance <= ArcLengths[i + 1] || i == ArcLengths.Count - 2)
                {
                    return i;
                }
            }

            return 0;
        }

        private static double Clamp(double v, double min, double max) => v < min ? min : v > max ? max : v;
    }

    public static class CurveFlattener
    {
        /// <summary>
        /// Разбивает кривую в полилинию. tolerance — допустимое отклонение от истинной кривой Безье, мм
        /// (по умолчанию 0,02 мм — незаметно на печати трафарета, но не создаёт лишних точек).
        /// </summary>
        public static FlattenedCurve Flatten(Curve curve, double toleranceMm = 0.02)
        {
            var points = new List<FlattenedPoint>();

            for (int segIndex = 0; segIndex < curve.Segments.Count; segIndex++)
            {
                CurveSegment seg = curve.Segments[segIndex];

                if (segIndex == 0)
                {
                    points.Add(new FlattenedPoint(seg.Start, true));
                }

                if (seg.IsLine)
                {
                    points.Add(new FlattenedPoint(seg.End, true));
                }
                else
                {
                    FlattenCubic(seg.Start, seg.Control1!.Value, seg.Control2!.Value, seg.End, toleranceMm, points);
                    // Последняя добавленная FlattenCubic точка — конец сегмента, но с isSegmentJoint=false.
                    // Помечаем её как границу сегмента (конец сегмента = потенциальный стык/угол).
                    var last = points[points.Count - 1];
                    points[points.Count - 1] = new FlattenedPoint(last.Position, true);
                }
            }

            return new FlattenedCurve(points, curve.IsClosed);
        }

        private static void FlattenCubic(
            Point2D p0, Point2D p1, Point2D p2, Point2D p3, double toleranceMm,
            List<FlattenedPoint> output, int depth = 0)
        {
            if (depth >= 24 || IsFlatEnough(p0, p1, p2, p3, toleranceMm))
            {
                output.Add(new FlattenedPoint(p3, false));
                return;
            }

            // Подразбиение де Кастельжо пополам.
            Point2D p01 = Point2D.Lerp(p0, p1, 0.5);
            Point2D p12 = Point2D.Lerp(p1, p2, 0.5);
            Point2D p23 = Point2D.Lerp(p2, p3, 0.5);
            Point2D p012 = Point2D.Lerp(p01, p12, 0.5);
            Point2D p123 = Point2D.Lerp(p12, p23, 0.5);
            Point2D mid = Point2D.Lerp(p012, p123, 0.5);

            FlattenCubic(p0, p01, p012, mid, toleranceMm, output, depth + 1);
            FlattenCubic(mid, p123, p23, p3, toleranceMm, output, depth + 1);
        }

        /// <summary>Плоская ли кривая настолько, чтобы заменить её хордой: контрольные точки близко к прямой P0-P3.</summary>
        private static bool IsFlatEnough(Point2D p0, Point2D p1, Point2D p2, Point2D p3, double toleranceMm)
        {
            double d1 = DistanceToLine(p1, p0, p3);
            double d2 = DistanceToLine(p2, p0, p3);
            return d1 <= toleranceMm && d2 <= toleranceMm;
        }

        private static double DistanceToLine(Point2D p, Point2D a, Point2D b)
        {
            Point2D ab = b - a;
            double len = ab.Length;
            if (len < 1e-12)
            {
                return Point2D.Distance(p, a);
            }

            double cross = Math.Abs((p - a).Cross(ab));
            return cross / len;
        }
    }
}
