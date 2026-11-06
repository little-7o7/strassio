using System;
using System.Collections.Generic;
using Strassio.Core.Geometry;

namespace Strassio.Core.Editing
{
    /// <summary>Итог «Выровнять просветы»: куда сдвинуть стразы и насколько ровнее стало.</summary>
    public sealed class GapEvenResult
    {
        public GapEvenResult(EditResult edit, IReadOnlyList<int> unevenBefore, IReadOnlyList<int> unevenAfter, double worstBeforeMm, double worstAfterMm)
        {
            Edit = edit;
            UnevenBefore = unevenBefore;
            UnevenAfter = unevenAfter;
            WorstBeforeMm = worstBeforeMm;
            WorstAfterMm = worstAfterMm;
        }

        /// <summary>Сдвиги (удалений нет).</summary>
        public EditResult Edit { get; }

        /// <summary>Стразы, у которых просветы по разные стороны в ряду отличались больше допуска.</summary>
        public IReadOnlyList<int> UnevenBefore { get; }

        /// <summary>Такие же — после выравнивания (им не хватило места или предела сдвига).</summary>
        public IReadOnlyList<int> UnevenAfter { get; }

        /// <summary>Самая большая разница просветов по разные стороны от стразы, мм.</summary>
        public double WorstBeforeMm { get; }

        public double WorstAfterMm { get; }
    }

    /// <summary>
    /// «Выровнять просветы» (docs/SPEC.md, 7.2): после удаления наложений и ручной правки один просвет
    /// бывает 0,2 мм, а соседний 1,5 мм. Для каждой стразы ищутся соседи по ряду — пары, лежащие с
    /// разных сторон почти на одной прямой. Страза сдвигается вдоль этой прямой (ряд остаётся на своей
    /// линии) так, чтобы просветы с обеих сторон сравнялись. Сдвиг не больше предела, закреплённые не
    /// двигаются, концы линий и углы (у них нет пары с другой стороны) — тоже, а просвет между стразами
    /// не становится меньше заданного (или того, что было, если уже было меньше).
    /// </summary>
    public static class GapEvener
    {
        /// <summary>Соседи «по ряду» — с разных сторон, угол между ними не меньше 160°.</summary>
        private const double StraightCos = -0.94;

        private const int Iterations = 80;

        /// <summary>Стразы с неровными просветами (для «Показать»): разница по разные стороны больше <paramref name="toleranceMm"/>.</summary>
        public static IReadOnlyList<int> FindUneven(IReadOnlyList<DocStone> stones, double toleranceMm, out double worstMm)
        {
            var centers = new Point2D[stones.Count];
            for (int i = 0; i < stones.Count; i++)
            {
                centers[i] = stones[i].Center;
            }

            List<(int A, int B)>[] pairs = Pairs(stones, 0, out _);
            return Uneven(stones, centers, pairs, toleranceMm, out worstMm);
        }

        /// <param name="maxShiftMm">Насколько можно сдвинуть одну стразу от её места.</param>
        /// <param name="minGapMm">Наименьший просвет между стразами после сдвига.</param>
        /// <param name="toleranceMm">Разница просветов, которая считается неровной.</param>
        public static GapEvenResult Even(IReadOnlyList<DocStone> stones, double maxShiftMm, double minGapMm, double toleranceMm = 0.2)
        {
            List<(int A, int B)>[] pairs = Pairs(stones, minGapMm + 2 * Math.Max(0, maxShiftMm), out StoneGrid grid);
            var original = new Point2D[stones.Count];
            var current = new Point2D[stones.Count];
            for (int i = 0; i < stones.Count; i++)
            {
                original[i] = stones[i].Center;
                current[i] = stones[i].Center;
            }

            IReadOnlyList<int> before = Uneven(stones, current, pairs, toleranceMm, out double worstBefore);
            if (before.Count == 0 || maxShiftMm <= 0)
            {
                return new GapEvenResult(EditResult.Nothing, before, before, worstBefore, worstBefore);
            }

            // С кем страза может столкнуться: каждая сдвигается не дальше предела, так что хватает
            // тех, кто был ближе «радиусы + просвет + два предела». Список один раз — дальше без сетки.
            int[][] close = Close(stones, grid, minGapMm + 2 * maxShiftMm);

            // Кого пересчитать, если страза сдвинулась: тех, у кого она сосед по ряду, и тех, кому она мешала.
            var dependents = new List<int>[stones.Count];
            for (int i = 0; i < stones.Count; i++)
            {
                dependents[i] = new List<int>(close[i]);
            }

            for (int k = 0; k < stones.Count; k++)
            {
                foreach ((int a, int b) in pairs[k])
                {
                    dependents[a].Add(k);
                    dependents[b].Add(k);
                }
            }

            // Шаг меньше этого не делаем: глазу не видно, а итерации тратятся.
            double minStep = Math.Max(0.002, toleranceMm / 50);
            var active = new bool[stones.Count];
            var next = new bool[stones.Count];
            for (int i = 0; i < stones.Count; i++)
            {
                active[i] = !stones[i].IsLocked && pairs[i].Count > 0;
            }

            for (int iteration = 0; iteration < Iterations; iteration++)
            {
                bool moved = false;
                Array.Clear(next, 0, next.Length);
                for (int i = 0; i < stones.Count; i++)
                {
                    if (!active[i])
                    {
                        continue;
                    }

                    // Сдвиг — среднее по всем парам соседей: вдоль каждой прямой «сосед — страза — сосед»
                    // половина разницы просветов. Неполный шаг: соседи тоже двигаются.
                    Point2D push = new Point2D(0, 0);
                    foreach ((int a, int b) in pairs[i])
                    {
                        Point2D along = current[b] - current[a];
                        double length = along.Length;
                        if (length < 1e-9)
                        {
                            continue;
                        }

                        double gapA = Point2D.Distance(current[i], current[a]) - stones[i].Radius - stones[a].Radius;
                        double gapB = Point2D.Distance(current[i], current[b]) - stones[i].Radius - stones[b].Radius;
                        push = push + along * ((gapB - gapA) / 2 / length);
                    }

                    push = push * (0.8 / pairs[i].Count);
                    if (push.Length < minStep)
                    {
                        continue;
                    }

                    Point2D target = Clamp(original[i], current[i] + push, maxShiftMm);
                    if (Point2D.Distance(target, current[i]) < minStep)
                    {
                        continue;
                    }

                    if (TryMove(stones, close[i], original, current, i, target, minGapMm) ||
                        TryMove(stones, close[i], original, current, i, current[i] + (target - current[i]) * 0.5, minGapMm))
                    {
                        moved = true;
                        next[i] = true;
                        foreach (int k in dependents[i])
                        {
                            next[k] = true;
                        }
                    }
                }

                if (!moved)
                {
                    break;
                }

                for (int i = 0; i < stones.Count; i++)
                {
                    active[i] = next[i] && !stones[i].IsLocked && pairs[i].Count > 0;
                }
            }

            var moves = new List<StoneMove>();
            for (int i = 0; i < stones.Count; i++)
            {
                if (Point2D.Distance(current[i], original[i]) > 0.002)
                {
                    moves.Add(new StoneMove(i, current[i]));
                }
            }

            IReadOnlyList<int> after = Uneven(stones, current, pairs, toleranceMm, out double worstAfter);
            return new GapEvenResult(new EditResult(Array.Empty<int>(), moves), before, after, worstBefore, worstAfter);
        }

        /// <summary>
        /// Новое место годится, если просвет до каждого соседа не меньше заданного — или не меньше, чем
        /// был у этой пары с самого начала (если он и так был меньше: тогда хуже не делаем).
        /// </summary>
        private static bool TryMove(
            IReadOnlyList<DocStone> stones, int[] close, Point2D[] original, Point2D[] current, int i, Point2D target, double minGapMm)
        {
            foreach (int j in close)
            {
                double radii = stones[i].Radius + stones[j].Radius;
                double was = Point2D.Distance(original[i], original[j]) - radii;
                double need = Math.Min(minGapMm, was) - 1e-6;
                double now = Point2D.Distance(target, current[j]) - radii;
                if (now < need && now < Point2D.Distance(current[i], current[j]) - radii)
                {
                    return false;
                }
            }

            current[i] = target;
            return true;
        }

        private static int[][] Close(IReadOnlyList<DocStone> stones, StoneGrid grid, double extraMm)
        {
            var close = new int[stones.Count][];
            var list = new List<int>();
            for (int i = 0; i < stones.Count; i++)
            {
                list.Clear();
                foreach (int j in grid.Near(stones[i].Center))
                {
                    if (j != i && Point2D.Distance(stones[i].Center, stones[j].Center) - stones[i].Radius - stones[j].Radius <= extraMm)
                    {
                        list.Add(j);
                    }
                }

                close[i] = list.ToArray();
            }

            return close;
        }

        private static Point2D Clamp(Point2D origin, Point2D p, double maxShiftMm)
        {
            Point2D d = p - origin;
            double length = d.Length;
            return length <= maxShiftMm ? p : origin + d * (maxShiftMm / length);
        }

        private static IReadOnlyList<int> Uneven(
            IReadOnlyList<DocStone> stones, Point2D[] centers, List<(int A, int B)>[] pairs, double toleranceMm, out double worstMm)
        {
            var result = new List<int>();
            worstMm = 0;
            for (int i = 0; i < stones.Count; i++)
            {
                double worst = 0;
                foreach ((int a, int b) in pairs[i])
                {
                    double gapA = Point2D.Distance(centers[i], centers[a]) - stones[i].Radius - stones[a].Radius;
                    double gapB = Point2D.Distance(centers[i], centers[b]) - stones[i].Radius - stones[b].Radius;
                    worst = Math.Max(worst, Math.Abs(gapA - gapB));
                }

                worstMm = Math.Max(worstMm, worst);
                if (worst > toleranceMm + 1e-9)
                {
                    result.Add(i);
                }
            }

            return result;
        }

        /// <summary>
        /// Пары соседей «по ряду» у каждой стразы. Сосед — страза, до которой просвет не больше
        /// размера камня (большая дыра — уже не просвет, её закрывает «Найти дырки»). Из соседей
        /// берутся пары, лежащие с разных сторон почти на одной прямой; каждый сосед — в одной паре.
        /// </summary>
        /// <param name="extraReachMm">Сетка нужна ещё и для поиска столкновений: насколько дальше соседа «по ряду» смотреть.</param>
        private static List<(int A, int B)>[] Pairs(IReadOnlyList<DocStone> stones, double extraReachMm, out StoneGrid grid)
        {
            double maxDiameter = 0;
            foreach (DocStone s in stones)
            {
                maxDiameter = Math.Max(maxDiameter, s.DiameterMm);
            }

            // Центры соседей не дальше «диаметр + просвет»: клетка такого размера, и Near смотрит соседние клетки.
            double reach = Math.Max(Math.Max(1.0, maxDiameter), extraReachMm);
            grid = new StoneGrid(maxDiameter + reach + 0.01);
            foreach (DocStone s in stones)
            {
                grid.Add(s.Center, s.Radius);
            }

            var pairs = new List<(int, int)>[stones.Count];
            var near = new List<(int Index, Point2D Dir, double Gap)>();
            for (int i = 0; i < stones.Count; i++)
            {
                pairs[i] = new List<(int, int)>();
                near.Clear();
                Point2D p = stones[i].Center;
                foreach (int j in grid.Near(p))
                {
                    if (j == i)
                    {
                        continue;
                    }

                    Point2D d = stones[j].Center - p;
                    double distance = d.Length;
                    double gap = distance - stones[i].Radius - stones[j].Radius;
                    if (distance < 1e-6 || gap > Math.Max(1.0, Math.Min(stones[i].DiameterMm, stones[j].DiameterMm)))
                    {
                        continue;
                    }

                    near.Add((j, d * (1 / distance), gap));
                }

                // Сначала самые близкие соседи — их пары и есть ряд.
                near.Sort((x, y) => x.Gap.CompareTo(y.Gap));
                var used = new HashSet<int>();
                for (int x = 0; x < near.Count; x++)
                {
                    if (used.Contains(x))
                    {
                        continue;
                    }

                    int best = -1;
                    double bestCos = StraightCos;
                    for (int y = 0; y < near.Count; y++)
                    {
                        if (y == x || used.Contains(y))
                        {
                            continue;
                        }

                        double cos = near[x].Dir.X * near[y].Dir.X + near[x].Dir.Y * near[y].Dir.Y;
                        if (cos <= bestCos)
                        {
                            bestCos = cos;
                            best = y;
                        }
                    }

                    if (best >= 0)
                    {
                        used.Add(x);
                        used.Add(best);
                        pairs[i].Add((near[x].Index, near[best].Index));
                    }
                }
            }

            return pairs;
        }
    }
}
