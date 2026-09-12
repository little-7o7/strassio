using System;
using System.Collections.Generic;

namespace Strassio.Core.Geometry
{
    /// <summary>
    /// Строит параллельный контур на заданном расстоянии от кривой — нужен методам L2 «вокруг линии»
    /// и L3 «по смещённой кривой» (docs/SPEC.md, разделы 4 и 6.1).
    ///
    /// distance > 0 — смещение влево по ходу кривой, distance &lt; 0 — вправо.
    /// Снаружи угла (там, где смещённые отрезки расходятся) вставляется дуга — «внешняя сторона
    /// обходит угол дугой». Изнутри угла (где смещённые отрезки перекрывались бы и образовали
    /// петлю) — они обрезаются до точки пересечения — «петля вырезается ДО расстановки камней».
    /// </summary>
    public static class CurveOffsetter
    {
        public static List<Point2D> Offset(FlattenedCurve flat, double distance, double toleranceMm = 0.02)
        {
            IReadOnlyList<FlattenedPoint> pts = flat.Points;
            int n = pts.Count;
            bool closed = flat.IsClosed;

            if (Math.Abs(distance) < 1e-9)
            {
                var same = new List<Point2D>(n);
                foreach (FlattenedPoint p in pts)
                {
                    same.Add(p.Position);
                }

                return same;
            }

            // Направление и левая нормаль каждого маленького отрезка исходной полилинии.
            var dirs = new Point2D[n - 1];
            var normals = new Point2D[n - 1];
            for (int i = 0; i < n - 1; i++)
            {
                Point2D d = (pts[i + 1].Position - pts[i].Position).Normalized();
                dirs[i] = d;
                normals[i] = new Point2D(-d.Y, d.X);
            }

            var result = new List<Point2D>();

            if (!closed)
            {
                result.Add(pts[0].Position + normals[0] * distance); // плоский торец в начале
            }

            int firstVertex = closed ? 0 : 1;
            for (int i = firstVertex; i < n - 1; i++)
            {
                int prevSeg = closed && i == 0 ? n - 2 : i - 1;
                int nextSeg = i;

                Point2D dPrev = dirs[prevSeg];
                Point2D dNext = dirs[nextSeg];
                Point2D incomingEnd = pts[i].Position + normals[prevSeg] * distance;
                Point2D outgoingStart = pts[i].Position + normals[nextSeg] * distance;

                double turn = dPrev.Cross(dNext); // > 0 — поворот налево (против часовой стрелки)

                if (Math.Abs(turn) < 1e-6)
                {
                    result.Add(incomingEnd); // почти прямая — стыка нет
                    continue;
                }

                bool isOuterSide = (turn > 0) != (distance > 0);

                if (isOuterSide)
                {
                    AddArc(result, pts[i].Position, incomingEnd, outgoingStart, distance, toleranceMm);
                }
                else
                {
                    // Обрезка (митр) изнутри угла: точка пересечения смещённых прямых. На очень острых
                    // углах (как у тонкого шипа звезды) она улетает далеко от вершины — вместо этого
                    // делаем срез (bevel), как принято в графических редакторах (аналог stroke-miterlimit
                    // в SVG), иначе получается самопересекающийся мусор вместо аккуратного контура.
                    const double miterLimit = 3.0;
                    Point2D? trimmed = LineIntersection(incomingEnd, dPrev, outgoingStart, dNext);
                    if (trimmed.HasValue && Point2D.Distance(trimmed.Value, pts[i].Position) <= miterLimit * Math.Abs(distance))
                    {
                        result.Add(trimmed.Value);
                    }
                    else
                    {
                        result.Add(incomingEnd);
                        result.Add(outgoingStart);
                    }
                }
            }

            if (!closed)
            {
                result.Add(pts[n - 1].Position + normals[n - 2] * distance); // плоский торец в конце
            }
            else
            {
                result.Add(result[0]); // замыкаем дублем первой точки, как в FlattenedCurve
            }

            return result;
        }

        private static void AddArc(
            List<Point2D> output, Point2D center, Point2D from, Point2D to, double radius, double toleranceMm)
        {
            double r = Math.Abs(radius);
            Point2D v0 = (from - center).Normalized();
            Point2D v1 = (to - center).Normalized();
            double angle = Math.Atan2(v0.Cross(v1), v0.Dot(v1));

            double maxStep = r > toleranceMm ? 2 * Math.Acos(1 - toleranceMm / r) : Math.PI / 8;
            if (maxStep < 1e-3)
            {
                maxStep = 1e-3;
            }

            int steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(angle) / maxStep));

            output.Add(from);
            for (int s = 1; s < steps; s++)
            {
                double t = angle * s / steps;
                double cos = Math.Cos(t), sin = Math.Sin(t);
                var v = new Point2D(v0.X * cos - v0.Y * sin, v0.X * sin + v0.Y * cos);
                output.Add(center + v * r);
            }

            output.Add(to);
        }

        private static Point2D? LineIntersection(Point2D p1, Point2D d1, Point2D p2, Point2D d2)
        {
            double denom = d1.Cross(d2);
            if (Math.Abs(denom) < 1e-6)
            {
                return null;
            }

            double t = (p2 - p1).Cross(d2) / denom;
            return p1 + d1 * t;
        }
    }
}
