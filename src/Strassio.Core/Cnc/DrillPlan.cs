using System;
using System.Collections.Generic;
using System.Linq;
using Strassio.Core.Geometry;

namespace Strassio.Core.Cnc
{
    /// <summary>Отверстие под стразу: центр в координатах документа (мм), диаметр, размер и цвет камня.</summary>
    public readonly struct DrillHole
    {
        public DrillHole(Point2D center, double diameterMm, string size, string color)
        {
            Center = center;
            DiameterMm = diameterMm;
            Size = size ?? string.Empty;
            Color = color ?? string.Empty;
        }

        public Point2D Center { get; }

        public double DiameterMm { get; }

        public string Size { get; }

        public string Color { get; }
    }

    /// <summary>Прямоугольник доски в координатах документа (мм): левый нижний угол и размер.</summary>
    public readonly struct DrillBoard
    {
        public DrillBoard(Point2D lowerLeft, double widthMm, double heightMm)
        {
            LowerLeft = lowerLeft;
            WidthMm = widthMm;
            HeightMm = heightMm;
        }

        public Point2D LowerLeft { get; }

        public double WidthMm { get; }

        public double HeightMm { get; }

        public bool IsValid => WidthMm > 0 && HeightMm > 0;

        public Point2D UpperRight => LowerLeft + new Point2D(WidthMm, HeightMm);
    }

    /// <summary>Одно сверло: какие размеры камней им сверлятся и в каком порядке обходить отверстия.</summary>
    public sealed class DrillJob
    {
        public DrillJob(int tool, double drillMm, IReadOnlyList<string> sizes, string color, IReadOnlyList<Point2D> points, double travelMm, double seconds,
            IReadOnlyList<double>? millRadii = null, IReadOnlyList<Point2D>? marks = null, double feed = 0, int rpm = 0, string name = "")
        {
            Tool = tool;
            DrillMm = drillMm;
            Sizes = sizes;
            Color = color;
            Points = points;
            TravelMm = travelMm;
            Seconds = seconds;
            MillRadii = millRadii ?? new double[points.Count];
            Marks = marks ?? Array.Empty<Point2D>();
            Feed = feed;
            Rpm = rpm;
            Name = name ?? string.Empty;
        }

        /// <summary>Номер инструмента T (свой номер сверла из каталога или место в списке, с 1).</summary>
        public int Tool { get; }

        public double DrillMm { get; }

        /// <summary>Название сверла из каталога (может быть пустым).</summary>
        public string Name { get; }

        /// <summary>Размеры камней этого сверла — по возрастанию отверстия.</summary>
        public IReadOnlyList<string> Sizes { get; }

        /// <summary>Цвет шаблона (пусто, если шаблоны по цветам не делятся).</summary>
        public string Color { get; }

        /// <summary>Центры отверстий в координатах станка (от X0 Y0), в порядке обхода (метки — среди них).</summary>
        public IReadOnlyList<Point2D> Points { get; }

        /// <summary>
        /// На сколько дофрезеровать каждое отверстие по кругу, мм (радиус круга центра сверла; 0 — только
        /// просверлить). В том же порядке, что <see cref="Points"/>.
        /// </summary>
        public IReadOnlyList<double> MillRadii { get; }

        /// <summary>Метки совмещения среди <see cref="Points"/> (в координатах станка).</summary>
        public IReadOnlyList<Point2D> Marks { get; }

        /// <summary>Подача опускания, мм/мин (своя у сверла или из профиля).</summary>
        public double Feed { get; }

        /// <summary>Обороты шпинделя (свои у сверла или из профиля; 0 — шпиндель руками).</summary>
        public int Rpm { get; }

        /// <summary>Сколько отверстий дофрезеровывается.</summary>
        public int Milled => MillRadii.Count(r => r > 0);

        /// <summary>Холостой путь от нуля станка по всем отверстиям и обратно, мм.</summary>
        public double TravelMm { get; }

        /// <summary>Примерное время работы, секунд.</summary>
        public double Seconds { get; }
    }

    /// <summary>Отверстия размера, у которых сверло заметно отличается от диаметра отверстия.</summary>
    public sealed class DrillMismatch
    {
        public DrillMismatch(string size, double holeMm, double drillMm, int count)
        {
            Size = size;
            HoleMm = holeMm;
            DrillMm = drillMm;
            Count = count;
        }

        public string Size { get; }

        public double HoleMm { get; }

        public double DrillMm { get; }

        public int Count { get; }
    }

    /// <summary>Тонкие перемычки готового шаблона: сколько мест, самая тонкая и где они (координаты станка).</summary>
    public sealed class DrillBridges
    {
        public DrillBridges(int count, double thinnestMm, IReadOnlyList<Point2D> spots)
        {
            Count = count;
            ThinnestMm = thinnestMm;
            Spots = spots;
        }

        public static DrillBridges None { get; } = new DrillBridges(0, 0, Array.Empty<Point2D>());

        public int Count { get; }

        /// <summary>Самая тонкая перемычка, мм (меньше нуля — отверстия налезают друг на друга).</summary>
        public double ThinnestMm { get; }

        /// <summary>Середины тонких перемычек (не больше <see cref="DrillPlanner.MaxBridgeSpots"/>).</summary>
        public IReadOnlyList<Point2D> Spots { get; }
    }

    /// <summary>План сверления: свёрла по порядку, общий ноль, размер рисунка, предупреждения.</summary>
    public sealed class DrillPlan
    {
        internal DrillPlan(IReadOnlyList<DrillJob> jobs, Point2D origin, Point2D min, Point2D max, int duplicates, IReadOnlyList<DrillMismatch> mismatches)
        {
            Jobs = jobs;
            Origin = origin;
            Min = min;
            Max = max;
            Duplicates = duplicates;
            Mismatches = mismatches;
        }

        /// <summary>Доска в координатах станка (только когда ноль — на доске).</summary>
        public DrillBoard? Board { get; internal set; }

        /// <summary>Отверстий (и меток), которые хоть краем выходят за доску (их нельзя сверлить — там прижимы или край).</summary>
        public int OutsideBoard { get; internal set; }

        /// <summary>Насколько рисунок выходит за доску, мм (0 — помещается).</summary>
        public double OverhangMm { get; internal set; }

        public IReadOnlyList<DrillJob> Jobs { get; }

        /// <summary>Где ноль станка в координатах документа — общий для всех свёрл и цветов (шаблоны совпадают).</summary>
        public Point2D Origin { get; }

        /// <summary>Левый нижний край рисунка (по краю отверстий, все копии, без меток) в координатах станка.</summary>
        public Point2D Min { get; }

        /// <summary>Правый верхний край рисунка в координатах станка.</summary>
        public Point2D Max { get; }

        public double WidthMm => Max.X - Min.X;

        public double HeightMm => Max.Y - Min.Y;

        /// <summary>Копий рисунка по X и по Y (1 × 1 — без повтора).</summary>
        public int CopiesX { get; internal set; } = 1;

        public int CopiesY { get; internal set; } = 1;

        /// <summary>Шаг копий по X и по Y, мм (0 — без повтора).</summary>
        public Point2D Pitch { get; internal set; }

        /// <summary>Отверстий, которые одно и то же сверло встретило дважды (стразы друг на друге) — сверлятся один раз.</summary>
        public int Duplicates { get; }

        public IReadOnlyList<DrillMismatch> Mismatches { get; }

        /// <summary>Тонкие перемычки (пусто, если проверка выключена).</summary>
        public DrillBridges Bridges { get; internal set; } = DrillBridges.None;

        /// <summary>Все отверстия, вместе с метками.</summary>
        public int Holes => Jobs.Sum(j => j.Points.Count);

        /// <summary>Отверстий меток совмещения (во всех шаблонах).</summary>
        public int MarkHoles => Jobs.Sum(j => j.Marks.Count);

        /// <summary>Дофрезеровываемых отверстий.</summary>
        public int Milled => Jobs.Sum(j => j.Milled);

        public double Seconds => Jobs.Sum(j => j.Seconds);

        /// <summary>Цвета шаблонов по порядку (одна пустая строка, если по цветам не делили).</summary>
        public IReadOnlyList<string> Colors => Jobs.Select(j => j.Color).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        public bool IsEmpty => Jobs.Count == 0;
    }

    /// <summary>
    /// Раскладывает стразы по свёрлам (docs/SPEC.md, раздел 11.1): сверло размера из каталога → ближайшее
    /// из каталога свёрл → сверло ровно по отверстию, если свёрл нет. Сверло меньше отверстия —
    /// дофрезеровка по кругу. Повторяет рисунок на доске, ставит метки совмещения, считает общий ноль,
    /// убирает повторы, выстраивает короткий путь, ищет тонкие перемычки и оценивает время.
    /// </summary>
    public static class DrillPlanner
    {
        /// <summary>Отверстия ближе этого — одно и то же отверстие.</summary>
        public const double SameHoleMm = 0.05;

        /// <summary>Сверло меньше отверстия хотя бы на столько — отверстие дофрезеровывается.</summary>
        public const double MillFromMm = 0.05;

        /// <summary>Больше стольких мест тонких перемычек не запоминается (для картинки хватает).</summary>
        public const int MaxBridgeSpots = 2000;

        /// <summary>
        /// Какое сверло для отверстия: выбранное у размера в каталоге; иначе ближайшее (при равенстве —
        /// большее), а если ближайшее заметно больше отверстия и можно дофрезеровать — самое большое из
        /// меньших; свёрл нет — по отверстию.
        /// </summary>
        public static double ChooseDrill(string size, double holeMm, DrillSettings settings, DrillRack? rack = null)
        {
            rack ??= DrillRack.Empty;
            double? own = rack.DrillForSize(size);
            if (own.HasValue)
            {
                return own.Value;
            }

            List<double> drills = rack.Tools.Select(t => t.DiameterMm).ToList();
            if (drills.Count == 0)
            {
                return Math.Round(holeMm, 2);
            }

            double best = drills[0];
            foreach (double d in drills)
            {
                double diff = Math.Abs(d - holeMm);
                double bestDiff = Math.Abs(best - holeMm);
                if (diff < bestDiff - 1e-9 || (Math.Abs(diff - bestDiff) <= 1e-9 && d > best))
                {
                    best = d;
                }
            }

            if (settings.MillSmaller && best > holeMm + settings.MaxMismatchMm + 1e-9 && drills.Any(d => d < holeMm - 1e-9))
            {
                best = drills.Where(d => d < holeMm - 1e-9).Max();
            }

            return best;
        }

        /// <summary>На сколько дофрезеровать (радиус круга центра сверла), мм; 0 — не нужно.</summary>
        public static double MillRadius(double holeMm, double drillMm, DrillSettings settings) =>
            settings.MillSmaller && holeMm - drillMm >= MillFromMm - 1e-9 ? Math.Round((holeMm - drillMm) / 2, 4) : 0;

        /// <summary>
        /// Круги дофрезеровки от центра наружу: шаг не больше 0,4 диаметра сверла, последний — ровно
        /// <paramref name="radiusMm"/>. Для 2,4 сверлом 2,2 — один круг радиусом 0,1 мм.
        /// </summary>
        public static IReadOnlyList<double> MillRings(double radiusMm, double drillMm)
        {
            var rings = new List<double>();
            if (!(radiusMm > 0))
            {
                return rings;
            }

            double step = Math.Max(0.05, 0.4 * drillMm);
            int count = Math.Max(1, (int)Math.Ceiling(radiusMm / step - 1e-9));
            for (int k = 1; k <= count; k++)
            {
                rings.Add(k == count ? radiusMm : radiusMm * k / count);
            }

            return rings;
        }

        /// <summary>На сколько отрезков разбить круг: отклонение от круга не больше 0,002 мм, от 8 до 90.</summary>
        public static int MillSegments(double radiusMm)
        {
            if (!(radiusMm > 0))
            {
                return 8;
            }

            double c = 1 - Math.Min(1, 0.002 / radiusMm);
            int n = c <= 0 ? 8 : (int)Math.Ceiling(Math.PI / Math.Acos(c));
            return Math.Max(8, Math.Min(90, n));
        }

        /// <param name="rack">Каталог свёрл и свёрла размеров (null — свёрл нет, сверло по отверстию).</param>
        /// <param name="page">
        /// Страница CorelDRAW в координатах документа — это доска, если ноль на доске. Размер доски из
        /// настроек (если задан) откладывается от левого нижнего угла страницы. Нет ни того, ни другого —
        /// доской считается рамка рисунка.
        /// </param>
        /// <summary>Отверстие с настоящими числами: NaN или бесконечность (битая фигура) пропускаются, а не роняют расчёт.</summary>
        private static bool Usable(DrillHole h) =>
            h.DiameterMm > 0 && !double.IsInfinity(h.DiameterMm) && Finite(h.Center.X) && Finite(h.Center.Y);

        private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        public static DrillPlan Plan(IEnumerable<DrillHole> holes, DrillSettings settings, DrillRack? rack = null, bool optimize = true, DrillBoard? page = null)
        {
            rack ??= DrillRack.Empty;
            List<DrillHole> design = holes.Where(Usable).ToList();
            if (design.Count == 0)
            {
                return new DrillPlan(Array.Empty<DrillJob>(), Point2D.Zero, Point2D.Zero, Point2D.Zero, 0, Array.Empty<DrillMismatch>());
            }

            // Сверло для каждого (размер, диаметр) считается один раз.
            var drillOf = new Dictionary<(string, double), double>();
            double DrillFor(DrillHole h)
            {
                var key = (h.Size.ToLowerInvariant(), Math.Round(h.DiameterMm, 3));
                if (!drillOf.TryGetValue(key, out double d))
                {
                    d = Math.Round(ChooseDrill(h.Size, h.DiameterMm, settings, rack), 3);
                    drillOf[key] = d;
                }

                return d;
            }

            var colorGroups = settings.SplitByColor
                ? design.GroupBy(h => h.Color, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase).Select(g => (g.Key, g.ToList())).ToList()
                : new List<(string Key, List<DrillHole>)> { (string.Empty, design) };

            // Метки сверлятся самым маленьким сверлом своего шаблона.
            var markDrill = colorGroups.ToDictionary(g => g.Key, g => g.Item2.Min(h => DrillFor(h)), StringComparer.OrdinalIgnoreCase);
            Rect frame = Rect.Of(design);
            double markOut = settings.Marks ? Math.Max(0, settings.MarksOffsetMm) + markDrill.Values.Max() / 2 : 0;

            // Повтор: копии вправо и вниз, как строки текста; между копиями (и их метками) — промежуток.
            int nx = Math.Max(1, Math.Min(DrillSettings.MaxRepeat, settings.RepeatX));
            int ny = Math.Max(1, Math.Min(DrillSettings.MaxRepeat, settings.RepeatY));
            double gap = Math.Max(0, settings.RepeatGapMm);
            var pitch = new Point2D(frame.Width + 2 * markOut + gap, frame.Height + 2 * markOut + gap);
            var shifts = new List<Point2D>();
            for (int j = 0; j < ny; j++)
            {
                for (int i = 0; i < nx; i++)
                {
                    shifts.Add(new Point2D(i * pitch.X, -j * pitch.Y));
                }
            }

            Rect all = new Rect(frame.MinX, frame.MinY - (ny - 1) * pitch.Y, frame.MaxX + (nx - 1) * pitch.X, frame.MaxY);
            DrillBoard? board = settings.Origin == DrillOrigin.Board ? BoardOf(settings, page, all) : null;
            Point2D origin = settings.Origin switch
            {
                DrillOrigin.Board => Corner(board!.Value, settings.Home),
                DrillOrigin.DesignCenter => new Point2D((all.MinX + all.MaxX) / 2, (all.MinY + all.MaxY) / 2),
                DrillOrigin.Document => Point2D.Zero,
                _ => new Point2D(all.MinX, all.MinY),
            };

            List<DrillMismatch> mismatches = design
                .GroupBy(h => (Size: h.Size, Hole: Math.Round(h.DiameterMm, 2)))
                .Select(g => new DrillMismatch(g.Key.Size, g.Key.Hole, DrillFor(g.First()), g.Count() * shifts.Count))
                .Where(m => m.DrillMm > m.HoleMm + settings.MaxMismatchMm + 1e-9 || (!settings.MillSmaller && m.DrillMm < m.HoleMm - settings.MaxMismatchMm - 1e-9))
                .OrderBy(m => m.HoleMm)
                .ToList();

            // Номер инструмента — из каталога; свёрла не из каталога идут следом по размеру.
            var tools = new Dictionary<double, int>();
            int nextTool = rack.Tools.Select(t => rack.ToolNumber(t.DiameterMm)).DefaultIfEmpty(0).Max();
            foreach (double d in design.Select(DrillFor).Distinct().OrderBy(d => d))
            {
                int t = rack.ToolNumber(d);
                tools[d] = t > 0 ? t : ++nextTool;
            }

            var jobs = new List<DrillJob>();
            var cuts = new List<(string Color, Point2D At, double Diameter)>();
            int duplicates = 0;
            foreach ((string color, List<DrillHole> group) in colorGroups)
            {
                bool first = true;
                foreach (IGrouping<double, DrillHole> byDrill in group.GroupBy(DrillFor).OrderBy(g => g.Key))
                {
                    double drill = byDrill.Key;
                    List<string> sizes = byDrill
                        .GroupBy(h => h.Size, StringComparer.OrdinalIgnoreCase)
                        .OrderBy(g => g.Average(h => h.DiameterMm))
                        .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(g => g.Key)
                        .ToList();

                    var raw = new List<(Point2D At, double Mill, bool Mark)>();
                    foreach (Point2D shift in shifts)
                    {
                        foreach (DrillHole h in byDrill)
                        {
                            raw.Add((h.Center + shift - origin, MillRadius(h.DiameterMm, drill, settings), false));
                        }
                    }

                    if (first && settings.Marks)
                    {
                        double off = Math.Max(0, settings.MarksOffsetMm);
                        foreach (Point2D shift in shifts)
                        {
                            Point2D lo = new Point2D(frame.MinX - off, frame.MinY - off) + shift - origin;
                            Point2D hi = new Point2D(frame.MaxX + off, frame.MaxY + off) + shift - origin;
                            raw.Add((lo, 0, true));
                            raw.Add((new Point2D(hi.X, lo.Y), 0, true));
                            raw.Add((hi, 0, true));
                            raw.Add((new Point2D(lo.X, hi.Y), 0, true));
                        }
                    }

                    first = false;
                    List<int> keep = Unique(raw.Select(r => r.At).ToList(), out int dup);
                    duplicates += dup;
                    List<Point2D> points = keep.Select(i => raw[i].At).ToList();
                    int[] order = PathOrder.Arrange(points, Point2D.Zero, settings.PathStyle, drill, optimize);
                    Point2D[] ordered = order.Select(i => points[i]).ToArray();
                    double[] radii = order.Select(i => raw[keep[i]].Mill).ToArray();
                    Point2D[] marks = order.Where(i => raw[keep[i]].Mark).Select(i => points[i]).ToArray();
                    double travel = PathOrder.Length(ordered, Enumerable.Range(0, ordered.Length).ToArray(), Point2D.Zero, returnToStart: true);

                    DrillTool? tool = rack.Find(drill);
                    // Подача и обороты сверла — из каталога (мог прийти из чужого файла настроек): вне пределов
                    // каталога (DrillCatalog.Validate) берутся общие.
                    double feed = tool != null && tool.Feed >= 1 && tool.Feed <= 20000 ? tool.Feed : settings.PlungeFeed;
                    int rpm = settings.SpindleRpm > 0 ? (tool != null && tool.Rpm > 0 && tool.Rpm <= 100000 ? tool.Rpm : settings.SpindleRpm) : 0;
                    double millPath = radii.Sum(r => MillRings(r, drill).Sum(ring => 2 * Math.PI * ring + ring));
                    double seconds = Estimate(ordered.Length, travel, settings, feed) + millPath / Math.Max(settings.MillFeed, 1) * 60 + radii.Count(r => r > 0) * 0.3;
                    jobs.Add(new DrillJob(tools[drill], drill, sizes, color, ordered, travel, seconds, radii, marks, feed, rpm, tool?.Name ?? string.Empty));
                    for (int i = 0; i < ordered.Length; i++)
                    {
                        cuts.Add((color, ordered[i], drill + 2 * radii[i]));
                    }
                }
            }

            var plan = new DrillPlan(jobs, origin, new Point2D(all.MinX, all.MinY) - origin, new Point2D(all.MaxX, all.MaxY) - origin, duplicates, mismatches)
            {
                CopiesX = nx,
                CopiesY = ny,
                Pitch = shifts.Count > 1 ? pitch : Point2D.Zero,
            };

            if (board.HasValue)
            {
                DrillBoard b = board.Value;
                plan.Board = new DrillBoard(b.LowerLeft - origin, b.WidthMm, b.HeightMm);
                const double eps = 0.01;
                Point2D lo = b.LowerLeft - origin, hi = b.UpperRight - origin;
                plan.OutsideBoard = cuts.Count(c =>
                    c.At.X - c.Diameter / 2 < lo.X - eps || c.At.Y - c.Diameter / 2 < lo.Y - eps ||
                    c.At.X + c.Diameter / 2 > hi.X + eps || c.At.Y + c.Diameter / 2 > hi.Y + eps);
                plan.OverhangMm = cuts.Count == 0 ? 0 : Math.Max(0, new[]
                {
                    lo.X - cuts.Min(c => c.At.X - c.Diameter / 2), lo.Y - cuts.Min(c => c.At.Y - c.Diameter / 2),
                    cuts.Max(c => c.At.X + c.Diameter / 2) - hi.X, cuts.Max(c => c.At.Y + c.Diameter / 2) - hi.Y,
                }.Max());
            }

            plan.Bridges = settings.MinBridgeMm > 0 ? FindBridges(cuts, settings.MinBridgeMm) : DrillBridges.None;
            return plan;
        }

        /// <summary>
        /// Сколько копий рисунка (с метками) помещается на доске вправо и вниз от того места, где рисунок
        /// стоит на странице. Хотя бы 1 × 1.
        /// </summary>
        public static (int X, int Y) FitCopies(IEnumerable<DrillHole> holes, DrillSettings settings, DrillRack? rack, DrillBoard? page)
        {
            List<DrillHole> design = holes.Where(Usable).ToList();
            if (design.Count == 0)
            {
                return (1, 1);
            }

            Rect frame = Rect.Of(design);
            DrillBoard board = BoardOf(settings, page, frame);
            if (!(page.HasValue && page.Value.IsValid))
            {
                // Без страницы доска начинается у рисунка — сверху, раз копии идут вниз.
                board = new DrillBoard(new Point2D(frame.MinX, frame.MaxY - board.HeightMm), board.WidthMm, board.HeightMm);
            }

            double markOut = 0;
            if (settings.Marks)
            {
                IEnumerable<IGrouping<string, DrillHole>> groups = settings.SplitByColor
                    ? design.GroupBy(h => h.Color, StringComparer.OrdinalIgnoreCase)
                    : design.GroupBy(h => string.Empty);
                markOut = Math.Max(0, settings.MarksOffsetMm) + groups.Max(g => g.Min(h => ChooseDrill(h.Size, h.DiameterMm, settings, rack))) / 2;
            }

            double gap = Math.Max(0, settings.RepeatGapMm);
            double w = frame.Width + 2 * markOut, h = frame.Height + 2 * markOut;
            double left = frame.MinX - markOut, top = frame.MaxY + markOut;
            double room = board.UpperRight.X - left, down = top - board.LowerLeft.Y;
            int nx = room + 1e-6 < w ? 1 : 1 + (int)Math.Floor((room - w + 1e-6) / (w + gap));
            int ny = down + 1e-6 < h ? 1 : 1 + (int)Math.Floor((down - h + 1e-6) / (h + gap));
            return (Math.Max(1, Math.Min(DrillSettings.MaxRepeat, nx)), Math.Max(1, Math.Min(DrillSettings.MaxRepeat, ny)));
        }

        /// <summary>Тонкие перемычки по настоящим отверстиям (по сверлу или по кругу фрезеровки), в каждом шаблоне отдельно.</summary>
        private static DrillBridges FindBridges(List<(string Color, Point2D At, double Diameter)> cuts, double minBridgeMm)
        {
            int count = 0;
            double thinnest = double.MaxValue;
            var spots = new List<Point2D>();
            foreach (IGrouping<string, (string Color, Point2D At, double Diameter)> template in cuts.GroupBy(c => c.Color, StringComparer.OrdinalIgnoreCase))
            {
                List<(string Color, Point2D At, double Diameter)> list = template.ToList();
                var stones = list.Select((c, i) => new Editing.DocStone(c.At, c.Diameter, i)).ToList();
                foreach (Editing.ThinBridge b in Editing.BridgeChecker.Find(stones, minBridgeMm))
                {
                    count++;
                    thinnest = Math.Min(thinnest, b.GapMm);
                    if (spots.Count < MaxBridgeSpots)
                    {
                        spots.Add(b.Middle);
                    }
                }
            }

            return count == 0 ? DrillBridges.None : new DrillBridges(count, thinnest, spots);
        }

        /// <summary>Доска: размер из настроек, иначе страница; угол — левый нижний угол страницы (или рисунка).</summary>
        private static DrillBoard BoardOf(DrillSettings s, DrillBoard? page, Rect design)
        {
            bool hasPage = page.HasValue && page.Value.IsValid;
            Point2D lowerLeft = hasPage ? page!.Value.LowerLeft : new Point2D(design.MinX, design.MinY);
            double w = s.BoardWidthMm > 0 ? s.BoardWidthMm : hasPage ? page!.Value.WidthMm : design.Width;
            double h = s.BoardHeightMm > 0 ? s.BoardHeightMm : hasPage ? page!.Value.HeightMm : design.Height;
            return new DrillBoard(lowerLeft, Math.Max(w, 1e-6), Math.Max(h, 1e-6));
        }

        /// <summary>Ноль станка на доске, в координатах документа.</summary>
        public static Point2D Corner(DrillBoard board, BoardCorner home)
        {
            Point2D lo = board.LowerLeft, hi = board.UpperRight;
            return home switch
            {
                BoardCorner.UpperLeft => new Point2D(lo.X, hi.Y),
                BoardCorner.LowerRight => new Point2D(hi.X, lo.Y),
                BoardCorner.UpperRight => hi,
                BoardCorner.Center => (lo + hi) / 2,
                _ => lo,
            };
        }

        /// <summary>
        /// Примерное время: холостые переходы на скорости холостого хода, опускание на подаче, подъём —
        /// холостым ходом, плюс по 0,15 с на разгон каждого перехода. Настоящее время зависит от станка.
        /// </summary>
        public static double Estimate(int holes, double travelMm, DrillSettings s, double plungeFeed = 0)
        {
            double rapid = Math.Max(s.RapidFeed, 1) / 60.0;
            double plunge = Math.Max(plungeFeed > 0 ? plungeFeed : s.PlungeFeed, 1) / 60.0;
            int passes = s.PeckMm > 0 ? (int)Math.Ceiling(s.DepthMm / s.PeckMm - 1e-9) : 1;
            double down = s.RetractMm + s.DepthMm + Math.Max(0, passes - 1) * 0.3;
            double up = passes * (s.RetractMm + s.DepthMm);
            double perHole = down / plunge + up / rapid + 0.15 * (1 + passes);
            return travelMm / rapid + holes * perHole + 2 * s.ClearanceMm / rapid;
        }

        /// <summary>Какие точки оставить: первые из тех, что ближе <see cref="SameHoleMm"/> друг к другу.</summary>
        private static List<int> Unique(IReadOnlyList<Point2D> points, out int duplicates)
        {
            var seen = new Dictionary<(long, long), List<Point2D>>();
            var result = new List<int>();
            duplicates = 0;
            for (int i = 0; i < points.Count; i++)
            {
                Point2D p = points[i];
                long cx = (long)Math.Floor(p.X / SameHoleMm), cy = (long)Math.Floor(p.Y / SameHoleMm);
                bool dup = false;
                for (long dx = -1; dx <= 1 && !dup; dx++)
                {
                    for (long dy = -1; dy <= 1 && !dup; dy++)
                    {
                        if (seen.TryGetValue((cx + dx, cy + dy), out List<Point2D>? near))
                        {
                            dup = near.Any(q => Point2D.Distance(p, q) < SameHoleMm);
                        }
                    }
                }

                if (dup)
                {
                    duplicates++;
                    continue;
                }

                if (!seen.TryGetValue((cx, cy), out List<Point2D>? cell))
                {
                    cell = new List<Point2D>();
                    seen[(cx, cy)] = cell;
                }

                cell.Add(p);
                result.Add(i);
            }

            return result;
        }

        /// <summary>Рамка по краю отверстий.</summary>
        private readonly struct Rect
        {
            public Rect(double minX, double minY, double maxX, double maxY)
            {
                MinX = minX;
                MinY = minY;
                MaxX = maxX;
                MaxY = maxY;
            }

            public double MinX { get; }

            public double MinY { get; }

            public double MaxX { get; }

            public double MaxY { get; }

            public double Width => MaxX - MinX;

            public double Height => MaxY - MinY;

            public static Rect Of(IReadOnlyList<DrillHole> holes) => new Rect(
                holes.Min(h => h.Center.X - h.DiameterMm / 2),
                holes.Min(h => h.Center.Y - h.DiameterMm / 2),
                holes.Max(h => h.Center.X + h.DiameterMm / 2),
                holes.Max(h => h.Center.Y + h.DiameterMm / 2));
        }
    }
}
