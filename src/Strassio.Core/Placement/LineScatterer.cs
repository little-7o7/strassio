using System;
using System.Collections.Generic;
using System.Linq;
using Strassio.Core.Geometry;

namespace Strassio.Core.Placement
{
    /// <summary>
    /// Метод L1 «по линии» (docs/SPEC.md, раздел 4): расставляет стразы вдоль кривой одним рядом.
    /// В каждом остром угле — страза точно в вершине, отрезки между углами заполняются независимо
    /// (раздел 6.1), поэтому у зигзага и звезды не «плывёт» рисунок в углах.
    /// </summary>
    public static class LineScatterer
    {
        public static IReadOnlyList<PlacedStone> Scatter(Curve curve, LineScatterOptions options)
        {
            FlattenedCurve flat = CurveFlattener.Flatten(curve, options.FlattenToleranceMm);
            if (options.Reverse)
            {
                flat = flat.Reverse();
            }

            double total = flat.TotalLength;
            double stoneStep = options.StoneDiameterMm + options.GapMm;
            List<double> cornerDistances = CornerDetector.FindSharpCornerDistances(flat, options.CornerAngleThresholdDeg);

            List<(double Distance, bool IsCorner)> breakpoints;
            bool loopCloses;

            if (flat.IsClosed)
            {
                breakpoints = cornerDistances.Select(d => (Distance: d, IsCorner: true)).OrderBy(t => t.Distance).ToList();
                if (breakpoints.Count == 0)
                {
                    // Гладкая замкнутая кривая без углов (например, окружность) — единственная точка
                    // разреза, откуда начинается ряд, задаётся отступом начала.
                    breakpoints.Add((Mod(options.StartOffsetMm, total), false));
                }

                loopCloses = true;
            }
            else
            {
                double activeStart = Clamp(options.StartOffsetMm, 0, total);
                double activeEnd = Clamp(total - options.EndMarginMm, activeStart, total);

                breakpoints = new List<(double, bool)> { (activeStart, false) };
                breakpoints.AddRange(cornerDistances
                    .Where(d => d > activeStart + 1e-9 && d < activeEnd - 1e-9)
                    .Select(d => (d, true)));
                breakpoints.Add((activeEnd, false));
                breakpoints = breakpoints.OrderBy(t => t.Item1).ToList();

                loopCloses = false;
            }

            int segCount = loopCloses ? breakpoints.Count : breakpoints.Count - 1;

            int[]? gapsPerSegment = null;
            if (options.Mode == StepMode.ExactCount && options.ExactCount.HasValue && segCount > 0)
            {
                var lengths = new double[segCount];
                for (int i = 0; i < segCount; i++)
                {
                    lengths[i] = SegmentLength(breakpoints, i, segCount, loopCloses, total);
                }

                int targetGaps = loopCloses ? options.ExactCount.Value : Math.Max(0, options.ExactCount.Value - 1);
                gapsPerSegment = DistributeGaps(lengths, targetGaps);
            }

            var result = new List<PlacedStone>();
            var perSegmentStones = new List<(int ResultIndex, double LocalDist)>[segCount];

            for (int i = 0; i < segCount; i++)
            {
                double aDist = breakpoints[i].Distance;
                bool bIsCorner;
                double bDist;
                double bRawDist;

                if (loopCloses && i == segCount - 1)
                {
                    bRawDist = breakpoints[0].Distance;
                    bDist = bRawDist + total;
                    bIsCorner = breakpoints[0].IsCorner;
                }
                else
                {
                    bRawDist = breakpoints[i + 1].Distance;
                    bDist = bRawDist;
                    bIsCorner = breakpoints[i + 1].IsCorner;
                }

                double subLength = bDist - aDist;
                bool endForced = bIsCorner || options.Mode != StepMode.ExactStep;
                int? gapsForSegment = gapsPerSegment?[i];

                List<double> positions = FillSubSegmentCore(subLength, options, stoneStep, endForced, gapsForSegment);

                bool isLastSegment = i == segCount - 1;
                bool skipEndPoint = loopCloses && isLastSegment;
                var segmentStones = new List<(int ResultIndex, double LocalDist)>();

                foreach (double pos in positions)
                {
                    if (i > 0 && pos <= 1e-9)
                    {
                        continue; // уже добавлено как конец предыдущего отрезка
                    }

                    if (skipEndPoint && pos >= subLength - 1e-9)
                    {
                        continue; // это та же точка, что начало первого отрезка (замкнутый контур)
                    }

                    double distance = aDist + pos;
                    if (loopCloses)
                    {
                        distance = Mod(distance, total);
                    }

                    bool isCorner = (pos <= 1e-9 && breakpoints[i].IsCorner) || (pos >= subLength - 1e-9 && bIsCorner);

                    result.Add(new PlacedStone(flat.PointAtDistance(distance), options.StoneDiameterMm, isCorner));
                    if (!isCorner)
                    {
                        segmentStones.Add((result.Count - 1, pos));
                    }
                }

                perSegmentStones[i] = segmentStones;
            }

            NudgeStonesNearSharpCorners(
                flat, result, perSegmentStones, breakpoints, segCount,
                options.StoneDiameterMm, options.CornerMinGapMm, options.MaxCornerNudgeMm, options.CornerTaperCount);

            return result;
        }

        /// <summary>
        /// У очень острых углов страза слева и страза справа от вершины физически близки друг к другу,
        /// даже если каждая идеально стоит на своём отрезке (геометрия угла, не ошибка расстановки).
        /// По замечанию автора: не оставляем дырку, не растягиваем весь ряд и не сдвигаем одну стразу
        /// резко — вместо этого расходимся до небольшого зазора (CornerMinGapMm, обычно меньше обычного
        /// зазора ряда — у самого острия это не бросается в глаза) и распределяем сдвиг по нескольким
        /// стразам подряд с каждой стороны угла (CornerTaperCount), плавно затухая к нулю. Сама угловая
        /// страза остаётся точно в вершине (раздел 4 ТЗ).
        ///
        /// Если один из двух отрезков у угла идёт строго горизонтально или строго вертикально — это,
        /// как правило, «опорная» линия дизайна, и трогать её нежелательно даже чуть-чуть (по замечанию
        /// автора: соседний, уже наклонный отрезок может взять на себя весь сдвиг незаметно, а прямая
        /// линия — нет). Поэтому доля сдвига между двумя отрезками не всегда 50/50: чем ближе отрезок
        /// к горизонтали/вертикали, тем меньше его доля, а строго горизонтальный/вертикальный получает 0.
        /// </summary>
        private static void NudgeStonesNearSharpCorners(
            FlattenedCurve flat, List<PlacedStone> result, List<(int ResultIndex, double LocalDist)>[] perSegmentStones,
            List<(double Distance, bool IsCorner)> breakpoints, int segCount,
            double stoneDiameterMm, double cornerMinGapMm, double maxNudgeMm, int cornerTaperCount)
        {
            double targetMinDistance = stoneDiameterMm + cornerMinGapMm;
            var appliedNudge = new double[result.Count];
            int taperCount = Math.Max(1, cornerTaperCount);

            for (int i = 0; i < segCount; i++)
            {
                if (!breakpoints[i].IsCorner)
                {
                    continue;
                }

                int incomingSeg = i == 0 ? segCount - 1 : i - 1;
                int outgoingSeg = i;

                // Ближе к углу — в конце входящего отрезка (по убыванию LocalDist) и в начале
                // исходящего (по возрастанию LocalDist).
                var incoming = perSegmentStones[incomingSeg].OrderByDescending(t => t.LocalDist).ToList();
                var outgoing = perSegmentStones[outgoingSeg].OrderBy(t => t.LocalDist).ToList();

                if (incoming.Count == 0 || outgoing.Count == 0)
                {
                    continue;
                }

                Point2D a0 = result[incoming[0].ResultIndex].Center;
                Point2D b0 = result[outgoing[0].ResultIndex].Center;
                double dist0 = Point2D.Distance(a0, b0);
                double deficit = targetMinDistance - dist0;

                if (deficit <= 1e-9)
                {
                    continue; // ближайшая пара и так не теснее нужного — дальше от угла подавно
                }

                Point2D dir = dist0 > 1e-9 ? (a0 - b0) * (1.0 / dist0) : new Point2D(1, 0);

                Point2D cornerPos = flat.PointAtDistance(breakpoints[i].Distance);
                double diagonalityA = ArmDiagonality(a0 - cornerPos);
                double diagonalityB = ArmDiagonality(b0 - cornerPos);
                double totalDiagonality = diagonalityA + diagonalityB;
                double shareA = totalDiagonality > 1e-9 ? diagonalityA / totalDiagonality : 0.5;
                double shareB = totalDiagonality > 1e-9 ? diagonalityB / totalDiagonality : 0.5;

                ApplyCornerTaper(result, incoming, dir, deficit * shareA, taperCount, appliedNudge, maxNudgeMm);
                ApplyCornerTaper(result, outgoing, dir * -1, deficit * shareB, taperCount, appliedNudge, maxNudgeMm);
            }
        }

        /// <summary>
        /// 0 — направление строго горизонтальное или строго вертикальное (опорная линия, лучше не
        /// трогать), 1 — направление ровно диагональное (45°, трогать не жалко).
        /// </summary>
        private static double ArmDiagonality(Point2D direction)
        {
            Point2D d = direction.Normalized();
            double angleFromHorizontalDeg = Math.Atan2(Math.Abs(d.Y), Math.Abs(d.X)) * 180 / Math.PI;
            double distanceFromNearestAxisDeg = Math.Min(angleFromHorizontalDeg, 90 - angleFromHorizontalDeg);
            return distanceFromNearestAxisDeg / 45.0;
        }

        /// <summary>
        /// Сдвигает до taperCount ближайших к углу страз одного ряда в направлении dir: у самой ближней —
        /// полный необходимый сдвиг peak, дальше — плавно (по косинусу) убывающий до нуля. Так после угла
        /// нет одной резко смещённой стразы — есть незаметный плавный изгиб на несколько страз.
        /// </summary>
        private static void ApplyCornerTaper(
            List<PlacedStone> result, List<(int ResultIndex, double LocalDist)> arm, Point2D dir, double peak,
            int taperCount, double[] appliedNudge, double maxNudgeMm)
        {
            int n = Math.Min(taperCount, arm.Count);
            for (int k = 0; k < n; k++)
            {
                double weight = 0.5 * (1 + Math.Cos(Math.PI * k / taperCount));
                double want = peak * weight;
                if (want <= 1e-9)
                {
                    continue;
                }

                int idx = arm[k].ResultIndex;
                double budget = Math.Max(0, maxNudgeMm - appliedNudge[idx]);
                double apply = Math.Min(want, budget);
                if (apply <= 1e-9)
                {
                    continue;
                }

                Point2D c = result[idx].Center;
                result[idx] = new PlacedStone(c + dir * apply, result[idx].DiameterMm, false);
                appliedNudge[idx] += apply;
            }
        }

        private static double SegmentLength(
            List<(double Distance, bool IsCorner)> breakpoints, int i, int segCount, bool loopCloses, double total)
        {
            double a = breakpoints[i].Distance;
            double b = loopCloses && i == segCount - 1 ? breakpoints[0].Distance + total : breakpoints[i + 1].Distance;
            return b - a;
        }

        private static List<double> FillSubSegmentCore(
            double subLength, LineScatterOptions options, double stoneStep, bool endForced, int? gapsOverride)
        {
            var positions = new List<double>();

            if (subLength <= 1e-9)
            {
                positions.Add(0);
                return positions;
            }

            switch (options.Mode)
            {
                case StepMode.ExactStep:
                {
                    double step = options.ExactStepMm.GetValueOrDefault();
                    if (step <= 1e-9)
                    {
                        step = stoneStep;
                    }

                    double d = 0;
                    while (d <= subLength + 1e-9)
                    {
                        positions.Add(Math.Min(d, subLength));
                        d += step;
                    }

                    if (endForced)
                    {
                        double last = positions[positions.Count - 1];
                        if (subLength - last > 1e-6)
                        {
                            positions.Add(subLength);
                        }
                    }

                    break;
                }

                case StepMode.ExactCount:
                {
                    int gaps = Math.Max(1, gapsOverride ?? 1);
                    AddEvenlySpaced(positions, subLength, gaps + 1);
                    break;
                }

                case StepMode.FitEven:
                default:
                {
                    int count = Math.Max(2, (int)Math.Round(subLength / stoneStep) + 1);
                    AddEvenlySpaced(positions, subLength, count);
                    break;
                }
            }

            return positions;
        }

        private static void AddEvenlySpaced(List<double> positions, double subLength, int count)
        {
            if (count <= 1)
            {
                positions.Add(0);
                return;
            }

            double step = subLength / (count - 1);
            for (int i = 0; i < count; i++)
            {
                positions.Add(i * step);
            }
        }

        /// <summary>Распределяет targetGapsSum промежутков между сегментами пропорционально их длине (метод наибольшего остатка).</summary>
        private static int[] DistributeGaps(double[] lengths, int targetGapsSum)
        {
            int n = lengths.Length;
            var gaps = new int[n];
            double totalLength = lengths.Sum();

            if (totalLength <= 1e-9 || targetGapsSum <= n)
            {
                for (int i = 0; i < n; i++)
                {
                    gaps[i] = 1;
                }

                return gaps;
            }

            var raw = new double[n];
            int assigned = 0;
            for (int i = 0; i < n; i++)
            {
                raw[i] = Math.Max(1.0, targetGapsSum * lengths[i] / totalLength);
                gaps[i] = Math.Max(1, (int)Math.Floor(raw[i]));
                assigned += gaps[i];
            }

            int remainder = targetGapsSum - assigned;
            if (remainder > 0)
            {
                var order = Enumerable.Range(0, n).OrderByDescending(i => raw[i] - Math.Floor(raw[i])).ToArray();
                for (int k = 0; k < remainder; k++)
                {
                    gaps[order[k % n]]++;
                }
            }
            else if (remainder < 0)
            {
                var order = Enumerable.Range(0, n).OrderBy(i => raw[i]).ToArray();
                int k = 0;
                int guard = 0;
                while (remainder < 0 && guard < n * 1000)
                {
                    int seg = order[k % n];
                    if (gaps[seg] > 1)
                    {
                        gaps[seg]--;
                        remainder++;
                    }

                    k++;
                    guard++;
                }
            }

            return gaps;
        }

        private static double Mod(double a, double m) => m <= 0 ? 0 : ((a % m) + m) % m;

        private static double Clamp(double v, double min, double max) => v < min ? min : v > max ? max : v;
    }
}
