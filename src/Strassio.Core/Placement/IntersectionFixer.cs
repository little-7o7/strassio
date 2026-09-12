using System;
using System.Collections.Generic;
using System.Linq;

namespace Strassio.Core.Placement
{
    /// <summary>
    /// Исправление пересечений (docs/SPEC.md, раздел 6): находит стразы, которые накладываются друг
    /// на друга — из одного ряда, из разных рядов (L2) или вообще из разных методов — и убирает те,
    /// что ниже приоритетом. В отличие от плавного сдвига в LineScatterer (который чинит соседние
    /// стразы ОДНОГО ряда на общем угле), это общая проверка: любые две стразы, независимо от того,
    /// кто и как их поставил.
    ///
    /// Порядок приоритета по умолчанию (раздел 6.3): угловые/акцентные → более крупные → раньше
    /// в списке (обычно — раньше добавленный ряд, например центральный).
    /// </summary>
    public static class IntersectionFixer
    {
        /// <summary>
        /// Возвращает стразы без пересечений — лишние (ниже приоритетом) убраны. Порядок сохраняется.
        /// Поиск конфликтов — через пространственную сетку (раздел 6.2), не перебором всех пар:
        /// цель — 50 000 страз меньше чем за секунду.
        /// </summary>
        public static List<PlacedStone> RemoveOverlaps(
            IReadOnlyList<PlacedStone> stones, double minGapMm = 0.1, double toleranceMm = 0.01)
        {
            bool[] removed = FindIndicesToRemove(stones, minGapMm, toleranceMm);

            var result = new List<PlacedStone>(stones.Count);
            for (int i = 0; i < stones.Count; i++)
            {
                if (!removed[i])
                {
                    result.Add(stones[i]);
                }
            }

            return result;
        }

        /// <summary>Как RemoveOverlaps, но возвращает булев массив (true — эту стразу нужно убрать) — для режима «только показать» (раздел 6.4).</summary>
        public static bool[] FindIndicesToRemove(
            IReadOnlyList<PlacedStone> stones, double minGapMm = 0.1, double toleranceMm = 0.01)
        {
            int n = stones.Count;
            var removed = new bool[n];
            if (n < 2)
            {
                return removed;
            }

            double maxDiameter = 0;
            for (int i = 0; i < n; i++)
            {
                if (stones[i].DiameterMm > maxDiameter)
                {
                    maxDiameter = stones[i].DiameterMm;
                }
            }

            double cellSize = Math.Max(0.1, maxDiameter + minGapMm);
            var grid = new Dictionary<(int, int), List<int>>();

            (int, int) CellOf(int index)
            {
                Geometry.Point2D c = stones[index].Center;
                return ((int)Math.Floor(c.X / cellSize), (int)Math.Floor(c.Y / cellSize));
            }

            for (int i = 0; i < n; i++)
            {
                (int, int) cell = CellOf(i);
                if (!grid.TryGetValue(cell, out List<int>? list))
                {
                    list = new List<int>();
                    grid[cell] = list;
                }

                list.Add(i);
            }

            // Приоритет: угловые впереди, затем крупнее, затем — раньше в списке (индекс).
            int[] order = Enumerable.Range(0, n)
                .OrderByDescending(i => stones[i].IsCorner)
                .ThenByDescending(i => stones[i].DiameterMm)
                .ThenBy(i => i)
                .ToArray();

            var priorityRank = new int[n];
            for (int rank = 0; rank < order.Length; rank++)
            {
                priorityRank[order[rank]] = rank;
            }

            foreach (int i in order)
            {
                if (removed[i])
                {
                    continue;
                }

                (int cx, int cy) = CellOf(i);
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (!grid.TryGetValue((cx + dx, cy + dy), out List<int>? neighbors))
                        {
                            continue;
                        }

                        foreach (int j in neighbors)
                        {
                            if (j == i || removed[j] || priorityRank[j] < priorityRank[i])
                            {
                                continue; // j уже обработан раньше как более приоритетный — эту пару уже проверили с его стороны
                            }

                            double required = stones[i].DiameterMm / 2 + stones[j].DiameterMm / 2 + minGapMm - toleranceMm;
                            double dist = Geometry.Point2D.Distance(stones[i].Center, stones[j].Center);

                            if (dist < required)
                            {
                                removed[j] = true;
                            }
                        }
                    }
                }
            }

            return removed;
        }
    }
}
