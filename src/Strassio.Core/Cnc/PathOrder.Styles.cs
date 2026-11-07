using System;
using System.Collections.Generic;
using System.Linq;
using Strassio.Core.Geometry;

namespace Strassio.Core.Cnc
{
    /// <summary>В каком порядке сверлить отверстия одного сверла (docs/SPEC.md, раздел 11.1).</summary>
    public enum DrillPathStyle
    {
        /// <summary>Самый короткий путь (ближайшее следующее + распутывание).</summary>
        Fast,

        /// <summary>Рядами снизу вверх, каждый ряд слева направо.</summary>
        Rows,

        /// <summary>Рядами снизу вверх змейкой: ряд слева направо, следующий справа налево.</summary>
        Snake,

        /// <summary>Столбцами слева направо, каждый столбец снизу вверх.</summary>
        Columns,

        /// <summary>По группам: мотив (кучка камней) целиком, потом следующий, ближайший.</summary>
        Groups,

        /// <summary>
        /// Вразброс: соседние отверстия не подряд — путь проходится в несколько заходов через одно-два
        /// отверстия, чтобы материал не грелся и тонкие перемычки не рвались.
        /// </summary>
        Spread,
    }

    public static partial class PathOrder
    {
        /// <summary>Через сколько отверстий шагает «вразброс» за один заход.</summary>
        public const int SpreadStep = 3;

        /// <summary>
        /// Порядок обхода по выбранному способу. <paramref name="pitchMm"/> — примерный шаг отверстий
        /// (обычно диаметр сверла): по нему отверстия собираются в ряды и группы.
        /// </summary>
        public static int[] Arrange(IReadOnlyList<Point2D> points, Point2D start, DrillPathStyle style, double pitchMm, bool improve = true, int budgetMs = 1500)
        {
            int n = points.Count;
            if (n == 0)
            {
                return Array.Empty<int>();
            }

            double pitch = Math.Max(0.1, double.IsNaN(pitchMm) ? 1 : pitchMm);
            switch (style)
            {
                case DrillPathStyle.Rows:
                    return Lines(points, pitch, byY: true, snake: false);
                case DrillPathStyle.Snake:
                    return Lines(points, pitch, byY: true, snake: true);
                case DrillPathStyle.Columns:
                    return Lines(points, pitch, byY: false, snake: false);
                case DrillPathStyle.Groups:
                    return Groups(points, start, pitch, improve, budgetMs);
                case DrillPathStyle.Spread:
                    int[] fast = Order(points, start, improve, budgetMs);
                    var spread = new List<int>(n);
                    for (int pass = 0; pass < SpreadStep; pass++)
                    {
                        for (int i = pass; i < n; i += SpreadStep)
                        {
                            spread.Add(fast[i]);
                        }
                    }

                    return spread.ToArray();
                default:
                    return Order(points, start, improve, budgetMs);
            }
        }

        /// <summary>
        /// Ряды (или столбцы): отверстия по высоте ближе половины шага к первому в ряду — один ряд.
        /// Ряды снизу вверх (столбцы слева направо); внутри — слева направо (снизу вверх) или змейкой.
        /// </summary>
        private static int[] Lines(IReadOnlyList<Point2D> points, double pitch, bool byY, bool snake)
        {
            double Across(int i) => byY ? points[i].Y : points[i].X;
            double Along(int i) => byY ? points[i].X : points[i].Y;

            int[] sorted = Enumerable.Range(0, points.Count).OrderBy(Across).ThenBy(Along).ToArray();
            var result = new List<int>(points.Count);
            var line = new List<int>();
            double lineStart = double.NaN;
            bool reverse = false;

            void Flush()
            {
                if (line.Count == 0)
                {
                    return;
                }

                IEnumerable<int> ordered = line.OrderBy(Along).ThenBy(Across);
                result.AddRange(reverse ? ordered.Reverse() : ordered);
                line.Clear();
                if (snake)
                {
                    reverse = !reverse;
                }
            }

            foreach (int i in sorted)
            {
                if (line.Count > 0 && Across(i) - lineStart > pitch / 2)
                {
                    Flush();
                }

                if (line.Count == 0)
                {
                    lineStart = Across(i);
                }

                line.Add(i);
            }

            Flush();
            return result.ToArray();
        }

        /// <summary>
        /// Группы: отверстия ближе 2,5 шага друг к другу — одна группа (мотив). Следующая группа —
        /// ближайшая к тому месту, где закончили; внутри группы — самый короткий путь от входа.
        /// </summary>
        private static int[] Groups(IReadOnlyList<Point2D> points, Point2D start, double pitch, bool improve, int budgetMs)
        {
            int n = points.Count;
            double link = 2.5 * pitch;
            var parent = Enumerable.Range(0, n).ToArray();
            int Find(int i)
            {
                while (parent[i] != i)
                {
                    parent[i] = parent[parent[i]];
                    i = parent[i];
                }

                return i;
            }

            var cells = new Dictionary<(long, long), List<int>>();
            for (int i = 0; i < n; i++)
            {
                long cx = (long)Math.Floor(points[i].X / link), cy = (long)Math.Floor(points[i].Y / link);
                for (long dx = -1; dx <= 1; dx++)
                {
                    for (long dy = -1; dy <= 1; dy++)
                    {
                        if (!cells.TryGetValue((cx + dx, cy + dy), out List<int>? near))
                        {
                            continue;
                        }

                        foreach (int j in near)
                        {
                            if (Point2D.Distance(points[i], points[j]) <= link)
                            {
                                int a = Find(i), b = Find(j);
                                if (a != b)
                                {
                                    parent[a] = b;
                                }
                            }
                        }
                    }
                }

                if (!cells.TryGetValue((cx, cy), out List<int>? cell))
                {
                    cell = new List<int>();
                    cells[(cx, cy)] = cell;
                }

                cell.Add(i);
            }

            List<List<int>> groups = Enumerable.Range(0, n).GroupBy(Find).Select(g => g.ToList()).ToList();
            var centers = groups.Select(g => new Point2D(g.Average(i => points[i].X), g.Average(i => points[i].Y))).ToList();
            var done = new bool[groups.Count];
            var result = new List<int>(n);
            Point2D at = start;
            int perGroupBudget = Math.Max(5, budgetMs / Math.Max(1, groups.Count));
            for (int k = 0; k < groups.Count; k++)
            {
                int best = -1;
                double bestD = double.MaxValue;
                for (int g = 0; g < groups.Count; g++)
                {
                    if (!done[g])
                    {
                        double d = Point2D.Distance(at, centers[g]);
                        if (d < bestD)
                        {
                            bestD = d;
                            best = g;
                        }
                    }
                }

                done[best] = true;
                List<int> members = groups[best];
                var local = members.Select(i => points[i]).ToList();
                int[] order = Order(local, at, improve && members.Count > 3, perGroupBudget);
                foreach (int i in order)
                {
                    result.Add(members[i]);
                }

                at = points[result[result.Count - 1]];
            }

            return result.ToArray();
        }
    }
}
