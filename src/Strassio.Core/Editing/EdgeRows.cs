using System;
using System.Collections.Generic;
using Strassio.Core.Geometry;

namespace Strassio.Core.Editing
{
    /// <summary>
    /// «Цвет крайних рядов» (пункт 6 прокачки): какие стразы лежат в крайних рядах рисунка — у внешнего
    /// края и у больших отверстий. Работает по самим стразам, а не по методу, поэтому годится для канта,
    /// комбинированной заливки, «вокруг линии», сот и камней, расставленных руками.
    /// </summary>
    public static class EdgeRows
    {
        /// <summary>Пустой сектор вокруг стразы шире этого — с той стороны край (внутри сот сектора по 60°).</summary>
        private const double OpenAngle = 100 * Math.PI / 180;

        /// <summary>Пустой сектор такой ширины — точно край (дырка от одного камня даёт около 120°).</summary>
        private const double WideOpen = 150 * Math.PI / 180;

        /// <summary>
        /// Номер крайнего ряда для каждой стразы: 1 — самый крайний, 2 — следующий внутрь и так далее до
        /// <paramref name="rows"/>; 0 — страза глубже.
        /// </summary>
        public static int[] Find(IReadOnlyList<Point2D> centers, IReadOnlyList<double> diameters, int rows)
        {
            int n = centers.Count;
            var row = new int[n];
            if (n == 0 || rows <= 0)
            {
                return row;
            }

            double maxD = 0;
            for (int i = 0; i < n; i++)
            {
                maxD = Math.Max(maxD, diameters[i]);
            }

            // Ближние соседи (просвет до камня) — для секторов: у рыхлой раскладки (стык рядов в
            // «Комбинированной») камень внутри тоже окружён со всех сторон. Дальние — чтобы отличить
            // край от дырки в один камень.
            var grid = new StoneGrid(Math.Max(0.5, 3 * maxD));
            for (int i = 0; i < n; i++)
            {
                grid.Add(centers[i], diameters[i] / 2);
            }

            var touching = new List<int>[n];
            var around = new List<int>[n];
            for (int i = 0; i < n; i++)
            {
                touching[i] = new List<int>();
                around[i] = new List<int>();
                foreach (int j in grid.Near(centers[i]))
                {
                    if (j == i)
                    {
                        continue;
                    }

                    double distance = Point2D.Distance(centers[i], centers[j]);
                    double gap = distance - (diameters[i] + diameters[j]) / 2;
                    if (gap <= Math.Min(diameters[i], diameters[j]))
                    {
                        touching[i].Add(j);
                    }

                    if (distance <= 2.6 * Math.Max(diameters[i], diameters[j]))
                    {
                        around[i].Add(j);
                    }
                }
            }

            var angles = new List<double>();
            var newly = new List<int>();
            for (int level = 1; level <= rows; level++)
            {
                newly.Clear();
                for (int i = 0; i < n; i++)
                {
                    if (row[i] == 0 && IsOnEdge(i, centers, touching[i], around[i], row, angles))
                    {
                        newly.Add(i);
                    }
                }

                if (newly.Count == 0)
                {
                    break;
                }

                // Ряд отмечается целиком после прохода, иначе край «проедал» бы рисунок за один уровень.
                foreach (int i in newly)
                {
                    row[i] = level;
                }
            }

            return row;
        }

        /// <summary>
        /// Страза на краю, если вокруг неё (среди ещё не отмеченных соседей) есть пустой сектор шире
        /// <see cref="OpenAngle"/>, и в его сторону дальше тоже пусто — то есть это край, а не дырка от
        /// одного пропущенного камня.
        /// </summary>
        private static bool IsOnEdge(int i, IReadOnlyList<Point2D> centers, List<int> touching, List<int> around, int[] row, List<double> angles)
        {
            angles.Clear();
            foreach (int j in touching)
            {
                if (row[j] == 0)
                {
                    Point2D d = centers[j] - centers[i];
                    angles.Add(Math.Atan2(d.Y, d.X));
                }
            }

            if (angles.Count < 2)
            {
                return true;
            }

            angles.Sort();
            for (int k = 0; k < angles.Count; k++)
            {
                double from = angles[k];
                double to = k + 1 < angles.Count ? angles[k + 1] : angles[0] + 2 * Math.PI;
                if (to - from <= OpenAngle)
                {
                    continue;
                }

                // Пусто на полкруга — край, даже если напротив близко другой лепесток (узкая выемка).
                if (to - from >= WideOpen)
                {
                    return true;
                }

                // Смотрим в середину пустого сектора: есть ли там камни дальше (не отмеченные).
                double middle = (from + to) / 2;
                // Узкий конус: дырка от одного камня — камень сразу за ней, точно напротив; у края пусто.
                double halfWidth = Math.Min(25 * Math.PI / 180, (to - from) / 2 - 15 * Math.PI / 180);
                bool blocked = false;
                foreach (int j in around)
                {
                    if (row[j] != 0)
                    {
                        continue;
                    }

                    Point2D d = centers[j] - centers[i];
                    double off = Math.Abs(Normalize(Math.Atan2(d.Y, d.X) - middle));
                    if (off < halfWidth)
                    {
                        blocked = true;
                        break;
                    }
                }

                if (!blocked)
                {
                    return true;
                }
            }

            return false;
        }

        private static double Normalize(double angle)
        {
            while (angle > Math.PI)
            {
                angle -= 2 * Math.PI;
            }

            while (angle < -Math.PI)
            {
                angle += 2 * Math.PI;
            }

            return angle;
        }
    }
}
