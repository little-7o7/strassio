using System;
using System.Collections.Generic;
using System.Linq;
using Strassio.Core.Geometry;

namespace Strassio.Core.Editing
{
    /// <summary>
    /// Поиск дырок (docs/SPEC.md, раздел 7.2, [NEW]): пустые места внутри уже выложенных страз, куда
    /// помещается камень. «Внутри» — место окружено стразами со всех четырёх сторон, поэтому
    /// пустота снаружи дизайна дыркой не считается.
    /// </summary>
    public static class HoleFinder
    {
        /// <summary>
        /// Центры новых страз диаметра <paramref name="diameterMm"/> в дырках (не налезают ни на
        /// существующие, ни друг на друга; зазор — <paramref name="gapMm"/>).
        /// </summary>
        public static List<Point2D> Find(IReadOnlyList<DocStone> stones, double diameterMm, double gapMm)
        {
            var holes = new List<Point2D>();
            if (stones.Count < 3)
            {
                return holes;
            }

            double maxD = Math.Max(diameterMm, stones.Max(s => s.DiameterMm));
            var grid = new StoneGrid(maxD + gapMm);
            foreach (DocStone s in stones)
            {
                grid.Add(s.Center, s.Radius);
            }

            // Окрестность, в которой ищем соседей-«стенки» дырки: примерно два шага камня.
            double reach = 2 * (maxD + gapMm);
            var near = new StoneGrid(reach);
            foreach (DocStone s in stones)
            {
                near.Add(s.Center, s.Radius);
            }

            double minX = stones.Min(s => s.Center.X);
            double maxX = stones.Max(s => s.Center.X);
            double minY = stones.Min(s => s.Center.Y);
            double maxY = stones.Max(s => s.Center.Y);
            double step = Math.Max(0.05, diameterMm / 6);
            double radius = diameterMm / 2;

            for (double y = minY; y <= maxY; y += step)
            {
                for (double x = minX; x <= maxX; x += step)
                {
                    var start = new Point2D(x, y);
                    if (!IsSurrounded(near, start, reach))
                    {
                        continue;
                    }

                    // Дырка ровно под камень находится только в одной точке — точки сетки поиска в неё
                    // не попадают. Поэтому точку, где камень «почти» влезает, чуть отталкиваем от соседей.
                    Point2D? p = Settle(grid, start, radius, gapMm);
                    if (p == null || !IsSurrounded(near, p.Value, reach))
                    {
                        continue;
                    }

                    grid.Add(p.Value, radius);
                    holes.Add(p.Value);
                }
            }

            return holes;
        }

        /// <summary>
        /// Сдвигает камень от тех, на кого он налезает (сумма «толчков»), пока он не встанет свободно.
        /// null — налезает слишком сильно (больше трети диаметра) или не встаёт за 30 шагов.
        /// </summary>
        private static Point2D? Settle(StoneGrid grid, Point2D p, double radius, double gap)
        {
            for (int iteration = 0; iteration < 30; iteration++)
            {
                Point2D push = Point2D.Zero;
                double worst = 0;
                foreach (int i in grid.Near(p))
                {
                    (Point2D c, double r) = grid[i];
                    Point2D away = p - c;
                    double dist = away.Length;
                    double need = r + radius + gap;
                    if (dist < need - 1e-9)
                    {
                        double overlap = need - dist;
                        worst = Math.Max(worst, overlap);
                        push += dist > 1e-9 ? away / dist * overlap : new Point2D(overlap, 0);
                    }
                }

                if (worst <= 1e-6)
                {
                    return p;
                }

                if (iteration == 0 && worst > radius * 2 / 3)
                {
                    return null;
                }

                p += push * 0.6;
            }

            return grid.Overlaps(p, radius, gap - 0.01) ? (Point2D?)null : p;
        }

        /// <summary>
        /// Место внутри дизайна, если стразы есть во всех четырёх четвертях вокруг (в одной из двух
        /// раскладок четвертей — прямой или повёрнутой на 45°). Щель в сотах окружена шестью
        /// соседями, в квадратной сетке — четырьмя по диагоналям; снаружи у края дизайна соседи есть
        /// только с одной стороны.
        /// </summary>
        private static bool IsSurrounded(StoneGrid near, Point2D p, double reach)
        {
            var angles = new List<double>();
            foreach (int i in near.Near(p))
            {
                (Point2D c, _) = near[i];
                Point2D d = c - p;
                double dist = d.Length;
                if (dist > 1e-9 && dist <= reach)
                {
                    angles.Add(Math.Atan2(d.Y, d.X));
                }
            }

            foreach (double rotation in new[] { 0.0, Math.PI / 4 })
            {
                var quarters = new bool[4];
                foreach (double a in angles)
                {
                    double t = a - rotation + 2 * Math.PI;
                    quarters[(int)Math.Floor(t / (Math.PI / 2)) % 4] = true;
                }

                if (quarters.All(q => q))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
