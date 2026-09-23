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
        /// <summary>
        /// Насколько далеко от вершины разрешено срезать угол при скруглении — в шагах ряда.
        /// Два шага примерно соответствуют углу 29°: тупее — скругляем, острее — оставляем острым,
        /// иначе кончик фигуры (клюв сердца, тонкий шип) срезается почти целиком.
        /// </summary>
        internal const double MaxRoundCutInSteps = 2.0;


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

            // Скруглённые углы: вместо стразы в вершине обе стороны укорачиваются на cut, и две
            // крайние стразы обходят вершину вплотную друг к другу (см. CornerPlacement).
            double[] cornerCut = PlanCornerRounding(flat, breakpoints, segCount, loopCloses, total, options, stoneStep);

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

                // У скруглённого угла отрезок начинается (и заканчивается) не в вершине, а на срезе.
                int nextIndex = loopCloses && i == segCount - 1 ? 0 : i + 1;
                double startTrim = cornerCut[i];
                double endTrim = cornerCut[nextIndex];

                double subLength = bDist - aDist - startTrim - endTrim;
                bool endForced = bIsCorner || options.Mode != StepMode.ExactStep;
                int? gapsForSegment = gapsPerSegment?[i];

                List<double> positions = FillSubSegmentCore(subLength, options, stoneStep, endForced, gapsForSegment);

                bool isLastSegment = i == segCount - 1;
                // Конец последнего отрезка совпадает с началом первого только у острого угла: у
                // скруглённого это две разные стразы по разные стороны среза.
                bool skipEndPoint = loopCloses && isLastSegment && cornerCut[0] <= 0;
                var segmentStones = new List<(int ResultIndex, double LocalDist)>();

                foreach (double pos in positions)
                {
                    if (i > 0 && pos <= 1e-9 && startTrim <= 0)
                    {
                        continue; // уже добавлено как конец предыдущего отрезка
                    }

                    if (skipEndPoint && pos >= subLength - 1e-9)
                    {
                        continue; // это та же точка, что начало первого отрезка (замкнутый контур)
                    }

                    double distance = aDist + startTrim + pos;
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
                flat, result, perSegmentStones, breakpoints, segCount, cornerCut,
                options.StoneDiameterMm, options.CornerMinGapMm, options.MaxCornerNudgeMm, options.CornerTaperCount);

            return result;
        }

        /// <summary>
        /// Решает по каждому углу: оставить острым (страза точно в вершине) или скруглить, и если
        /// скруглять — насколько срезать вершину.
        ///
        /// Скругление устроено просто: обе стороны угла укорачиваются на одно и то же расстояние
        /// cut, и крайние стразы этих сторон встают ровно на срезе. Расстояние между ними тогда
        /// равно обычному шагу ряда — они касаются друг друга так же, как все остальные, а камня
        /// в самой вершине нет. Центры этих двух страз лежат на дуге, вписанной в угол, поэтому
        /// ряд обходит вершину плавно: cut = шаг / (2·sin(половина угла)).
        ///
        /// Если срезать столько некуда (сторона слишком короткая), угол остаётся острым — лучше
        /// небольшой просвет у вершины, чем съеденная половина стороны.
        /// </summary>
        private static double[] PlanCornerRounding(
            FlattenedCurve flat, List<(double Distance, bool IsCorner)> breakpoints, int segCount,
            bool loopCloses, double total, LineScatterOptions options, double stoneStep)
        {
            var cuts = new double[breakpoints.Count];
            if (options.CornerPlacement == CornerPlacement.Sharp || segCount <= 0)
            {
                return cuts;
            }

            for (int i = 0; i < breakpoints.Count; i++)
            {
                if (!breakpoints[i].IsCorner)
                {
                    continue;
                }

                double angleDeg = InteriorAngleDeg(flat, breakpoints[i].Distance);
                if (double.IsNaN(angleDeg))
                {
                    continue;
                }

                // Допуск: угол, построенный через синусы, может получиться 59,9999999 вместо 60 —
                // ровно пороговый угол должен уверенно считаться тупым.
                if (options.CornerPlacement == CornerPlacement.Mixed && angleDeg >= options.RoundCornerBelowDeg - 1e-6)
                {
                    continue; // тупее порога — обычный острый угол со стразой в вершине
                }

                double sinHalf = Math.Sin(angleDeg * Math.PI / 360.0);
                if (sinHalf <= 1e-6)
                {
                    continue; // сложенная вдвое линия — скруглять нечего
                }

                double cut = stoneStep / (2 * sinHalf);

                // Место для среза: не больше 45% каждой из двух сторон, иначе сторона «съедается».
                int incoming = i == 0 ? (loopCloses ? segCount - 1 : -1) : i - 1;
                int outgoing = i < segCount ? i : -1;
                if (incoming < 0 || outgoing < 0)
                {
                    continue;
                }

                double room = 0.45 * Math.Min(
                    SegmentLength(breakpoints, incoming, segCount, loopCloses, total),
                    SegmentLength(breakpoints, outgoing, segCount, loopCloses, total));

                // И не больше двух шагов ряда. Чем острее угол, тем дальше приходится отступать:
                // при 36° это ещё полтора шага (кончик почти на месте), а при 15° — уже шесть,
                // и от клюва сердца ничего не остаётся. Такой угол лучше оставить острым: страза
                // встаёт в самую вершину, а соседние разводит плавный сдвиг (NudgeStonesNearSharpCorners).
                room = Math.Min(room, MaxRoundCutInSteps * stoneStep);

                if (cut > room)
                {
                    continue;
                }

                cuts[i] = cut;
            }

            return cuts;
        }

        /// <summary>
        /// Угол между сторонами в вершине (градусы): 180 — линия идёт прямо, 90 — прямой угол,
        /// 36 — кончик звезды. Направления берутся по соседним точкам полилинии, поэтому считаются
        /// точно, без приближений по длине дуги. NaN — вершину не нашли.
        /// </summary>
        internal static double InteriorAngleDeg(FlattenedCurve flat, double distance)
        {
            IReadOnlyList<FlattenedPoint> pts = flat.Points;
            IReadOnlyList<double> arcs = flat.ArcLengths;
            int n = pts.Count;
            if (n < 3)
            {
                return double.NaN;
            }

            int index = -1;
            double best = double.MaxValue;
            for (int i = 0; i < n; i++)
            {
                double d = Math.Abs(arcs[i] - distance);
                if (d < best)
                {
                    best = d;
                    index = i;
                }
            }

            if (index < 0 || best > 1e-6)
            {
                return double.NaN;
            }

            Point2D prev, next;
            Point2D cur = pts[index].Position;

            if (index == 0 || index == n - 1)
            {
                if (!flat.IsClosed)
                {
                    return double.NaN; // конец разомкнутой кривой — не угол
                }

                prev = pts[n - 2].Position; // точка перед дублирующей точкой замыкания
                next = pts[1].Position;
                cur = pts[0].Position;
            }
            else
            {
                prev = pts[index - 1].Position;
                next = pts[index + 1].Position;
            }

            Point2D u1 = (prev - cur).Normalized();
            Point2D u2 = (next - cur).Normalized();
            if (u1 == Point2D.Zero || u2 == Point2D.Zero)
            {
                return double.NaN;
            }

            double cos = u1.X * u2.X + u1.Y * u2.Y;
            cos = cos < -1 ? -1 : cos > 1 ? 1 : cos;
            return Math.Acos(cos) * 180.0 / Math.PI;
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
            List<(double Distance, bool IsCorner)> breakpoints, int segCount, double[] cornerCut,
            double stoneDiameterMm, double cornerMinGapMm, double maxNudgeMm, int cornerTaperCount)
        {
            double targetMinDistance = stoneDiameterMm + cornerMinGapMm;
            var appliedNudge = new double[result.Count];
            int taperCount = Math.Max(1, cornerTaperCount);

            for (int i = 0; i < segCount; i++)
            {
                // Скруглённый угол разведён самой геометрией среза — сдвигать там нечего.
                if (!breakpoints[i].IsCorner || cornerCut[i] > 0)
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

                    // Подгонка может чуть сжать шаг, но не больше чем на половину зазора: при зазоре 0
                    // любое сжатие — это налезающие стразы (скриншоты автора: ряд по прямоугольнику
                    // рвался, лишние стразы удалялись). Тогда шаг не сжимается, а растягивается.
                    double minSpacing = options.StoneDiameterMm + options.GapMm * 0.5;
                    while (count > 2 && subLength / (count - 1) < minSpacing - 1e-9)
                    {
                        count--;
                    }

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
