using System;
using System.Collections.Generic;
using System.Linq;
using Strassio.Core.Geometry;

namespace Strassio.Core.Placement
{
    /// <summary>Куда «растворяется» заливка F14: где камни редеют.</summary>
    public enum FadeDirection
    {
        /// <summary>Плотно в середине, к краю формы реже.</summary>
        Edge,

        /// <summary>Плотно у края, к середине реже.</summary>
        Center,

        Right,
        Left,
        Up,
        Down,
    }

    /// <summary>
    /// Случайная раскладка с переменной плотностью (docs/SPEC.md: F14 «Растворение» и «Россыпь», раздел 8).
    /// Камни ставятся «диском Пуассона»: каждый новый — рядом с уже поставленным, но не ближе заданного
    /// просвета, а просвет плавно растёт туда, где рисунок должен редеть. Получается живая россыпь без
    /// наложений и без рядов. Номер варианта задаёт случайность: тот же номер — та же раскладка.
    /// </summary>
    public static class Scatter
    {
        /// <summary>Сколько попыток вокруг каждого камня (больше — плотнее, но дольше).</summary>
        private const int Attempts = 30;

        /// <summary>
        /// F14 «Растворение»: заливка формы, плотная с одной стороны и всё реже к другой.
        /// <paramref name="gapMm"/> — просвет в самом плотном месте, <paramref name="fadeGapMm"/> — в самом редком.
        /// </summary>
        public static List<PlacedStone> Dissolve(
            IReadOnlyList<Curve> contours, double diameterMm, double gapMm, double fadeGapMm, FadeDirection direction,
            double marginMm, int variant = 1)
        {
            var region = new ShapeRegion(contours, diameterMm);
            double r = diameterMm / 2;
            double gap = Math.Max(0, gapMm);
            double fade = Math.Max(gap, fadeGapMm);
            double inner = Math.Max(1e-6, region.MaxDepth - r - marginMm);
            Point2D min = region.Min, max = region.Max;
            double w = Math.Max(1e-6, max.X - min.X), h = Math.Max(1e-6, max.Y - min.Y);

            double Fade(Point2D p)
            {
                switch (direction)
                {
                    case FadeDirection.Center:
                        return Clamp((region.Depth(p) - r - marginMm) / inner);
                    case FadeDirection.Right:
                        return Clamp((p.X - min.X) / w);
                    case FadeDirection.Left:
                        return Clamp((max.X - p.X) / w);
                    case FadeDirection.Up:
                        return Clamp((p.Y - min.Y) / h);
                    case FadeDirection.Down:
                        return Clamp((max.Y - p.Y) / h);
                    default:
                        return 1 - Clamp((region.Depth(p) - r - marginMm) / inner);
                }
            }

            double Gap(Point2D p) => gap + (fade - gap) * Ease(Fade(p));
            return FadingHoneycomb(region, diameterMm, gap, Gap, marginMm, variant);
        }

        /// <summary>
        /// «Россыпь» (брызги) вокруг дизайна: случайные камни снаружи формы <paramref name="shapes"/> и страз
        /// <paramref name="design"/> на ширину <paramref name="widthMm"/>. У дизайна — плотно и крупно
        /// (первый размер <paramref name="sizes"/>), дальше — реже и мельче (последний размер).
        /// Камни не налезают на <paramref name="design"/> и <paramref name="obstacles"/>.
        /// </summary>
        public static List<PlacedStone> Splash(
            IReadOnlyList<Curve> shapes, IReadOnlyList<PlacedStone> design, IReadOnlyList<PlacedStone> obstacles,
            IReadOnlyList<double> sizes, double gapMm, double widthMm, double fadeGapMm, int variant = 1)
        {
            if (sizes.Count == 0 || widthMm <= 0 || (shapes.Count == 0 && design.Count == 0))
            {
                return new List<PlacedStone>();
            }

            double gap = Math.Max(0, gapMm);
            double fade = Math.Max(gap, fadeGapMm);
            double biggest = sizes.Max();
            var closed = shapes.Select(c => CurveFlattener.Flatten(c)).Where(f => f.IsClosed && f.TotalLength > 1e-6).ToList();
            SignedDistanceField? field = closed.Count == 0 ? null
                : SignedDistanceField.Build(closed, Math.Max(0.1, sizes.Min() / 4), paddingMm: widthMm + biggest, union: true);

            var stoneIndex = new StoneIndex(design, widthMm + biggest);
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (Point2D p in design.Select(s => s.Center).Concat(closed.SelectMany(f => f.Points.Select(q => q.Position))))
            {
                minX = Math.Min(minX, p.X);
                minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X);
                maxY = Math.Max(maxY, p.Y);
            }

            double pad = widthMm + biggest;
            var min = new Point2D(minX - pad, minY - pad);
            var max = new Point2D(maxX + pad, maxY + pad);

            // Расстояние от точки до дизайна: до края формы снаружи и до края ближайшей стразы; внутри формы — null.
            double? Distance(Point2D p)
            {
                double best = double.MaxValue;
                if (field != null)
                {
                    double v = field.ValueAt(p);
                    if (v > 0)
                    {
                        return null;
                    }

                    if (!double.IsNegativeInfinity(v))
                    {
                        best = -v;
                    }
                }

                return Math.Min(best, stoneIndex.EdgeDistance(p));
            }

            double Fade(Point2D p)
            {
                double? d = Distance(p);
                return d == null ? 1 : Clamp(d.Value / widthMm);
            }

            Spot? Rule(Point2D p)
            {
                double? dist = Distance(p);
                if (dist == null || dist.Value > widthMm)
                {
                    return null;
                }

                double t = Clamp(dist.Value / widthMm);
                int index = Math.Min(sizes.Count - 1, (int)Math.Floor(t * sizes.Count));
                double d = sizes[index];
                double g = gap + (fade - gap) * Ease(t);

                // Камень целиком снаружи формы: край камня не ближе просвета.
                return dist.Value < d / 2 + gap ? (Spot?)null : new Spot(d, g);
            }

            return Grow(min, max, biggest + fade, Rule, Fade, design.Concat(obstacles).ToList(), variant);
        }

        /// <summary>Плавное нарастание: сначала медленно (плотная часть шире), к концу быстрее.</summary>
        private static double Ease(double t) => t * t * (1.6 - 0.6 * t);

        private static double Clamp(double t) => t < 0 ? 0 : t > 1 ? 1 : t;

        /// <summary>Какой камень можно поставить в точке и какой просвет он требует от соседей.</summary>
        private readonly struct Spot
        {
            public Spot(double diameter, double gap)
            {
                Diameter = diameter;
                Gap = gap;
            }

            public double Diameter { get; }

            public double Gap { get; }
        }

        /// <summary>
        /// Диск Пуассона с переменным просветом. Начинает с самого «плотного» места и растёт: вокруг случайного
        /// уже поставленного камня пробует точки чуть дальше «вплотную с просветом». Когда рост кончился,
        /// начинает заново с ещё не покрытого места (другая часть формы).
        /// </summary>
        private static List<PlacedStone> Grow(
            Point2D min, Point2D max, double reach, Func<Point2D, Spot?> rule, Func<Point2D, double> fade,
            IReadOnlyList<PlacedStone> obstacles, int variant)
        {
            var random = new Random(unchecked(variant * 7919 + 17));
            double cell = Math.Max(0.2, reach);
            var hash = new Dictionary<(int, int), List<int>>();
            var all = new List<(Point2D P, double D, double G)>();
            var result = new List<PlacedStone>();

            (int, int) Key(Point2D p) => ((int)Math.Floor(p.X / cell), (int)Math.Floor(p.Y / cell));

            void Add(Point2D p, double d, double g)
            {
                all.Add((p, d, g));
                (int, int) key = Key(p);
                if (!hash.TryGetValue(key, out List<int>? list))
                {
                    list = new List<int>();
                    hash[key] = list;
                }

                list.Add(all.Count - 1);
            }

            bool Free(Point2D p, double d, double g)
            {
                (int cx, int cy) = Key(p);
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (!hash.TryGetValue((cx + dx, cy + dy), out List<int>? list))
                        {
                            continue;
                        }

                        foreach (int i in list)
                        {
                            (Point2D q, double dq, double gq) = all[i];
                            // Препятствие (gq < 0) — уже готовые стразы: от них весь просвет нового камня.
                            double need = (d + dq) / 2 + (gq < 0 ? g : (g + gq) / 2);
                            if ((p - q).Length < need - 1e-9)
                            {
                                return false;
                            }
                        }
                    }
                }

                return true;
            }

            foreach (PlacedStone o in obstacles)
            {
                Add(o.Center, o.DiameterMm, -1);
            }

            int fixedCount = all.Count;

            // Затравки — узлы редкой сетки, от самых плотных мест к редким.
            double seedStep = Math.Max(0.5, cell / 2);
            var seeds = new List<(Point2D P, double T)>();
            for (double x = min.X; x <= max.X; x += seedStep)
            {
                for (double y = min.Y; y <= max.Y; y += seedStep)
                {
                    var p = new Point2D(x + seedStep / 2, y + seedStep / 2);
                    if (rule(p) != null)
                    {
                        seeds.Add((p, fade(p)));
                    }
                }
            }

            var active = new List<int>();
            foreach ((Point2D seed, double _) in seeds.OrderBy(s => s.T).ThenBy(s => s.P.X).ThenBy(s => s.P.Y))
            {
                Spot? first = rule(seed);
                if (first == null || !Free(seed, first.Value.Diameter, first.Value.Gap))
                {
                    continue;
                }

                Add(seed, first.Value.Diameter, first.Value.Gap);
                active.Add(all.Count - 1);
                while (active.Count > 0)
                {
                    int pick = random.Next(active.Count);
                    (Point2D a, double da, double ga) = all[active[pick]];
                    bool placed = false;
                    for (int k = 0; k < Attempts && !placed; k++)
                    {
                        double angle = random.NextDouble() * 2 * Math.PI;
                        double radius = (da + ga) * (1 + 0.35 * random.NextDouble());
                        var p = new Point2D(a.X + radius * Math.Cos(angle), a.Y + radius * Math.Sin(angle));
                        Spot? spot = rule(p);
                        if (spot != null && Free(p, spot.Value.Diameter, spot.Value.Gap))
                        {
                            Add(p, spot.Value.Diameter, spot.Value.Gap);
                            active.Add(all.Count - 1);
                            placed = true;
                        }
                    }

                    if (!placed)
                    {
                        active.RemoveAt(pick);
                    }
                }
            }

            for (int i = fixedCount; i < all.Count; i++)
            {
                result.Add(new PlacedStone(all[i].P, all[i].D, false));
            }

            return result;
        }

        /// <summary>
        /// «Тающие соты»: узлы сот с шагом «камень + зазор» внутри формы. Сколько узлов оставить, решает
        /// местная плотность (шаг / (камень + местный просвет))², а какие именно — рассеивание ошибки по
        /// соседним узлам, как в полутоновой печати: где просвет равен зазору — ровные соты целиком, дальше
        /// камни выпадают поодиночке, ровно распределяясь, без кучек и дыр. Потом камни в редкой части чуть
        /// сдвигаются в случайную сторону, чтобы не читалась сетка.
        /// </summary>
        private static List<PlacedStone> FadingHoneycomb(
            ShapeRegion region, double d, double gap, Func<Point2D, double> gapAt, double marginMm, int variant)
        {
            var random = new Random(unchecked(variant * 7919 + 17));
            double r = d / 2;
            double step = d + gap;
            double rowH = step * Math.Sqrt(3) / 2;
            Point2D c = region.DeepestPoint;
            int iMin = (int)Math.Floor((region.Min.X - c.X) / step) - 1, iMax = (int)Math.Ceiling((region.Max.X - c.X) / step) + 1;
            int jMin = (int)Math.Floor((region.Min.Y - c.Y) / rowH) - 1, jMax = (int)Math.Ceiling((region.Max.Y - c.Y) / rowH) + 1;
            int nx = iMax - iMin + 1, ny = jMax - jMin + 1;
            var error = new double[nx + 2, ny + 2];

            Point2D Site(int i, int j) => new Point2D(c.X + i * step + ((j & 1) == 0 ? 0 : step / 2), c.Y + j * rowH);

            var kept = new List<(Point2D P, double G)>();
            for (int j = jMin; j <= jMax; j++)
            {
                bool forward = ((j - jMin) & 1) == 0;
                for (int n = 0; n < nx; n++)
                {
                    int i = forward ? iMin + n : iMax - n;
                    Point2D p = Site(i, j);
                    if (!region.Fits(p, r, marginMm))
                    {
                        continue;
                    }

                    double g = gapAt(p);
                    double density = Math.Min(1, step * step / ((d + g) * (d + g)));
                    int xi = i - iMin + 1, yj = j - jMin + 1;
                    double value = density + error[xi, yj];

                    // Чуть случайный порог — без него на ровной плотности проступают «червячки».
                    double threshold = 0.5 + (density < 0.999 ? 0.16 * (random.NextDouble() - 0.5) : 0);
                    bool keep = value >= threshold;
                    if (keep)
                    {
                        kept.Add((p, g));
                    }

                    double e = value - (keep ? 1 : 0);

                    // Половина ошибки — следующему узлу в ряду, по четверти — двум соседям в следующем ряду.
                    error[xi + (forward ? 1 : -1), yj] += e / 2;
                    int left = (j & 1) == 0 ? xi - 1 : xi;
                    error[left, yj + 1] += e / 4;
                    error[left + 1, yj + 1] += e / 4;
                }
            }

            double maxGap = kept.Count == 0 ? gap : kept.Max(k => k.G);
            var grid = new PointHash(d + maxGap);
            var centers = kept.Select(k => k.P).ToArray();
            for (int i = 0; i < centers.Length; i++)
            {
                grid.Add(i, centers[i]);
            }

            // Сдвиг в редкой части: не дальше половины «лишнего» просвета, не ближе к соседям, чем зазор
            // плюс половина местного запаса, и не за край формы.
            for (int i = 0; i < centers.Length; i++)
            {
                double slack = kept[i].G - gap;
                if (slack < 0.1)
                {
                    continue;
                }

                for (int attempt = 0; attempt < 8; attempt++)
                {
                    double a = random.NextDouble() * 2 * Math.PI;
                    double len = Math.Min(step, 0.5 * slack) * Math.Sqrt(random.NextDouble());
                    var p = new Point2D(centers[i].X + len * Math.Cos(a), centers[i].Y + len * Math.Sin(a));
                    if (!region.Fits(p, r, marginMm))
                    {
                        continue;
                    }

                    bool ok = true;
                    foreach (int k in grid.Near(p))
                    {
                        if (k == i)
                        {
                            continue;
                        }

                        double need = d + gap + 0.5 * Math.Max(0, (kept[i].G + kept[k].G) / 2 - gap);
                        if ((p - centers[k]).Length < Math.Min(need, (centers[i] - centers[k]).Length) - 1e-6)
                        {
                            ok = false;
                            break;
                        }
                    }

                    if (ok)
                    {
                        grid.Move(i, centers[i], p);
                        centers[i] = p;
                        break;
                    }
                }
            }

            return centers.Select(p => new PlacedStone(p, d, false)).ToList();
        }

        /// <summary>Номера точек по клеткам: соседи ищутся в 3×3 клетках.</summary>
        private sealed class PointHash
        {
            private readonly double cell;
            private readonly Dictionary<(int, int), List<int>> cells = new Dictionary<(int, int), List<int>>();

            public PointHash(double cell)
            {
                this.cell = Math.Max(0.1, cell);
            }

            private (int, int) Key(Point2D p) => ((int)Math.Floor(p.X / cell), (int)Math.Floor(p.Y / cell));

            public void Add(int i, Point2D p)
            {
                (int, int) key = Key(p);
                if (!cells.TryGetValue(key, out List<int>? list))
                {
                    list = new List<int>();
                    cells[key] = list;
                }

                list.Add(i);
            }

            public void Move(int i, Point2D from, Point2D to)
            {
                if (Key(from) != Key(to))
                {
                    cells[Key(from)].Remove(i);
                    Add(i, to);
                }
            }

            public IEnumerable<int> Near(Point2D p)
            {
                (int cx, int cy) = Key(p);
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (cells.TryGetValue((cx + dx, cy + dy), out List<int>? list))
                        {
                            foreach (int i in list)
                            {
                                yield return i;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>Стразы дизайна по клеткам: расстояние от точки до края ближайшей (в пределах reach).</summary>
        private sealed class StoneIndex
        {
            private readonly double cell;
            private readonly double reach;
            private readonly Dictionary<(int, int), List<PlacedStone>> cells = new Dictionary<(int, int), List<PlacedStone>>();

            public StoneIndex(IReadOnlyList<PlacedStone> stones, double reach)
            {
                this.reach = reach;
                cell = Math.Max(1, reach / 2);
                foreach (PlacedStone s in stones)
                {
                    (int, int) key = Key(s.Center);
                    if (!cells.TryGetValue(key, out List<PlacedStone>? list))
                    {
                        list = new List<PlacedStone>();
                        cells[key] = list;
                    }

                    list.Add(s);
                }
            }

            private (int, int) Key(Point2D p) => ((int)Math.Floor(p.X / cell), (int)Math.Floor(p.Y / cell));

            public double EdgeDistance(Point2D p)
            {
                if (cells.Count == 0)
                {
                    return double.MaxValue;
                }

                int r = (int)Math.Ceiling(reach / cell);
                (int cx, int cy) = Key(p);
                double best = double.MaxValue;
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dy = -r; dy <= r; dy++)
                    {
                        if (!cells.TryGetValue((cx + dx, cy + dy), out List<PlacedStone>? list))
                        {
                            continue;
                        }

                        foreach (PlacedStone s in list)
                        {
                            best = Math.Min(best, (s.Center - p).Length - s.DiameterMm / 2);
                        }
                    }
                }

                return best;
            }
        }
    }
}
