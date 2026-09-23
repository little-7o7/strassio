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
