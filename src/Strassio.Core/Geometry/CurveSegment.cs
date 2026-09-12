namespace Strassio.Core.Geometry
{
    /// <summary>
    /// Один сегмент кривой: прямая (Control1/Control2 == null) или кубическая кривая Безье.
    /// CorelDRAW отдаёт узлы и контрольные точки в этом виде — сегменты читаются один раз,
    /// дальше вся работа с кривой идёт только здесь, в Core.
    /// </summary>
    public sealed class CurveSegment
    {
        public Point2D Start { get; }
        public Point2D End { get; }
        public Point2D? Control1 { get; }
        public Point2D? Control2 { get; }

        public bool IsLine => Control1 == null;

        private CurveSegment(Point2D start, Point2D end, Point2D? control1, Point2D? control2)
        {
            Start = start;
            End = end;
            Control1 = control1;
            Control2 = control2;
        }

        public static CurveSegment Line(Point2D start, Point2D end) => new CurveSegment(start, end, null, null);

        public static CurveSegment Cubic(Point2D start, Point2D control1, Point2D control2, Point2D end) =>
            new CurveSegment(start, end, control1, control2);

        /// <summary>Точка на сегменте при параметре t в [0, 1].</summary>
        public Point2D PointAt(double t)
        {
            if (IsLine)
            {
                return Point2D.Lerp(Start, End, t);
            }

            double mt = 1 - t;
            double a = mt * mt * mt;
            double b = 3 * mt * mt * t;
            double c = 3 * mt * t * t;
            double d = t * t * t;
            return Start * a + Control1!.Value * b + Control2!.Value * c + End * d;
        }
    }
}
