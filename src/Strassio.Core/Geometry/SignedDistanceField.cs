using System;
using System.Collections.Generic;

namespace Strassio.Core.Geometry
{
    /// <summary>
    /// Карта расстояний до края формы: для каждого узла сетки хранится, насколько он далеко от
    /// границы (внутри — со знаком плюс, снаружи — минус).
    ///
    /// Зачем это нужно. Контурная заливка (F3-F5, docs/SPEC.md раздел 5) — это ряды на расстоянии
    /// 1, 2, 3… шага от края. Раньше такой ряд строился смещением контура (CurveOffsetter), и на
    /// сложных фигурах он ломался: у звезды при сжатии внутрь противоположные стороны тонкого луча
    /// налезают друг на друга, контур выворачивается наизнанку, ряды начинают путаться и оставлять
    /// дыры. Смещение умеет срезать только петлю В УГЛУ, а такое самопересечение — не местное.
    ///
    /// Линия равного расстояния до края самопересечься не может в принципе, и сама распадается
    /// на несколько отдельных колец там, где форма распадается (например, лучи звезды отделяются
    /// от середины). Поэтому ряды строятся именно так.
    ///
    /// Форма может состоять из нескольких контуров (буква «О», кольцо): внутри — по чётно-нечётному
    /// правилу, расстояние — до ближайшего из контуров.
    /// </summary>
    public sealed class SignedDistanceField
    {
        private readonly double[] values;

        private SignedDistanceField(double[] values, int width, int height, double cellSizeMm, Point2D origin)
        {
            this.values = values;
            Width = width;
            Height = height;
            CellSizeMm = cellSizeMm;
            Origin = origin;
        }

        /// <summary>Число узлов сетки по горизонтали.</summary>
        public int Width { get; }

        /// <summary>Число узлов сетки по вертикали.</summary>
        public int Height { get; }

        public double CellSizeMm { get; }

        /// <summary>Координаты узла (0, 0).</summary>
        public Point2D Origin { get; }

        /// <summary>Расстояние до края в узле сетки: внутри формы — плюс, снаружи — минус, мм.</summary>
        public double ValueAt(int ix, int iy) => values[iy * Width + ix];

        public Point2D PositionOf(int ix, int iy) =>
            new Point2D(Origin.X + ix * CellSizeMm, Origin.Y + iy * CellSizeMm);

        /// <summary>То же значение в произвольной точке — билинейно между четырьмя соседними узлами.</summary>
        public double ValueAt(Point2D point)
        {
            double gx = (point.X - Origin.X) / CellSizeMm;
            double gy = (point.Y - Origin.Y) / CellSizeMm;

            int ix = (int)Math.Floor(gx);
            int iy = (int)Math.Floor(gy);

            if (ix < 0 || iy < 0 || ix >= Width - 1 || iy >= Height - 1)
            {
                return double.NegativeInfinity; // за пределами сетки — заведомо снаружи формы
            }

            double tx = gx - ix;
            double ty = gy - iy;

            double v00 = ValueAt(ix, iy);
            double v10 = ValueAt(ix + 1, iy);
            double v01 = ValueAt(ix, iy + 1);
            double v11 = ValueAt(ix + 1, iy + 1);

            return (v00 * (1 - tx) + v10 * tx) * (1 - ty) + (v01 * (1 - tx) + v11 * tx) * ty;
        }

        /// <summary>
        /// Строит карту по контурам формы. cellSizeMm — шаг сетки: чем мельче, тем точнее ряды,
        /// но дольше счёт. Число узлов ограничено сверху (maxNodesPerSide), иначе на большом
        /// дизайне сетка выросла бы до сотен миллионов узлов.
        /// </summary>
        public static SignedDistanceField Build(
            IReadOnlyList<FlattenedCurve> contours, double cellSizeMm, int maxNodesPerSide = 1400)
        {
            if (contours == null || contours.Count == 0)
            {
                throw new ArgumentException("Нужен хотя бы один контур.", nameof(contours));
            }

            if (cellSizeMm <= 0)
            {
                throw new ArgumentException("Шаг сетки должен быть больше нуля.", nameof(cellSizeMm));
            }

            var segments = CollectSegments(contours);
            if (segments.Count == 0)
            {
                throw new ArgumentException("В контурах нет ни одного отрезка.", nameof(contours));
            }

            GetBounds(segments, out double minX, out double minY, out double maxX, out double maxY);

            // Поля по краям: чтобы вся граница формы попала внутрь сетки вместе с соседними узлами.
            const int marginCells = 2;
            double width = maxX - minX;
            double height = maxY - minY;

            double cell = cellSizeMm;
            double neededPerSide = Math.Max(width, height) / cell + 2 * marginCells + 1;
            if (neededPerSide > maxNodesPerSide)
            {
                cell = Math.Max(width, height) / (maxNodesPerSide - 2 * marginCells - 1);
            }

            var origin = new Point2D(minX - marginCells * cell, minY - marginCells * cell);
            int nx = (int)Math.Ceiling(width / cell) + 2 * marginCells + 1;
            int ny = (int)Math.Ceiling(height / cell) + 2 * marginCells + 1;

            double[] distances = ComputeUnsignedDistances(segments, origin, nx, ny, cell);
            ApplySign(segments, distances, origin, nx, ny, cell);

            return new SignedDistanceField(distances, nx, ny, cell, origin);
        }

        private static List<(Point2D A, Point2D B)> CollectSegments(IReadOnlyList<FlattenedCurve> contours)
        {
            var segments = new List<(Point2D, Point2D)>();

            foreach (FlattenedCurve contour in contours)
            {
                IReadOnlyList<FlattenedPoint> pts = contour.Points;
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    if (Point2D.Distance(pts[i].Position, pts[i + 1].Position) > 1e-12)
                    {
                        segments.Add((pts[i].Position, pts[i + 1].Position));
                    }
                }

                // Незамкнутый контур для заливки смысла не имеет — замыкаем его сами.
                Point2D first = pts[0].Position;
                Point2D last = pts[pts.Count - 1].Position;
                if (Point2D.Distance(first, last) > 1e-9)
                {
                    segments.Add((last, first));
                }
            }

            return segments;
        }

        private static void GetBounds(
            List<(Point2D A, Point2D B)> segments,
            out double minX, out double minY, out double maxX, out double maxY)
        {
            minX = double.MaxValue;
            minY = double.MaxValue;
            maxX = double.MinValue;
            maxY = double.MinValue;

            foreach ((Point2D a, Point2D b) in segments)
            {
                if (a.X < minX) minX = a.X;
                if (a.X > maxX) maxX = a.X;
                if (a.Y < minY) minY = a.Y;
                if (a.Y > maxY) maxY = a.Y;
                if (b.X < minX) minX = b.X;
                if (b.X > maxX) maxX = b.X;
                if (b.Y < minY) minY = b.Y;
                if (b.Y > maxY) maxY = b.Y;
            }
        }

        /// <summary>
        /// Расстояние до границы без знака. Сначала точно считаем его у самих отрезков (узкая полоса
        /// вокруг границы), потом разносим по всей сетке алгоритмом Данielsson'а: каждый узел
        /// перенимает у соседа не расстояние, а САМУ ближайшую точку границы, и меряет до неё
        /// заново. Поэтому результат остаётся честным евклидовым расстоянием, а не «по клеточкам».
        /// </summary>
        private static double[] ComputeUnsignedDistances(
            List<(Point2D A, Point2D B)> segments, Point2D origin, int nx, int ny, double cell)
        {
            int count = nx * ny;
            var nearest = new Point2D[count];
            var distances = new double[count];

            for (int i = 0; i < count; i++)
            {
                distances[i] = double.MaxValue;
            }

            // Засев: у каждого отрезка обходим узлы в его габаритах, расширенных на пару клеток.
            const int seedRadiusCells = 2;
            foreach ((Point2D a, Point2D b) in segments)
            {
                int x0 = (int)Math.Floor((Math.Min(a.X, b.X) - origin.X) / cell) - seedRadiusCells;
                int x1 = (int)Math.Ceiling((Math.Max(a.X, b.X) - origin.X) / cell) + seedRadiusCells;
                int y0 = (int)Math.Floor((Math.Min(a.Y, b.Y) - origin.Y) / cell) - seedRadiusCells;
                int y1 = (int)Math.Ceiling((Math.Max(a.Y, b.Y) - origin.Y) / cell) + seedRadiusCells;

                if (x0 < 0) x0 = 0;
                if (y0 < 0) y0 = 0;
                if (x1 > nx - 1) x1 = nx - 1;
                if (y1 > ny - 1) y1 = ny - 1;

                for (int iy = y0; iy <= y1; iy++)
                {
                    for (int ix = x0; ix <= x1; ix++)
                    {
                        var p = new Point2D(origin.X + ix * cell, origin.Y + iy * cell);
                        Point2D closest = ClosestPointOnSegment(p, a, b);
                        double d = Point2D.Distance(p, closest);

                        int index = iy * nx + ix;
                        if (d < distances[index])
                        {
                            distances[index] = d;
                            nearest[index] = closest;
                        }
                    }
                }
            }

            // Два прохода по сетке: вперёд (снизу вверх, слева направо) и назад.
            for (int iy = 0; iy < ny; iy++)
            {
                for (int ix = 0; ix < nx; ix++)
                {
                    Relax(nearest, distances, origin, nx, ny, cell, ix, iy, -1, 0);
                    Relax(nearest, distances, origin, nx, ny, cell, ix, iy, 0, -1);
                    Relax(nearest, distances, origin, nx, ny, cell, ix, iy, -1, -1);
                    Relax(nearest, distances, origin, nx, ny, cell, ix, iy, 1, -1);
                }

                for (int ix = nx - 1; ix >= 0; ix--)
                {
                    Relax(nearest, distances, origin, nx, ny, cell, ix, iy, 1, 0);
                }
            }

            for (int iy = ny - 1; iy >= 0; iy--)
            {
                for (int ix = nx - 1; ix >= 0; ix--)
                {
                    Relax(nearest, distances, origin, nx, ny, cell, ix, iy, 1, 0);
                    Relax(nearest, distances, origin, nx, ny, cell, ix, iy, 0, 1);
                    Relax(nearest, distances, origin, nx, ny, cell, ix, iy, 1, 1);
                    Relax(nearest, distances, origin, nx, ny, cell, ix, iy, -1, 1);
                }

                for (int ix = 0; ix < nx; ix++)
                {
                    Relax(nearest, distances, origin, nx, ny, cell, ix, iy, -1, 0);
                }
            }

            return distances;
        }

        private static void Relax(
            Point2D[] nearest, double[] distances, Point2D origin, int nx, int ny, double cell,
            int ix, int iy, int dx, int dy)
        {
            int sx = ix + dx;
            int sy = iy + dy;
            if (sx < 0 || sy < 0 || sx >= nx || sy >= ny)
            {
                return;
            }

            int source = sy * nx + sx;
            if (distances[source] == double.MaxValue)
            {
                return;
            }

            var p = new Point2D(origin.X + ix * cell, origin.Y + iy * cell);
            double d = Point2D.Distance(p, nearest[source]);

            int target = iy * nx + ix;
            if (d < distances[target])
            {
                distances[target] = d;
                nearest[target] = nearest[source];
            }
        }

        /// <summary>
        /// Расставляет знак: внутри формы плюс, снаружи минус. Идём по строкам сетки и для каждой
        /// считаем, где горизонтальная линия пересекает контуры — дальше обычное чётно-нечётное
        /// правило, как в PointInPolygon, только сразу для всей строки.
        /// </summary>
        private static void ApplySign(
            List<(Point2D A, Point2D B)> segments, double[] distances, Point2D origin, int nx, int ny, double cell)
        {
            var crossings = new List<double>();

            for (int iy = 0; iy < ny; iy++)
            {
                double y = origin.Y + iy * cell;
                crossings.Clear();

                foreach ((Point2D a, Point2D b) in segments)
                {
                    if ((a.Y > y) == (b.Y > y))
                    {
                        continue;
                    }

                    crossings.Add(a.X + (y - a.Y) / (b.Y - a.Y) * (b.X - a.X));
                }

                if (crossings.Count == 0)
                {
                    for (int ix = 0; ix < nx; ix++)
                    {
                        distances[iy * nx + ix] = -distances[iy * nx + ix];
                    }

                    continue;
                }

                crossings.Sort();

                for (int ix = 0; ix < nx; ix++)
                {
                    double x = origin.X + ix * cell;

                    int before = 0;
                    for (int c = 0; c < crossings.Count; c++)
                    {
                        if (crossings[c] <= x)
                        {
                            before++;
                        }
                        else
                        {
                            break;
                        }
                    }

                    if (before % 2 == 0)
                    {
                        distances[iy * nx + ix] = -distances[iy * nx + ix];
                    }
                }
            }
        }

        private static Point2D ClosestPointOnSegment(Point2D p, Point2D a, Point2D b)
        {
            Point2D ab = b - a;
            double lengthSquared = ab.Dot(ab);
            if (lengthSquared < 1e-24)
            {
                return a;
            }

            double t = (p - a).Dot(ab) / lengthSquared;
            if (t < 0) t = 0;
            if (t > 1) t = 1;

            return a + ab * t;
        }
    }
}
