using System;
using System.Collections.Generic;
using System.Linq;
using Strassio.Core.Geometry;
using Strassio.Core.Vector;

namespace Strassio.Core.Placement
{
    /// <summary>
    /// F13 «По толщине штриха» (docs/SPEC.md, раздел 5): один ряд по середине буквы или мазка кистью,
    /// а размер камня следует ширине — в тонком месте мелкий камень, в толстом крупный. Как рука кладёт
    /// каллиграфию: ряд не обрывается в тонких местах и не «тонет» в толстых.
    /// <para>
    /// Середина штриха — <see cref="Centerline"/>. Вдоль неё мелким шагом меряется глубина (расстояние
    /// до края); в каждой точке берётся самый крупный камень из списка, который там помещается. Свободные
    /// концы середины чуть продлеваются к кончикам мазка — туда встают мелкие камни. Ряд ставится от
    /// конца к концу так, чтобы между соседями был заданный зазор с учётом размеров обоих, а остаток
    /// длины раздаётся поровну по всем промежуткам. Сначала ставится самая длинная линия, остальные
    /// ветки (у букв Т, А, Н) начинаются там, где камни уже не задевают поставленные.
    /// </para>
    /// </summary>
    public static class StrokeRow
    {
        /// <param name="contours">Контуры формы (внешний и отверстия), мм.</param>
        /// <param name="diameters">Какие камни можно брать, мм (обычно все размеры таблицы от мелкого до основного).</param>
        /// <param name="gapMm">Зазор между соседними камнями.</param>
        /// <param name="marginMm">Отступ камня от края формы.</param>
        public static List<PlacedStone> Place(IReadOnlyList<Curve> contours, IReadOnlyList<double> diameters, double gapMm, double marginMm)
        {
            List<double> sizes = diameters.Where(v => v > 0).Distinct().OrderBy(v => v).ToList();
            if (sizes.Count == 0 || contours.Count == 0)
            {
                return new List<PlacedStone>();
            }

            var region = new ShapeRegion(contours, sizes[0]);
            var sizer = new Sizer(region, new EdgeIndex(region.Contours, region.MaxDepth + 0.2), sizes, marginMm);
            double step = Math.Max(0.01, Math.Min(0.05, sizes[0] / 20));

            // Линии середины режутся в резких поворотах: у развилки (Т, А, Н) Centerline ведёт одну линию
            // из ножки в перекладину через скруглённый угол — ряд по такой дуге ушёл бы с середины.
            var pieces = new List<(List<Point2D> Points, bool Closed)>();
            foreach (Curve line in Centerline.Build(contours, smooth: false))
            {
                FlattenedCurve flat = CurveFlattener.Flatten(line);
                if (flat.TotalLength > 1e-6)
                {
                    pieces.AddRange(SplitAtCorners(Resample(flat, step), flat.IsClosed, step, Math.Max(1.0, sizes[0])));
                }
            }

            pieces = pieces.OrderByDescending(p => Length(p.Points, p.Closed)).ToList();
            var packer = new StonePacker(sizes[sizes.Count - 1], gapMm);
            for (int row = 0; row < pieces.Count; row++)
            {
                List<List<Point2D>> others = pieces.Where((_, i) => i != row).Select(p => p.Points).ToList();
                Track track = Track.Build(pieces[row].Points, pieces[row].Closed, others, sizer, step);
                foreach (int k in track.Place(packer, gapMm))
                {
                    packer.TryAdd(track.Positions[k], track.Sizes[k], row);
                }
            }

            // Круг, квадрат — не штрих: середина у них стягивается в точку. Тогда — один камень в самом глубоком месте.
            if (packer.Stones.Count == 0)
            {
                double size = sizer.SizeAt(region.DeepestPoint);
                if (size > 0)
                {
                    packer.TryAdd(region.DeepestPoint, size);
                }
            }

            return packer.Stones;
        }

        /// <summary>Поворот больше этого (на отрезке около камня) — угол, линия здесь режется на куски.</summary>
        private const double CornerTurnDeg = 50;

        private static List<Point2D> Resample(FlattenedCurve line, double step)
        {
            var points = new List<Point2D>();
            double total = line.TotalLength;
            int count = Math.Max(1, (int)Math.Ceiling(total / step));
            int last = line.IsClosed ? count - 1 : count;
            for (int i = 0; i <= last; i++)
            {
                points.Add(line.PointAtDistance(total * i / count));
            }

            return points;
        }

        private static double Length(List<Point2D> points, bool closed)
        {
            double length = 0;
            for (int i = 1; i < points.Count; i++)
            {
                length += Point2D.Distance(points[i - 1], points[i]);
            }

            return closed && points.Count > 2 ? length + Point2D.Distance(points[points.Count - 1], points[0]) : length;
        }

        /// <summary>
        /// Режет линию в резких поворотах: смотрим направление на отрезке <paramref name="window"/> до точки и
        /// после неё; где оно поворачивает больше <see cref="CornerTurnDeg"/>, — угол, разрез в самом резком месте.
        /// </summary>
        private static List<(List<Point2D> Points, bool Closed)> SplitAtCorners(List<Point2D> points, bool closed, double step, double window)
        {
            int n = points.Count;
            int w = Math.Max(2, (int)Math.Round(window / step));
            var whole = new List<(List<Point2D>, bool)> { (points, closed) };
            if (n < 2 * w + 3)
            {
                return whole;
            }

            double TurnAt(IReadOnlyList<Point2D> pts, int i)
            {
                int a = i - w, b = i + w;
                if (!closed && (a < 0 || b >= n))
                {
                    return 0;
                }

                Point2D d1 = pts[i] - pts[(a + n) % n];
                Point2D d2 = pts[b % n] - pts[i];
                if (d1.Length < 1e-9 || d2.Length < 1e-9)
                {
                    return 0;
                }

                double cos = Math.Max(-1, Math.Min(1, d1.Dot(d2) / (d1.Length * d2.Length)));
                return Math.Acos(cos) * 180 / Math.PI;
            }

            double[] turn = Enumerable.Range(0, n).Select(i => TurnAt(points, i)).ToArray();
            if (closed)
            {
                // Кольцо начинаем с прямого места, чтобы угол не разрезало пополам на стыке начала и конца.
                int calm = Array.FindIndex(turn, t => t <= CornerTurnDeg);
                if (calm < 0)
                {
                    return whole;
                }

                points = points.Skip(calm).Concat(points.Take(calm)).ToList();
                turn = turn.Skip(calm).Concat(turn.Take(calm)).ToArray();
            }

            var cuts = new List<int>();
            for (int i = 0; i < n;)
            {
                if (turn[i] <= CornerTurnDeg)
                {
                    i++;
                    continue;
                }

                int sharpest = i;
                int j = i;
                while (j + 1 < n && turn[j + 1] > CornerTurnDeg)
                {
                    j++;
                    if (turn[j] > turn[sharpest])
                    {
                        sharpest = j;
                    }
                }

                cuts.Add(sharpest);
                i = j + 1;
            }

            if (cuts.Count == 0)
            {
                return whole;
            }

            var pieces = new List<(List<Point2D>, bool)>();
            if (closed)
            {
                for (int k = 0; k < cuts.Count; k++)
                {
                    int from = cuts[k];
                    int to = cuts[(k + 1) % cuts.Count];
                    int length = ((to - from + n - 1) % n) + 1;
                    pieces.Add((Enumerable.Range(0, length + 1).Select(t => points[(from + t) % n]).ToList(), false));
                }
            }
            else
            {
                int from = 0;
                foreach (int cut in cuts.Append(n - 1))
                {
                    if (cut > from)
                    {
                        pieces.Add((points.GetRange(from, cut - from + 1), false));
                    }

                    from = cut;
                }
            }

            return pieces;
        }

        /// <summary>
        /// Точное расстояние до края вблизи точки. Карта расстояний считает его с ошибкой в доли клетки:
        /// в полоске ровно на камень ss10 она «не пускала» ss10 и ставила мелкий. Здесь отрезки контура
        /// разложены по клеткам, и ищутся только ближайшие — не дальше <see cref="reach"/>.
        /// </summary>
        private sealed class EdgeIndex
        {
            private readonly double reach;
            private readonly double cell;
            private readonly Dictionary<(int, int), List<(Point2D A, Point2D B)>> cells = new Dictionary<(int, int), List<(Point2D A, Point2D B)>>();

            public EdgeIndex(IReadOnlyList<FlattenedCurve> contours, double reach)
            {
                this.reach = reach;
                cell = Math.Max(0.25, reach);
                foreach (FlattenedCurve contour in contours)
                {
                    IReadOnlyList<FlattenedPoint> pts = contour.Points;
                    for (int i = 0; i < pts.Count; i++)
                    {
                        if (i + 1 == pts.Count && !contour.IsClosed)
                        {
                            break;
                        }

                        Point2D a = pts[i].Position;
                        Point2D b = pts[(i + 1) % pts.Count].Position;
                        int x0 = Key(Math.Min(a.X, b.X)), x1 = Key(Math.Max(a.X, b.X));
                        int y0 = Key(Math.Min(a.Y, b.Y)), y1 = Key(Math.Max(a.Y, b.Y));
                        for (int x = x0; x <= x1; x++)
                        {
                            for (int y = y0; y <= y1; y++)
                            {
                                if (!cells.TryGetValue((x, y), out List<(Point2D, Point2D)>? list))
                                {
                                    list = new List<(Point2D, Point2D)>();
                                    cells[(x, y)] = list;
                                }

                                list.Add((a, b));
                            }
                        }
                    }
                }
            }

            private int Key(double v) => (int)Math.Floor(v / cell);

            /// <summary>Расстояние до ближайшего края, но не больше <see cref="reach"/>.</summary>
            public double Distance(Point2D p)
            {
                double best = reach;
                int x0 = Key(p.X - reach), x1 = Key(p.X + reach), y0 = Key(p.Y - reach), y1 = Key(p.Y + reach);
                for (int x = x0; x <= x1; x++)
                {
                    for (int y = y0; y <= y1; y++)
                    {
                        if (!cells.TryGetValue((x, y), out List<(Point2D A, Point2D B)>? list))
                        {
                            continue;
                        }

                        foreach ((Point2D a, Point2D b) in list)
                        {
                            best = Math.Min(best, DistanceToSegment(p, a, b));
                        }
                    }
                }

                return best;
            }
        }

        private static double DistanceToSegment(Point2D p, Point2D a, Point2D b)
        {
            Point2D ab = b - a;
            double len2 = ab.X * ab.X + ab.Y * ab.Y;
            double t = len2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / len2));
            return Point2D.Distance(p, new Point2D(a.X + ab.X * t, a.Y + ab.Y * t));
        }

        /// <summary>Какой камень встаёт в точку с данной глубиной.</summary>
        private sealed class Sizer
        {
            private readonly ShapeRegion region;
            private readonly EdgeIndex edges;
            private readonly List<double> sizes;
            private readonly double margin;

            public Sizer(ShapeRegion region, EdgeIndex edges, List<double> sizes, double margin)
            {
                this.region = region;
                this.edges = edges;
                this.sizes = sizes;
                this.margin = margin;
            }

            public double Smallest => sizes[0];

            public double Margin => margin;

            /// <summary>Глубина точки: снаружи — по карте (минус), внутри — точное расстояние до края.</summary>
            public double Depth(Point2D p)
            {
                double rough = region.Depth(p);
                return rough <= 0 ? rough : edges.Distance(p);
            }

            /// <summary>
            /// Самый крупный камень, который помещается целиком (с отступом от края). В волосяной линии,
            /// где не помещается даже самый мелкий, он всё равно ставится, если выступает за край не больше
            /// чем на четверть — иначе ряд рвался бы в каждом тонком месте буквы. 0 — камню здесь не место.
            /// </summary>
            public double SizeAt(Point2D p)
            {
                double depth = Depth(p);
                for (int i = sizes.Count - 1; i >= 0; i--)
                {
                    if (depth + 1e-6 >= sizes[i] / 2 + margin)
                    {
                        return sizes[i];
                    }
                }

                return depth >= sizes[0] / 4 ? sizes[0] : 0;
            }
        }

        /// <summary>Середина штриха, разбитая на мелкие шаги: точка, пройденная длина и камень в этой точке.</summary>
        private sealed class Track
        {
            private Track(List<Point2D> positions, bool closed, List<double> sizes)
            {
                Positions = positions;
                Closed = closed;
                Sizes = sizes;
                Lengths = new double[positions.Count];
                for (int i = 1; i < positions.Count; i++)
                {
                    Lengths[i] = Lengths[i - 1] + Point2D.Distance(positions[i - 1], positions[i]);
                }
            }

            public List<Point2D> Positions { get; }

            public List<double> Sizes { get; }

            public double[] Lengths { get; }

            public bool Closed { get; }

            public static Track Build(List<Point2D> line, bool closed, List<List<Point2D>> others, Sizer sizer, double step)
            {
                List<Point2D> points = ToRidge(line, closed, sizer, step);
                if (!closed && points.Count >= 2)
                {
                    // Свободные концы (не у развилки) тянем к кончику мазка, пока туда помещается мелкий камень.
                    double reach = sizer.Smallest;
                    if (IsFree(points[0], others, reach))
                    {
                        List<Point2D> head = Extend(points, fromStart: true, sizer, step);
                        head.Reverse();
                        points.InsertRange(0, head);
                    }

                    if (IsFree(points[points.Count - 1], others, reach))
                    {
                        points.AddRange(Extend(points, fromStart: false, sizer, step));
                    }
                }

                List<double> sizes = points.Select(sizer.SizeAt).ToList();
                if (closed)
                {
                    // Начало кольца — в самом тонком месте: там «шов» заметнее всего сгладится мелкими камнями.
                    int start = 0;
                    for (int i = 1; i < sizes.Count; i++)
                    {
                        if (sizes[i] > 0 && (sizes[start] == 0 || sizes[i] < sizes[start]))
                        {
                            start = i;
                        }
                    }

                    points = points.Skip(start).Concat(points.Take(start)).ToList();
                    sizes = sizes.Skip(start).Concat(sizes.Take(start)).ToList();
                }

                var track = new Track(points, closed, sizes);
                track.RemoveBumps();
                return track;
            }

            /// <summary>
            /// Середина от <see cref="Centerline"/> построена по сетке и сглажена, поэтому уходит от настоящей
            /// середины на десятые доли миллиметра, а у развилок — и на миллиметр. Для размера это важно: в
            /// полоске ровно под ss10 сдвиг на 0,1 мм уже не пускает ss10. Поэтому каждая точка сдвигается
            /// поперёк линии туда, где до края дальше всего, а сдвиги чуть сглаживаются, чтобы ряд не дрожал.
            /// </summary>
            private static List<Point2D> ToRidge(List<Point2D> points, bool closed, Sizer sizer, double step)
            {
                int n = points.Count;
                if (n < 3)
                {
                    return points;
                }

                int every = Math.Max(1, (int)Math.Round(0.25 / step));
                var keys = new List<int>();
                for (int i = 0; i < n; i += every)
                {
                    keys.Add(i);
                }

                if (keys[keys.Count - 1] != n - 1)
                {
                    keys.Add(n - 1);
                }

                Point2D NormalAt(int i)
                {
                    int a = closed ? (i - 4 + n) % n : Math.Max(0, i - 4);
                    int b = closed ? (i + 4) % n : Math.Min(n - 1, i + 4);
                    Point2D t = points[b] - points[a];
                    return t.Length < 1e-9 ? Point2D.Zero : new Point2D(-t.Y, t.X).Normalized();
                }

                double range = Math.Max(0.6, sizer.Smallest * 0.75);
                var normals = keys.Select(NormalAt).ToArray();
                var raw = keys.Select((i, k) => BestOffset(points[i], normals[k], range, sizer)).ToArray();
                var offsets = new double[raw.Length];
                for (int k = 0; k < raw.Length; k++)
                {
                    double sum = 0;
                    int used = 0;
                    for (int w = -2; w <= 2; w++)
                    {
                        int j = k + w;
                        if (closed)
                        {
                            j = ((j % raw.Length) + raw.Length) % raw.Length;
                        }
                        else if (j < 0 || j >= raw.Length)
                        {
                            continue;
                        }

                        sum += raw[j];
                        used++;
                    }

                    offsets[k] = sum / used;
                }

                var result = new List<Point2D>(n);
                for (int k = 0; k < keys.Count; k++)
                {
                    int from = keys[k];
                    int to = k + 1 < keys.Count ? keys[k + 1] : (closed ? n : from + 1);
                    Point2D shiftA = normals[k] * offsets[k];
                    int next = k + 1 < keys.Count ? k + 1 : 0;
                    Point2D shiftB = k + 1 < keys.Count || closed ? normals[next] * offsets[next] : shiftA;
                    for (int i = from; i < to && i < n; i++)
                    {
                        double t = to == from ? 0 : (double)(i - from) / (to - from);
                        result.Add(points[i] + Point2D.Lerp(shiftA, shiftB, t));
                    }
                }

                return result;
            }

            /// <summary>Сдвиг поперёк линии (в пределах <paramref name="range"/>), где до края дальше всего.</summary>
            private static double BestOffset(Point2D p, Point2D normal, double range, Sizer sizer)
            {
                if (normal == Point2D.Zero)
                {
                    return 0;
                }

                const int coarse = 8;
                double best = 0;
                double bestDepth = sizer.Depth(p);
                for (int s = -coarse; s <= coarse; s++)
                {
                    double o = range * s / coarse;
                    double depth = sizer.Depth(p + normal * o);
                    if (depth > bestDepth + 1e-9)
                    {
                        best = o;
                        bestDepth = depth;
                    }
                }

                double lo = best - range / coarse, hi = best + range / coarse;
                for (int i = 0; i < 12; i++)
                {
                    double m1 = lo + (hi - lo) / 3, m2 = hi - (hi - lo) / 3;
                    if (sizer.Depth(p + normal * m1) < sizer.Depth(p + normal * m2))
                    {
                        lo = m1;
                    }
                    else
                    {
                        hi = m2;
                    }
                }

                double refined = (lo + hi) / 2;
                return sizer.Depth(p + normal * refined) >= bestDepth ? refined : best;
            }

            private static bool IsFree(Point2D end, List<List<Point2D>> others, double reach)
            {
                foreach (List<Point2D> other in others)
                {
                    for (int i = 0; i + 1 < other.Count; i++)
                    {
                        if (DistanceToSegment(end, other[i], other[i + 1]) < reach)
                        {
                            return false;
                        }
                    }
                }

                return true;
            }

            /// <summary>Продолжение конца по касательной, пока там целиком помещается самый мелкий камень.</summary>
            private static List<Point2D> Extend(List<Point2D> points, bool fromStart, Sizer sizer, double step)
            {
                var added = new List<Point2D>();
                Point2D end = fromStart ? points[0] : points[points.Count - 1];

                // Направление — по отрезку длиной примерно в мелкий камень, чтобы дрожь линии не уводила в сторону.
                Point2D inner = end;
                double walked = 0;
                for (int i = 1; i < points.Count && walked < sizer.Smallest; i++)
                {
                    Point2D prev = inner;
                    inner = fromStart ? points[i] : points[points.Count - 1 - i];
                    walked += Point2D.Distance(prev, inner);
                }

                if (walked < 1e-6)
                {
                    return added;
                }

                Point2D dir = (end - inner).Normalized();
                double need = sizer.Smallest / 2 + sizer.Margin - 1e-9;
                double limit = sizer.Smallest * 4;
                for (double t = step; t <= limit; t += step)
                {
                    Point2D p = end + dir * t;
                    if (sizer.Depth(p) < need)
                    {
                        break;
                    }

                    added.Add(p);
                }

                return added;
            }

            /// <summary>
            /// Короткий «бугорок» — участок крупнее соседей с обеих сторон и короче самого камня — дал бы
            /// одинокий крупный камень среди мелких, как случайную бусину. Такой участок берёт размер соседей
            /// (меньший камень помещается всегда).
            /// </summary>
            private void RemoveBumps()
            {
                int n = Sizes.Count;
                bool changed = true;
                for (int round = 0; changed && round < 8; round++)
                {
                    changed = false;
                    int i = 0;
                    while (i < n)
                    {
                        int j = i;
                        while (j + 1 < n && Sizes[j + 1] == Sizes[i])
                        {
                            j++;
                        }

                        double size = Sizes[i];
                        double before = i > 0 ? Sizes[i - 1] : (Closed ? Sizes[n - 1] : -1);
                        double after = j < n - 1 ? Sizes[j + 1] : (Closed ? Sizes[0] : -1);
                        double length = Lengths[j] - Lengths[i];
                        bool bump = size > 0 && before >= 0 && after >= 0 && before < size && after < size && length < size;
                        if (bump && !(i == 0 && j == n - 1))
                        {
                            double lower = Math.Max(before, after);
                            for (int k = i; k <= j; k++)
                            {
                                Sizes[k] = lower;
                            }

                            changed = true;
                        }

                        i = j + 1;
                    }
                }
            }

            /// <summary>Номера точек, где встанут камни: по кускам, где камню есть место и он не задевает уже поставленные.</summary>
            public List<int> Place(StonePacker packer, double gap)
            {
                int n = Positions.Count;
                var free = new bool[n];
                bool all = true;
                for (int i = 0; i < n; i++)
                {
                    free[i] = Sizes[i] > 0 && packer.Fits(Positions[i], Sizes[i]);
                    all &= free[i];
                }

                var result = new List<int>();
                if (Closed && all && n > 2)
                {
                    result.AddRange(Spread(0, n - 1, gap, closed: true));
                    return result;
                }

                int start = 0;
                while (start < n)
                {
                    if (!free[start])
                    {
                        start++;
                        continue;
                    }

                    int end = start;
                    while (end + 1 < n && free[end + 1])
                    {
                        end++;
                    }

                    result.AddRange(Spread(start, end, gap, closed: false));
                    start = end + 1;
                }

                return result;
            }

            /// <summary>
            /// Камни от <paramref name="a"/> до <paramref name="b"/> с одинаковым просветом: зазор
            /// увеличивается, пока камней столько же, сколько при заданном, — так остаток длины
            /// расходится поровну, и последний камень встаёт у конца.
            /// </summary>
            private List<int> Spread(int a, int b, double gap, bool closed)
            {
                if (!closed)
                {
                    a = BestEnd(a, b, fromStart: true);
                    b = Math.Max(a, BestEnd(a, b, fromStart: false));
                }

                List<int> first = Greedy(a, b, gap, closed);
                if (first.Count <= 1)
                {
                    if (closed)
                    {
                        return first;
                    }

                    // Один камень на куске — посередине куска.
                    double middle = (Lengths[a] + Lengths[b]) / 2;
                    int best = a;
                    for (int k = a; k <= b; k++)
                    {
                        if (Math.Abs(Lengths[k] - middle) < Math.Abs(Lengths[best] - middle))
                        {
                            best = k;
                        }
                    }

                    return new List<int> { best };
                }

                double lastPiece = closed
                    ? Point2D.Distance(Positions[first[first.Count - 1]], Positions[first[0]])
                    : Lengths[b] - Lengths[first[first.Count - 1]];
                double lo = gap;
                double hi = gap + 2 * lastPiece / (first.Count - 1) + 1e-3;
                List<int> bestList = first;
                for (int iteration = 0; iteration < 16; iteration++)
                {
                    double mid = (lo + hi) / 2;
                    List<int> trial = Greedy(a, b, mid, closed);
                    if (trial.Count >= first.Count)
                    {
                        lo = mid;
                        bestList = trial;
                    }
                    else
                    {
                        hi = mid;
                    }
                }

                return bestList;
            }

            /// <summary>
            /// Крайний камень куска — тот, что краем подходит к концу ближе всех; если почти поровну — крупнее.
            /// У острого кончика это мелкий камень у самого острия, у тупого конца (полоска, перекладина Т) —
            /// крупный, который касается края, а не мелкий, после которого сразу идёт крупный.
            /// </summary>
            private int BestEnd(int a, int b, bool fromStart)
            {
                double window = 0;
                for (int k = a; k <= b; k++)
                {
                    window = Math.Max(window, Sizes[k]);
                }

                int origin = fromStart ? a : b;
                int dir = fromStart ? 1 : -1;
                double Lead(int k) => Math.Abs(Lengths[k] - Lengths[origin]) - Sizes[k] / 2;
                double bestLead = double.MaxValue;
                for (int k = origin; k >= a && k <= b && Math.Abs(Lengths[k] - Lengths[origin]) <= window; k += dir)
                {
                    bestLead = Math.Min(bestLead, Lead(k));
                }

                int best = origin;
                for (int k = origin; k >= a && k <= b && Math.Abs(Lengths[k] - Lengths[origin]) <= window; k += dir)
                {
                    if (Lead(k) <= bestLead + 0.12 && (Sizes[k] > Sizes[best] || (Sizes[k] == Sizes[best] && Lead(k) < Lead(best))))
                    {
                        best = k;
                    }
                }

                return best;
            }

            /// <summary>Следующий камень — в первой точке, где он с зазором не задевает предыдущий (с учётом размеров обоих).</summary>
            private List<int> Greedy(int a, int b, double gap, bool closed)
            {
                var list = new List<int> { a };
                int k = a;
                while (true)
                {
                    int next = -1;
                    for (int j = k + 1; j <= b; j++)
                    {
                        double need = (Sizes[k] + Sizes[j]) / 2 + gap;
                        if (DistanceSquared(Positions[k], Positions[j]) >= need * need - 1e-9)
                        {
                            next = j;
                            break;
                        }
                    }

                    if (next < 0)
                    {
                        break;
                    }

                    if (closed)
                    {
                        double close = (Sizes[next] + Sizes[a]) / 2 + gap;
                        if (DistanceSquared(Positions[next], Positions[a]) < close * close - 1e-9)
                        {
                            break;
                        }
                    }

                    list.Add(next);
                    k = next;
                }

                return list;
            }

            private static double DistanceSquared(Point2D p, Point2D q)
            {
                double dx = p.X - q.X, dy = p.Y - q.Y;
                return dx * dx + dy * dy;
            }
        }
    }
}
