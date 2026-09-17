using System;
using System.Collections.Generic;

namespace Strassio.Core.Geometry
{
    /// <summary>
    /// Достаёт из карты расстояний (<see cref="SignedDistanceField"/>) линию равного расстояния
    /// до края — «пройди по форме везде, где до края ровно 3 мм». Это и есть ряд контурной заливки.
    ///
    /// Способ обычный для такой задачи («шагающие квадраты»): сетка обходится клетка за клеткой,
    /// в каждой смотрим, какие её углы уже «дальше» нужного расстояния, а какие ещё «ближе», и
    /// проводим через клетку отрезок ровно там, где расстояние равно искомому. Потом отрезки
    /// сшиваются в замкнутые кольца.
    ///
    /// Важное свойство: такие кольца никогда не пересекают сами себя и не налезают друг на друга,
    /// а там, где форма распадается (лучи звезды отходят от середины), кольцо само распадается
    /// на несколько — именно этого не умело смещение контура.
    /// </summary>
    public static class IsoContour
    {
        /// <summary>
        /// Все замкнутые кольца на расстоянии levelMm от края формы. Возвращает пустой список,
        /// если так глубоко внутрь формы уже ничего нет.
        /// </summary>
        public static List<List<Point2D>> Trace(SignedDistanceField field, double levelMm)
        {
            if (field == null)
            {
                throw new ArgumentNullException(nameof(field));
            }

            // Точка пересечения живёт на ребре сетки. Ребро — надёжный «номер» точки: у двух
            // соседних клеток это одно и то же ребро, поэтому отрезки сшиваются без возни
            // со сравнением дробных координат.
            var links = new Dictionary<int, List<int>>();
            var edgePoints = new Dictionary<int, Point2D>();

            int nx = field.Width;
            int ny = field.Height;

            for (int iy = 0; iy < ny - 1; iy++)
            {
                for (int ix = 0; ix < nx - 1; ix++)
                {
                    double v00 = field.ValueAt(ix, iy);
                    double v10 = field.ValueAt(ix + 1, iy);
                    double v11 = field.ValueAt(ix + 1, iy + 1);
                    double v01 = field.ValueAt(ix, iy + 1);

                    int code = 0;
                    if (v00 >= levelMm) code |= 1;
                    if (v10 >= levelMm) code |= 2;
                    if (v11 >= levelMm) code |= 4;
                    if (v01 >= levelMm) code |= 8;

                    if (code == 0 || code == 15)
                    {
                        continue;
                    }

                    // Спорные случаи 5 и 10 (две противоположные «дальние» вершины): решаем по
                    // среднему значению в середине клетки — стандартный приём.
                    if (code == 5 || code == 10)
                    {
                        bool centerFar = (v00 + v10 + v11 + v01) / 4 >= levelMm;
                        if (code == 5)
                        {
                            if (centerFar)
                            {
                                Connect(links, edgePoints, field, levelMm, ix, iy, Edge.Left, Edge.Top, v00, v10, v11, v01);
                                Connect(links, edgePoints, field, levelMm, ix, iy, Edge.Bottom, Edge.Right, v00, v10, v11, v01);
                            }
                            else
                            {
                                Connect(links, edgePoints, field, levelMm, ix, iy, Edge.Left, Edge.Bottom, v00, v10, v11, v01);
                                Connect(links, edgePoints, field, levelMm, ix, iy, Edge.Right, Edge.Top, v00, v10, v11, v01);
                            }
                        }
                        else
                        {
                            if (centerFar)
                            {
                                Connect(links, edgePoints, field, levelMm, ix, iy, Edge.Left, Edge.Bottom, v00, v10, v11, v01);
                                Connect(links, edgePoints, field, levelMm, ix, iy, Edge.Right, Edge.Top, v00, v10, v11, v01);
                            }
                            else
                            {
                                Connect(links, edgePoints, field, levelMm, ix, iy, Edge.Left, Edge.Top, v00, v10, v11, v01);
                                Connect(links, edgePoints, field, levelMm, ix, iy, Edge.Bottom, Edge.Right, v00, v10, v11, v01);
                            }
                        }

                        continue;
                    }

                    Edge a, b;
                    switch (code)
                    {
                        case 1: a = Edge.Left; b = Edge.Bottom; break;
                        case 2: a = Edge.Bottom; b = Edge.Right; break;
                        case 3: a = Edge.Left; b = Edge.Right; break;
                        case 4: a = Edge.Right; b = Edge.Top; break;
                        case 6: a = Edge.Bottom; b = Edge.Top; break;
                        case 7: a = Edge.Left; b = Edge.Top; break;
                        case 8: a = Edge.Top; b = Edge.Left; break;
                        case 9: a = Edge.Bottom; b = Edge.Top; break;
                        case 11: a = Edge.Right; b = Edge.Top; break;
                        case 12: a = Edge.Left; b = Edge.Right; break;
                        case 13: a = Edge.Bottom; b = Edge.Right; break;
                        case 14: a = Edge.Left; b = Edge.Bottom; break;
                        default: continue;
                    }

                    Connect(links, edgePoints, field, levelMm, ix, iy, a, b, v00, v10, v11, v01);
                }
            }

            return BuildLoops(links, edgePoints);
        }

        private enum Edge
        {
            Bottom,
            Right,
            Top,
            Left,
        }

        /// <summary>
        /// Номер ребра сетки. Горизонтальные и вертикальные рёбра нумеруются подряд, в двух
        /// диапазонах — так один и тот же номер получается у обеих клеток, которым ребро общее.
        /// </summary>
        private static int EdgeId(SignedDistanceField field, int ix, int iy, Edge edge)
        {
            int nx = field.Width;
            int ny = field.Height;
            int horizontalCount = (nx - 1) * ny;

            switch (edge)
            {
                case Edge.Bottom: return iy * (nx - 1) + ix;
                case Edge.Top: return (iy + 1) * (nx - 1) + ix;
                case Edge.Left: return horizontalCount + iy * nx + ix;
                default: return horizontalCount + iy * nx + (ix + 1);
            }
        }

        private static Point2D EdgePoint(
            SignedDistanceField field, double level, int ix, int iy, Edge edge,
            double v00, double v10, double v11, double v01)
        {
            switch (edge)
            {
                case Edge.Bottom:
                    return Interpolate(field.PositionOf(ix, iy), v00, field.PositionOf(ix + 1, iy), v10, level);
                case Edge.Top:
                    return Interpolate(field.PositionOf(ix, iy + 1), v01, field.PositionOf(ix + 1, iy + 1), v11, level);
                case Edge.Left:
                    return Interpolate(field.PositionOf(ix, iy), v00, field.PositionOf(ix, iy + 1), v01, level);
                default:
                    return Interpolate(field.PositionOf(ix + 1, iy), v10, field.PositionOf(ix + 1, iy + 1), v11, level);
            }
        }

        /// <summary>Где на ребре расстояние равно искомому — линейно между значениями на концах.</summary>
        private static Point2D Interpolate(Point2D a, double va, Point2D b, double vb, double level)
        {
            double denominator = vb - va;
            if (Math.Abs(denominator) < 1e-12)
            {
                return Point2D.Lerp(a, b, 0.5);
            }

            double t = (level - va) / denominator;
            if (t < 0) t = 0;
            if (t > 1) t = 1;

            return Point2D.Lerp(a, b, t);
        }

        private static void Connect(
            Dictionary<int, List<int>> links, Dictionary<int, Point2D> edgePoints,
            SignedDistanceField field, double level, int ix, int iy, Edge a, Edge b,
            double v00, double v10, double v11, double v01)
        {
            int idA = EdgeId(field, ix, iy, a);
            int idB = EdgeId(field, ix, iy, b);
            if (idA == idB)
            {
                return;
            }

            if (!edgePoints.ContainsKey(idA))
            {
                edgePoints[idA] = EdgePoint(field, level, ix, iy, a, v00, v10, v11, v01);
            }

            if (!edgePoints.ContainsKey(idB))
            {
                edgePoints[idB] = EdgePoint(field, level, ix, iy, b, v00, v10, v11, v01);
            }

            AddLink(links, idA, idB);
            AddLink(links, idB, idA);
        }

        private static void AddLink(Dictionary<int, List<int>> links, int from, int to)
        {
            if (!links.TryGetValue(from, out List<int> list))
            {
                list = new List<int>(2);
                links[from] = list;
            }

            if (!list.Contains(to))
            {
                list.Add(to);
            }
        }

        /// <summary>Сшивает отрезки в кольца: у каждой точки ровно два соседа, идём по цепочке.</summary>
        private static List<List<Point2D>> BuildLoops(
            Dictionary<int, List<int>> links, Dictionary<int, Point2D> edgePoints)
        {
            var loops = new List<List<Point2D>>();
            var visited = new HashSet<int>();

            foreach (int start in links.Keys)
            {
                if (visited.Contains(start))
                {
                    continue;
                }

                var loop = new List<Point2D>();
                int current = start;
                int previous = -1;

                while (true)
                {
                    visited.Add(current);
                    loop.Add(edgePoints[current]);

                    int next = -1;
                    foreach (int candidate in links[current])
                    {
                        if (candidate != previous && !visited.Contains(candidate))
                        {
                            next = candidate;
                            break;
                        }
                    }

                    if (next < 0)
                    {
                        break;
                    }

                    previous = current;
                    current = next;
                }

                // Замыкаем дублем первой точки — тот же вид, что у FlattenedCurve замкнутой кривой.
                if (loop.Count >= 3)
                {
                    loop.Add(loop[0]);
                    loops.Add(loop);
                }
            }

            return loops;
        }
    }
}
