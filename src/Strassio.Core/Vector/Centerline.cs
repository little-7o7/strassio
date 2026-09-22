using System;
using System.Collections.Generic;
using System.Linq;
using Strassio.Core.Geometry;

namespace Strassio.Core.Vector
{
    /// <summary>
    /// Центральная линия толстой формы или буквы (docs/SPEC.md, раздел 10): одна линия посередине
    /// «штриха», по которой потом можно пустить ряд страз (L1). Форма раскладывается на мелкую сетку
    /// (карта расстояний до края), сетка истончается до скелета толщиной в одну клетку (Чжан — Суэнь),
    /// короткие «усики» у углов убираются, клетки соединяются в линии, линии упрощаются и сглаживаются.
    /// </summary>
    public static class Centerline
    {
        /// <param name="contours">Контуры формы (внешний и отверстия).</param>
        /// <param name="minBranchMm">Короче этого «усики» (ветки со свободным концом) убираются; 0 — по толщине формы.</param>
        public static List<Curve> Build(IReadOnlyList<Curve> contours, double minBranchMm = 0)
        {
            List<FlattenedCurve> flats = contours.Select(c => CurveFlattener.Flatten(c)).Where(f => f.IsClosed).ToList();
            var result = new List<Curve>();
            if (flats.Count == 0)
            {
                return result;
            }

            // Сначала грубо — узнать толщину, потом сетка мельче толщины примерно в 12 раз.
            (double w, double h) = CurveMetrics.BoundingSize(flats[0]);
            SignedDistanceField probe = SignedDistanceField.Build(flats, Math.Max(0.05, Math.Max(w, h) / 200));
            double thickness = 2 * MaxValue(probe);
            if (thickness <= 0)
            {
                return result;
            }

            double cell = Math.Max(0.02, thickness / 12);
            SignedDistanceField field = SignedDistanceField.Build(flats, cell);
            int nx = field.Width, ny = field.Height;
            var grid = new bool[nx, ny];
            for (int y = 0; y < ny; y++)
            {
                for (int x = 0; x < nx; x++)
                {
                    grid[x, y] = field.ValueAt(x, y) > cell * 0.5;
                }
            }

            Thin(grid, nx, ny);
            RemoveBumps(grid, nx, ny);
            double minBranch = minBranchMm > 0 ? minBranchMm : thickness * 0.5;
            PruneSpurs(grid, nx, ny, (int)Math.Ceiling(minBranch / cell));

            List<List<(int X, int Y)>> paths = TracePaths(grid, nx, ny, out List<bool> closedList);
            for (int i = 0; i < paths.Count; i++)
            {
                List<Point2D> points = paths[i].Select(c => field.PositionOf(c.X, c.Y)).ToList();
                if (PathLength(points) < minBranch * 0.5 || points.Count < 2)
                {
                    continue;
                }

                List<Point2D> simple = VectorTools.Simplify(points, closedList[i], cell * 0.8);
                if (simple.Count < 2)
                {
                    continue;
                }

                result.Add(simple.Count >= 3 ? VectorTools.Smooth(simple, closedList[i]) : Curve.FromPolyline(simple, closedList[i]));
            }

            return result;
        }

        private static double MaxValue(SignedDistanceField f)
        {
            double best = 0;
            for (int y = 0; y < f.Height; y++)
            {
                for (int x = 0; x < f.Width; x++)
                {
                    best = Math.Max(best, f.ValueAt(x, y));
                }
            }

            return best;
        }

        private static double PathLength(List<Point2D> pts)
        {
            double length = 0;
            for (int i = 1; i < pts.Count; i++)
            {
                length += Point2D.Distance(pts[i - 1], pts[i]);
            }

            return length;
        }

        private static readonly (int X, int Y)[] Ring =
        {
            (0, 1), (1, 1), (1, 0), (1, -1), (0, -1), (-1, -1), (-1, 0), (-1, 1),
        };

        private static bool At(bool[,] g, int nx, int ny, int x, int y) => x >= 0 && y >= 0 && x < nx && y < ny && g[x, y];

        /// <summary>Истончение Чжана — Суэня: снимает клетки с краёв, пока не останется линия толщиной в одну клетку.</summary>
        private static void Thin(bool[,] g, int nx, int ny)
        {
            var remove = new List<(int, int)>();
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int pass = 0; pass < 2; pass++)
                {
                    remove.Clear();
                    for (int y = 0; y < ny; y++)
                    {
                        for (int x = 0; x < nx; x++)
                        {
                            if (!g[x, y])
                            {
                                continue;
                            }

                            bool p2 = At(g, nx, ny, x, y + 1), p3 = At(g, nx, ny, x + 1, y + 1), p4 = At(g, nx, ny, x + 1, y);
                            bool p5 = At(g, nx, ny, x + 1, y - 1), p6 = At(g, nx, ny, x, y - 1), p7 = At(g, nx, ny, x - 1, y - 1);
                            bool p8 = At(g, nx, ny, x - 1, y), p9 = At(g, nx, ny, x - 1, y + 1);
                            bool[] n = { p2, p3, p4, p5, p6, p7, p8, p9 };
                            int b = n.Count(v => v);
                            if (b < 2 || b > 6)
                            {
                                continue;
                            }

                            int a = 0;
                            for (int i = 0; i < 8; i++)
                            {
                                if (!n[i] && n[(i + 1) % 8])
                                {
                                    a++;
                                }
                            }

                            if (a != 1)
                            {
                                continue;
                            }

                            bool ok = pass == 0
                                ? !(p2 && p4 && p6) && !(p4 && p6 && p8)
                                : !(p2 && p4 && p8) && !(p2 && p6 && p8);
                            if (ok)
                            {
                                remove.Add((x, y));
                            }
                        }
                    }

                    foreach ((int x, int y) in remove)
                    {
                        g[x, y] = false;
                    }

                    changed |= remove.Count > 0;
                }
            }
        }

        private static int Neighbours(bool[,] g, int nx, int ny, int x, int y) =>
            Ring.Count(d => At(g, nx, ny, x + d.X, y + d.Y));

        /// <summary>
        /// Сколько раз вокруг клетки «пусто» сменяется на «занято»: 1 — конец линии, 2 — середина,
        /// 3 и больше — развилка. В отличие от простого числа соседей, «лесенка» из клеток не считается
        /// развилкой.
        /// </summary>
        private static int Transitions(bool[,] g, int nx, int ny, int x, int y)
        {
            int count = 0;
            for (int i = 0; i < 8; i++)
            {
                (int ax, int ay) = Ring[i];
                (int bx, int by) = Ring[(i + 1) % 8];
                if (!At(g, nx, ny, x + ax, y + ay) && At(g, nx, ny, x + bx, y + by))
                {
                    count++;
                }
            }

            return count;
        }

        private static bool IsEnd(bool[,] g, int nx, int ny, int x, int y) => Neighbours(g, nx, ny, x, y) == 1;

        /// <summary>
        /// После истончения остаются «бугорки» — клетки, у которых все соседи стоят рядом друг с другом
        /// (один переход вокруг, но соседей два и больше). Линию они не разрывают, а концы изображают —
        /// из-за них кольцо развалилось бы на куски. Убираем, пока такие есть.
        /// </summary>
        private static void RemoveBumps(bool[,] g, int nx, int ny)
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int y = 0; y < ny; y++)
                {
                    for (int x = 0; x < nx; x++)
                    {
                        if (g[x, y] && Neighbours(g, nx, ny, x, y) >= 2 && Transitions(g, nx, ny, x, y) == 1)
                        {
                            g[x, y] = false;
                            changed = true;
                        }
                    }
                }
            }
        }

        private static bool IsJunction(bool[,] g, int nx, int ny, int x, int y) => Transitions(g, nx, ny, x, y) >= 3;

        /// <summary>Убирает «усики» — ветки со свободным концом короче <paramref name="maxLength"/> клеток.</summary>
        private static void PruneSpurs(bool[,] g, int nx, int ny, int maxLength)
        {
            for (int round = 0; round < 3; round++)
            {
                bool any = false;
                for (int y = 0; y < ny; y++)
                {
                    for (int x = 0; x < nx; x++)
                    {
                        if (!g[x, y] || !IsEnd(g, nx, ny, x, y))
                        {
                            continue;
                        }

                        // Идём от свободного конца до развилки.
                        var branch = new List<(int, int)> { (x, y) };
                        (int cx, int cy) = (x, y);
                        (int px, int py) = (-1, -1);
                        bool reachedJunction = false;
                        while (branch.Count <= maxLength)
                        {
                            (int X, int Y)[] next = Ring
                                .Select(d => (X: cx + d.X, Y: cy + d.Y))
                                .Where(c => At(g, nx, ny, c.X, c.Y) && (c.X != px || c.Y != py) && !branch.Contains((c.X, c.Y)))
                                .OrderBy(c => Math.Abs(c.X - cx) + Math.Abs(c.Y - cy))
                                .ToArray();
                            if (next.Length == 0)
                            {
                                break;
                            }

                            if (next.Length > 1 && IsJunction(g, nx, ny, cx, cy))
                            {
                                reachedJunction = true;
                                break;
                            }

                            (px, py) = (cx, cy);
                            (cx, cy) = next[0];
                            if (IsJunction(g, nx, ny, cx, cy))
                            {
                                reachedJunction = true;
                                break;
                            }

                            branch.Add((cx, cy));
                        }

                        if (reachedJunction && branch.Count <= maxLength)
                        {
                            foreach ((int bx, int by) in branch)
                            {
                                g[bx, by] = false;
                            }

                            any = true;
                        }
                    }
                }

                if (!any)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Скелет → линии. Начинаем с клеток, у которых меньше всего непройденных соседей (концы
        /// линий), и идём по непройденным клеткам (сначала соседи по стороне, потом по диагонали — так
        /// «лесенка» проходится целиком). Ветка, начавшаяся или закончившаяся рядом с уже пройденной
        /// линией, к ней пристёгивается — развилки не рвутся. Линия, чей конец касается начала, — кольцо.
        /// </summary>
        private static List<List<(int X, int Y)>> TracePaths(bool[,] g, int nx, int ny, out List<bool> closed)
        {
            var closedFlags = new List<bool>();
            closed = closedFlags;
            var paths = new List<List<(int X, int Y)>>();
            var visited = new bool[nx, ny];
            var cells = new List<(int X, int Y)>();
            for (int y = 0; y < ny; y++)
            {
                for (int x = 0; x < nx; x++)
                {
                    if (g[x, y])
                    {
                        cells.Add((x, y));
                    }
                }
            }

            IEnumerable<(int X, int Y)> Around((int X, int Y) c) =>
                Ring.Select(d => (X: c.X + d.X, Y: c.Y + d.Y))
                    .Where(p => At(g, nx, ny, p.X, p.Y))
                    .OrderBy(p => Math.Abs(p.X - c.X) + Math.Abs(p.Y - c.Y));

            bool Touch((int X, int Y) a, (int X, int Y) b) => Math.Abs(a.X - b.X) <= 1 && Math.Abs(a.Y - b.Y) <= 1;

            while (true)
            {
                (int X, int Y)? start = null;
                int fewest = int.MaxValue;
                foreach ((int X, int Y) c in cells)
                {
                    if (visited[c.X, c.Y])
                    {
                        continue;
                    }

                    int free = Around(c).Count(p => !visited[p.X, p.Y]);
                    if (free < fewest)
                    {
                        fewest = free;
                        start = c;
                    }
                }

                if (start == null)
                {
                    break;
                }

                var path = new List<(int X, int Y)>();
                (int X, int Y)[] joinStart = Around(start.Value).Where(p => visited[p.X, p.Y]).ToArray();
                if (joinStart.Length > 0)
                {
                    path.Add(joinStart[0]);
                }

                (int X, int Y) cur = start.Value;
                while (true)
                {
                    path.Add(cur);
                    visited[cur.X, cur.Y] = true;
                    (int X, int Y)[] next = Around(cur).Where(p => !visited[p.X, p.Y]).ToArray();
                    if (next.Length == 0)
                    {
                        break;
                    }

                    cur = next[0];
                }

                bool loop = joinStart.Length == 0 && path.Count > 3 && Touch(path[path.Count - 1], path[0]);
                if (!loop)
                {
                    // Конец ветки рядом с уже пройденной линией (развилка) — пристёгиваем.
                    (int X, int Y) last = path[path.Count - 1];
                    (int X, int Y) before = path.Count > 1 ? path[path.Count - 2] : last;
                    (int X, int Y)[] joinEnd = Around(last)
                        .Where(p => visited[p.X, p.Y] && p != before && !path.Skip(Math.Max(0, path.Count - 4)).Contains(p))
                        .ToArray();
                    if (joinEnd.Length > 0)
                    {
                        path.Add(joinEnd[0]);
                    }
                }

                paths.Add(path);
                closedFlags.Add(loop);
            }

            return paths;
        }
    }
}
