using System;
using System.Collections.Generic;
using System.Linq;
using Strassio.Core.Geometry;
using Strassio.Core.Methods;
using Strassio.Core.Placement;

namespace Strassio.Core.Vector
{
    /// <summary>Как выложить надпись (docs/SPEC.md, раздел 10.1).</summary>
    public enum TextMode
    {
        /// <summary>Обвести буквы: один ряд по краю каждой буквы изнутри («Кант» в один ряд).</summary>
        Outline,

        /// <summary>Один ряд по середине штриха, размер камня следует толщине («По толщине штриха»).</summary>
        Middle,

        /// <summary>Залить буквы («Контурная»).</summary>
        Fill,
    }

    /// <summary>Что не так с надписью для выбранного камня и способа.</summary>
    public sealed class TextReport
    {
        internal TextReport(
            int letters, double strokeMm, double neededMm, IReadOnlyList<Point2D> thinSpots,
            double closestMm, double extraSpacingMm, IReadOnlyList<Point2D> closeSpots, int overlaps)
        {
            LetterCount = letters;
            StrokeMm = strokeMm;
            NeededMm = neededMm;
            ThinSpots = thinSpots;
            ClosestMm = closestMm;
            ExtraSpacingMm = extraSpacingMm;
            CloseSpots = closeSpots;
            Overlaps = overlaps;
        }

        /// <summary>Сколько букв (у «i» и «ё» точки — отдельные буквы, это не мешает).</summary>
        public int LetterCount { get; }

        /// <summary>Обычная толщина штриха — половина длины всех штрихов тоньше, половина толще, мм.</summary>
        public double StrokeMm { get; }

        /// <summary>Какая толщина штриха нужна выбранному способу и камню, мм.</summary>
        public double NeededMm { get; }

        /// <summary>Во сколько раз увеличить надпись, чтобы обычный штрих стал нужной толщины (1 — не нужно).</summary>
        public double Scale => StrokeMm <= 0 ? 1 : Math.Max(1, NeededMm / StrokeMm);

        public bool IsThin => Scale > 1.0001;

        /// <summary>Тонкие места, где камень не встанет (по одной точке на место, не больше 40).</summary>
        public IReadOnlyList<Point2D> ThinSpots { get; }

        /// <summary>Самый узкий промежуток между соседними буквами, мм (бесконечность — буква одна).</summary>
        public double ClosestMm { get; }

        /// <summary>На сколько раздвинуть буквы, чтобы камни соседних букв не слились, мм (0 — не нужно).</summary>
        public double ExtraSpacingMm { get; }

        public bool IsTight => ExtraSpacingMm > 0;

        /// <summary>Где буквы почти касаются (по точке на пару букв).</summary>
        public IReadOnlyList<Point2D> CloseSpots { get; }

        /// <summary>Сколько пар букв налезают друг на друга (рукописные шрифты) — их надо объединить в CorelDRAW.</summary>
        public int Overlaps { get; }
    }

    /// <summary>
    /// Надпись стразами одной кнопкой (docs/SPEC.md, раздел 10.1): какой метод нужен способу, и проверка
    /// текста до того, как камни поставлены, — не слишком ли тонкие буквы для камня и не слишком ли
    /// близко они стоят друг к другу.
    /// </summary>
    public static class TextCheck
    {
        private const int MaxSpots = 40;

        /// <summary>Метод, которым выкладывается надпись.</summary>
        public static MethodKind MethodFor(TextMode mode) =>
            mode == TextMode.Outline ? MethodKind.F5 : mode == TextMode.Middle ? MethodKind.F13 : MethodKind.F3;

        /// <summary>
        /// Параметры метода для надписи: свои у пользователя (зазор, отступ, узор середины), а для обводки —
        /// ровно один ряд по краю.
        /// </summary>
        public static MethodParameters ParametersFor(TextMode mode, MethodParameters user)
        {
            MethodParameters p = user.Clone();
            if (mode == TextMode.Outline)
            {
                p.Rings = 1;
            }

            return p;
        }

        /// <summary>
        /// Какая толщина штриха нужна. Обвести: два ряда (по одному у каждого края) и зазор между ними — иначе
        /// ряды с двух сторон штриха налезут друг на друга. Залить: хотя бы один камень. По середине: самый
        /// мелкий камень.
        /// </summary>
        public static double NeededStroke(TextMode mode, double stoneMm, double smallestMm, double gapMm, double marginMm)
        {
            switch (mode)
            {
                case TextMode.Outline:
                    return 2 * stoneMm + gapMm + 2 * marginMm;
                case TextMode.Middle:
                    return Math.Min(stoneMm, smallestMm) + 2 * marginMm;
                default:
                    return stoneMm + 2 * marginMm;
            }
        }

        /// <summary>
        /// Делит контуры надписи на буквы: внешний контур и дырки внутри него («о», «а», «8»). Контур целиком
        /// внутри нечётного числа других — дырка; она достаётся самому маленькому внешнему контуру, который её
        /// обнимает. Контур, который заходит в другой только частью (буквы рукописного шрифта налезают друг на
        /// друга), — отдельная буква.
        /// </summary>
        public static List<List<Curve>> Letters(IReadOnlyList<Curve> contours)
        {
            var flats = contours.Select(c => CurveFlattener.Flatten(c)).ToList();
            int n = contours.Count;
            var depth = new int[n];
            var area = new double[n];
            for (int i = 0; i < n; i++)
            {
                area[i] = Math.Abs(CurveMetrics.SignedArea(flats[i]));
                List<Point2D> probes = Resample(flats[i], Math.Max(0.1, flats[i].TotalLength / 16)).ToList();
                for (int j = 0; j < n; j++)
                {
                    if (j != i && flats[j].IsClosed && probes.All(p => PointInPolygon.IsInside(flats[j], p)))
                    {
                        depth[i]++;
                    }
                }
            }

            var letters = new List<List<Curve>>();
            var owner = new int[n];
            for (int i = 0; i < n; i++)
            {
                owner[i] = -1;
                if (depth[i] % 2 == 0)
                {
                    owner[i] = letters.Count;
                    letters.Add(new List<Curve> { contours[i] });
                }
            }

            for (int i = 0; i < n; i++)
            {
                if (depth[i] % 2 == 0)
                {
                    continue;
                }

                Point2D probe = flats[i].Points[0].Position;
                int best = -1;
                for (int j = 0; j < n; j++)
                {
                    if (owner[j] >= 0 && PointInPolygon.IsInside(flats[j], probe) && (best < 0 || area[j] < area[best]))
                    {
                        best = j;
                    }
                }

                if (best >= 0)
                {
                    letters[owner[best]].Add(contours[i]);
                }
            }

            return letters;
        }

        public static TextReport Analyze(
            IReadOnlyList<Curve> contours, TextMode mode, double stoneMm, double smallestMm, double gapMm, double marginMm)
        {
            List<List<Curve>> letters = Letters(contours.Where(c => CurveFlattener.Flatten(c).IsClosed).ToList());
            double needed = NeededStroke(mode, stoneMm, smallestMm, gapMm, marginMm);

            // Толщина штриха — по средней линии каждой буквы, через каждые 0,2 мм.
            var widths = new List<(double Width, Point2D At)>();
            var thinSpots = new List<Point2D>();
            double thinLimit = mode == TextMode.Middle ? Math.Min(stoneMm, smallestMm) / 2 : needed;
            foreach (List<Curve> letter in letters)
            {
                var region = new ShapeRegion(letter, Math.Max(0.4, Math.Min(stoneMm, smallestMm)));
                foreach (Curve line in Centerline.Build(letter, smooth: false))
                {
                    FlattenedCurve flat = CurveFlattener.Flatten(line);
                    int count = Math.Max(1, (int)Math.Ceiling(flat.TotalLength / 0.2));
                    var run = new List<Point2D>();
                    for (int i = 0; i <= count; i++)
                    {
                        Point2D p = flat.PointAtDistance(flat.TotalLength * i / count);
                        double width = 2 * region.Depth(p);
                        widths.Add((width, p));
                        if (width < thinLimit)
                        {
                            run.Add(p);
                        }
                        else
                        {
                            AddSpot(run, thinSpots, stoneMm);
                        }
                    }

                    AddSpot(run, thinSpots, stoneMm);
                }
            }

            double stroke = widths.Count == 0 ? 0 : widths.Select(w => w.Width).OrderBy(w => w).ElementAt(widths.Count / 2);

            // Буквы ближе, чем на камень (по краям камней), сливаются. У «по середине» камни стоят дальше от края
            // буквы — там хватает половины камня.
            double want = mode == TextMode.Middle ? stoneMm / 2 : Math.Max(0, stoneMm - 2 * marginMm);
            (double closest, List<Point2D> closeSpots, int overlaps) = Closeness(letters, want);
            double extra = closest < want ? want - Math.Max(0, closest) : 0;
            return new TextReport(
                letters.Count, stroke, needed, thinSpots.Take(MaxSpots).ToList(), closest, extra, closeSpots.Take(MaxSpots).ToList(), overlaps);
        }

        /// <summary>Тонкое место длиннее половины камня — одна пометка посередине (короткие острия засечек не в счёт).</summary>
        private static void AddSpot(List<Point2D> run, List<Point2D> spots, double stoneMm)
        {
            if (run.Count * 0.2 >= stoneMm / 2)
            {
                spots.Add(run[run.Count / 2]);
            }

            run.Clear();
        }

        /// <summary>Самый узкий промежуток между буквами и точки, где соседние буквы ближе <paramref name="want"/>.</summary>
        private static (double Closest, List<Point2D> Spots, int Overlaps) Closeness(List<List<Curve>> letters, double want)
        {
            var spots = new List<Point2D>();
            if (letters.Count < 2)
            {
                return (double.PositiveInfinity, spots, 0);
            }

            List<FlattenedCurve>[] flats = letters.Select(l => l.Select(c => CurveFlattener.Flatten(c)).ToList()).ToArray();
            List<Point2D>[] samples = flats.Select(f => f.SelectMany(c => Resample(c, 0.2)).ToList()).ToArray();
            double reach = Math.Max(want, 0.5) + 0.5;

            // Точки всех букв по клеткам — соседи ищутся только рядом.
            var grid = new Dictionary<(int, int), List<(int Letter, Point2D P)>>();
            (int, int) Key(Point2D p) => ((int)Math.Floor(p.X / reach), (int)Math.Floor(p.Y / reach));
            for (int l = 0; l < samples.Length; l++)
            {
                foreach (Point2D p in samples[l])
                {
                    if (!grid.TryGetValue(Key(p), out List<(int, Point2D)>? list))
                    {
                        list = new List<(int, Point2D)>();
                        grid[Key(p)] = list;
                    }

                    list.Add((l, p));
                }
            }

            var pairs = new Dictionary<(int, int), (double Distance, Point2D Mid)>();
            for (int l = 0; l < samples.Length; l++)
            {
                foreach (Point2D p in samples[l])
                {
                    (int kx, int ky) = Key(p);
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            if (!grid.TryGetValue((kx + dx, ky + dy), out List<(int Letter, Point2D P)>? list))
                            {
                                continue;
                            }

                            foreach ((int other, Point2D q) in list)
                            {
                                if (other <= l)
                                {
                                    continue;
                                }

                                double d = Point2D.Distance(p, q);
                                if (d < reach && (!pairs.TryGetValue((l, other), out var best) || d < best.Distance))
                                {
                                    pairs[(l, other)] = (d, (p + q) / 2);
                                }
                            }
                        }
                    }
                }
            }

            double closest = double.PositiveInfinity;
            int overlaps = 0;
            foreach (KeyValuePair<(int, int), (double Distance, Point2D Mid)> pair in pairs)
            {
                (int a, int b) = pair.Key;

                // Буквы налезают: точка одной внутри другой (рукописные шрифты).
                bool overlap = samples[a].Any(p => Inside(flats[b], p)) || samples[b].Any(p => Inside(flats[a], p));
                double distance = overlap ? 0 : pair.Value.Distance;
                if (overlap)
                {
                    overlaps++;
                }

                closest = Math.Min(closest, distance);
                if (distance < want)
                {
                    spots.Add(pair.Value.Mid);
                }
            }

            return (closest, spots, overlaps);
        }

        private static bool Inside(List<FlattenedCurve> letter, Point2D p) =>
            letter.Count(c => PointInPolygon.IsInside(c, p)) % 2 == 1;

        private static IEnumerable<Point2D> Resample(FlattenedCurve c, double step)
        {
            int count = Math.Max(1, (int)Math.Ceiling(c.TotalLength / step));
            for (int i = 0; i < count; i++)
            {
                yield return c.PointAtDistance(c.TotalLength * i / count);
            }
        }
    }
}
