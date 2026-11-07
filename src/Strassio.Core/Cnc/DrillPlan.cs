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
        public DrillJob(int tool, double drillMm, IReadOnlyList<string> sizes, string color, IReadOnlyList<Point2D> points, double travelMm, double seconds)
        {
            Tool = tool;
            DrillMm = drillMm;
            Sizes = sizes;
            Color = color;
            Points = points;
            TravelMm = travelMm;
            Seconds = seconds;
        }

        /// <summary>Номер инструмента T (место сверла в списке пользователя, с 1).</summary>
        public int Tool { get; }

        public double DrillMm { get; }

        /// <summary>Размеры камней этого сверла — по возрастанию отверстия.</summary>
        public IReadOnlyList<string> Sizes { get; }

        /// <summary>Цвет шаблона (пусто, если шаблоны по цветам не делятся).</summary>
        public string Color { get; }

        /// <summary>Центры отверстий в координатах станка (от X0 Y0), в порядке обхода.</summary>
        public IReadOnlyList<Point2D> Points { get; }

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

        /// <summary>Отверстий, которые хоть краем выходят за доску (их нельзя сверлить — там прижимы или край).</summary>
        public int OutsideBoard { get; internal set; }

        /// <summary>Насколько рисунок выходит за доску, мм (0 — помещается).</summary>
        public double OverhangMm { get; internal set; }

        public IReadOnlyList<DrillJob> Jobs { get; }

        /// <summary>Где ноль станка в координатах документа — общий для всех свёрл и цветов (шаблоны совпадают).</summary>
        public Point2D Origin { get; }

        /// <summary>Левый нижний край рисунка (по краю отверстий) в координатах станка.</summary>
        public Point2D Min { get; }

        /// <summary>Правый верхний край рисунка в координатах станка.</summary>
        public Point2D Max { get; }

        public double WidthMm => Max.X - Min.X;

        public double HeightMm => Max.Y - Min.Y;

        /// <summary>Отверстий, которые одно и то же сверло встретило дважды (стразы друг на друге) — сверлятся один раз.</summary>
        public int Duplicates { get; }

        public IReadOnlyList<DrillMismatch> Mismatches { get; }

        public int Holes => Jobs.Sum(j => j.Points.Count);

        public double Seconds => Jobs.Sum(j => j.Seconds);

        /// <summary>Цвета шаблонов по порядку (одна пустая строка, если по цветам не делили).</summary>
        public IReadOnlyList<string> Colors => Jobs.Select(j => j.Color).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        public bool IsEmpty => Jobs.Count == 0;
    }

    /// <summary>
    /// Раскладывает стразы по свёрлам (docs/SPEC.md, раздел 11.1): своё сверло для размера → ближайшее
    /// из списка пользователя → сверло ровно по отверстию, если список пуст. Считает общий ноль, убирает
    /// повторы, выстраивает короткий путь и оценивает время.
    /// </summary>
    public static class DrillPlanner
    {
        /// <summary>Отверстия ближе этого — одно и то же отверстие.</summary>
        public const double SameHoleMm = 0.05;

        /// <summary>Какое сверло для отверстия: своё правило, иначе ближайшее (при равенстве — большее), иначе по отверстию.</summary>
        public static double ChooseDrill(string size, double holeMm, DrillSettings settings)
        {
            double? rule = settings.RuleFor(size);
            if (rule.HasValue)
            {
                return rule.Value;
            }

            List<double> drills = (settings.DrillsMm ?? new List<double>()).Where(d => d > 0).ToList();
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

            return best;
        }

        /// <param name="page">
        /// Страница CorelDRAW в координатах документа — это доска, если ноль на доске. Размер доски из
        /// настроек (если задан) откладывается от левого нижнего угла страницы. Нет ни того, ни другого —
        /// доской считается рамка рисунка.
        /// </param>
        public static DrillPlan Plan(IEnumerable<DrillHole> holes, DrillSettings settings, bool optimize = true, DrillBoard? page = null)
        {
            List<DrillHole> all = holes.Where(h => h.DiameterMm > 0 && !double.IsNaN(h.Center.X) && !double.IsNaN(h.Center.Y)).ToList();
            if (all.Count == 0)
            {
                return new DrillPlan(Array.Empty<DrillJob>(), Point2D.Zero, Point2D.Zero, Point2D.Zero, 0, Array.Empty<DrillMismatch>());
            }

            double minX = all.Min(h => h.Center.X - h.DiameterMm / 2);
            double minY = all.Min(h => h.Center.Y - h.DiameterMm / 2);
            double maxX = all.Max(h => h.Center.X + h.DiameterMm / 2);
            double maxY = all.Max(h => h.Center.Y + h.DiameterMm / 2);
            DrillBoard? board = settings.Origin == DrillOrigin.Board ? BoardOf(settings, page, minX, minY, maxX, maxY) : null;
            Point2D origin = settings.Origin switch
            {
                DrillOrigin.Board => Corner(board!.Value, settings.Home),
                DrillOrigin.DesignCenter => new Point2D((minX + maxX) / 2, (minY + maxY) / 2),
                DrillOrigin.Document => Point2D.Zero,
                _ => new Point2D(minX, minY),
            };

            // Сверло для каждого (размер, диаметр) считается один раз.
            var drillOf = new Dictionary<(string, double), double>();
            double DrillFor(DrillHole h)
            {
                var key = (h.Size.ToLowerInvariant(), Math.Round(h.DiameterMm, 3));
                if (!drillOf.TryGetValue(key, out double d))
                {
                    d = ChooseDrill(h.Size, h.DiameterMm, settings);
                    drillOf[key] = d;
                }

                return d;
            }

            List<DrillMismatch> mismatches = all
                .GroupBy(h => (Size: h.Size, Hole: Math.Round(h.DiameterMm, 2)))
                .Select(g => new DrillMismatch(g.Key.Size, g.Key.Hole, DrillFor(g.First()), g.Count()))
                .Where(m => Math.Abs(m.DrillMm - m.HoleMm) > settings.MaxMismatchMm + 1e-9)
                .OrderBy(m => m.HoleMm)
                .ToList();

            // Номер инструмента — место в списке пользователя; свёрла не из списка идут следом по размеру.
            List<double> rack = (settings.DrillsMm ?? new List<double>()).Where(d => d > 0).ToList();
            var tools = new Dictionary<double, int>();
            for (int i = 0; i < rack.Count; i++)
            {
                double key = Math.Round(rack[i], 3);
                if (!tools.ContainsKey(key))
                {
                    tools[key] = i + 1;
                }
            }

            foreach (double d in all.Select(h => Math.Round(DrillFor(h), 3)).Distinct().OrderBy(d => d))
            {
                if (!tools.ContainsKey(d))
                {
                    tools[d] = Math.Max(rack.Count, tools.Count == 0 ? 0 : tools.Values.Max()) + 1;
                }
            }

            var colorGroups = settings.SplitByColor
                ? all.GroupBy(h => h.Color, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase).ToList()
                : all.GroupBy(h => string.Empty).ToList();

            var jobs = new List<DrillJob>();
            int duplicates = 0;
            foreach (IGrouping<string, DrillHole> colorGroup in colorGroups)
            {
                foreach (IGrouping<double, DrillHole> byDrill in colorGroup.GroupBy(h => Math.Round(DrillFor(h), 3)).OrderBy(g => g.Key))
                {
                    List<string> sizes = byDrill
                        .GroupBy(h => h.Size, StringComparer.OrdinalIgnoreCase)
                        .OrderBy(g => g.Average(h => h.DiameterMm))
                        .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(g => g.Key)
                        .ToList();

                    List<Point2D> points = Unique(byDrill.Select(h => h.Center - origin), out int dup);
                    duplicates += dup;
                    int[] order = PathOrder.Order(points, Point2D.Zero, optimize);
                    Point2D[] ordered = order.Select(i => points[i]).ToArray();
                    double travel = PathOrder.Length(ordered, Enumerable.Range(0, ordered.Length).ToArray(), Point2D.Zero, returnToStart: true);
                    jobs.Add(new DrillJob(tools[byDrill.Key], byDrill.Key, sizes, colorGroup.Key, ordered, travel, Estimate(ordered.Length, travel, settings)));
                }
            }

            var plan = new DrillPlan(jobs, origin, new Point2D(minX, minY) - origin, new Point2D(maxX, maxY) - origin, duplicates, mismatches);
            if (board.HasValue)
            {
                DrillBoard b = board.Value;
                plan.Board = new DrillBoard(b.LowerLeft - origin, b.WidthMm, b.HeightMm);
                const double eps = 0.01;
                Point2D lo = b.LowerLeft, hi = b.UpperRight;
                plan.OutsideBoard = all.Count(h =>
                    h.Center.X - h.DiameterMm / 2 < lo.X - eps || h.Center.Y - h.DiameterMm / 2 < lo.Y - eps ||
                    h.Center.X + h.DiameterMm / 2 > hi.X + eps || h.Center.Y + h.DiameterMm / 2 > hi.Y + eps);
                plan.OverhangMm = Math.Max(0, new[] { lo.X - minX, lo.Y - minY, maxX - hi.X, maxY - hi.Y }.Max());
            }

            return plan;
        }

        /// <summary>Доска: размер из настроек, иначе страница; угол — левый нижний угол страницы (или рисунка).</summary>
        private static DrillBoard BoardOf(DrillSettings s, DrillBoard? page, double minX, double minY, double maxX, double maxY)
        {
            Point2D lowerLeft = page.HasValue && page.Value.IsValid ? page.Value.LowerLeft : new Point2D(minX, minY);
            double w = s.BoardWidthMm > 0 ? s.BoardWidthMm : page.HasValue && page.Value.IsValid ? page.Value.WidthMm : maxX - minX;
            double h = s.BoardHeightMm > 0 ? s.BoardHeightMm : page.HasValue && page.Value.IsValid ? page.Value.HeightMm : maxY - minY;
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
        public static double Estimate(int holes, double travelMm, DrillSettings s)
        {
            double rapid = Math.Max(s.RapidFeed, 1) / 60.0;
            double plunge = Math.Max(s.PlungeFeed, 1) / 60.0;
            int passes = s.PeckMm > 0 ? (int)Math.Ceiling(s.DepthMm / s.PeckMm - 1e-9) : 1;
            double down = s.RetractMm + s.DepthMm + Math.Max(0, passes - 1) * 0.3;
            double up = passes * (s.RetractMm + s.DepthMm);
            double perHole = down / plunge + up / rapid + 0.15 * (1 + passes);
            return travelMm / rapid + holes * perHole + 2 * s.ClearanceMm / rapid;
        }

        private static List<Point2D> Unique(IEnumerable<Point2D> points, out int duplicates)
        {
            var seen = new Dictionary<(long, long), List<Point2D>>();
            var result = new List<Point2D>();
            duplicates = 0;
            foreach (Point2D p in points)
            {
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
                result.Add(p);
            }

            return result;
        }
    }
}
