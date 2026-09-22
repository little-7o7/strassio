using System;
using System.Collections.Generic;
using Strassio.Core.Geometry;

namespace Strassio.Core.Placement
{
    /// <summary>
    /// Один ряд вдоль линии камнями РАЗНОГО размера (docs/SPEC.md, раздел 4: L5 «переход размера»,
    /// L6 «чередование размеров»). Размер каждого следующего камня спрашивается у
    /// <c>diameterAt(номер, t)</c>, где t — доля пройденной длины (0 — начало, 1 — конец). Соседние
    /// камни стоят через заданный зазор с учётом размеров обоих; в конце лишняя длина равномерно
    /// раздаётся по всем промежуткам (как «подгонка» в L1) — поэтому у конца линии нет дырки.
    /// Замкнутая линия заполняется по кругу без «шва».
    /// </summary>
    public static class VariableLineScatterer
    {
        public static IReadOnlyList<PlacedStone> Scatter(
            Curve curve, Func<int, double, double> diameterAt, double gapMm, double flattenToleranceMm = 0.02)
        {
            FlattenedCurve flat = CurveFlattener.Flatten(curve, flattenToleranceMm);
            double length = flat.TotalLength;
            var result = new List<PlacedStone>();
            if (length <= 0)
            {
                return result;
            }

            // Жадно: ставим камни, пока следующий помещается.
            var positions = new List<double>();
            var diameters = new List<double>();
            double s = 0;
            double d = Math.Max(0.05, diameterAt(0, 0));
            while (true)
            {
                positions.Add(s);
                diameters.Add(d);

                double nextD = Math.Max(0.05, diameterAt(positions.Count, Math.Min(1, s / length)));
                double step = (d + nextD) / 2 + gapMm;
                double closing = flat.IsClosed ? (nextD + diameters[0]) / 2 + gapMm : 0;

                // У замкнутой линии следующему камню нужно место и до первого камня (стык по кругу).
                if (s + step + closing > length + 1e-9 || positions.Count > 100000)
                {
                    break;
                }

                // Размер следующего камня уточняем по месту, куда он встанет.
                nextD = Math.Max(0.05, diameterAt(positions.Count, Math.Min(1, (s + step) / length)));
                step = (d + nextD) / 2 + gapMm;
                if (s + step > length + 1e-9)
                {
                    break;
                }

                s += step;
                d = nextD;
            }

            int n = positions.Count;
            double used = flat.IsClosed ? s + (diameters[n - 1] + diameters[0]) / 2 + gapMm : s;
            int gaps = flat.IsClosed ? n : n - 1;
            double extra = gaps > 0 ? Math.Max(0, length - used) / gaps : 0;

            for (int i = 0; i < n; i++)
            {
                double at = positions[i] + extra * i;
                result.Add(new PlacedStone(flat.PointAtDistance(Math.Min(at, length)), diameters[i], isCorner: false));
            }

            return result;
        }

        /// <summary>
        /// Доля длины линии (0…1) для точки рядом с ней: где на линии ближайшее к точке место.
        /// Нужна, например, «каллиграфии» — какой ширины линия в месте каждого камня.
        /// </summary>
        public static double ProjectFraction(FlattenedCurve flat, Point2D p)
        {
            IReadOnlyList<FlattenedPoint> pts = flat.Points;
            if (pts.Count < 2 || flat.TotalLength <= 0)
            {
                return 0;
            }

            double best = double.MaxValue;
            double bestAt = 0;
            int segments = flat.IsClosed ? pts.Count : pts.Count - 1;
            for (int i = 0; i < segments; i++)
            {
                Point2D a = pts[i].Position;
                Point2D b = pts[(i + 1) % pts.Count].Position;
                Point2D ab = b - a;
                double len2 = ab.Dot(ab);
                double t = len2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, (p - a).Dot(ab) / len2));
                double dist = Point2D.Distance(a + ab * t, p);
                if (dist < best)
                {
                    best = dist;
                    double start = flat.ArcLengths[i];
                    double segLen = Math.Sqrt(len2);
                    bestAt = start + segLen * t;
                }
            }

            return Math.Max(0, Math.Min(1, bestAt / flat.TotalLength));
        }
    }
}
