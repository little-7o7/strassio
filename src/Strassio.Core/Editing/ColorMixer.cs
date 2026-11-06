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
            int common = Array.IndexOf(quota, quota.Max());

            // «Вбок» — обмен, который меняет только дальних соседей, а вплотную ничего не ухудшает.
            double sidewaysLimit = 3 * FarWeight * price.Max();
            var candidates = new List<int>();
            var sideways = new List<int>();
            int idle = 0;
            for (int pass = 0; pass < 150 && idle < 30; pass++)
            {
                int improved = 0;
                int touching = 0;
                Shuffle(order, rnd);
                foreach (int i in order)
                {
                    int a = colors[i];
                    if (a == common || !Touches(i, a, colors, near))
                    {
                        continue;
                    }

                    touching++;
                    candidates.Clear();
                    foreach ((int k, double w) in near[i])
                    {
                        candidates.Add(k);
                        if (w >= 1)
                        {
                            foreach ((int k2, _) in near[k])
                            {
                                candidates.Add(k2);
                            }
                        }
                    }

                    double now = Cost(i, a, colors, near, price);
                    int bestJ = -1;
                    double bestDelta = -1e-9;
                    sideways.Clear();
                    foreach (int j in candidates)
                    {
                        int b = colors[j];
                        if (b == a || j == i)
                        {
                            continue;
                        }

                        double wij = WeightBetween(near[i], j);
                        double delta = (Cost(i, b, colors, near, price) - wij * price[b]) - now
                            + (Cost(j, a, colors, near, price) - wij * price[a]) - Cost(j, b, colors, near, price);
                        if (delta < bestDelta)
                        {
                            bestDelta = delta;
                            bestJ = j;
                        }
                        else if (delta <= sidewaysLimit)
                        {
                            sideways.Add(j);
                        }
                    }

                    // Нет обмена, от которого лучше, — иногда меняемся «вбок» (не хуже): так стык двух
                    // ровных участков сдвигается, пока не встретит другой стык и оба не исчезнут.
                    if (bestJ < 0 && sideways.Count > 0 && rnd.Next(2) == 0)
                    {
                        bestJ = sideways[rnd.Next(sideways.Count)];
                    }
                    else if (bestJ >= 0)
                    {
                        improved++;
                    }

                    if (bestJ >= 0)
                    {
                        colors[i] = colors[bestJ];
                        colors[bestJ] = a;
                    }
                }

                if (touching == 0)
                {
                    break;
                }

                idle = improved == 0 ? idle + 1 : 0;
            }

            return colors;
        }

        private static bool Touches(int i, int color, int[] colors, List<(int Other, double Weight)>[] near)
        {
            foreach ((int j, double w) in near[i])
            {
                if (w >= 1 && colors[j] == color)
                {
                    return true;
                }
            }

            return false;
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

        private static double Cost(int i, int color, int[] colors, List<(int Other, double Weight)>[] near, double[] price)
        {
            double sum = 0;
            foreach ((int j, double w) in near[i])
            {
                if (colors[j] == color)
                {
                    sum += w;
                }
            }

            return sum * price[color];
        }

        private static double WeightBetween(List<(int Other, double Weight)> near, int j)
        {
            foreach ((int other, double w) in near)
            {
                if (other == j)
                {
                    return w;
                }
            }

            return 0;
        }

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
