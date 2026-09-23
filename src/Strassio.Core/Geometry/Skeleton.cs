using System;
using System.Collections.Generic;

namespace Strassio.Core.Geometry
{
    /// <summary>
    /// Срединная линия фигуры («скелет») и расстояние до неё.
    ///
    /// Зачем. Ряды страз в ручной работе идут вдоль формы и встречаются ровно посередине — как
    /// прожилка листа. Если класть ряды только от края внутрь, то с двух сторон они приходят под
    /// разными углами и на стыке получается белый завиток (сравнение автора: зелёная работа против
    /// красной). Чтобы ряды сошлись красиво, нужно знать, где у фигуры середина.
    ///
    /// Как считается. Фигура превращается в чёрно-белую картинку по карте расстояний
    /// (<see cref="SignedDistanceField"/>), и картинка «утоньшается» до линии толщиной в одну
    /// клетку — это классическое утоньшение Чжан-Суэня: за проход убираются только те клетки,
    /// без которых фигура не разорвётся и не укоротится. Потом двумя проходами по сетке считается
    /// расстояние от каждой клетки до ближайшей клетки скелета.
    /// </summary>
    public sealed class Skeleton
    {
        private readonly double[] distance;

        private Skeleton(bool[] cells, double[] distance, int width, int height, double cellSizeMm, Point2D origin)
        {
            Cells = cells;
            this.distance = distance;
            Width = width;
            Height = height;
            CellSizeMm = cellSizeMm;
            Origin = origin;
        }

        /// <summary>true в клетках, которые остались после утоньшения (сама срединная линия).</summary>
        public bool[] Cells { get; }

        public int Width { get; }

        public int Height { get; }

        public double CellSizeMm { get; }

        public Point2D Origin { get; }

        /// <summary>Расстояние от клетки до ближайшей клетки срединной линии, мм.</summary>
        public double DistanceAt(int ix, int iy) => distance[iy * Width + ix];

        /// <summary>
        /// Срединная линия как упорядоченная ломаная — по ней можно вести ряд страз («прожилка»).
        ///
        /// После утоньшения остаются не только главная линия, но и короткие «усы» к углам и кончикам.
        /// Берём самый длинный путь: от любой клетки ищем самую дальнюю, от неё — снова самую дальнюю;
        /// путь между ними и есть главная линия (обычный приём для древовидных фигур). Ломаная потом
        /// сглаживается скользящим средним, иначе она идёт ступеньками по клеткам сетки.
        /// </summary>
        public List<Point2D> LongestPath()
        {
            int start = -1;
            for (int i = 0; i < Cells.Length; i++)
            {
                if (Cells[i])
                {
                    start = i;
                    break;
                }
            }

            if (start < 0)
            {
                return new List<Point2D>();
            }

            int farthest = FarthestFrom(start, out _);
            FarthestFrom(farthest, out int[] cameFrom);

            int end = -1;
            double bestDistance = -1;
            for (int i = 0; i < Cells.Length; i++)
            {
                if (Cells[i] && cameFrom[i] != -2)
                {
                    double d = PathLength(i, cameFrom);
                    if (d > bestDistance)
                    {
                        bestDistance = d;
                        end = i;
                    }
                }
            }

            var path = new List<Point2D>();
            for (int i = end; i >= 0; i = cameFrom[i])
            {
                path.Add(new Point2D(Origin.X + (i % Width) * CellSizeMm, Origin.Y + (i / Width) * CellSizeMm));
                if (cameFrom[i] == i)
                {
                    break;
                }
            }

            return Smooth(path);
        }

        /// <summary>Обход в ширину по клеткам скелета: возвращает самую дальнюю клетку и откуда пришли.</summary>
        private int FarthestFrom(int start, out int[] cameFrom)
        {
            var from = new int[Cells.Length];
            for (int i = 0; i < from.Length; i++)
            {
                from[i] = -2; // не посещали
            }

            var queue = new Queue<int>();
            queue.Enqueue(start);
            from[start] = start;
            int last = start;

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                last = current;
                int cx = current % Width;
                int cy = current / Width;

                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0)
                        {
                            continue;
                        }

                        int nx = cx + dx, ny = cy + dy;
                        if (nx < 0 || ny < 0 || nx >= Width || ny >= Height)
                        {
                            continue;
                        }

                        int next = ny * Width + nx;
                        if (Cells[next] && from[next] == -2)
                        {
                            from[next] = current;
                            queue.Enqueue(next);
                        }
                    }
                }
            }

            cameFrom = from;
            return last;
        }

        private double PathLength(int index, int[] cameFrom)
        {
            double length = 0;
            int current = index;
            int guard = 0;

            while (cameFrom[current] != current && guard++ < 1_000_000)
            {
                int previous = cameFrom[current];
                int dx = (current % Width) - (previous % Width);
                int dy = (current / Width) - (previous / Width);
                length += dx != 0 && dy != 0 ? 1.41421356 : 1;
                current = previous;
            }

            return length;
        }

        /// <summary>Сглаживание ломаной скользящим средним — убирает ступеньки сетки.</summary>
        private static List<Point2D> Smooth(List<Point2D> path)
        {
            if (path.Count < 5)
            {
                return path;
            }

            const int Window = 4;
            var smoothed = new List<Point2D>(path.Count);

            for (int i = 0; i < path.Count; i++)
            {
                double sx = 0, sy = 0;
                int n = 0;
                for (int k = -Window; k <= Window; k++)
                {
                    int j = i + k;
                    if (j >= 0 && j < path.Count)
                    {
                        sx += path[j].X;
                        sy += path[j].Y;
                        n++;
                    }
                }

                smoothed.Add(new Point2D(sx / n, sy / n));
            }

            return smoothed;
        }

        /// <summary>Строит срединную линию по карте расстояний до края фигуры.</summary>
        public static Skeleton Build(SignedDistanceField field)
        {
            if (field == null)
            {
                throw new ArgumentNullException(nameof(field));
            }

            int w = field.Width;
            int h = field.Height;
            var inside = new bool[w * h];
            for (int iy = 0; iy < h; iy++)
            {
                for (int ix = 0; ix < w; ix++)
                {
                    inside[iy * w + ix] = field.ValueAt(ix, iy) > 0;
                }
            }

            bool[] thinned = Thin(inside, w, h);
            double[] dist = DistanceToCells(thinned, w, h, field.CellSizeMm);
            return new Skeleton(thinned, dist, w, h, field.CellSizeMm, field.Origin);
        }

        /// <summary>
        /// Утоньшение Чжан-Суэня: пока есть что убирать, за два прохода снимаем краевые клетки,
        /// от которых фигура не разваливается. Остаётся линия толщиной в одну клетку.
        /// </summary>
        private static bool[] Thin(bool[] source, int w, int h)
        {
            var image = (bool[])source.Clone();
            var toRemove = new List<int>();

            for (int round = 0; round < 500; round++)
            {
                bool changed = false;

                for (int stepNumber = 0; stepNumber < 2; stepNumber++)
                {
                    toRemove.Clear();

                    for (int iy = 1; iy < h - 1; iy++)
                    {
                        for (int ix = 1; ix < w - 1; ix++)
                        {
                            int i = iy * w + ix;
                            if (!image[i])
                            {
                                continue;
                            }

                            // Соседи по кругу, начиная с верхнего и по часовой стрелке.
                            bool p2 = image[(iy - 1) * w + ix];
                            bool p3 = image[(iy - 1) * w + ix + 1];
                            bool p4 = image[iy * w + ix + 1];
                            bool p5 = image[(iy + 1) * w + ix + 1];
                            bool p6 = image[(iy + 1) * w + ix];
                            bool p7 = image[(iy + 1) * w + ix - 1];
                            bool p8 = image[iy * w + ix - 1];
                            bool p9 = image[(iy - 1) * w + ix - 1];

                            int filled = Count(p2, p3, p4, p5, p6, p7, p8, p9);
                            if (filled < 2 || filled > 6)
                            {
                                continue;
                            }

                            if (Transitions(p2, p3, p4, p5, p6, p7, p8, p9) != 1)
                            {
                                continue;
                            }

                            bool canRemove = stepNumber == 0
                                ? (!p2 || !p4 || !p6) && (!p4 || !p6 || !p8)
                                : (!p2 || !p4 || !p8) && (!p2 || !p6 || !p8);

                            if (canRemove)
                            {
                                toRemove.Add(i);
                            }
                        }
                    }

                    foreach (int i in toRemove)
                    {
                        image[i] = false;
                        changed = true;
                    }
                }

                if (!changed)
                {
                    break;
                }
            }

            return image;
        }

        private static int Count(params bool[] values)
        {
            int n = 0;
            foreach (bool v in values)
            {
                if (v)
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>Сколько раз по кругу соседей пусто сменяется занятым — проверка связности.</summary>
        private static int Transitions(params bool[] values)
        {
            int n = 0;
            for (int i = 0; i < values.Length; i++)
            {
                if (!values[i] && values[(i + 1) % values.Length])
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>
        /// Расстояние до ближайшей отмеченной клетки — два прохода по сетке (вперёд и назад),
        /// шаг по стороне 1, по диагонали 1,41. Быстро и достаточно точно для рядов.
        /// </summary>
        private static double[] DistanceToCells(bool[] cells, int w, int h, double cellSizeMm)
        {
            const double Far = 1e9;
            const double Side = 1.0;
            const double Diagonal = 1.41421356;

            var d = new double[w * h];
            for (int i = 0; i < d.Length; i++)
            {
                d[i] = cells[i] ? 0 : Far;
            }

            for (int iy = 0; iy < h; iy++)
            {
                for (int ix = 0; ix < w; ix++)
                {
                    int i = iy * w + ix;
                    if (ix > 0)
                    {
                        d[i] = Math.Min(d[i], d[i - 1] + Side);
                    }

                    if (iy > 0)
                    {
                        d[i] = Math.Min(d[i], d[i - w] + Side);
                        if (ix > 0)
                        {
                            d[i] = Math.Min(d[i], d[i - w - 1] + Diagonal);
                        }

                        if (ix < w - 1)
                        {
                            d[i] = Math.Min(d[i], d[i - w + 1] + Diagonal);
                        }
                    }
                }
            }

            for (int iy = h - 1; iy >= 0; iy--)
            {
                for (int ix = w - 1; ix >= 0; ix--)
                {
                    int i = iy * w + ix;
                    if (ix < w - 1)
                    {
                        d[i] = Math.Min(d[i], d[i + 1] + Side);
                    }

                    if (iy < h - 1)
                    {
                        d[i] = Math.Min(d[i], d[i + w] + Side);
                        if (ix < w - 1)
                        {
                            d[i] = Math.Min(d[i], d[i + w + 1] + Diagonal);
                        }

                        if (ix > 0)
                        {
                            d[i] = Math.Min(d[i], d[i + w - 1] + Diagonal);
                        }
                    }
                }
            }

            for (int i = 0; i < d.Length; i++)
            {
                d[i] = d[i] >= Far ? Far : d[i] * cellSizeMm;
            }

            return d;
        }
    }
}
