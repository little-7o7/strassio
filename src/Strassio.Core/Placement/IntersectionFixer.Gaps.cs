using System;
using System.Collections.Generic;
using Strassio.Core.Geometry;

namespace Strassio.Core.Placement
{
    /// <summary>
    /// «После удаления соседние камни той же линии раздвигаются, чтобы не осталось дырки»
    /// (docs/SPEC.md, раздел 6.4).
    /// </summary>
    public static partial class IntersectionFixer
    {
        /// <summary>Сколько самое большее соседей с каждой стороны дырки можно раздвинуть.</summary>
        private const int MaxGapWindow = 6;

        /// <summary>
        /// Насколько самое большее может вырасти шаг ряда после раздвигания — доля диаметра стразы.
        /// Если чтобы закрыть дырку, шаг пришлось бы растянуть сильнее, это не дырка, а место, где
        /// ряды сходятся (там стоит чужая страза) — такое не трогаем.
        /// </summary>
        private const double MaxStepGrowthOfDiameter = 0.25;

        /// <summary>
        /// Для каждой дырки в ряду (подряд убранные стразы, по обе стороны от которых стразы остались)
        /// берёт по несколько соседей с каждой стороны и расставляет их равномерно вдоль линии ряда;
        /// крайние стразы этого «окна» стоят на месте. Окно растёт от 1 до <see cref="MaxGapWindow"/>
        /// соседей, пока шаг не станет достаточно ровным и никто не налезет на чужие стразы.
        /// Линия ряда — ломаная через ИСХОДНЫЕ центры всех его страз (включая убранные): при шаге
        /// порядка диаметра она почти совпадает с кривой. Угловые и уже сдвинутые («Сдвинуть»)
        /// стразы не двигаются — окно на них заканчивается.
        /// </summary>
        private static void CloseGaps(
            IReadOnlyList<PlacedStone> stones, Point2D[] positions, bool[] removed, bool[] shifted, IntersectionFixOptions options)
        {
            int n = stones.Count;
            var pinned = new bool[n];
            double maxDiameter = 0;
            for (int i = 0; i < n; i++)
            {
                pinned[i] = shifted[i] || stones[i].IsCorner;
                maxDiameter = Math.Max(maxDiameter, stones[i].DiameterMm);
            }

            double cellSize = Math.Max(0.1, maxDiameter + options.MinGapMm);
            var grid = new Dictionary<(int, int), List<int>>();
            (int, int) CellOf(Point2D p) => ((int)Math.Floor(p.X / cellSize), (int)Math.Floor(p.Y / cellSize));

            void AddToGrid(int index)
            {
                (int, int) cell = CellOf(positions[index]);
                if (!grid.TryGetValue(cell, out List<int>? list))
                {
                    list = new List<int>();
                    grid[cell] = list;
                }

                list.Add(index);
            }

            for (int i = 0; i < n; i++)
            {
                if (!removed[i])
                {
                    AddToGrid(i);
                }
            }

            double Required(int a, int b) =>
                stones[a].DiameterMm / 2 + stones[b].DiameterMm / 2 + options.MinGapMm - options.ToleranceMm;

            bool FitsAt(int index, Point2D p, HashSet<int> ignore)
            {
                (int cx, int cy) = CellOf(p);
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
                            if (!ignore.Contains(j) && Point2D.Distance(p, positions[j]) < Required(index, j))
                            {
                                return false;
                            }
                        }
                    }
                }

                return true;
            }

            foreach (StoneRow row in SplitIntoRows(stones))
            {
                List<int> m = row.Members;
                int count = m.Count;
                if (count < 3)
                {
                    continue;
                }

                // Шаг по ряду: номер позиции в ряду → соседняя позиция (−1 — конец открытого ряда).
                int StepFrom(int pos, int dir)
                {
                    int next = pos + dir;
                    if (next >= 0 && next < count)
                    {
                        return next;
                    }

                    return row.IsClosed ? (next + count) % count : -1;
                }

                // Длина отрезка линии ряда от позиции k до следующей.
                var segment = new double[count];
                for (int k = 0; k < count; k++)
                {
                    int next = StepFrom(k, 1);
                    segment[k] = next < 0 ? 0 : Point2D.Distance(stones[m[k]].Center, stones[m[next]].Center);
                }

                // Насколько страза на позиции k уже сдвинута вдоль линии от своего исходного центра
                // (закрытием соседней дырки), мм; «+» — вперёд по ряду.
                var offset = new double[count];

                Point2D PointAlong(int fromPos, double distance)
                {
                    int k = fromPos;
                    while (distance < 0)
                    {
                        int back = StepFrom(k, -1);
                        if (back < 0)
                        {
                            return stones[m[k]].Center;
                        }

                        k = back;
                        distance += segment[k];
                    }

                    for (int guard = 0; guard < count; guard++)
                    {
                        int next = StepFrom(k, 1);
                        if (next < 0)
                        {
                            break;
                        }

                        if (distance <= segment[k])
                        {
                            double t = segment[k] > 0 ? distance / segment[k] : 0;
                            return Point2D.Lerp(stones[m[k]].Center, stones[m[next]].Center, t);
                        }

                        distance -= segment[k];
                        k = next;
                    }

                    return stones[m[k]].Center;
                }

                // Соседи дырки, которых можно двигать, и неподвижная «опора» за ними.
                List<int> CollectMovable(int startPos, int dir, int window, out int anchorPos)
                {
                    var movable = new List<int>();
                    int cur = startPos;
                    while (true)
                    {
                        if (pinned[m[cur]] || movable.Count == window)
                        {
                            anchorPos = cur;
                            return movable;
                        }

                        movable.Add(cur);
                        int next = StepFrom(cur, dir);
                        if (next < 0 || removed[m[next]])
                        {
                            // Дальше двигать некого (конец ряда или другая дырка) — последний становится опорой.
                            anchorPos = movable[movable.Count - 1];
                            movable.RemoveAt(movable.Count - 1);
                            return movable;
                        }

                        cur = next;
                    }
                }

                bool TryCloseGap(int beforePos, int afterPos)
                {
                    int previousCount = -1;
                    for (int window = 1; window <= MaxGapWindow; window++)
                    {
                        List<int> left = CollectMovable(beforePos, -1, window, out int leftAnchor);
                        List<int> right = CollectMovable(afterPos, 1, window, out int rightAnchor);
                        int movableCount = left.Count + right.Count;
                        if (movableCount == 0 || movableCount == previousCount)
                        {
                            return false; // окно больше не растёт
                        }

                        previousCount = movableCount;

                        // В замкнутом ряду окна с двух сторон могут встретиться — тогда все позиции не различны.
                        var used = new HashSet<int> { leftAnchor, rightAnchor };
                        used.UnionWith(left);
                        used.UnionWith(right);
                        if (used.Count != movableCount + 2)
                        {
                            return false;
                        }

                        // Расстояние вдоль линии от исходного центра левой опоры до каждой позиции окна.
                        var alongFromLeft = new Dictionary<int, double>();
                        double span = 0;
                        int intervals = 0;
                        for (int k = leftAnchor; k != rightAnchor; k = StepFrom(k, 1))
                        {
                            alongFromLeft[k] = span;
                            span += segment[k];
                            intervals++;
                        }

                        span += offset[rightAnchor] - offset[leftAnchor];

                        double newStep = span / (movableCount + 1);
                        double oldStep = span / intervals;
                        if (newStep - oldStep > MaxStepGrowthOfDiameter * stones[m[beforePos]].DiameterMm + 1e-6)
                        {
                            continue;
                        }

                        // Порядок вдоль ряда: левые от дальнего к ближнему, затем правые.
                        left.Reverse();
                        var orderPos = new List<int>(left);
                        orderPos.AddRange(right);
                        var order = orderPos.ConvertAll(pos => m[pos]);

                        var ignore = new HashSet<int>(order);
                        var targets = new Point2D[movableCount];
                        bool fits = true;
                        for (int j = 0; j < movableCount && fits; j++)
                        {
                            targets[j] = PointAlong(leftAnchor, offset[leftAnchor] + newStep * (j + 1));
                            fits = FitsAt(order[j], targets[j], ignore) &&
                                (j == 0 || Point2D.Distance(targets[j], targets[j - 1]) >= Required(order[j], order[j - 1]));
                        }

                        if (!fits)
                        {
                            continue;
                        }

                        for (int j = 0; j < movableCount; j++)
                        {
                            int index = order[j];
                            grid[CellOf(positions[index])].Remove(index);
                            positions[index] = targets[j];
                            shifted[index] = true;
                            offset[orderPos[j]] = offset[leftAnchor] + newStep * (j + 1) - alongFromLeft[orderPos[j]];
                            AddToGrid(index);
                        }

                        return true;
                    }

                    return false;
                }

                // Ищем начала дырок: убранная позиция, перед которой страза осталась.
                for (int start = 0; start < count; start++)
                {
                    int before = StepFrom(start, -1);
                    if (!removed[m[start]] || before < 0 || removed[m[before]])
                    {
                        continue;
                    }

                    int end = start;
                    int guard = 0;
                    while (end >= 0 && removed[m[end]] && guard++ < count)
                    {
                        end = StepFrom(end, 1);
                    }

                    if (end < 0 || removed[m[end]])
                    {
                        continue; // дырка упирается в конец открытого ряда — это просто ряд короче
                    }

                    TryCloseGap(before, end);
                }
            }
        }
    }
}
