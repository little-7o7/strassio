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

            bool closed = a.IsClosed && b.IsClosed;
            // Самый плотный вариант, где ряды почти не налезают друг на друга (выкинуто не больше 5%
            // камней). Если ряды налезают, лишние камни выкидываются вразнобой — ряды рвутся и
            // сливаются, это выглядит хуже честного просвета.
            List<PlacedStone> best = null;
            List<PlacedStone> leastCrowded = null;
            double leastRejected = double.MaxValue;
            foreach (int rows in BlendRowCandidates(pa, pb, a, b, d + rowGap))
            {
                List<PlacedStone> stones = BlendRows(pa, pb, closed, rows, d, gap, out int tried);
                double rejected = tried == 0 ? 0 : 1 - (double)stones.Count / tried;
                if (rejected <= 0.05 && (best == null || stones.Count > best.Count))
                {
                    best = stones;
                }

                if (rejected < leastRejected)
                {
                    leastRejected = rejected;
                    leastCrowded = stones;
                }
            }

            return best ?? leastCrowded;
        }

        /// <summary>
        /// Заливка «ряды вдоль формы», как автор кладёт лист вручную: крайний ряд идёт по контуру,
        /// а внутренние ряды параллельны срединной линии формы — стоят на равном расстоянии друг от
        /// друга «в шахматку» (как соты, изогнутые вместе с формой) и просто упираются в крайний ряд.
        /// Колец внутри нет, поэтому нет и шва посередине, где кольца с двух сторон сходились бы
        /// под углом. Сдвиг сетки поперёк и вдоль подбирается так, чтобы камней вошло больше.
        /// </summary>
        /// <param name="rings">Сколько рядов идёт по контуру, прежде чем начнутся ряды вдоль формы.</param>
        /// <returns>
        /// null, если у фигуры нет «длины» (круг, квадрат): срединная линия короче её ширины, и
        /// кольца там уложат камни лучше.
        /// </returns>
        public static List<PlacedStone>? Lengthwise(IReadOnlyList<Curve> contours, double d, double gap, double margin, int rings = 1)
        {
            List<PlacedStone> ring = ContourFiller.Fill(contours, new ContourFillOptions
            {
                StoneDiameterMm = d,
                GapMm = gap,
                MarginFromEdgeMm = margin,
                MaxRings = Math.Max(1, rings),
                FillCenter = false,
            });

            var region = new ShapeRegion(contours, d);
            SignedDistanceField coarse = SignedDistanceField.Build(region.Contours, Math.Max(0.2, d / 2));
            List<Point2D> path = Skeleton.Build(coarse).LongestPath();
            double depth = 0;
            for (int iy = 0; iy < coarse.Height; iy++)
            {
                for (int ix = 0; ix < coarse.Width; ix++)
                {
                    depth = Math.Max(depth, coarse.ValueAt(ix, iy));
                }
            }

            double pathLength = 0;
            for (int i = 1; i < path.Count; i++)
            {
                pathLength += Point2D.Distance(path[i - 1], path[i]);
            }

            if (path.Count < 3 || pathLength < 2 * depth)
            {
                return null;
            }

            double diagonal = Point2D.Distance(region.Min, region.Max);
            List<Point2D> guide = Extend(SmoothPath(path, d), diagonal);
            var guideFlat = new FlattenedCurve(guide.Select(p => new FlattenedPoint(p, true)).ToList(), isClosed: false);

            double step = d + gap;
            double rowStep = step * Math.Sqrt(3) / 2;

            List<PlacedStone>? best = null;
            // Сдвиг сетки поперёк и вдоль: по два варианта. Больше почти ничего не даёт
            // (на листе 176 камней против 177), а считается вчетверо дольше.
            const int shifts = 2;
            for (int across = 0; across < shifts; across++)
            {
                double shift = rowStep * across / shifts;
                List<Point2D> middle = Math.Abs(shift) < 1e-9 ? guide : CurveOffsetter.Offset(guideFlat, shift, roundOuterCorners: true);
                for (int along = 0; along < shifts; along++)
                {
                    var packer = new StonePacker(d, gap / 2);
                    foreach (PlacedStone s in ring)
                    {
                        packer.Add(s);
                    }

                    // Средний ряд — по середине формы с точным шагом.
                    var first = new List<Point2D>();
                    var options = new LineScatterOptions
                    {
                        StoneDiameterMm = d,
                        GapMm = gap,
                        Mode = StepMode.ExactStep,
                        ExactStepMm = step,
                        StartOffsetMm = step * along / shifts,
                        CornerAngleThresholdDeg = 170,
                    };
                    foreach (PlacedStone s in LineScatterer.Scatter(Curve.FromPolyline(middle, isClosed: false), options))
                    {
                        if (region.Fits(s.Center, d / 2, margin) && packer.TryAdd(s.Center, d, 0))
                        {
                            first.Add(s.Center);
                        }
                    }

                    // Дальше в обе стороны: каждый следующий ряд — в ямки между камнями предыдущего.
                    foreach (int side in new[] { 1, -1 })
                    {
                        List<Point2D> previous = first;
                        for (int k = 1; previous.Count > 0 && k < 1000; k++)
                        {
                            previous = NestRow(previous, side, step, d, margin, region, packer, side * k);
                        }
                    }

                    FillNests(packer, region, d, gap, margin);
                    if (best == null || packer.Stones.Count > best.Count)
                    {
                        best = packer.Stones;
                    }
                }
            }

            return best ?? ring;
        }

        /// <summary>
        /// Срединная линия, посчитанная по клеткам, идёт мелким зигзагом; от каждого излома ряды
        /// разъезжаются и соты рвутся. Линия переразбивается с шагом в камень и много раз
        /// сглаживается (каждая точка тянется к середине соседей), концы остаются на месте.
        /// Плавные изгибы формы сохраняются — сглаживание действует на длине в несколько камней.
        /// </summary>
        private static List<Point2D> SmoothPath(List<Point2D> path, double d)
        {
            FlattenedCurve flat = CurveFlattener.Flatten(Curve.FromPolyline(path, isClosed: false));
            int count = Math.Max(3, (int)Math.Round(flat.TotalLength / d) + 1);
            List<Point2D> pts = Resample(flat, count);
            for (int pass = 0; pass < 30; pass++)
            {
                var next = new List<Point2D>(pts);
                for (int i = 1; i + 1 < pts.Count; i++)
                {
                    next[i] = Point2D.Lerp(pts[i], Point2D.Lerp(pts[i - 1], pts[i + 1], 0.5), 0.5);
                }

                pts = next;
            }

            return pts;
        }

        /// <summary>
        /// Доводка, как руками: во всякую ямку, где новый камень касается двух уже лежащих и
        /// помещается внутри формы, кладётся камень. Проходы повторяются, пока что-то добавляется.
        /// Соседи ищутся по клеткам — быстро и на десятках тысяч камней.
        /// </summary>
        private static void FillNests(StonePacker packer, ShapeRegion region, double d, double gap, double margin)
        {
            double step = d + gap;
            double reach = 2 * step;
            var cells = new Dictionary<(int, int), List<Point2D>>();
            (int, int) Key(Point2D p) => ((int)Math.Floor(p.X / reach), (int)Math.Floor(p.Y / reach));
            void Remember(Point2D p)
            {
                (int, int) key = Key(p);
                if (!cells.TryGetValue(key, out List<Point2D>? list))
                {
                    cells[key] = list = new List<Point2D>();
                }

                list.Add(p);
            }

            // Очередь камней, у которых ещё не проверены ямки с соседями. Каждый камень проверяется
            // один раз: неудачная ямка потом удачной не станет (камней только прибавляется), а ямки
            // с новыми камнями проверятся, когда до них дойдёт очередь.
            var queue = new Queue<Point2D>();
            foreach (PlacedStone st in packer.Stones)
            {
                Remember(st.Center);
                queue.Enqueue(st.Center);
            }

            var found = new List<Point2D>();
            while (queue.Count > 0)
            {
                Point2D p = queue.Dequeue();
                (int cx, int cy) = Key(p);
                found.Clear();
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (!cells.TryGetValue((cx + dx, cy + dy), out List<Point2D>? list))
                        {
                            continue;
                        }

                        foreach (Point2D q in list)
                        {
                            double half = Point2D.Distance(p, q) / 2;
                            if (half < 1e-9 || half >= step)
                            {
                                continue;
                            }

                            Point2D along = (q - p).Normalized();
                            var normal = new Point2D(-along.Y, along.X);
                            double h = Math.Sqrt(step * step - half * half);
                            Point2D mid = Point2D.Lerp(p, q, 0.5);
                            foreach (Point2D nest in new[] { mid + normal * h, mid - normal * h })
                            {
                                if (region.Fits(nest, d / 2, margin) && packer.TryAdd(nest, d, -3))
                                {
                                    found.Add(nest);
                                }
                            }
                        }
                    }
                }

                foreach (Point2D nest in found)
                {
                    Remember(nest);
                    queue.Enqueue(nest);
                }
            }
        }

        /// <summary>
        /// Следующий ряд «в шахматку»: камень встаёт в ямку между двумя соседними камнями
        /// предыдущего ряда — касаясь обоих (вершина равностороннего треугольника) — с нужной стороны.
        /// На концах ряд продлевается прямо, пока есть место. Возвращает центры нового ряда по порядку.
        /// </summary>
        private static List<Point2D> NestRow(
            List<Point2D> previous, int side, double step, double d, double margin, ShapeRegion region, StonePacker packer, int rowId)
        {
            var row = new List<Point2D>();
            for (int i = 0; i + 1 < previous.Count; i++)
            {
                Point2D p = previous[i];
                Point2D q = previous[i + 1];
                double half = Point2D.Distance(p, q) / 2;
                if (half >= step || half < 1e-9)
                {
                    continue; // разрыв в ряду — ямки нет
                }

                Point2D along = (q - p).Normalized();
                var normal = new Point2D(-along.Y * side, along.X * side);
                Point2D nest = Point2D.Lerp(p, q, 0.5) + normal * Math.Sqrt(step * step - half * half);
                if (region.Fits(nest, d / 2, margin) && packer.TryAdd(nest, d, rowId))
                {
                    row.Add(nest);
                }
            }

            // Продлить ряд за крайние ямки, пока камни помещаются внутри формы.
            if (row.Count >= 2)
            {
                for (int end = 0; end < 2; end++)
                {
                    for (int n = 0; n < 50; n++)
                    {
                        Point2D last = end == 0 ? row[0] : row[row.Count - 1];
                        Point2D before = end == 0 ? row[1] : row[row.Count - 2];
                        Point2D next = last + (last - before).Normalized() * step;
                        if (!region.Fits(next, d / 2, margin) || !packer.TryAdd(next, d, rowId))
                        {
                            break;
                        }

                        if (end == 0)
                        {
                            row.Insert(0, next);
                        }
                        else
                        {
                            row.Add(next);
                        }
                    }
                }
            }

            return row;
        }

        /// <summary>Две самые далёкие друг от друга точки ломаной — кончики вытянутой формы.</summary>
        private static (int, int) FarthestPair(List<Point2D> pts)
        {
            int step = Math.Max(1, pts.Count / 400);
            (int, int) best = (0, 0);
            double bestDist = -1;
            for (int i = 0; i < pts.Count; i += step)
            {
                for (int j = i + 1; j < pts.Count; j += step)
                {
                    double dist = Point2D.Distance(pts[i], pts[j]);
                    if (dist > bestDist)
                    {
                        bestDist = dist;
                        best = (i, j);
                    }
                }
            }

            return best;
        }

        /// <summary>Раскладывает переход с заданным числом промежутков между рядами.</summary>
        private static List<PlacedStone> BlendRows(List<Point2D> pa, List<Point2D> pb, bool closed, int rows, double d, double gap, out int tried)
        {
            tried = 0;
            var packer = new StonePacker(d, gap / 2);
            var options = new LineScatterOptions { StoneDiameterMm = d, GapMm = gap, Mode = StepMode.FitEven };

            // Ряды кладутся от краёв к середине: 0, последний, 1, предпоследний… Если где-то кривые
            // сходятся и ряды не помещаются, тесно становится в середине, а оба края остаются ровными.
            for (int i = 0; i <= rows; i++)
            {
                int k = i % 2 == 0 ? i / 2 : rows - i / 2;
                double t = (double)k / rows;
                List<Point2D> line = pa.Zip(pb, (p, q) => Point2D.Lerp(p, q, t)).ToList();
                if (closed)
                {
                    line.RemoveAt(line.Count - 1);
                }

                foreach (PlacedStone s in LineScatterer.Scatter(Curve.FromPolyline(line, closed), options))
                {
                    tried++;
                    packer.TryAdd(s.Center, d, k);
                }
            }

            return packer.Stones;
        }

        /// <summary>
        /// Какое число промежутков между рядами пробовать. Раньше оно считалось одной формулой
        /// (среднее расстояние ÷ шаг, с округлением), и когда округление шло вверх, ряды вставали
        /// теснее камня: каждый второй ряд налезал на соседа и выкидывался целиком, а на его месте
        /// оставалась дыра почти в камень. Теперь пробуются все разумные варианты — от «по самому
        /// узкому месту» до «по самому широкому», а выбирает из них <see cref="Blend"/>.
        /// Ширина меряется поперёк: от точки одной кривой до ближайшей точки другой.
        /// </summary>
        private static IEnumerable<int> BlendRowCandidates(List<Point2D> pa, List<Point2D> pb, FlattenedCurve a, FlattenedCurve b, double step)
        {
            double min = double.MaxValue;
            double max = 0;
            for (int i = 0; i < Math.Min(pa.Count, pb.Count); i++)
            {
                double width = Math.Max(
                    Editing.StoneEditor.DistanceToPolyline(b, pa[i]),
                    Editing.StoneEditor.DistanceToPolyline(a, pb[i]));
                min = Math.Min(min, width);
                max = Math.Max(max, Point2D.Distance(pa[i], pb[i]));
            }

            int from = Math.Max(1, (int)Math.Floor(min / step));
            int to = Math.Max(from, (int)Math.Ceiling(max / step));
            for (int rows = from; rows <= Math.Min(to, from + 15); rows++)
            {
                yield return rows;
            }
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
                // Архимедова спираль r = a·θ: соседние витки — ровно через шаг. Идём мелкими шажками и
                // ставим стразу, когда до предыдущей по прямой набралось не меньше шага (по дуге было бы
                // теснее — на крутых витках у центра стразы налезали и выпадали, оставляя дырки).
                double a = step / (2 * Math.PI);
                double theta = 0;
                Point2D? last = null;
                while (a * theta <= reach)
                {
                    double r = a * theta;
                    var p = new Point2D(center.X + r * Math.Cos(theta), center.Y + r * Math.Sin(theta));
                    if (last == null || Point2D.Distance(p, last.Value) >= step - 1e-9)
                    {
                        if (region.Fits(p, d / 2, margin))
                        {
                            packer.TryAdd(p, d);
                        }

                        last = p;
                    }

                    theta += Math.Min(0.05, step / 8 / Math.Sqrt(r * r + a * a));
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

                    // Сколько страз на кольце — по хорде, а не по дуге: хорда короче, иначе соседние
                    // стразы кольца стояли бы теснее шага и выпадали.
                    int count = Math.Max(1, (int)Math.Floor(Math.PI / Math.Asin(Math.Min(1, step / (2 * r)))));
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
