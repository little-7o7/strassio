using System;
using System.Collections.Generic;
using System.Linq;
using Strassio.Core.Geometry;

namespace Strassio.Core.Editing
{
    /// <summary>
    /// Смешение цветов (docs/SPEC.md, раздел 3.4): какой цвет получит каждая страза.
    /// «Случайно» — доли в процентах соблюдаются точно (70% из 100 страз — ровно 70), а стразы
    /// выбираются вперемешку; «равномерно» — доли те же, но одинаковые камни по возможности не
    /// соседствуют; «по очереди» — шаблон вдоль линии, например 2 и 1 → К-К-Б-К-К-Б.
    /// </summary>
    public static class ColorMixer
    {
        /// <summary>
        /// Случайное смешение: номер цвета для каждой из <paramref name="count"/> страз по долям
        /// <paramref name="weights"/> (например 70 и 30). <paramref name="seed"/> — тот же номер даёт ту же раскладку.
        /// </summary>
        public static int[] Random(int count, IReadOnlyList<double> weights, int seed)
        {
            int[] quota = Quotas(count, weights);
            var result = new List<int>(count);
            for (int c = 0; c < quota.Length; c++)
            {
                result.AddRange(Enumerable.Repeat(c, quota[c]));
            }

            // Перемешивание Фишера–Йетса.
            var rnd = new Random(seed);
            for (int i = result.Count - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                (result[i], result[j]) = (result[j], result[i]);
            }

            return result.ToArray();
        }

        /// <summary>
        /// Равномерное смешение: доли соблюдаются так же точно, как в <see cref="Random"/>, но камни одного
        /// цвета по возможности не стоят рядом — нет кучек из трёх золотых, мерцание ровное. Особенно
        /// разводятся редкие цвета: соседство двух камней цвета с долей 30% «дороже», чем двух из 70%.
        /// </summary>
        /// <param name="centers">Центры страз, мм.</param>
        /// <param name="diameters">Диаметры страз, мм.</param>
        public static int[] Even(IReadOnlyList<Point2D> centers, IReadOnlyList<double> diameters, IReadOnlyList<double> weights, int seed)
        {
            int n = centers.Count;
            int[] quota = Quotas(n, weights);
            if (n < 3 || quota.Count(q => q > 0) < 2)
            {
                return Random(n, weights, seed);
            }

            List<(int Other, double Weight)>[] near = Neighbours(centers, diameters);

            // «Цена» соседства одного цвета: чем реже цвет, тем дороже ему стоять рядом с таким же.
            var price = new double[quota.Length];
            for (int c = 0; c < quota.Length; c++)
            {
                price[c] = quota[c] > 0 ? (double)n / quota[c] : 0;
            }

            var rnd = new Random(seed);
            int[] order = Enumerable.Range(0, n).ToArray();
            Shuffle(order, rnd);

            // 1. Раскладка по порядку страз (вдоль линии он и есть порядок по линии): каждый следующий
            //    камень берёт цвет, которому больше всех «недодали» до его доли. Доли выходят точно, а на
            //    линии 50/50 получается ровное чередование. Начальный «долг» случайный — раскладка каждый
            //    раз другая.
            int[] colors = new int[n];
            var left = (int[])quota.Clone();
            var debt = new double[quota.Length];
            for (int c = 0; c < quota.Length; c++)
            {
                debt[c] = rnd.NextDouble() * quota[c] / n;
            }

            for (int i = 0; i < n; i++)
            {
                int best = -1;
                for (int c = 0; c < quota.Length; c++)
                {
                    debt[c] += (double)quota[c] / n;
                    if (left[c] > 0 && (best < 0 || debt[c] > debt[best]))
                    {
                        best = c;
                    }
                }

                colors[i] = best;
                left[best]--;
                debt[best] -= 1;
            }

            // 2. Обмены с камнями поблизости (обмен сохраняет доли точно): камень, у которого вплотную
            //    стоит такой же, меняется цветом с соседом, если от этого всем вместе лучше. Самый
            //    частый цвет не перебираем: его соседство дешёвое, а обмен с ним видно со стороны редкого.
            Spread(colors, near, price, Array.IndexOf(quota, quota.Max()), sideways: true, rnd, order, share: null);
            return colors;
        }

        /// <summary>Направление омбре.</summary>
        public enum OmbreDirection
        {
            /// <summary>Слева направо.</summary>
            LeftToRight,

            /// <summary>Сверху вниз (в координатах CorelDRAW: Y растёт вверх).</summary>
            TopToBottom,

            /// <summary>От центра рисунка к краю.</summary>
            CenterOut,

            /// <summary>Вдоль линии — по порядку страз (у страз по линии порядок и есть путь по линии).</summary>
            AlongLine,
        }

        /// <summary>
        /// Омбре: <paramref name="colorCount"/> цветов (2–3) плавно перетекают друг в друга — первый в начале,
        /// последний в конце. Доля следующего цвета растёт по рисунку, а камни раскладываются так же ровно,
        /// как в <see cref="Even"/>: без кучек, переход мягкий, а не полосой.
        /// </summary>
        public static int[] Ombre(
            IReadOnlyList<Point2D> centers, IReadOnlyList<double> diameters, int colorCount, OmbreDirection direction, int seed)
        {
            int n = centers.Count;
            var colors = new int[n];
            if (n == 0 || colorCount < 2)
            {
                return colors;
            }

            double[] t = Positions(centers, direction);

            // Доля каждого цвета у камня: цвета стоят на равных расстояниях вдоль 0…1, между соседними —
            // линейный переход (у трёх цветов: 0 — первый, 0,5 — второй, 1 — третий).
            var share = new double[n, colorCount];
            var expected = new double[colorCount];
            for (int i = 0; i < n; i++)
            {
                double x = t[i] * (colorCount - 1);
                int lo = Math.Min(colorCount - 2, (int)Math.Floor(x));
                double f = x - lo;
                share[i, lo] = 1 - f;
                share[i, lo + 1] = f;
                expected[lo] += 1 - f;
                expected[lo + 1] += f;
            }

            // 1. Раскладка от начала к концу перехода: каждый камень берёт цвет, которому больше всех
            //    «недодали» до его доли в этом месте. Камни с одинаковым положением — в случайном порядке.
            var rnd = new Random(seed);
            int[] order = Enumerable.Range(0, n).ToArray();
            Shuffle(order, rnd);
            Array.Sort(order, (a, b) => t[a].CompareTo(t[b]));
            var debt = new double[colorCount];
            for (int c = 0; c < colorCount; c++)
            {
                debt[c] = rnd.NextDouble() * 0.5;
            }

            foreach (int i in order)
            {
                int best = 0;
                for (int c = 0; c < colorCount; c++)
                {
                    debt[c] += share[i, c];
                    if (debt[c] > debt[best])
                    {
                        best = c;
                    }
                }

                colors[i] = best;
                debt[best] -= 1;
            }

            // 2. Обмены только с ближними соседями и только к лучшему — переход не размывается, а кучки
            //    одного цвета посреди перехода расходятся.
            var price = new double[colorCount];
            for (int c = 0; c < colorCount; c++)
            {
                price[c] = n / Math.Max(1.0, expected[c]);
            }

            Spread(colors, Neighbours(centers, diameters), price, common: -1, sideways: false, rnd, order, share);
            return colors;
        }

        /// <summary>Положение камня вдоль перехода, 0…1.</summary>
        private static double[] Positions(IReadOnlyList<Point2D> centers, OmbreDirection direction)
        {
            int n = centers.Count;
            var t = new double[n];
            if (direction == OmbreDirection.AlongLine)
            {
                for (int i = 0; i < n; i++)
                {
                    t[i] = n > 1 ? (double)i / (n - 1) : 0;
                }

                return t;
            }

            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            double cx = 0, cy = 0;
            foreach (Point2D p in centers)
            {
                minX = Math.Min(minX, p.X);
                maxX = Math.Max(maxX, p.X);
                minY = Math.Min(minY, p.Y);
                maxY = Math.Max(maxY, p.Y);
                cx += p.X / n;
                cy += p.Y / n;
            }

            double maxR = 0;
            if (direction == OmbreDirection.CenterOut)
            {
                foreach (Point2D p in centers)
                {
                    maxR = Math.Max(maxR, Point2D.Distance(p, new Point2D(cx, cy)));
                }
            }

            for (int i = 0; i < n; i++)
            {
                Point2D p = centers[i];
                switch (direction)
                {
                    case OmbreDirection.LeftToRight:
                        t[i] = maxX > minX ? (p.X - minX) / (maxX - minX) : 0;
                        break;
                    case OmbreDirection.TopToBottom:
                        t[i] = maxY > minY ? (maxY - p.Y) / (maxY - minY) : 0;
                        break;
                    default:
                        t[i] = maxR > 0 ? Point2D.Distance(p, new Point2D(cx, cy)) / maxR : 0;
                        break;
                }
            }

            return t;
        }

        /// <summary>
        /// Обмены цветами с камнями поблизости, пока одинаковые камни стоят вплотную и от обмена всем
        /// вместе лучше. Доли не меняются. <paramref name="common"/> — цвет, который не перебираем (−1 — все).
        /// С <paramref name="sideways"/> иногда меняемся и «вбок» (не хуже): стык двух ровных участков
        /// сдвигается, пока не встретит другой стык и оба не исчезнут. <paramref name="share"/> (для омбре) — какая
        /// доля каждого цвета должна быть у камня: туда, где цвета быть не должно, он не переезжает, и обмен
        /// против перехода «стоит» дороже.
        /// </summary>
        private static void Spread(
            int[] colors, List<(int Other, double Weight)>[] near, double[] price, int common, bool sideways, Random rnd, int[] order,
            double[,]? share)
        {
            int n = colors.Length;
            int k = price.Length;
            double fidelity = price.Min();

            // Сколько «соседства» каждого цвета вокруг камня (вплотную — 1, дальше — чуть-чуть) и сколько
            // соседей вплотную каждого цвета. Обмен пересчитывает только соседей двух камней.
            var around = new double[n * k];
            var touching = new int[n * k];
            for (int i = 0; i < n; i++)
            {
                foreach ((int j, double w) in near[i])
                {
                    around[i * k + colors[j]] += w;
                    if (w >= 1)
                    {
                        touching[i * k + colors[j]]++;
                    }
                }
            }

            // «Вбок» — обмен, который меняет только дальних соседей, а вплотную ничего не ухудшает.
            double sidewaysLimit = 3 * FarWeight * price.Max();
            var sidewaysMoves = new List<int>();
            int idle = 0;
            int patience = sideways ? 30 : 1;
            for (int pass = 0; pass < 150 && idle < patience; pass++)
            {
                int improved = 0;
                int touchingCount = 0;
                Shuffle(order, rnd);
                foreach (int i in order)
                {
                    int a = colors[i];
                    if (a == common || touching[i * k + a] == 0)
                    {
                        continue;
                    }

                    touchingCount++;
                    double now = around[i * k + a] * price[a];
                    int bestJ = -1;
                    double bestDelta = -1e-9;
                    sidewaysMoves.Clear();
                    foreach ((int j, double wij) in near[i])
                    {
                        int b = colors[j];
                        if (b == a || (share != null && (share[i, b] <= 0 || share[j, a] <= 0)))
                        {
                            continue;
                        }

                        // После обмена у i цвет b, а j уже не b; у j цвет a, а i уже не a.
                        double delta = (around[i * k + b] - wij) * price[b] - now
                            + (around[j * k + a] - wij) * price[a] - around[j * k + b] * price[b];
                        if (share != null)
                        {
                            delta += fidelity * (share[i, a] - share[i, b] + share[j, b] - share[j, a]);
                        }

                        if (delta < bestDelta)
                        {
                            bestDelta = delta;
                            bestJ = j;
                        }
                        else if (delta <= sidewaysLimit)
                        {
                            sidewaysMoves.Add(j);
                        }
                    }

                    if (bestJ < 0 && sideways && sidewaysMoves.Count > 0 && rnd.Next(2) == 0)
                    {
                        bestJ = sidewaysMoves[rnd.Next(sidewaysMoves.Count)];
                    }
                    else if (bestJ >= 0)
                    {
                        improved++;
                    }

                    if (bestJ >= 0)
                    {
                        int b = colors[bestJ];
                        Recolor(i, a, b, near, around, touching, k);
                        Recolor(bestJ, b, a, near, around, touching, k);
                        colors[i] = b;
                        colors[bestJ] = a;
                    }
                }

                if (touchingCount == 0)
                {
                    break;
                }

                idle = improved == 0 ? idle + 1 : 0;
            }
        }

        /// <summary>Камень <paramref name="i"/> меняет цвет с <paramref name="from"/> на <paramref name="to"/>: соседи пересчитывают окружение.</summary>
        private static void Recolor(int i, int from, int to, List<(int Other, double Weight)>[] near, double[] around, int[] touching, int k)
        {
            foreach ((int j, double w) in near[i])
            {
                around[j * k + from] -= w;
                around[j * k + to] += w;
                if (w >= 1)
                {
                    touching[j * k + from]--;
                    touching[j * k + to]++;
                }
            }
        }

        /// <summary>Сколько раз одинаковые камни стоят вплотную (для проверки и подсказки).</summary>
        public static int SameColorTouches(IReadOnlyList<Point2D> centers, IReadOnlyList<double> diameters, IReadOnlyList<int> colors, int color)
        {
            List<(int Other, double Weight)>[] near = Neighbours(centers, diameters);
            int touches = 0;
            for (int i = 0; i < near.Length; i++)
            {
                if (colors[i] != color)
                {
                    continue;
                }

                foreach ((int j, double w) in near[i])
                {
                    if (j > i && w >= 1 && colors[j] == color)
                    {
                        touches++;
                    }
                }
            }

            return touches;
        }

        private const double FarWeight = 0.02;

        /// <summary>
        /// Соседи: вплотную (просвет до половины камня) — вес 1; через одного (до полутора камней) — чуть-чуть,
        /// только чтобы при прочих равных цвет расходился шире. Больше нельзя: в самой ровной раскладке
        /// 30% (каждый третий камень в сотах) редкий цвет как раз стоит «через одного» от такого же.
        /// </summary>
        private static List<(int Other, double Weight)>[] Neighbours(IReadOnlyList<Point2D> centers, IReadOnlyList<double> diameters)
        {
            int n = centers.Count;
            double maxD = 0;
            for (int i = 0; i < n; i++)
            {
                maxD = Math.Max(maxD, diameters[i]);
            }

            var grid = new StoneGrid(Math.Max(0.5, 2.5 * maxD));
            for (int i = 0; i < n; i++)
            {
                grid.Add(centers[i], diameters[i] / 2);
            }

            var near = new List<(int, double)>[n];
            for (int i = 0; i < n; i++)
            {
                near[i] = new List<(int, double)>();
                foreach (int j in grid.Near(centers[i]))
                {
                    if (j == i)
                    {
                        continue;
                    }

                    double small = Math.Min(diameters[i], diameters[j]);
                    double gap = Point2D.Distance(centers[i], centers[j]) - (diameters[i] + diameters[j]) / 2;
                    if (gap <= 0.5 * small)
                    {
                        near[i].Add((j, 1.0));
                    }
                    else if (gap <= 1.5 * small)
                    {
                        near[i].Add((j, FarWeight));
                    }
                }
            }

            return near;
        }

        private static void Shuffle(int[] items, Random rnd)
        {
            for (int i = items.Length - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                (items[i], items[j]) = (items[j], items[i]);
            }
        }

        /// <summary>По очереди: цвет c повторяется <paramref name="runs"/>[c] раз подряд, и так по кругу.</summary>
        public static int[] Sequence(int count, IReadOnlyList<int> runs)
        {
            var pattern = new List<int>();
            for (int c = 0; c < runs.Count; c++)
            {
                pattern.AddRange(Enumerable.Repeat(c, Math.Max(0, runs[c])));
            }

            if (pattern.Count == 0)
            {
                pattern.Add(0);
            }

            return Enumerable.Range(0, count).Select(i => pattern[i % pattern.Count]).ToArray();
        }

        /// <summary>Сколько страз каждого цвета: доли, округлённые так, чтобы в сумме вышло ровно <paramref name="count"/>.</summary>
        public static int[] Quotas(int count, IReadOnlyList<double> weights)
        {
            double total = weights.Where(w => w > 0).Sum();
            var quota = new int[weights.Count];
            if (total <= 0 || count <= 0)
            {
                if (weights.Count > 0 && count > 0)
                {
                    quota[0] = count;
                }

                return quota;
            }

            var remainders = new List<(int Index, double Fraction)>();
            int assigned = 0;
            for (int c = 0; c < weights.Count; c++)
            {
                double exact = count * Math.Max(0, weights[c]) / total;
                quota[c] = (int)Math.Floor(exact);
                assigned += quota[c];
                remainders.Add((c, exact - quota[c]));
            }

            // Остаток раздаём тем, у кого дробная часть больше (метод наибольших остатков).
            foreach ((int index, _) in remainders.OrderByDescending(r => r.Fraction).ThenBy(r => r.Index).Take(count - assigned))
            {
                quota[index]++;
            }

            return quota;
        }
    }
}
