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

            NudgeStonesNearSharpCorners(result, perSegmentStones, breakpoints, segCount, loopCloses, stoneStep, options.MaxCornerNudgeMm);

            return result;
        }

        /// <summary>
        /// У очень острых углов страза слева и страза справа от вершины физически близки друг к другу,
        /// даже если каждая идеально стоит на своём отрезке (геометрия угла, не ошибка расстановки).
        /// Вместо дырки или растягивания всего ряда — по замечанию автора — точечно, плавно сдвигаем
        /// в сторону буквально одну-две ближайшие к углу стразы с каждой стороны, ровно настолько,
        /// сколько нужно, но не больше maxNudgeMm. Ряд остаётся частым и без пропусков.
        /// </summary>
        private static void NudgeStonesNearSharpCorners(
            List<PlacedStone> result, List<(int ResultIndex, double LocalDist)>[] perSegmentStones,
            List<(double Distance, bool IsCorner)> breakpoints, int segCount, bool loopCloses, double stoneStep,
            double maxNudgeMm)
        {
            var appliedNudge = new double[result.Count];

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

                int pairs = Math.Min(incoming.Count, outgoing.Count);
                for (int k = 0; k < pairs; k++)
                {
                    int idxA = incoming[k].ResultIndex;
                    int idxB = outgoing[k].ResultIndex;

                    Point2D a = result[idxA].Center;
                    Point2D b = result[idxB].Center;
                    double dist = Point2D.Distance(a, b);
                    double deficit = stoneStep - dist;

                    if (deficit <= 1e-9)
                    {
                        break; // дальше от угла зазор только растёт — можно не проверять
                    }

                    Point2D dir = dist > 1e-9 ? (a - b) * (1.0 / dist) : new Point2D(1, 0);
                    double pushEach = deficit / 2;

                    double budgetA = Math.Max(0, maxNudgeMm - appliedNudge[idxA]);
                    double budgetB = Math.Max(0, maxNudgeMm - appliedNudge[idxB]);
                    double applyA = Math.Min(pushEach, budgetA);
                    double applyB = Math.Min(pushEach, budgetB);

                    result[idxA] = new PlacedStone(a + dir * applyA, result[idxA].DiameterMm, false);
                    result[idxB] = new PlacedStone(b - dir * applyB, result[idxB].DiameterMm, false);
                    appliedNudge[idxA] += applyA;
                    appliedNudge[idxB] += applyB;
                }
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
