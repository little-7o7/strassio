using System;
using System.Collections.Generic;
using System.Linq;
using Strassio.Core.Geometry;

namespace Strassio.Core.Vector
{
    /// <summary>
    /// Векторные инструменты для подготовки линий (docs/SPEC.md, раздел 10): от качества линии
    /// зависит ровность ряда страз. Всё считается здесь, без CorelDRAW; аддон только читает кривые и
    /// создаёт новые.
    /// </summary>
    public static class VectorTools
    {
        /// <summary>Точность, с которой смещённые и сглаженные линии упрощаются после расчёта, мм.</summary>
        public const double OutputToleranceMm = 0.02;

        // ---- Смещение и параллельные линии ------------------------------------------------------

        /// <summary>
        /// Смещение контура (раздел 10): у замкнутой фигуры <paramref name="distanceMm"/> &gt; 0 — наружу,
        /// &lt; 0 — внутрь (в какую сторону ни была нарисована фигура); у незамкнутой линии — вбок
        /// (плюс — одна сторона, минус — другая). Углы — круглые или острые.
        /// </summary>
        public static Curve Offset(Curve curve, double distanceMm, bool roundCorners)
        {
            FlattenedCurve flat = CurveFlattener.Flatten(curve);
            double area = flat.IsClosed ? CurveMetrics.SignedArea(flat) : 0;
            double sign = flat.IsClosed && area > 0 ? -1 : 1;
            if (flat.IsClosed && distanceMm < 0)
            {
                // Внутрь дальше половины меньшей стороны — такого контура нет (он схлопнулся бы).
                (double w, double h) = CurveMetrics.BoundingSize(flat);
                if (-2 * distanceMm >= Math.Min(w, h))
                {
                    throw new ArgumentException("Смещение внутрь больше самой фигуры.", nameof(distanceMm));
                }
            }

            List<Point2D> points = CurveOffsetter.Offset(flat, sign * distanceMm, OutputToleranceMm, roundCorners);
            if (flat.IsClosed)
            {
                var result = new FlattenedCurve(points.Select(p => new FlattenedPoint(p, true)).ToList(), isClosed: true);
                double newArea = CurveMetrics.SignedArea(result);
                if (Math.Sign(newArea) != Math.Sign(area) || Math.Abs(newArea) < 1e-6)
                {
                    throw new ArgumentException("Контур вывернулся наизнанку.", nameof(distanceMm));
                }
            }

            return Curve.FromPolyline(Simplify(points, flat.IsClosed, OutputToleranceMm), flat.IsClosed);
        }

        /// <summary>
        /// Параллельные линии (раздел 10): <paramref name="count"/> копий через <paramref name="stepMm"/>
        /// — в одну сторону или в обе (тогда по <paramref name="count"/> с каждой стороны).
        /// </summary>
        public static List<Curve> Parallel(Curve curve, int count, double stepMm, bool bothSides, bool roundCorners)
        {
            var result = new List<Curve>();
            for (int k = 1; k <= count; k++)
            {
                TryAdd(result, curve, k * stepMm, roundCorners);
                if (bothSides)
                {
                    TryAdd(result, curve, -k * stepMm, roundCorners);
                }
            }

            return result;
        }

        private static void TryAdd(List<Curve> into, Curve curve, double distance, bool round)
        {
            try
            {
                into.Add(Offset(curve, distance, round));
            }
            catch (ArgumentException)
            {
                // Смещение внутрь больше самой фигуры — такой линии нет.
            }
        }

        // ---- Разрезать на равные части ---------------------------------------------------------

        /// <summary>
        /// Разрезает кривую на <paramref name="parts"/> частей равной длины (раздел 10). Кривые Безье
        /// режутся точно (алгоритм де Кастельжо) — форма линии не меняется, узлов добавляется минимум.
        /// Замкнутая кривая режется от своего начала.
        /// </summary>
        public static List<Curve> SplitEqual(Curve curve, int parts)
        {
            parts = Math.Max(1, parts);
            List<double> lengths = curve.Segments.Select(SegmentLength).ToList();
            double total = lengths.Sum();
            var result = new List<Curve>();
            var current = new List<CurveSegment>();
            double target = total / parts;
            double done = 0;
            int piece = 1;

            for (int i = 0; i < curve.Segments.Count; i++)
            {
                CurveSegment original = curve.Segments[i];
                CurveSegment seg = original;
                double segStart = done;
                double segLen = lengths[i];
                double from = 0; // уже отрезанная доля этого сегмента, параметр t

                while (piece < parts && segStart + segLen > target * piece + 1e-9)
                {
                    // t — по исходному сегменту; хвост после прошлого разреза — та же кривая с параметром
                    // (t - from) / (1 - from), поэтому режем его в пересчитанной точке.
                    double tCut = ParameterAtLength(original, target * piece - segStart);
                    (CurveSegment head, CurveSegment tail) = Split(seg, (tCut - from) / (1 - from));
                    current.Add(head);
                    result.Add(new Curve(current, isClosed: false));
                    current = new List<CurveSegment>();
                    seg = tail;
                    from = tCut;
                    piece++;
                }

                current.Add(seg);
                done += segLen;
            }

            if (current.Count > 0)
            {
                result.Add(new Curve(current, isClosed: false));
            }

            return result;
        }

        /// <summary>Длина сегмента: прямой — точно, кривой Безье — по 64 точкам.</summary>
        public static double SegmentLength(CurveSegment seg)
        {
            if (seg.IsLine)
            {
                return Point2D.Distance(seg.Start, seg.End);
            }

            double length = 0;
            Point2D prev = seg.Start;
            for (int i = 1; i <= 64; i++)
            {
                Point2D p = seg.PointAt(i / 64.0);
                length += Point2D.Distance(prev, p);
                prev = p;
            }

            return length;
        }

        /// <summary>Параметр t, на котором от начала сегмента пройдено <paramref name="length"/>.</summary>
        private static double ParameterAtLength(CurveSegment seg, double length)
        {
            if (seg.IsLine)
            {
                double full = Point2D.Distance(seg.Start, seg.End);
                return full <= 0 ? 0 : Math.Max(0, Math.Min(1, length / full));
            }

            const int steps = 256;
            double walked = 0;
            Point2D prev = seg.Start;
            for (int i = 1; i <= steps; i++)
            {
                Point2D p = seg.PointAt((double)i / steps);
                double step = Point2D.Distance(prev, p);
                if (walked + step >= length)
                {
                    double frac = step <= 0 ? 0 : (length - walked) / step;
                    return (i - 1 + frac) / steps;
                }

                walked += step;
                prev = p;
            }

            return 1;
        }

        /// <summary>Делит сегмент в точке t на два — для кривой Безье по де Кастельжо (форма та же).</summary>
        public static (CurveSegment Head, CurveSegment Tail) Split(CurveSegment seg, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            if (seg.IsLine)
            {
                Point2D m = Point2D.Lerp(seg.Start, seg.End, t);
                return (CurveSegment.Line(seg.Start, m), CurveSegment.Line(m, seg.End));
            }

            Point2D p0 = seg.Start, p1 = seg.Control1!.Value, p2 = seg.Control2!.Value, p3 = seg.End;
            Point2D a = Point2D.Lerp(p0, p1, t), b = Point2D.Lerp(p1, p2, t), c = Point2D.Lerp(p2, p3, t);
            Point2D d = Point2D.Lerp(a, b, t), e = Point2D.Lerp(b, c, t);
            Point2D m2 = Point2D.Lerp(d, e, t);
            return (CurveSegment.Cubic(p0, a, d, m2), CurveSegment.Cubic(m2, e, c, p3));
        }

        // ---- Соединить концы, замкнуть, найти разрывы ------------------------------------------

        /// <summary>
        /// Соединяет незамкнутые линии, концы которых ближе <paramref name="toleranceMm"/> (раздел 10):
        /// ближайшие концы — первыми; если концы не совпадают точно, между ними ставится короткий отрезок.
        /// Линия, у которой после этого начало и конец ближе допуска, замыкается.
        /// </summary>
        public static List<Curve> JoinEnds(IReadOnlyList<Curve> curves, double toleranceMm)
        {
            var open = curves.Where(c => !c.IsClosed).Select(c => c.Segments.ToList()).ToList();
            var result = curves.Where(c => c.IsClosed).ToList();

            while (true)
            {
                double best = double.MaxValue;
                (int A, bool AEnd, int B, bool BEnd) pick = default;
                for (int i = 0; i < open.Count; i++)
                {
                    for (int j = i + 1; j < open.Count; j++)
                    {
                        foreach (bool ie in new[] { false, true })
                        {
                            foreach (bool je in new[] { false, true })
                            {
                                double dist = Point2D.Distance(End(open[i], ie), End(open[j], je));
                                if (dist < best)
                                {
                                    best = dist;
                                    pick = (i, ie, j, je);
                                }
                            }
                        }
                    }
                }

                if (best > toleranceMm)
                {
                    break;
                }

                // Порядок: …a → стык → b…  (a заканчивается в месте стыка, b начинается в нём).
                List<CurveSegment> a = pick.AEnd ? open[pick.A] : Reverse(open[pick.A]);
                List<CurveSegment> b = pick.BEnd ? Reverse(open[pick.B]) : open[pick.B];
                var joined = new List<CurveSegment>(a);
                Point2D from = a[a.Count - 1].End, to = b[0].Start;
                if (Point2D.Distance(from, to) > CurveFlattener.DuplicateToleranceMm)
                {
                    joined.Add(CurveSegment.Line(from, to));
                }

                joined.AddRange(b);
                open.RemoveAt(pick.B);
                open.RemoveAt(pick.A);
                open.Add(joined);
            }

            foreach (List<CurveSegment> segments in open)
            {
                result.Add(CloseIfNear(segments, toleranceMm));
            }

            return result;
        }

        /// <summary>Замыкает линию, если её начало и конец ближе допуска (раздел 10).</summary>
        public static Curve Close(Curve curve, double toleranceMm) =>
            curve.IsClosed ? curve : CloseIfNear(curve.Segments.ToList(), toleranceMm);

        private static Curve CloseIfNear(List<CurveSegment> segments, double toleranceMm)
        {
            Point2D start = segments[0].Start, end = segments[segments.Count - 1].End;
            double gap = Point2D.Distance(start, end);
            if (gap > toleranceMm || (segments.Count < 2 && gap <= CurveFlattener.DuplicateToleranceMm))
            {
                return new Curve(segments, isClosed: false);
            }

            var closed = new List<CurveSegment>(segments);
            if (gap > CurveFlattener.DuplicateToleranceMm)
            {
                closed.Add(CurveSegment.Line(end, start));
            }

            return new Curve(closed, isClosed: true);
        }

        /// <summary>
        /// Разрывы (раздел 10): пары концов незамкнутых линий на расстоянии от
        /// <paramref name="minMm"/> до <paramref name="maxMm"/> — там линия, скорее всего, должна быть
        /// цельной. Возвращает середины разрывов.
        /// </summary>
        public static List<Point2D> FindGaps(IReadOnlyList<Curve> curves, double minMm, double maxMm)
        {
            var ends = new List<(Point2D P, int Curve)>();
            for (int i = 0; i < curves.Count; i++)
            {
                if (!curves[i].IsClosed)
                {
                    ends.Add((curves[i].Segments[0].Start, i));
                    ends.Add((curves[i].Segments[curves[i].Segments.Count - 1].End, i));
                }
            }

            var gaps = new List<Point2D>();
            for (int i = 0; i < ends.Count; i++)
            {
                for (int j = i + 1; j < ends.Count; j++)
                {
                    double dist = Point2D.Distance(ends[i].P, ends[j].P);
                    bool sameCurveOwnEnds = ends[i].Curve == ends[j].Curve && dist < minMm;
                    if (dist >= minMm && dist <= maxMm && !sameCurveOwnEnds)
                    {
                        gaps.Add((ends[i].P + ends[j].P) / 2);
                    }
                }
            }

            return gaps;
        }

        private static Point2D End(List<CurveSegment> segments, bool atEnd) =>
            atEnd ? segments[segments.Count - 1].End : segments[0].Start;

        private static List<CurveSegment> Reverse(List<CurveSegment> segments) =>
            segments.AsEnumerable().Reverse().Select(ReverseSegment).ToList();

        private static CurveSegment ReverseSegment(CurveSegment s) =>
            s.IsLine ? CurveSegment.Line(s.End, s.Start) : CurveSegment.Cubic(s.End, s.Control2!.Value, s.Control1!.Value, s.Start);

        /// <summary>Та же линия в обратную сторону (раздел 10, «развернуть направление»).</summary>
        public static Curve ReverseDirection(Curve curve) => new Curve(Reverse(curve.Segments.ToList()), curve.IsClosed);

        // ---- Упростить и сгладить --------------------------------------------------------------

        /// <summary>
        /// Сглаживание и упрощение узлов (раздел 10): лишние узлы убираются (Рамер — Дуглас — Пекер с
        /// допуском <paramref name="toleranceMm"/>), через оставшиеся проводится плавная кривая Безье
        /// (Катмулл — Ром). Дрожащая от руки линия становится ровной — и ряд страз по ней тоже.
        /// </summary>
        public static Curve SimplifyAndSmooth(Curve curve, double toleranceMm, bool smooth)
        {
            FlattenedCurve flat = CurveFlattener.Flatten(curve);
            List<Point2D> points = Simplify(flat.Points.Select(p => p.Position).ToList(), flat.IsClosed, toleranceMm);
            return smooth ? Smooth(points, flat.IsClosed) : Curve.FromPolyline(points, flat.IsClosed);
        }

        /// <summary>Плавная кривая Безье через точки (Катмулл — Ром): проходит через каждую точку.</summary>
        public static Curve Smooth(IReadOnlyList<Point2D> points, bool closed)
        {
            int n = points.Count;
            if (n < 3)
            {
                return Curve.FromPolyline(points, closed);
            }

            Point2D At(int i) => closed ? points[((i % n) + n) % n] : points[Math.Max(0, Math.Min(n - 1, i))];

            var segments = new List<CurveSegment>();
            int count = closed ? n : n - 1;
            for (int i = 0; i < count; i++)
            {
                Point2D p0 = At(i - 1), p1 = At(i), p2 = At(i + 1), p3 = At(i + 2);
                Point2D c1 = p1 + (p2 - p0) / 6;
                Point2D c2 = p2 - (p3 - p1) / 6;
                segments.Add(CurveSegment.Cubic(p1, c1, c2, p2));
            }

            return new Curve(segments, closed);
        }

        /// <summary>Рамер — Дуглас — Пекер: убирает точки, без которых линия отходит меньше допуска.</summary>
        public static List<Point2D> Simplify(IReadOnlyList<Point2D> points, bool closed, double toleranceMm)
        {
            var pts = points.ToList();
            if (closed && pts.Count > 1 && Point2D.Distance(pts[0], pts[pts.Count - 1]) < 1e-9)
            {
                pts.RemoveAt(pts.Count - 1);
            }

            if (pts.Count < 3)
            {
                return pts;
            }

            if (closed)
            {
                // Замкнутую — делим пополам по самой дальней от начала точке и упрощаем обе половины.
                int far = 0;
                double farDist = -1;
                for (int i = 1; i < pts.Count; i++)
                {
                    double dd = Point2D.Distance(pts[0], pts[i]);
                    if (dd > farDist)
                    {
                        farDist = dd;
                        far = i;
                    }
                }

                List<Point2D> first = Rdp(pts.GetRange(0, far + 1), toleranceMm);
                var secondSource = pts.GetRange(far, pts.Count - far);
                secondSource.Add(pts[0]);
                List<Point2D> second = Rdp(secondSource, toleranceMm);
                var result = new List<Point2D>(first);
                result.AddRange(second.Skip(1).Take(second.Count - 2));
                return result;
            }

            return Rdp(pts, toleranceMm);
        }

        private static List<Point2D> Rdp(List<Point2D> pts, double tolerance)
        {
            if (pts.Count < 3)
            {
                return new List<Point2D>(pts);
            }

            var keep = new bool[pts.Count];
            keep[0] = keep[pts.Count - 1] = true;
            var stack = new Stack<(int, int)>();
            stack.Push((0, pts.Count - 1));
            while (stack.Count > 0)
            {
                (int a, int b) = stack.Pop();
                double worst = -1;
                int index = -1;
                for (int i = a + 1; i < b; i++)
                {
                    double dd = DistanceToSegment(pts[i], pts[a], pts[b]);
                    if (dd > worst)
                    {
                        worst = dd;
                        index = i;
                    }
                }

                if (index >= 0 && worst > tolerance)
                {
                    keep[index] = true;
                    stack.Push((a, index));
                    stack.Push((index, b));
                }
            }

            return pts.Where((p, i) => keep[i]).ToList();
        }

        // ---- Проверки ---------------------------------------------------------------------------

        /// <summary>Самопересечения линии (раздел 10, «проверка»): точки, где линия пересекает сама себя.</summary>
        public static List<Point2D> SelfIntersections(Curve curve)
        {
            FlattenedCurve flat = CurveFlattener.Flatten(curve, 0.05);
            List<Point2D> pts = flat.Points.Select(p => p.Position).ToList();
            if (flat.IsClosed)
            {
                pts.Add(pts[0]);
            }

            var result = new List<Point2D>();
            int n = pts.Count - 1;
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 2; j < n; j++)
                {
                    if (flat.IsClosed && i == 0 && j == n - 1)
                    {
                        continue; // первый и последний отрезки замкнутой линии — соседи
                    }

                    if (SegmentsCross(pts[i], pts[i + 1], pts[j], pts[j + 1], out Point2D x) &&
                        !result.Any(r => Point2D.Distance(r, x) < 0.05))
                    {
                        result.Add(x);
                    }
                }
            }

            return result;
        }

        /// <summary>Микросегменты (раздел 10): сегменты короче <paramref name="minLengthMm"/> — середины таких мест.</summary>
        public static List<Point2D> MicroSegments(Curve curve, double minLengthMm) =>
            curve.Segments
                .Where(s => SegmentLength(s) < minLengthMm)
                .Select(s => (s.Start + s.End) / 2)
                .ToList();

        /// <summary>
        /// Одинаковые ли кривые (дубли, раздел 10): те же узлы и контрольные точки с точностью
        /// <paramref name="toleranceMm"/> — в том же или в обратном направлении.
        /// </summary>
        public static bool AreSame(Curve a, Curve b, double toleranceMm = 0.01)
        {
            if (a.Segments.Count != b.Segments.Count || a.IsClosed != b.IsClosed)
            {
                return false;
            }

            bool Match(IReadOnlyList<CurveSegment> x, IReadOnlyList<CurveSegment> y)
            {
                for (int i = 0; i < x.Count; i++)
                {
                    if (!Near(x[i].Start, y[i].Start) || !Near(x[i].End, y[i].End) || x[i].IsLine != y[i].IsLine)
                    {
                        return false;
                    }

                    if (!x[i].IsLine && (!Near(x[i].Control1!.Value, y[i].Control1!.Value) || !Near(x[i].Control2!.Value, y[i].Control2!.Value)))
                    {
                        return false;
                    }
                }

                return true;
            }

            bool Near(Point2D p, Point2D q) => Point2D.Distance(p, q) <= toleranceMm;

            return Match(a.Segments, b.Segments) || Match(a.Segments, ReverseDirection(b).Segments);
        }

        private static bool SegmentsCross(Point2D a, Point2D b, Point2D c, Point2D d, out Point2D at)
        {
            at = Point2D.Zero;
            Point2D r = b - a, s = d - c;
            double denom = r.Cross(s);
            if (Math.Abs(denom) < 1e-12)
            {
                return false;
            }

            double t = (c - a).Cross(s) / denom;
            double u = (c - a).Cross(r) / denom;
            if (t <= 1e-9 || t >= 1 - 1e-9 || u <= 1e-9 || u >= 1 - 1e-9)
            {
                return false;
            }

            at = a + r * t;
            return true;
        }

        private static double DistanceToSegment(Point2D p, Point2D a, Point2D b)
        {
            Point2D ab = b - a;
            double len2 = ab.Dot(ab);
            if (len2 < 1e-18)
            {
                return Point2D.Distance(p, a);
            }

            double t = Math.Max(0, Math.Min(1, (p - a).Dot(ab) / len2));
            return Point2D.Distance(a + ab * t, p);
        }
    }
}
