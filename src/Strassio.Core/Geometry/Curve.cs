using System;
using System.Collections.Generic;

namespace Strassio.Core.Geometry
{
    /// <summary>Кривая — последовательность сегментов, разомкнутая или замкнутая.</summary>
    public sealed class Curve
    {
        public IReadOnlyList<CurveSegment> Segments { get; }
        public bool IsClosed { get; }

        public Curve(IReadOnlyList<CurveSegment> segments, bool isClosed)
        {
            if (segments == null || segments.Count == 0)
            {
                throw new ArgumentException("Кривая должна содержать хотя бы один сегмент.", nameof(segments));
            }

            Segments = segments;
            IsClosed = isClosed;
        }

        /// <summary>Кривая из отрезков прямых по точкам ломаной (например, зигзаг или многоугольник).</summary>
        public static Curve FromPolyline(IReadOnlyList<Point2D> points, bool isClosed = false)
        {
            if (points == null || points.Count < 2)
            {
                throw new ArgumentException("Нужно минимум 2 точки.", nameof(points));
            }

            var segments = new List<CurveSegment>();
            for (int i = 0; i < points.Count - 1; i++)
            {
                segments.Add(CurveSegment.Line(points[i], points[i + 1]));
            }

            if (isClosed && points[0] != points[points.Count - 1])
            {
                segments.Add(CurveSegment.Line(points[points.Count - 1], points[0]));
            }

            return new Curve(segments, isClosed);
        }
    }
}
