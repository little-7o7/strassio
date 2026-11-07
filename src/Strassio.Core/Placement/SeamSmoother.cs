using System;
using System.Collections.Generic;
using System.Linq;
using Strassio.Core.Geometry;

namespace Strassio.Core.Placement
{
    /// <summary>
    /// Шов в контурной заливке (docs/SPEC.md, раздел 5, F3/F4): там, где ряды середины упираются в крайнее
    /// кольцо (или кольца сходятся посередине), остаётся цепочка дырок — глаз видит её как шов. Камни возле
    /// дырок чуть раздвигаются, как рука подталкивает их пинцетом: каждый отходит от слишком близких соседей
    /// и смещается в сторону пустоты. Одна большая дырка расходится на много маленьких просветов, а где
    /// места стало на целый камень — туда кладётся камень. Крайние кольца (по контуру) не двигаются, и
    /// далеко от дырок ничего не меняется.
    /// </summary>
    public static class SeamSmoother
    {
        /// <summary>Дырка — место, куда между камнями влезает круг больше этой доли камня.</summary>
        private const double HoleShare = 0.44;

        /// <summary>
        /// Камни отталкиваются, пока до соседа меньше этой доли от «вплотную с зазором». В ровных сотах силы
        /// со всех сторон равны и камень стоит; у дырки соседа с одной стороны нет — камень отходит в дырку.
        /// 1,26 на пяти фигурах (лист, сердце, овал, капля, лента) дал меньше всего пустоты: 0,4–5 мм² вместо 11–19.
        /// </summary>
        private const double Spread = 1.26;

        /// <param name="fixedRings">Сколько рядов от края не трогать.</param>
        public static List<PlacedStone> Smooth(
            IReadOnlyList<PlacedStone> stones, IReadOnlyList<Curve> contours, double gapMm, double marginMm, int fixedRings = 1)
        {
            if (stones.Count < 3)
            {
                return stones.ToList();
            }

            double d = stones.Min(s => s.DiameterMm);
            double big = stones.Max(s => s.DiameterMm);
            double step = d + gapMm;
            var region = new ShapeRegion(contours, d);

            var centers = stones.Select(s => s.Center).ToArray();
            var radii = stones.Select(s => s.DiameterMm / 2).ToArray();
            int n = centers.Length;
            var grid = new Grid(big + gapMm);
            for (int i = 0; i < n; i++)
            {
                grid.Add(i, centers[i]);
            }

            // Крайние ряды стоят по контуру — их не трогаем.
            double rowStep = step * Math.Sqrt(3) / 2;
            var movable = new bool[n];
            for (int i = 0; i < n; i++)
            {
                double fromEdge = region.Depth(centers[i]) - radii[i] - marginMm;
                movable[i] = fromEdge > (Math.Max(1, fixedRings) - 1) * rowStep + 0.3 * d;
            }

            List<Point2D> holes = FindHoles(region, centers, radii, grid, d, step, marginMm, HoleShare);
            if (holes.Count == 0)
            {
                return stones.ToList();
            }

            // Двигаются только камни рядом с дырками.
            var near = new bool[n];
            double reach = 2.5 * step;
            foreach (Point2D h in holes)
            {
                foreach (int i in grid.Around(h, reach))
                {
                    near[i] |= movable[i] && Point2D.Distance(centers[i], h) <= reach;
                }
            }

            int[] active = Enumerable.Range(0, n).Where(i => near[i]).ToArray();
            for (int iteration = 0; iteration < 60; iteration++)
            {
                double moved = 0;
                foreach (int i in active)
                {
                    Point2D push = Point2D.Zero;
                    foreach (int j in grid.Around(centers[i], 1.3 * step))
                    {
                        if (j == i)
                        {
                            continue;
                        }

                        Point2D away = centers[i] - centers[j];
                        double dist = away.Length;
                        double target = (radii[i] + radii[j] + gapMm) * Spread;
                        if (dist < target && dist > 1e-9)
                        {
                            push += away / dist * (target - dist);
                        }
                    }

                    double length = push.Length;
                    if (length < 1e-4)
                    {
                        continue;
                    }

                    Point2D delta = push * (Math.Min(0.4 * length, 0.08 * d) / length);
                    for (int attempt = 0; attempt < 3; attempt++, delta = delta * 0.5)
                    {
                        Point2D candidate = centers[i] + delta;
                        if (Allowed(i, candidate, region, centers, radii, grid, gapMm, marginMm))
                        {
                            grid.Move(i, centers[i], candidate);
                            centers[i] = candidate;
                            moved = Math.Max(moved, delta.Length);
                            break;
                        }
                    }
                }

                if (moved < 0.002)
                {
                    break;
                }
            }

            var result = new List<PlacedStone>(n);
            for (int i = 0; i < n; i++)
            {
                result.Add(new PlacedStone(centers[i], stones[i].DiameterMm, stones[i].IsCorner, stones[i].RowId));
            }

            // Где после раздвигания влез целый камень — кладём (только если камни одного размера).
            return stones.All(s => Math.Abs(s.DiameterMm - d) < 1e-6)
                ? AdvancedFillers.AddMissingStones(result, contours, d, gapMm, marginMm)
                : result;
        }

        /// <summary>Дырка, которую видно глазом: между камнями влезает круг больше 0,6 камня.</summary>
        private const double VisibleShare = 0.6;

        /// <summary>Сколько заметных дырок в раскладке (для сравнения «до и после»).</summary>
        public static int CountHoles(IReadOnlyList<PlacedStone> stones, IReadOnlyList<Curve> contours, double gapMm, double marginMm)
        {
            if (stones.Count == 0)
            {
                return 0;
            }

            double d = stones.Min(s => s.DiameterMm);
            var region = new ShapeRegion(contours, d);
            var centers = stones.Select(s => s.Center).ToArray();
            var radii = stones.Select(s => s.DiameterMm / 2).ToArray();
            var grid = new Grid(stones.Max(s => s.DiameterMm) + gapMm);
            for (int i = 0; i < centers.Length; i++)
            {
                grid.Add(i, centers[i]);
            }

            return FindHoles(region, centers, radii, grid, d, d + gapMm, marginMm, VisibleShare).Count;
        }

        /// <summary>
        /// Дырки внутри крайнего кольца: точки сетки (шаг — четверть камня) глубже камня от края, где до
        /// ближайшего камня (по его краю) дальше <paramref name="share"/>/2 камня. Точки одной дырки — одна.
        /// </summary>
        private static List<Point2D> FindHoles(
            ShapeRegion region, Point2D[] centers, double[] radii, Grid grid, double d, double step, double margin, double share)
        {
            var holes = new List<Point2D>();
            double cell = d / 4;
            double limit = share * d / 2;
            for (double x = region.Min.X; x <= region.Max.X; x += cell)
            {
                for (double y = region.Min.Y; y <= region.Max.Y; y += cell)
                {
                    var q = new Point2D(x, y);
                    if (region.Depth(q) < d + margin)
                    {
                        continue;
                    }

                    double clear = double.MaxValue;
                    foreach (int i in grid.Around(q, 2 * step))
                    {
                        clear = Math.Min(clear, Point2D.Distance(centers[i], q) - radii[i]);
                    }

                    if (clear > limit && holes.All(h => Point2D.Distance(h, q) > d))
                    {
                        holes.Add(q);
                    }
                }
            }

            return holes;
        }

        private static bool Allowed(
            int i, Point2D p, ShapeRegion region, Point2D[] centers, double[] radii, Grid grid, double gap, double margin)
        {
            if (!region.Fits(p, radii[i], margin))
            {
                return false;
            }

            foreach (int j in grid.Around(p, 2 * radii[i] + gap + 0.5))
            {
                if (j != i && Point2D.Distance(centers[j], p) < radii[i] + radii[j] + gap - StonePacker.Tolerance)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Номера камней по клеткам — соседи ищутся только рядом.</summary>
        private sealed class Grid
        {
            private readonly double cell;
            private readonly Dictionary<(int, int), List<int>> cells = new Dictionary<(int, int), List<int>>();

            public Grid(double cell)
            {
                this.cell = Math.Max(0.1, cell);
            }

            private (int, int) Key(Point2D p) => ((int)Math.Floor(p.X / cell), (int)Math.Floor(p.Y / cell));

            public void Add(int i, Point2D p)
            {
                if (!cells.TryGetValue(Key(p), out List<int>? list))
                {
                    list = new List<int>();
                    cells[Key(p)] = list;
                }

                list.Add(i);
            }

            public void Move(int i, Point2D from, Point2D to)
            {
                if (Key(from) == Key(to))
                {
                    return;
                }

                cells[Key(from)].Remove(i);
                Add(i, to);
            }

            public IEnumerable<int> Around(Point2D p, double radius)
            {
                int r = (int)Math.Ceiling(radius / cell);
                (int cx, int cy) = Key(p);
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dy = -r; dy <= r; dy++)
                    {
                        if (cells.TryGetValue((cx + dx, cy + dy), out List<int>? list))
                        {
                            foreach (int i in list)
                            {
                                yield return i;
                            }
                        }
                    }
                }
            }
        }
    }
}
