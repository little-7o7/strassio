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

            for (int i = 0; i < segCount; i++)
            {
                double aDist = breakpoints[i].Distance;
                bool bIsCorner;
                double bDist;

                if (loopCloses && i == segCount - 1)
                {
                    bDist = breakpoints[0].Distance + total;
                    bIsCorner = breakpoints[0].IsCorner;
                }
                else
                {
                    bDist = breakpoints[i + 1].Distance;
                    bIsCorner = breakpoints[i + 1].IsCorner;
                }

                double subLength = bDist - aDist;
                bool endForced = bIsCorner || options.Mode != StepMode.ExactStep;
                int? gapsForSegment = gapsPerSegment?[i];

                List<double> positions = FillSubSegment(subLength, options, stoneStep, endForced, gapsForSegment);

                bool isLastSegment = i == segCount - 1;
                bool skipEndPoint = loopCloses && isLastSegment;

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
                }
            }

            return result;
        }

        private static double SegmentLength(
            List<(double Distance, bool IsCorner)> breakpoints, int i, int segCount, bool loopCloses, double total)
        {
            double a = breakpoints[i].Distance;
            double b = loopCloses && i == segCount - 1 ? breakpoints[0].Distance + total : breakpoints[i + 1].Distance;
            return b - a;
        }

        /// <summary>Позиции страз внутри одного независимого отрезка (0 — его начало, всегда включается вызывающим кодом).</summary>
        private static List<double> FillSubSegment(
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
