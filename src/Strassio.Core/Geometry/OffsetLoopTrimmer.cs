using System;
using System.Collections.Generic;

namespace Strassio.Core.Geometry
{
    /// <summary>
    /// Убирает из смещённой линии петли — места, где она подходит к исходной кривой ближе, чем на
    /// расстояние смещения. Такие петли («ласточкин хвост») получаются внутри острых впадин (ямка
    /// сердца, щель буквы) и на изгибах круче радиуса смещения: смещённые куски сторон перехлёстываются,
    /// и стразы ряда ложились кашей поверх соседнего ряда. Остаётся только то, что лежит не ближе
    /// заданного расстояния; куски сшиваются там, где петля начиналась и кончалась (это одна точка).
    /// </summary>
    internal static class OffsetLoopTrimmer
    {
        /// <summary>
        /// Куски смещённой линии без петель. Обычно это одна линия (Closed — замкнута ли она); если
        /// смещение разорвало линию на части (узкое место фигуры), — несколько разомкнутых кусков.
        /// Пусто — от линии ничего не осталось.
        /// </summary>
        public static List<(List<Point2D> Points, bool Closed)> Trim(
            List<Point2D> raw, FlattenedCurve original, double distance, bool closed)
        {
            var result = new List<(List<Point2D>, bool)>();
            double r = Math.Abs(distance);
            if (raw.Count < 2 || r < 1e-9 || original.Points.Count < 2)
            {
                result.Add((raw, closed));
                return result;
            }

            var grid = new SegmentGrid(original, Math.Max(r, 0.5));
            double tolerance = Math.Max(1e-4, r * 1e-3);
            Func<Point2D, bool> isFar = p => grid.DistanceUpTo(p, r) >= r - tolerance;

            // Длинный отрезок смещённой линии может зайти в запретное место серединой — проверяем часто.
            List<Point2D> pts = Densify(raw, Math.Max(0.05, r / 4));
            if (closed && pts.Count > 1 && Point2D.Distance(pts[0], pts[pts.Count - 1]) < 1e-9)
            {
                pts.RemoveAt(pts.Count - 1);
            }

            int n = pts.Count;
            var ok = new bool[n];
            int okCount = 0;
            for (int i = 0; i < n; i++)
            {
                ok[i] = isFar(pts[i]);
                okCount += ok[i] ? 1 : 0;
            }

            if (okCount == n)
            {
                result.Add((raw, closed));
                return result;
            }

            if (okCount == 0)
            {
                return result;
            }

            // У замкнутой линии начинаем с начала «хорошего» куска, чтобы он не разрезался на два.
            int start = 0;
            if (closed)
            {
                for (int i = 0; i < n; i++)
                {
                    if (ok[i] && !ok[(i - 1 + n) % n])
                    {
                        start = i;
                        break;
                    }
                }
            }

            var pieces = new List<List<Point2D>>();
            List<Point2D>? current = null;
            int segments = closed ? n : n - 1;
            for (int k = 0; k < segments; k++)
            {
                int ia = (start + k) % n, ib = (start + k + 1) % n;
                Point2D a = pts[ia], b = pts[ib];
                if (ok[ia])
                {
                    if (current == null)
                    {
                        current = new List<Point2D> { a };
                    }

                    if (ok[ib])
                    {
                        current.Add(b);
                    }
                    else
                    {
                        current.Add(Boundary(a, b, isFar));
                        pieces.Add(current);
                        current = null;
                    }
                }
                else if (ok[ib])
                {
                    current = new List<Point2D> { Boundary(b, a, isFar), b };
                }
            }

            if (current != null)
            {
                pieces.Add(current);
            }

            // Петля начинается и кончается в одной точке (там, где стороны перехлестнулись): соседние куски
            // сшиваем, если между их концами почти нет места. Большой разрыв — линия правда распалась.
            double joinGap = Math.Max(0.05, r * 0.2);
            var joined = new List<List<Point2D>>();
            foreach (List<Point2D> piece in pieces)
            {
                if (joined.Count > 0 && Point2D.Distance(joined[joined.Count - 1][joined[joined.Count - 1].Count - 1], piece[0]) <= joinGap)
                {
                    Append(joined[joined.Count - 1], piece);
                }
                else
                {
                    joined.Add(piece);
                }
            }

            if (closed && joined.Count > 1)
            {
                List<Point2D> last = joined[joined.Count - 1], first = joined[0];
                if (Point2D.Distance(last[last.Count - 1], first[0]) <= joinGap)
                {
                    Append(last, first);
                    joined.RemoveAt(0);
                }
            }

            // Обрывки — кусочки возле острия петли, которые сами по себе далеко от кривой, но к ряду не
            // примыкают (у кривых сторон впадины угол «усов» уходит дальше точки перехлёста). Их убираем,
            // если есть настоящая линия.
            double longest = 0;
            foreach (List<Point2D> piece in joined)
            {
                longest = Math.Max(longest, Length(piece));
            }

            foreach (List<Point2D> piece in joined)
            {
                double length = Length(piece);
                if (piece.Count < 2 || length <= 1e-6 || (length < 2 * r && length < longest))
                {
                    continue;
                }

                // Кусок, который замкнулся сам на себя, — замкнутая линия.
                bool loop = closed && Point2D.Distance(piece[piece.Count - 1], piece[0]) <= joinGap;
                if (loop)
                {
                    // Конец и начало — две стороны одной точки перехлёста: сводим их в одну вершину.
                    piece[0] = (piece[0] + piece[piece.Count - 1]) * 0.5;
                    piece.RemoveAt(piece.Count - 1);
                }

                double minStep = Math.Max(0.005, r * 0.002);
                List<Point2D> clean = WithoutTinySteps(piece, minStep);
                if (loop)
                {
                    while (clean.Count > 2 && Point2D.Distance(clean[clean.Count - 1], clean[0]) < minStep)
                    {
                        clean.RemoveAt(clean.Count - 1);
                    }

                    clean.Add(clean[0]);
                }

                result.Add((clean, loop));
            }

            return result;
        }

        /// <summary>Точка на отрезке от «хорошего» конца к «плохому», где начинается запретное место.</summary>
        private static Point2D Boundary(Point2D good, Point2D bad, Func<Point2D, bool> isFar)
        {
            double lo = 0, hi = 1;
            for (int i = 0; i < 30; i++)
            {
                double mid = (lo + hi) / 2;
                if (isFar(good + (bad - good) * mid))
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }

            return good + (bad - good) * lo;
        }

        /// <summary>Пристыковывает кусок: конец предыдущего и начало следующего — одна точка перехлёста.</summary>
        private static void Append(List<Point2D> to, List<Point2D> piece)
        {
            to[to.Count - 1] = (to[to.Count - 1] + piece[0]) * 0.5;
            for (int i = 1; i < piece.Count; i++)
            {
                to.Add(piece[i]);
            }
        }

        /// <summary>Без соседних точек ближе minStep: в месте сшивки их бывает несколько, и ряд видел там лишние углы.</summary>
        private static List<Point2D> WithoutTinySteps(List<Point2D> pts, double minStep)
        {
            var result = new List<Point2D>(pts.Count) { pts[0] };
            for (int i = 1; i < pts.Count; i++)
            {
                if (Point2D.Distance(result[result.Count - 1], pts[i]) >= minStep)
                {
                    result.Add(pts[i]);
                }
            }

            return result;
        }

        private static double Length(List<Point2D> pts)
        {
            double sum = 0;
            for (int i = 1; i < pts.Count; i++)
            {
                sum += Point2D.Distance(pts[i - 1], pts[i]);
            }

            return sum;
        }

        private static List<Point2D> Densify(List<Point2D> pts, double maxLength)
        {
            var result = new List<Point2D>(pts.Count * 2) { pts[0] };
            for (int i = 1; i < pts.Count; i++)
            {
                Point2D a = pts[i - 1], b = pts[i];
                int parts = Math.Max(1, (int)Math.Ceiling(Point2D.Distance(a, b) / maxLength));
                for (int k = 1; k <= parts; k++)
                {
                    result.Add(a + (b - a) * ((double)k / parts));
                }
            }

            return result;
        }

        /// <summary>Отрезки исходной кривой, разложенные по клеткам, — чтобы быстро узнать, есть ли рядом кривая.</summary>
        private sealed class SegmentGrid
        {
            private readonly Dictionary<(int, int), List<int>> cells = new Dictionary<(int, int), List<int>>();
            private readonly IReadOnlyList<FlattenedPoint> points;
            private readonly double size;

            public SegmentGrid(FlattenedCurve curve, double cellSize)
            {
                points = curve.Points;
                size = cellSize;
                for (int i = 0; i + 1 < points.Count; i++)
                {
                    Point2D a = points[i].Position, b = points[i + 1].Position;
                    int x0 = Cell(Math.Min(a.X, b.X)), x1 = Cell(Math.Max(a.X, b.X));
                    int y0 = Cell(Math.Min(a.Y, b.Y)), y1 = Cell(Math.Max(a.Y, b.Y));
                    for (int x = x0; x <= x1; x++)
                    {
                        for (int y = y0; y <= y1; y++)
                        {
                            if (!cells.TryGetValue((x, y), out List<int>? list))
                            {
                                list = new List<int>();
                                cells[(x, y)] = list;
                            }

                            list.Add(i);
                        }
                    }
                }
            }

            /// <summary>Расстояние до кривой, если оно меньше limit; иначе — что-то не меньше limit.</summary>
            public double DistanceUpTo(Point2D p, double limit)
            {
                double best = double.MaxValue;
                int x0 = Cell(p.X - limit), x1 = Cell(p.X + limit), y0 = Cell(p.Y - limit), y1 = Cell(p.Y + limit);
                for (int x = x0; x <= x1; x++)
                {
                    for (int y = y0; y <= y1; y++)
                    {
                        if (!cells.TryGetValue((x, y), out List<int>? list))
                        {
                            continue;
                        }

                        foreach (int i in list)
                        {
                            best = Math.Min(best, SegmentDistance(p, points[i].Position, points[i + 1].Position));
                        }
                    }
                }

                return best;
            }

            private int Cell(double v) => (int)Math.Floor(v / size);

            private static double SegmentDistance(Point2D p, Point2D a, Point2D b)
            {
                Point2D ab = b - a;
                double len2 = ab.X * ab.X + ab.Y * ab.Y;
                double t = len2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / len2));
                return Point2D.Distance(p, a + ab * t);
            }
        }
    }
}
