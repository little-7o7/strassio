using System;
using System.Collections.Generic;
using Strassio.Core.Geometry;

namespace Strassio.Core.Placement
{
    /// <summary>Действия «Удалить», «Сдвинуть», «Только показать» (docs/SPEC.md, раздел 6.4).</summary>
    public static partial class IntersectionFixer
    {
        /// <summary>Шаг перебора вариантов сдвига вдоль ряда, мм.</summary>
        private const double ShiftSearchStepMm = 0.05;

        public static IntersectionFixResult Fix(IReadOnlyList<PlacedStone> stones, IntersectionFixOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            int[] conflicts = FindConflictIndices(stones, options.MinGapMm, options.ToleranceMm);

            switch (options.Action)
            {
                case IntersectionAction.ShowOnly:
                    return new IntersectionFixResult(stones, Array.Empty<int>(), Array.Empty<int>(), conflicts);

                case IntersectionAction.Shift:
                    return ShiftOrRemove(stones, options, conflicts);

                default:
                    bool[] removed = FindIndicesToRemove(stones, options.MinGapMm, options.ToleranceMm);
                    if (!options.CloseGaps)
                    {
                        return BuildResult(stones, stones, removed, new bool[stones.Count], conflicts);
                    }

                    var positions = new Point2D[stones.Count];
                    for (int i = 0; i < stones.Count; i++)
                    {
                        positions[i] = stones[i].Center;
                    }

                    var moved = new bool[stones.Count];
                    CloseGaps(stones, positions, removed, moved, options);
                    return BuildResult(stones, WithPositions(stones, positions), removed, moved, conflicts);
            }
        }

        /// <summary>
        /// Жадно, по приоритету: страза, которая не мешает уже оставленным, остаётся на месте. Мешает —
        /// пробуем отодвинуть её вдоль своего ряда (ближайший подходящий вариант, не дальше лимита и не
        /// в соседа по ряду); не вышло — убираем. Угловые стразы и стразы плоской сетки (RowId &lt; 0)
        /// не двигаем: угловая должна стоять точно в вершине (раздел 4), у сетки нет «своей линии».
        /// </summary>
        private static IntersectionFixResult ShiftOrRemove(
            IReadOnlyList<PlacedStone> stones, IntersectionFixOptions options, int[] conflicts)
        {
            int n = stones.Count;
            var positions = new Point2D[n];
            for (int i = 0; i < n; i++)
            {
                positions[i] = stones[i].Center;
            }

            var removed = new bool[n];
            var shifted = new bool[n];
            if (n < 2)
            {
                return BuildResult(stones, stones, removed, shifted, conflicts);
            }

            (int[] prevInRow, int[] nextInRow) = RowNeighbours(stones);

            double maxDiameter = 0;
            foreach (PlacedStone s in stones)
            {
                maxDiameter = Math.Max(maxDiameter, s.DiameterMm);
            }

            // Сетка содержит только уже оставленные стразы — по их текущим (возможно, сдвинутым) местам.
            double cellSize = Math.Max(0.1, maxDiameter + options.MinGapMm);
            var accepted = new Dictionary<(int, int), List<int>>();

            (int, int) CellOf(Point2D p) => ((int)Math.Floor(p.X / cellSize), (int)Math.Floor(p.Y / cellSize));

            bool FitsAt(int index, Point2D p)
            {
                (int cx, int cy) = CellOf(p);
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (!accepted.TryGetValue((cx + dx, cy + dy), out List<int>? list))
                        {
                            continue;
                        }

                        foreach (int j in list)
                        {
                            double required = stones[index].DiameterMm / 2 + stones[j].DiameterMm / 2 + options.MinGapMm - options.ToleranceMm;
                            if (Point2D.Distance(p, positions[j]) < required)
                            {
                                return false;
                            }
                        }
                    }
                }

                return true;
            }

            foreach (int i in PriorityOrder(stones))
            {
                if (!FitsAt(i, positions[i]))
                {
                    Point2D? moved = stones[i].IsCorner || stones[i].RowId < 0
                        ? null
                        : FindShiftAlongRow(i, stones, positions, removed, prevInRow[i], nextInRow[i], options, FitsAt);

                    if (moved == null)
                    {
                        removed[i] = true;
                        continue;
                    }

                    positions[i] = moved.Value;
                    shifted[i] = true;
                }

                (int, int) cell = CellOf(positions[i]);
                if (!accepted.TryGetValue(cell, out List<int>? bucket))
                {
                    bucket = new List<int>();
                    accepted[cell] = bucket;
                }

                bucket.Add(i);
            }

            RemoveLonelySurvivors(stones, removed);

            if (options.CloseGaps)
            {
                CloseGaps(stones, positions, removed, shifted, options);
            }

            return BuildResult(stones, WithPositions(stones, positions), removed, shifted, conflicts);
        }

        /// <summary>
        /// Ищет ближайшее место на ломаной «сосед до — страза — сосед после» (это и есть её ряд в
        /// окрестности), где страза не мешает оставленным и не налезает на соседей по ряду.
        /// </summary>
        private static Point2D? FindShiftAlongRow(
            int index,
            IReadOnlyList<PlacedStone> stones,
            Point2D[] positions,
            bool[] removed,
            int prev,
            int next,
            IntersectionFixOptions options,
            Func<int, Point2D, bool> fitsAt)
        {
            Point2D origin = positions[index];
            double diameter = stones[index].DiameterMm;

            // Направление «назад» и «вперёд» по ряду. Если соседа с одной стороны нет (край открытого
            // ряда), продолжаем линию от соседа с другой стороны.
            Point2D back = prev >= 0 ? (positions[prev] - origin).Normalized() : Point2D.Zero;
            Point2D forward = next >= 0 ? (positions[next] - origin).Normalized() : Point2D.Zero;
            if (prev < 0 && next >= 0)
            {
                back = -forward;
            }
            else if (next < 0 && prev >= 0)
            {
                forward = -back;
            }

            if (back == Point2D.Zero && forward == Point2D.Zero)
            {
                return null;
            }

            bool ClearOfNeighbour(Point2D p, int neighbour)
            {
                if (neighbour < 0 || removed[neighbour])
                {
                    return true;
                }

                double required = diameter / 2 + stones[neighbour].DiameterMm / 2 + options.MinGapMm - options.ToleranceMm;
                return Point2D.Distance(p, positions[neighbour]) >= required;
            }

            int steps = (int)Math.Floor(options.MaxShiftMm / ShiftSearchStepMm + 1e-9);
            for (int k = 1; k <= steps; k++)
            {
                double d = k * ShiftSearchStepMm;
                foreach (Point2D dir in new[] { back, forward })
                {
                    if (dir == Point2D.Zero)
                    {
                        continue;
                    }

                    Point2D candidate = origin + dir * d;
                    if (ClearOfNeighbour(candidate, prev) && ClearOfNeighbour(candidate, next) && fitsAt(index, candidate))
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }

        /// <summary>Соседи каждой стразы в её ряду (RowId ≥ 0) по порядку списка, −1 — соседа нет.</summary>
        private static (int[] Prev, int[] Next) RowNeighbours(IReadOnlyList<PlacedStone> stones)
        {
            int n = stones.Count;
            var prev = new int[n];
            var next = new int[n];
            for (int i = 0; i < n; i++)
            {
                prev[i] = -1;
                next[i] = -1;
            }

            foreach (StoneRow row in SplitIntoRows(stones))
            {
                List<int> m = row.Members;
                for (int k = 1; k < m.Count; k++)
                {
                    prev[m[k]] = m[k - 1];
                    next[m[k - 1]] = m[k];
                }

                if (row.IsClosed)
                {
                    prev[m[0]] = m[m.Count - 1];
                    next[m[m.Count - 1]] = m[0];
                }
            }

            return (prev, next);
        }

        /// <summary>Ряд: номера его страз по порядку списка (= по ходу кривой) и замкнут ли он.</summary>
        private sealed class StoneRow
        {
            public StoneRow(List<int> members, bool isClosed)
            {
                Members = members;
                IsClosed = isClosed;
            }

            public List<int> Members { get; }

            public bool IsClosed { get; }
        }

        /// <summary>
        /// Делит стразы на ряды (RowId ≥ 0; стразы плоской сетки пропускаются). Ряд считается
        /// замкнутым, если его последняя страза стоит рядом с первой (не дальше полутора самых
        /// длинных шагов этого ряда) — как у контура фигуры.
        /// </summary>
        private static List<StoneRow> SplitIntoRows(IReadOnlyList<PlacedStone> stones)
        {
            var byId = new Dictionary<int, List<int>>();
            var order = new List<int>();
            for (int i = 0; i < stones.Count; i++)
            {
                if (stones[i].RowId < 0)
                {
                    continue;
                }

                if (!byId.TryGetValue(stones[i].RowId, out List<int>? members))
                {
                    members = new List<int>();
                    byId[stones[i].RowId] = members;
                    order.Add(stones[i].RowId);
                }

                members.Add(i);
            }

            var rows = new List<StoneRow>(order.Count);
            foreach (int id in order)
            {
                List<int> m = byId[id];
                double longestStep = 0;
                for (int k = 1; k < m.Count; k++)
                {
                    longestStep = Math.Max(longestStep, Point2D.Distance(stones[m[k]].Center, stones[m[k - 1]].Center));
                }

                bool closed = m.Count >= 3 &&
                    Point2D.Distance(stones[m[m.Count - 1]].Center, stones[m[0]].Center) <= longestStep * 1.5;
                rows.Add(new StoneRow(m, closed));
            }

            return rows;
        }

        /// <summary>Все стразы, которые накладываются хоть на одну другую (по исходным местам).</summary>
        private static int[] FindConflictIndices(IReadOnlyList<PlacedStone> stones, double minGapMm, double toleranceMm)
        {
            int n = stones.Count;
            var inConflict = new bool[n];
            double maxDiameter = 0;
            foreach (PlacedStone s in stones)
            {
                maxDiameter = Math.Max(maxDiameter, s.DiameterMm);
            }

            double cellSize = Math.Max(0.1, maxDiameter + minGapMm);
            var grid = new Dictionary<(int, int), List<int>>();
            for (int i = 0; i < n; i++)
            {
                Point2D c = stones[i].Center;
                (int cx, int cy) = ((int)Math.Floor(c.X / cellSize), (int)Math.Floor(c.Y / cellSize));

                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (!grid.TryGetValue((cx + dx, cy + dy), out List<int>? list))
                        {
                            continue;
                        }

                        foreach (int j in list)
                        {
                            double required = stones[i].DiameterMm / 2 + stones[j].DiameterMm / 2 + minGapMm - toleranceMm;
                            if (Point2D.Distance(c, stones[j].Center) < required)
                            {
                                inConflict[i] = true;
                                inConflict[j] = true;
                            }
                        }
                    }
                }

                if (!grid.TryGetValue((cx, cy), out List<int>? own))
                {
                    own = new List<int>();
                    grid[(cx, cy)] = own;
                }

                own.Add(i);
            }

            var result = new List<int>();
            for (int i = 0; i < n; i++)
            {
                if (inConflict[i])
                {
                    result.Add(i);
                }
            }

            return result.ToArray();
        }

        private static PlacedStone[] WithPositions(IReadOnlyList<PlacedStone> stones, Point2D[] positions)
        {
            var result = new PlacedStone[stones.Count];
            for (int i = 0; i < stones.Count; i++)
            {
                result[i] = new PlacedStone(positions[i], stones[i].DiameterMm, stones[i].IsCorner, stones[i].RowId);
            }

            return result;
        }

        private static IntersectionFixResult BuildResult(
            IReadOnlyList<PlacedStone> original,
            IReadOnlyList<PlacedStone> current,
            bool[] removed,
            bool[] shifted,
            int[] conflicts)
        {
            var kept = new List<PlacedStone>(original.Count);
            var removedList = new List<int>();
            var shiftedList = new List<int>();
            for (int i = 0; i < original.Count; i++)
            {
                if (removed[i])
                {
                    removedList.Add(i);
                    continue;
                }

                kept.Add(current[i]);
                if (shifted[i])
                {
                    shiftedList.Add(i);
                }
            }

            return new IntersectionFixResult(kept, removedList, shiftedList, conflicts);
        }
    }
}
