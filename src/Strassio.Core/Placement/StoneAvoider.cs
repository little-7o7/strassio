using System;
using System.Collections.Generic;
using Strassio.Core.Editing;
using Strassio.Core.Geometry;

namespace Strassio.Core.Placement
{
    /// <summary>Что осталось от новых страз после встречи с уже лежащими.</summary>
    public sealed class AvoidResult
    {
        public AvoidResult(IReadOnlyList<PlacedStone> stones, int removed, int shifted, IReadOnlyList<int> keptIndices)
        {
            Stones = stones;
            Removed = removed;
            Shifted = shifted;
            KeptIndices = keptIndices;
        }

        /// <summary>Новые стразы, которые остались (сдвинутые — уже на новом месте).</summary>
        public IReadOnlyList<PlacedStone> Stones { get; }

        /// <summary>Сколько новых страз убрано — им не нашлось места рядом с уже лежащими.</summary>
        public int Removed { get; }

        /// <summary>Сколько новых страз чуть сдвинуто, чтобы не налезать.</summary>
        public int Shifted { get; }

        /// <summary>Для каждой оставшейся стразы — её номер в исходном списке новых.</summary>
        public IReadOnlyList<int> KeptIndices { get; }
    }

    /// <summary>
    /// «Учитывать готовые стразы»: новые стразы не должны налезать на те, что уже лежат в документе
    /// (кант положили раньше, теперь заливка внутри; или соседняя фигура уже выложена). Уже лежащие
    /// не трогаются никогда. Новая страза, которая на них налезает, сначала пробует чуть отодвинуться
    /// (не дальше <c>maxShiftMm</c> и не за край своей фигуры), а если места нет — убирается.
    /// </summary>
    public static class StoneAvoider
    {
        /// <param name="fresh">Новые стразы — результат метода.</param>
        /// <param name="obstacles">Уже лежащие стразы (центр и диаметр).</param>
        /// <param name="minGapMm">Наименьший зазор край в край между новой и лежащей стразой.</param>
        /// <param name="maxShiftMm">Насколько можно отодвинуть новую стразу; 0 — не двигать, только убирать.</param>
        /// <param name="allowed">Можно ли поставить стразу с центром в точке (например, центр внутри фигуры); null — везде.</param>
        public static AvoidResult Avoid(
            IReadOnlyList<PlacedStone> fresh,
            IReadOnlyList<PlacedStone> obstacles,
            double minGapMm,
            double maxShiftMm = 0,
            Func<Point2D, bool>? allowed = null)
        {
            if (fresh.Count == 0 || obstacles.Count == 0)
            {
                var all = new int[fresh.Count];
                for (int i = 0; i < all.Length; i++)
                {
                    all[i] = i;
                }

                return new AvoidResult(fresh, 0, 0, all);
            }

            double maxDiameter = 0;
            foreach (PlacedStone s in fresh)
            {
                maxDiameter = Math.Max(maxDiameter, s.DiameterMm);
            }

            foreach (PlacedStone s in obstacles)
            {
                maxDiameter = Math.Max(maxDiameter, s.DiameterMm);
            }

            // В сетке и лежащие, и новые: сдвинутая страза не должна задеть ни тех, ни других.
            // Клетка с запасом на сдвиг: соседей ищем в соседних клетках.
            var grid = new StoneGrid(maxDiameter + Math.Max(0, minGapMm) + maxShiftMm);
            foreach (PlacedStone s in obstacles)
            {
                grid.Add(s.Center, s.DiameterMm / 2);
            }

            int obstacleCount = obstacles.Count;
            var freshIds = new int[fresh.Count];
            for (int i = 0; i < fresh.Count; i++)
            {
                freshIds[i] = grid.Add(fresh[i].Center, fresh[i].DiameterMm / 2);
            }

            var kept = new List<PlacedStone>(fresh.Count);
            var keptIndices = new List<int>(fresh.Count);
            int shifted = 0;
            int removedCount = 0;

            for (int i = 0; i < fresh.Count; i++)
            {
                PlacedStone stone = fresh[i];
                double radius = stone.DiameterMm / 2;
                if (!HitsObstacle(grid, stone.Center, radius, minGapMm, obstacleCount))
                {
                    kept.Add(stone);
                    keptIndices.Add(i);
                    continue;
                }

                if (maxShiftMm > 0 && TryShift(grid, stone, minGapMm, maxShiftMm, freshIds[i], allowed, out Point2D moved))
                {
                    var movedStone = new PlacedStone(moved, stone.DiameterMm, stone.IsCorner, stone.RowId);
                    grid.Move(freshIds[i], moved);
                    kept.Add(movedStone);
                    keptIndices.Add(i);
                    shifted++;
                    continue;
                }

                grid.Remove(freshIds[i]);
                removedCount++;
            }

            return new AvoidResult(kept, removedCount, shifted, keptIndices);
        }

        /// <summary>Номера новых страз, которые налезают на уже лежащие, — для «только показать».</summary>
        public static IReadOnlyList<int> Conflicts(IReadOnlyList<PlacedStone> fresh, IReadOnlyList<PlacedStone> obstacles, double minGapMm)
        {
            var result = new List<int>();
            if (fresh.Count == 0 || obstacles.Count == 0)
            {
                return result;
            }

            double maxDiameter = 0;
            foreach (PlacedStone s in fresh)
            {
                maxDiameter = Math.Max(maxDiameter, s.DiameterMm);
            }

            foreach (PlacedStone s in obstacles)
            {
                maxDiameter = Math.Max(maxDiameter, s.DiameterMm);
            }

            var grid = new StoneGrid(maxDiameter + Math.Max(0, minGapMm));
            foreach (PlacedStone s in obstacles)
            {
                grid.Add(s.Center, s.DiameterMm / 2);
            }

            for (int i = 0; i < fresh.Count; i++)
            {
                if (HitsObstacle(grid, fresh[i].Center, fresh[i].DiameterMm / 2, minGapMm, obstacles.Count))
                {
                    result.Add(i);
                }
            }

            return result;
        }

        /// <summary>Налезает ли круг на какую-нибудь уже лежащую стразу (они в сетке первыми, номера меньше obstacleCount).</summary>
        private static bool HitsObstacle(StoneGrid grid, Point2D center, double radius, double minGapMm, int obstacleCount)
        {
            foreach (int j in grid.Near(center))
            {
                if (j >= obstacleCount)
                {
                    continue;
                }

                (Point2D c, double r) = grid[j];
                if (Point2D.Distance(c, center) < r + radius + minGapMm - 1e-6)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Отталкиваем стразу от всех, на кого она налезает (несколько шагов: отодвинулась от одной —
        /// могла задеть другую). Новое место не должно задевать ни лежащие, ни другие новые стразы.
        /// </summary>
        private static bool TryShift(
            StoneGrid grid, PlacedStone stone, double minGapMm, double maxShiftMm,
            int selfId, Func<Point2D, bool>? allowed, out Point2D result)
        {
            double radius = stone.DiameterMm / 2;
            Point2D p = stone.Center;
            for (int step = 0; step < 6; step++)
            {
                Point2D push = new Point2D(0, 0);
                bool any = false;
                foreach (int j in grid.Near(p))
                {
                    if (j == selfId)
                    {
                        continue;
                    }

                    (Point2D c, double r) = grid[j];
                    double need = r + radius + minGapMm;
                    double dist = Point2D.Distance(c, p);
                    if (dist >= need - 1e-6)
                    {
                        continue;
                    }

                    any = true;
                    Point2D away = dist > 1e-9 ? (p - c) * (1 / dist) : new Point2D(1, 0);
                    push = push + away * (need - dist + 1e-4);
                }

                if (!any)
                {
                    result = p;
                    return Point2D.Distance(p, stone.Center) <= maxShiftMm + 1e-9 && (allowed == null || allowed(p));
                }

                p = p + push;
                if (Point2D.Distance(p, stone.Center) > maxShiftMm + 1e-9)
                {
                    break;
                }
            }

            result = stone.Center;
            return false;
        }
    }
}
