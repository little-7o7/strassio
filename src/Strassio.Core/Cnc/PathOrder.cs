using System;
using System.Collections.Generic;
using System.Diagnostics;
using Strassio.Core.Geometry;

namespace Strassio.Core.Cnc
{
    /// <summary>
    /// Порядок обхода отверстий — чтобы станок не бегал зря через весь лист. Сначала «ближайший
    /// следующий» от нуля станка, потом улучшение 2-opt (распутывает перекрещенные переходы).
    /// Обе части работают через сетку соседей, поэтому 10 000 отверстий — доли секунды.
    /// </summary>
    public static partial class PathOrder
    {
        /// <summary>Сколько ближайших соседей перебирать при улучшении.</summary>
        private const int Neighbors = 8;

        /// <param name="points">Отверстия.</param>
        /// <param name="start">Откуда стартует станок (обычно ноль X0 Y0).</param>
        /// <param name="improve">Улучшать ли путь после «ближайшего следующего».</param>
        /// <param name="budgetMs">Предел времени на улучшение (путь всё равно правильный, просто чуть длиннее).</param>
        /// <returns>Номера отверстий в порядке обхода — каждое ровно один раз.</returns>
        public static int[] Order(IReadOnlyList<Point2D> points, Point2D start, bool improve = true, int budgetMs = 1500)
        {
            int n = points.Count;
            if (n == 0)
            {
                return Array.Empty<int>();
            }

            var grid = new PointGrid(points);
            int[] tour = NearestNeighbour(points, start, grid);
            if (improve && n > 3)
            {
                TwoOpt(points, start, tour, grid, budgetMs);
            }

            return tour;
        }

        /// <summary>Длина холостого пути: от старта по порядку, при <paramref name="returnToStart"/> — обратно.</summary>
        public static double Length(IReadOnlyList<Point2D> points, IReadOnlyList<int> order, Point2D start, bool returnToStart)
        {
            double length = 0;
            Point2D at = start;
            foreach (int i in order)
            {
                length += Point2D.Distance(at, points[i]);
                at = points[i];
            }

            if (returnToStart)
            {
                length += Point2D.Distance(at, start);
            }

            return length;
        }

        private static int[] NearestNeighbour(IReadOnlyList<Point2D> points, Point2D start, PointGrid grid)
        {
            int n = points.Count;
            var cells = new List<int>[grid.Cols * grid.Rows];
            var slot = new int[n];
            for (int i = 0; i < n; i++)
            {
                int c = grid.CellOf(points[i]);
                List<int> list = cells[c] ??= new List<int>();
                slot[i] = list.Count;
                list.Add(i);
            }

            void Remove(int i)
            {
                List<int> list = cells[grid.CellOf(points[i])];
                int last = list[list.Count - 1];
                list[slot[i]] = last;
                slot[last] = slot[i];
                list.RemoveAt(list.Count - 1);
            }

            var order = new int[n];
            Point2D at = start;
            int maxRing = Math.Max(grid.Cols, grid.Rows);
            for (int k = 0; k < n; k++)
            {
                (int cx, int cy) = grid.Clamp(at);
                int best = -1;
                double bestD = double.MaxValue;
                for (int ring = 0; ring <= maxRing + 1; ring++)
                {
                    grid.ForRing(cx, cy, ring, c =>
                    {
                        List<int> list = cells[c];
                        if (list == null)
                        {
                            return;
                        }

                        foreach (int i in list)
                        {
                            Point2D d = points[i] - at;
                            double d2 = d.X * d.X + d.Y * d.Y;
                            if (d2 < bestD || (d2 == bestD && i < best))
                            {
                                bestD = d2;
                                best = i;
                            }
                        }
                    });

                    // Всё, что в следующих кольцах, не ближе ring клеток — если уже нашлось ближе, хватит
                    // (и для старта вне сетки: кольца считаются от ближайшей к нему клетки).
                    double reach = ring * grid.Cell;
                    if (best >= 0 && bestD <= reach * reach)
                    {
                        break;
                    }
                }

                order[k] = best;
                Remove(best);
                at = points[best];
            }

            return order;
        }

        /// <summary>
        /// 2-opt на открытом пути с закреплённым началом (старт станка). Узел 0 тура — старт, дальше
        /// отверстия. Ход (i, j) переворачивает кусок tour[i+1..j]; конец пути свободен.
        /// </summary>
        private static void TwoOpt(IReadOnlyList<Point2D> points, Point2D start, int[] order, PointGrid grid, int budgetMs)
        {
            int n = order.Length;
            var t = new int[n + 1];
            t[0] = -1;
            Array.Copy(order, 0, t, 1, n);
            var pos = new int[n];
            for (int k = 1; k <= n; k++)
            {
                pos[t[k]] = k;
            }

            int[][] near = grid.KNearest(points, Neighbors);
            Point2D P(int v) => v < 0 ? start : points[v];
            double D(int a, int b) => Point2D.Distance(P(a), P(b));

            // Длина ребра от позиции k к k+1; за концом пути ребра нет.
            double Edge(int k) => k >= n ? 0 : D(t[k], t[k + 1]);

            double Delta(int i, int j)
            {
                double removed = Edge(i) + Edge(j);
                double added = D(t[i], t[j]) + (j >= n ? 0 : D(t[i + 1], t[j + 1]));
                return added - removed;
            }

            var queue = new Queue<int>(n);
            var queued = new bool[n];
            for (int k = 1; k <= n; k++)
            {
                queue.Enqueue(t[k]);
                queued[t[k]] = true;
            }

            void Push(int v)
            {
                if (v >= 0 && !queued[v])
                {
                    queued[v] = true;
                    queue.Enqueue(v);
                }
            }

            void Apply(int i, int j)
            {
                Push(t[i]);
                Push(t[i + 1]);
                Push(t[j]);
                if (j < n)
                {
                    Push(t[j + 1]);
                }

                for (int a = i + 1, b = j; a < b; a++, b--)
                {
                    int tmp = t[a];
                    t[a] = t[b];
                    t[b] = tmp;
                    pos[t[a]] = a;
                    pos[t[b]] = b;
                }
            }

            bool TryMove(int i, int j)
            {
                if (i < 0 || j <= i + 1 || j > n)
                {
                    return false;
                }

                if (Delta(i, j) < -1e-9)
                {
                    Apply(i, j);
                    return true;
                }

                return false;
            }

            var clock = Stopwatch.StartNew();
            int checks = 0;
            while (queue.Count > 0)
            {
                if ((++checks & 255) == 0 && clock.ElapsedMilliseconds > budgetMs)
                {
                    break;
                }

                int a = queue.Dequeue();
                queued[a] = false;
                bool moved;
                do
                {
                    moved = false;
                    int p = pos[a];
                    double toNext = p < n ? D(a, t[p + 1]) : double.MaxValue;
                    double toPrev = D(a, t[p - 1]);
                    foreach (int c in near[a])
                    {
                        double ac = D(a, c);
                        int q = pos[c];
                        // Новое ребро a–c вместо ребра к следующему за a…
                        if (ac < toNext && (q > p ? TryMove(p, q) : TryMove(q, p)))
                        {
                            moved = true;
                            break;
                        }

                        // …или вместо ребра к предыдущему.
                        if (ac < toPrev && (q > p ? TryMove(p - 1, q - 1) : TryMove(q - 1, p - 1)))
                        {
                            moved = true;
                            break;
                        }
                    }
                }
                while (moved && clock.ElapsedMilliseconds <= budgetMs);
            }

            Array.Copy(t, 1, order, 0, n);
        }

        /// <summary>Равномерная сетка по рамке точек: около одной точки на клетку.</summary>
        private sealed class PointGrid
        {
            private readonly double minX;
            private readonly double minY;

            public PointGrid(IReadOnlyList<Point2D> points)
            {
                double maxX = double.MinValue, maxY = double.MinValue;
                minX = double.MaxValue;
                minY = double.MaxValue;
                foreach (Point2D p in points)
                {
                    minX = Math.Min(minX, p.X);
                    minY = Math.Min(minY, p.Y);
                    maxX = Math.Max(maxX, p.X);
                    maxY = Math.Max(maxY, p.Y);
                }

                double w = Math.Max(maxX - minX, 1e-6);
                double h = Math.Max(maxY - minY, 1e-6);
                Cell = Math.Max(Math.Sqrt(w * h / points.Count), Math.Max(w, h) / 2048);
                Cell = Math.Max(Cell, 1e-3);
                Cols = (int)Math.Floor(w / Cell) + 1;
                Rows = (int)Math.Floor(h / Cell) + 1;
            }

            public double Cell { get; }

            public int Cols { get; }

            public int Rows { get; }

            public (int X, int Y) Clamp(Point2D p)
            {
                int x = (int)Math.Floor((p.X - minX) / Cell);
                int y = (int)Math.Floor((p.Y - minY) / Cell);
                return (Math.Min(Math.Max(x, 0), Cols - 1), Math.Min(Math.Max(y, 0), Rows - 1));
            }

            public int CellOf(Point2D p)
            {
                (int x, int y) = Clamp(p);
                return y * Cols + x;
            }

            /// <summary>Клетки на расстоянии ровно <paramref name="ring"/> (по Чебышёву) от (cx, cy).</summary>
            public void ForRing(int cx, int cy, int ring, Action<int> visit)
            {
                if (ring == 0)
                {
                    visit(cy * Cols + cx);
                    return;
                }

                int x0 = cx - ring, x1 = cx + ring, y0 = cy - ring, y1 = cy + ring;
                for (int x = Math.Max(x0, 0); x <= Math.Min(x1, Cols - 1); x++)
                {
                    if (y0 >= 0)
                    {
                        visit(y0 * Cols + x);
                    }

                    if (y1 < Rows)
                    {
                        visit(y1 * Cols + x);
                    }
                }

                for (int y = Math.Max(y0 + 1, 0); y <= Math.Min(y1 - 1, Rows - 1); y++)
                {
                    if (x0 >= 0)
                    {
                        visit(y * Cols + x0);
                    }

                    if (x1 < Cols)
                    {
                        visit(y * Cols + x1);
                    }
                }
            }

            /// <summary>Для каждой точки — до <paramref name="k"/> ближайших других (по возрастанию расстояния).</summary>
            public int[][] KNearest(IReadOnlyList<Point2D> points, int k)
            {
                int n = points.Count;
                var cells = new List<int>[Cols * Rows];
                for (int i = 0; i < n; i++)
                {
                    int c = CellOf(points[i]);
                    (cells[c] ??= new List<int>()).Add(i);
                }

                int want = Math.Min(k, n - 1);
                var result = new int[n][];
                var bestI = new int[want];
                var bestD = new double[want];
                int maxRing = Math.Max(Cols, Rows);
                for (int i = 0; i < n; i++)
                {
                    int found = 0;
                    Point2D at = points[i];
                    (int cx, int cy) = Clamp(at);
                    for (int ring = 0; ring <= maxRing; ring++)
                    {
                        ForRing(cx, cy, ring, c =>
                        {
                            List<int> list = cells[c];
                            if (list == null)
                            {
                                return;
                            }

                            foreach (int j in list)
                            {
                                if (j == i)
                                {
                                    continue;
                                }

                                Point2D d = points[j] - at;
                                double d2 = d.X * d.X + d.Y * d.Y;
                                if (found == want && d2 >= bestD[want - 1])
                                {
                                    continue;
                                }

                                int slot = found < want ? found++ : want - 1;
                                while (slot > 0 && bestD[slot - 1] > d2)
                                {
                                    bestD[slot] = bestD[slot - 1];
                                    bestI[slot] = bestI[slot - 1];
                                    slot--;
                                }

                                bestD[slot] = d2;
                                bestI[slot] = j;
                            }
                        });

                        double reach = ring * Cell;
                        if (found == want && bestD[want - 1] <= reach * reach)
                        {
                            break;
                        }
                    }

                    var list2 = new int[found];
                    Array.Copy(bestI, list2, found);
                    result[i] = list2;
                }

                return result;
            }
        }
    }
}
