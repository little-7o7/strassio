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
        /// <summary>
        /// Углы с учётом выбора «Углы» (CornerPlacement): линия режется в каждом остром углу,
        /// каждый кусок заполняется отдельно, поэтому камень попадает точно в вершину (а очень
        /// острый угол — скругляется). Размеры камней берутся так же, как в простой перегрузке.
        /// </summary>
        public static IReadOnlyList<PlacedStone> Scatter(
            Curve curve, Func<int, double, double> diameterAt, LineScatterOptions options)
        {
            FlattenedCurve flat = CurveFlattener.Flatten(curve, options.FlattenToleranceMm);
            double total = flat.TotalLength;
            if (total <= 0)
            {
                return new List<PlacedStone>();
            }

            List<double> corners = CornerDetector.FindSharpCornerDistances(flat, options.CornerAngleThresholdDeg);
            if (corners.Count == 0)
            {
                return Scatter(curve, diameterAt, options.GapMm, options.FlattenToleranceMm);
            }

            corners.Sort();

            // Точки разреза: у замкнутой линии это сами углы по кругу, у разомкнутой — ещё начало и конец.
            var marks = new List<(double Distance, bool IsCorner)>();
            if (!flat.IsClosed)
            {
                marks.Add((0, false));
            }

            foreach (double d0 in corners)
            {
                if (flat.IsClosed || (d0 > 1e-9 && d0 < total - 1e-9))
                {
                    marks.Add((d0, true));
                }
            }

            if (!flat.IsClosed)
            {
                marks.Add((total, false));
            }

            int pieces = flat.IsClosed ? marks.Count : marks.Count - 1;
            var result = new List<PlacedStone>();
            int placed = 0;

            for (int c = 0; c < pieces; c++)
            {
                bool lastPiece = c == pieces - 1;
                double from = marks[c].Distance;
                int nextMark = flat.IsClosed && lastPiece ? 0 : c + 1;
                double to = flat.IsClosed && lastPiece ? marks[0].Distance + total : marks[nextMark].Distance;

                // У скруглённого угла кусок начинается не в вершине, а на срезе.
                double startCut = marks[c].IsCorner ? CornerCut(flat, from, diameterAt, total, options) : 0;
                double endCut = marks[nextMark].IsCorner ? CornerCut(flat, marks[nextMark].Distance, diameterAt, total, options) : 0;

                double pieceStart = from + startCut;
                double pieceLength = to - endCut - pieceStart;
                if (pieceLength <= 1e-9)
                {
                    continue;
                }

                // Первый камень куска — в вершине (или на срезе). У острого угла он уже поставлен
                // концом предыдущего куска, кроме самого первого прохода.
                bool skipFirst = startCut <= 0 && result.Count > 0;

                foreach ((double at, double diameter) in WalkPiece(
                    flat, pieceStart, pieceLength, total, diameterAt, options.GapMm, placed))
                {
                    placed++;
                    if (skipFirst && at <= pieceStart + 1e-9)
                    {
                        continue;
                    }

                    // Последний камень последнего куска замкнутой линии — это первый камень первого.
                    bool closesLoop = flat.IsClosed && lastPiece && endCut <= 0 && at >= pieceStart + pieceLength - 1e-9;
                    if (closesLoop)
                    {
                        continue;
                    }

                    bool isCorner = at <= pieceStart + 1e-9 || at >= pieceStart + pieceLength - 1e-9;
                    result.Add(new PlacedStone(flat.PointAtDistance(Mod(at, total)), diameter, isCorner));
                }
            }

            return result;
        }

        /// <summary>Камни одного куска: жадно ставим по шагу, потом лишнюю длину поровну раздаём промежуткам.</summary>
        private static List<(double At, double Diameter)> WalkPiece(
            FlattenedCurve flat, double pieceStart, double pieceLength, double total,
            Func<int, double, double> diameterAt, double gapMm, int indexOffset)
        {
            var positions = new List<double>();
            var diameters = new List<double>();

            double s = 0;
            double d = Math.Max(0.05, diameterAt(indexOffset, Mod(pieceStart, total) / total));
            while (true)
            {
                positions.Add(s);
                diameters.Add(d);

                int next = indexOffset + positions.Count;
                double nextD = Math.Max(0.05, diameterAt(next, Mod(pieceStart + s, total) / total));
                double step = (d + nextD) / 2 + gapMm;
                if (s + step > pieceLength + 1e-9 || positions.Count > 100000)
                {
                    break;
                }

                s += step;
                d = nextD;
            }

            // Конец куска — это вершина (или срез): камень должен встать ровно туда.
            int gaps = positions.Count - 1;
            double extra = gaps > 0 ? Math.Max(0, pieceLength - positions[positions.Count - 1]) / gaps : 0;

            var result = new List<(double, double)>();
            for (int i = 0; i < positions.Count; i++)
            {
                result.Add((pieceStart + positions[i] + extra * i, diameters[i]));
            }

            return result;
        }

        /// <summary>Насколько срезать вершину при скруглении; 0 — угол остаётся острым.</summary>
        private static double CornerCut(
            FlattenedCurve flat, double distance, Func<int, double, double> diameterAt, double total, LineScatterOptions options)
        {
            if (options.CornerPlacement == CornerPlacement.Sharp)
            {
                return 0;
            }

            double angleDeg = LineScatterer.InteriorAngleDeg(flat, distance);
            if (double.IsNaN(angleDeg))
            {
                return 0;
            }

            if (options.CornerPlacement == CornerPlacement.Mixed && angleDeg >= options.RoundCornerBelowDeg - 1e-6)
            {
                return 0;
            }

            double sinHalf = Math.Sin(angleDeg * Math.PI / 360.0);
            if (sinHalf <= 1e-6)
            {
                return 0;
            }

            // Шаг у вершины — по размеру камня в этом месте линии.
            double step = Math.Max(0.05, diameterAt(0, Mod(distance, total) / total)) + options.GapMm;
            double cut = step / (2 * sinHalf);
            return cut > LineScatterer.MaxRoundCutInSteps * step ? 0 : cut;
        }

        private static double Mod(double a, double m) => m <= 0 ? 0 : ((a % m) + m) % m;

        /// <summary>
        /// Без учёта углов: ряд идёт по всей линии подряд. Оставлено для вызовов, где углы не нужны.
        /// </summary>
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
