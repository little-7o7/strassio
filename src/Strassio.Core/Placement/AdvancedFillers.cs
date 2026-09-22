using System;
using System.Collections.Generic;
using System.Linq;
using Strassio.Core.Geometry;

namespace Strassio.Core.Placement
{
    /// <summary>
    /// Заливки Этапа 4 (docs/SPEC.md, раздел 5): F6 по центральной линии, F7 переход между двумя
    /// кривыми, F8 по направляющей, F9 от центра, F10 случайная плотная, F11 градиент размера,
    /// F12 добивка щелей. Все работают с отверстиями в форме и не дают наложений.
    /// </summary>
    public static class AdvancedFillers
    {
        /// <summary>
        /// F6: ряды параллельно центральной линии формы. Как контурная заливка, только ряды
        /// отсчитываются от середины, а не от края: у вытянутых форм (стебель, буква) ряды идут вдоль
        /// «хребта» симметрично, а неполный ряд остаётся у края, а не в середине.
        /// </summary>
        public static List<PlacedStone> Centerline(IReadOnlyList<Curve> contours, double d, double gap, double margin)
        {
            var region = new ShapeRegion(contours, d);
            double step = d + gap;
            double minLevel = d / 2 + margin;
            var packer = new StonePacker(d, gap / 2);
            var options = new LineScatterOptions { StoneDiameterMm = d, GapMm = gap, Mode = StepMode.FitEven };

            int row = 0;
            for (double level = region.MaxDepth - step / 2; level >= minLevel - 1e-9; level -= step, row++)
            {
                foreach (List<Point2D> loop in IsoContour.Trace(region.Field, level))
                {
                    if (loop.Count < 3)
                    {
                        continue;
                    }

                    foreach (PlacedStone s in LineScatterer.Scatter(Curve.FromPolyline(loop, isClosed: true), options))
                    {
                        if (region.Fits(s.Center, d / 2, margin))
                        {
                            packer.TryAdd(s.Center, d, row);
                        }
                    }
                }
            }

            return packer.Stones;
        }

        /// <summary>
        /// F7: ряды плавно перетекают от одной кривой к другой. Обе кривые разбиваются на одинаковое
        /// число точек по длине; промежуточные ряды — смесь двух кривых. Число рядов — сколько
        /// помещается между кривыми при заданном зазоре.
        /// </summary>
        public static List<PlacedStone> Blend(Curve first, Curve second, double d, double gap, double rowGap)
        {
            FlattenedCurve a = CurveFlattener.Flatten(first);
            FlattenedCurve b = CurveFlattener.Flatten(second);
            const int samples = 240;
            List<Point2D> pa = Resample(a, samples);
            List<Point2D> pb = Resample(b, samples);

            // Кривые могли быть нарисованы в разные стороны — разворачиваем вторую, если так ближе.
            double straight = Point2D.Distance(pa[0], pb[0]) + Point2D.Distance(pa[samples - 1], pb[samples - 1]);
            double crossed = Point2D.Distance(pa[0], pb[samples - 1]) + Point2D.Distance(pa[samples - 1], pb[0]);
            if (crossed < straight)
            {
                pb.Reverse();
            }

            double averageDistance = pa.Zip(pb, Point2D.Distance).Average();
            int rows = Math.Max(1, (int)Math.Round(averageDistance / (d + rowGap)));
            bool closed = a.IsClosed && b.IsClosed;

            var packer = new StonePacker(d, gap / 2);
            var options = new LineScatterOptions { StoneDiameterMm = d, GapMm = gap, Mode = StepMode.FitEven };
            for (int k = 0; k <= rows; k++)
            {
                double t = (double)k / rows;
                List<Point2D> line = pa.Zip(pb, (p, q) => Point2D.Lerp(p, q, t)).ToList();
                if (closed)
                {
                    line.RemoveAt(line.Count - 1);
                }

                foreach (PlacedStone s in LineScatterer.Scatter(Curve.FromPolyline(line, closed), options))
                {
                    packer.TryAdd(s.Center, d, k);
                }
            }

            return packer.Stones;
        }

        /// <summary>
        /// F8: ряды внутри формы повторяют изгиб направляющей линии, которую нарисовал пользователь.
        /// Направляющая продлевается за края формы и смещается шагами в обе стороны; камни остаются
        /// только внутри формы.
        /// </summary>
        public static List<PlacedStone> AlongGuide(IReadOnlyList<Curve> contours, Curve guide, double d, double gap, double rowGap, double margin)
        {
            var region = new ShapeRegion(contours, d);
            double diagonal = Point2D.Distance(region.Min, region.Max);
            FlattenedCurve flat = CurveFlattener.Flatten(guide);
            List<Point2D> extended = Extend(flat.Points.Select(p => p.Position).ToList(), diagonal);
            var guideFlat = new FlattenedCurve(extended.Select(p => new FlattenedPoint(p, true)).ToList(), isClosed: false);

            double step = d + rowGap;
            int maxRows = (int)Math.Ceiling(diagonal / step) + 2;
            var packer = new StonePacker(d, gap / 2);
            var options = new LineScatterOptions { StoneDiameterMm = d, GapMm = gap, Mode = StepMode.ExactStep, ExactStepMm = d + gap };

            // Ряды от направляющей к краям — ближние ряды главнее.
            for (int i = 0; i <= 2 * maxRows; i++)
            {
                int k = (i + 1) / 2 * (i % 2 == 0 ? -1 : 1);
                List<Point2D> row = k == 0 ? extended : CurveOffsetter.Offset(guideFlat, k * step, roundOuterCorners: true);
                if (row.Count < 2)
                {
                    continue;
                }

                foreach (PlacedStone s in LineScatterer.Scatter(Curve.FromPolyline(row, isClosed: false), options))
                {
                    if (region.Fits(s.Center, d / 2, margin))
                    {
                        packer.TryAdd(s.Center, d, k + maxRows);
                    }
                }
            }

            return packer.Stones;
        }

        /// <summary>
        /// F9: от центра формы (самой «глубокой» точки) — кольцами или спиралью. Камни за краем
        /// формы и в отверстиях не ставятся.
        /// </summary>
        public static List<PlacedStone> FromCenter(IReadOnlyList<Curve> contours, double d, double gap, double margin, bool spiral)
        {
            var region = new ShapeRegion(contours, d);
            Point2D center = region.DeepestPoint;
            double step = d + gap;
            double reach = new[] { region.Min, region.Max, new Point2D(region.Min.X, region.Max.Y), new Point2D(region.Max.X, region.Min.Y) }
                .Max(c => Point2D.Distance(c, center)) + step;
            var packer = new StonePacker(d, gap / 2);

            if (spiral)
            {
                // Архимедова спираль r = a·θ: соседние витки — ровно через шаг.
                double a = step / (2 * Math.PI);
                double theta = 0;
                while (a * theta <= reach)
                {
                    double r = a * theta;
                    var p = new Point2D(center.X + r * Math.Cos(theta), center.Y + r * Math.Sin(theta));
                    if (region.Fits(p, d / 2, margin))
                    {
                        packer.TryAdd(p, d);
                    }

                    theta += step / Math.Sqrt(r * r + a * a);
                }
            }
            else
            {
                if (region.Fits(center, d / 2, margin))
                {
                    packer.TryAdd(center, d, 0);
                }

                for (int ring = 1; ring * step <= reach; ring++)
                {
                    double r = ring * step;
                    int count = Math.Max(1, (int)Math.Floor(2 * Math.PI * r / step));
                    double phase = ring % 2 == 0 ? 0 : Math.PI / count;
                    for (int i = 0; i < count; i++)
                    {
                        double angle = phase + 2 * Math.PI * i / count;
                        var p = new Point2D(center.X + r * Math.Cos(angle), center.Y + r * Math.Sin(angle));
                        if (region.Fits(p, d / 2, margin))
                        {
                            packer.TryAdd(p, d, ring);
                        }
                    }
                }
            }

            return packer.Stones;
        }

        /// <summary>
        /// F10: случайная плотная россыпь (Poisson-disk, алгоритм Бридсона) без наложений, с минимальным
        /// зазором; можно смешивать несколько размеров. <paramref name="seed"/> — «вариант»: другой номер
        /// даёт другую случайную раскладку, тот же — ту же самую.
        /// </summary>
        public static List<PlacedStone> Random(
            IReadOnlyList<Curve> contours, IReadOnlyList<double> diameters, double gap, double margin, int seed)
        {
            double maxD = diameters.Max();
            double minD = diameters.Min();
            var region = new ShapeRegion(contours, minD);
            var rnd = new Random(seed);
            var packer = new StonePacker(maxD, gap);
            var active = new List<int>();

            double Pick() => diameters[rnd.Next(diameters.Count)];

            void Seed(Point2D p)
            {
                double dd = Pick();
                if (region.Fits(p, dd / 2, margin) && packer.TryAdd(p, dd))
                {
                    active.Add(packer.Stones.Count - 1);
                }
            }

            void Grow()
            {
                while (active.Count > 0)
                {
                    int ai = rnd.Next(active.Count);
                    PlacedStone from = packer.Stones[active[ai]];
                    bool placed = false;
                    for (int attempt = 0; attempt < 30; attempt++)
                    {
                        double dd = Pick();
                        double min = (from.DiameterMm + dd) / 2 + gap;
                        double dist = min * (1 + 0.25 * rnd.NextDouble());
                        double angle = rnd.NextDouble() * 2 * Math.PI;
                        var p = new Point2D(from.Center.X + dist * Math.Cos(angle), from.Center.Y + dist * Math.Sin(angle));
                        if (region.Fits(p, dd / 2, margin) && packer.TryAdd(p, dd))
                        {
                            active.Add(packer.Stones.Count - 1);
                            placed = true;
                            break;
                        }
                    }

                    if (!placed)
                    {
                        active.RemoveAt(ai);
                    }
                }
            }

            Seed(region.DeepestPoint);
            Grow();

            // Несвязные части формы (буквы, «острова») — досеиваем по крупной сетке.
            double scan = maxD + gap;
            for (double y = region.Min.Y; y <= region.Max.Y; y += scan)
            {
                for (double x = region.Min.X; x <= region.Max.X; x += scan)
                {
                    Seed(new Point2D(x, y));
                    Grow();
                }
            }

            return packer.Stones;
        }

        /// <summary>
        /// F11: градиент размера — крупные камни в центре, мелкие к краю (или слева направо). Каждое
        /// место получает размер по тому, насколько оно близко к центру (к левому краю), из списка
        /// <paramref name="diameters"/> от первого к последнему.
        /// </summary>
        public static List<PlacedStone> Gradient(
            IReadOnlyList<Curve> contours, IReadOnlyList<double> diameters, double gap, double margin, bool horizontal)
        {
            double minD = diameters.Min();
            var region = new ShapeRegion(contours, minD);
            var packer = new StonePacker(diameters.Max(), gap);

            double Fraction(Point2D p) => horizontal
                ? (p.X - region.Min.X) / Math.Max(1e-9, region.Max.X - region.Min.X)
                : 1 - Math.Max(0, region.Depth(p)) / Math.Max(1e-9, region.MaxDepth);

            // Кандидаты — мелкая шахматная сетка; обходим от «крупного конца» к «мелкому».
            double spacing = minD / 3;
            var candidates = new List<(Point2D P, double T)>();
            int row = 0;
            for (double y = region.Min.Y; y <= region.Max.Y; y += spacing * Math.Sqrt(3) / 2, row++)
            {
                double shift = row % 2 == 0 ? 0 : spacing / 2;
                for (double x = region.Min.X + shift; x <= region.Max.X; x += spacing)
                {
                    var p = new Point2D(x, y);
                    if (region.Depth(p) >= minD / 2 + margin)
                    {
                        candidates.Add((p, Math.Max(0, Math.Min(1, Fraction(p)))));
                    }
                }
            }

            int k = diameters.Count;
            foreach ((Point2D p, double t) in candidates.OrderBy(c => c.T))
            {
                double dd = diameters[Math.Min(k - 1, (int)Math.Floor(t * k))];
                if (region.Fits(p, dd / 2, margin))
                {
                    packer.TryAdd(p, dd);
                }
            }

            return packer.Stones;
        }

        /// <summary>
        /// F12: основная заливка сотами, а в оставшиеся щели (у края, в узких местах) — мелкие камни.
        /// </summary>
        public static List<PlacedStone> FillGaps(
            IReadOnlyList<Curve> contours, double d, double smallD, double gap, double margin)
        {
            var region = new ShapeRegion(contours, Math.Min(d, smallD));
            var packer = new StonePacker(Math.Max(d, smallD), gap);
            foreach (PlacedStone s in GridFiller.Fill(contours, new GridFillOptions
            {
                StoneDiameterMm = d, GapMm = gap, Pattern = GridPattern.Honeycomb, MarginFromEdgeMm = margin,
            }))
            {
                packer.Add(s);
            }

            double spacing = smallD / 4;
            for (double y = region.Min.Y; y <= region.Max.Y; y += spacing)
            {
                for (double x = region.Min.X; x <= region.Max.X; x += spacing)
                {
                    var p = new Point2D(x, y);
                    if (region.Fits(p, smallD / 2, margin))
                    {
                        packer.TryAdd(p, smallD);
                    }
                }
            }

            return packer.Stones;
        }

        /// <summary>
        /// Автоподбор сетки (раздел 5, [NEW]): перебирает сдвиг сетки (и, если нужно, небольшой поворот)
        /// и выбирает вариант, где в форму помещается больше целых камней — меньше дыр у края.
        /// </summary>
        public static List<PlacedStone> AutoGrid(IReadOnlyList<Curve> contours, GridFillOptions baseOptions, bool tryAngles)
        {
            double step = baseOptions.StoneDiameterMm + baseOptions.GapMm;
            double rowSpacing = baseOptions.Pattern == GridPattern.Honeycomb ? step * Math.Sqrt(3) / 2 : step;
            double[] angles = tryAngles
                ? new[] { 0.0, -5, 5, -10, 10, -15, 15 }.Select(a => baseOptions.AngleDeg + a).ToArray()
                : new[] { baseOptions.AngleDeg };
            int offsets = tryAngles ? 3 : 5;

            List<PlacedStone> best = new List<PlacedStone>();
            foreach (double angle in angles)
            {
                for (int ix = 0; ix < offsets; ix++)
                {
                    for (int iy = 0; iy < offsets; iy++)
                    {
                        List<PlacedStone> stones = GridFiller.Fill(contours, new GridFillOptions
                        {
                            StoneDiameterMm = baseOptions.StoneDiameterMm,
                            GapMm = baseOptions.GapMm,
                            Pattern = baseOptions.Pattern,
                            AngleDeg = angle,
                            MarginFromEdgeMm = baseOptions.MarginFromEdgeMm,
                            OffsetXMm = step * ix / offsets,
                            OffsetYMm = rowSpacing * iy / offsets,
                        });
                        if (stones.Count > best.Count)
                        {
                            best = stones;
                        }
                    }
                }
            }

            return best;
        }

        /// <summary>Равномерно по длине: <paramref name="count"/> точек от начала до конца (у замкнутой — по кругу).</summary>
        private static List<Point2D> Resample(FlattenedCurve flat, int count)
        {
            var result = new List<Point2D>(count);
            double length = flat.TotalLength;
            for (int i = 0; i < count; i++)
            {
                double t = flat.IsClosed ? (double)i / count : (double)i / (count - 1);
                result.Add(flat.PointAtDistance(length * t));
            }

            if (flat.IsClosed)
            {
                result.Add(result[0]);
            }

            return result;
        }

        /// <summary>Продлевает ломаную прямыми отрезками на <paramref name="by"/> с обоих концов.</summary>
        private static List<Point2D> Extend(List<Point2D> points, double by)
        {
            if (points.Count < 2)
            {
                return points;
            }

            Point2D startDir = (points[0] - points[1]).Normalized();
            Point2D endDir = (points[points.Count - 1] - points[points.Count - 2]).Normalized();
            var result = new List<Point2D> { points[0] + startDir * by };
            result.AddRange(points);
            result.Add(points[points.Count - 1] + endDir * by);
            return result;
        }
    }
}
