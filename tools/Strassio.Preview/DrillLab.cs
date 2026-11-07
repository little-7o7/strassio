using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Strassio.Core.Cnc;
using Strassio.Core.Geometry;
using Strassio.Core.Methods;
using Strassio.Core.Motifs;
using Strassio.Core.Placement;

namespace Strassio.Preview
{
    /// <summary>
    /// Сверление шаблона на станке (docs/SPEC.md, раздел 11.1): G-code по свёрлам и путь сверла.
    /// Путь на картинке рисуется по самому файлу .tap (через самопроверку), а не по плану — так видно
    /// ровно то, что сделает станок. Картинки и файлы — в out/preview/drill/.
    /// </summary>
    internal static class DrillLab
    {
        private static readonly string[] Palette = { "#1E88E5", "#E53935", "#43A047", "#FB8C00", "#8E24AA", "#00897B" };

        public static void Render(string repoRoot)
        {
            string dir = Path.Combine(repoRoot, "out", "preview", "drill");
            Directory.CreateDirectory(dir);

            // 1. Сердце: контур ss10, заливка ss6, ожерелье ss16 вокруг — три сверла.
            var heart = new List<DrillHole>();
            Curve outline = Heart(1.6);
            heart.AddRange(Stones(MethodKind.L1, outline, 2.9, new MethodParameters { GapMm = 0.4 }).Select(s => new DrillHole(s.Center, s.DiameterMm, "ss10", "Кристалл")));
            heart.AddRange(Stones(MethodKind.F2, Heart(1.38), 2.4, new MethodParameters { GapMm = 0.35 }).Select(s => new DrillHole(s.Center, s.DiameterMm, "ss6", "Розовый")));
            heart.AddRange(Stones(MethodKind.L1, Circle(0, 2, 40), 4.0, new MethodParameters { GapMm = 1.2 }).Select(s => new DrillHole(s.Center, s.DiameterMm, "ss16", "Кристалл")));
            var settings = new DrillSettings { DrillsMm = new List<double> { 2.4, 3.0, 4.0 } };
            Scenario(dir, "heart", heart, settings);

            // 1б. Ноль в левом верхнем углу доски 160 × 120 мм (страница), рисунок сдвинут вправо-вниз.
            var page = new DrillBoard(new Point2D(-60, -70), 160, 120);
            Scenario(dir, "heart-board", heart, new DrillSettings { DrillsMm = new List<double> { 2.4, 3.0, 4.0 }, Origin = DrillOrigin.Board, Home = BoardCorner.UpperLeft }, page);

            // 2. Тот же дизайн, но шаблон на каждый цвет и G81 — для проверки вариантов.
            Scenario(dir, "heart-colors", heart, new DrillSettings { DrillsMm = new List<double> { 2.4, 3.0, 4.0 }, SplitByColor = true, CannedCycle = true });

            // 3. Лист трафарета без шва 300 × 400 мм из плитки подсолнухов — тысячи отверстий.
            List<(PlacedStone Stone, string Rgb)> tile = FabricLab.SunflowerTile();
            var options = new RepeatOptions { StepXMm = 46, StepYMm = 52, Shift = RepeatShift.HalfDrop, GapMm = 0.2 };
            RepeatSheet sheet = Repeat.SeamlessSheet(options, 300, 400);
            List<PlacedStone> tileStones = tile.Select(t => t.Stone).ToList();
            List<SheetStone> placed = Repeat.FillSheet(tileStones, options, sheet, new Point2D(tileStones.Min(s => s.Center.X), tileStones.Max(s => s.Center.Y)), out _);
            List<DrillHole> sheetHoles = placed.Select(p =>
            {
                double d = tile[p.Source].Stone.DiameterMm;
                return new DrillHole(p.Center, d, d < 1.5 ? "ss3" : "ss4", "Золото");
            }).ToList();
            Scenario(dir, "sheet", sheetHoles, new DrillSettings { DrillsMm = new List<double> { 1.4, 1.6 }, DepthMm = 1.2 });

            Console.WriteLine($"SVG и .tap сохранены: {dir}");
        }

        private static void Scenario(string dir, string name, List<DrillHole> holes, DrillSettings settings, DrillBoard? page = null)
        {
            var clock = Stopwatch.StartNew();
            DrillPlan plan = DrillPlanner.Plan(holes, settings, page: page);
            long ms = clock.ElapsedMilliseconds;
            DrillPlan plain = DrillPlanner.Plan(holes, settings, optimize: false, page: page);
            IReadOnlyList<GCodeFile> files = GCodeWriter.Files(plan, settings, name);

            var runs = new List<(GCodeFile File, GCodeRun Run)>();
            foreach (GCodeFile file in files)
            {
                File.WriteAllText(Path.Combine(dir, file.Name), file.Text);
                runs.Add((file, GCodeCheck.Run(file.Text)));
            }

            double travel = plan.Jobs.Sum(j => j.TravelMm), travelPlain = plain.Jobs.Sum(j => j.TravelMm);
            Console.WriteLine($"{name,-13} {plan.Holes,6} отв., {plan.Jobs.Count} свёрл, {files.Count} файлов, план {ms} мс; " +
                $"путь {travel / 1000:0.00} м (без улучшения {travelPlain / 1000:0.00} м, −{100 * (1 - travel / Math.Max(travelPlain, 1e-9)):0}%), " +
                $"≈ {plan.Seconds / 60:0} мин, повторов {plan.Duplicates}, за доской {plan.OutsideBoard}");
            foreach ((GCodeFile file, GCodeRun run) in runs)
            {
                string problems = run.Problems.Count == 0 ? "проверка: ок" : "ПРОБЛЕМЫ: " + string.Join("; ", run.Problems.Take(3));
                Console.WriteLine($"    {file.Name,-34} {run.Holes.Count,6} отв., Z до {run.MinZ:0.###}, {file.Text.Length / 1024.0:0} КБ — {problems}");
            }

            File.WriteAllText(Path.Combine(dir, name + ".svg"), Svg(plan, runs, settings));
        }

        /// <summary>Отверстия цветом сверла, путь сверла тонкой линией по файлу, ноль станка крестиком, подписи.</summary>
        private static string Svg(DrillPlan plan, List<(GCodeFile File, GCodeRun Run)> runs, DrillSettings settings)
        {
            Point2D lo = plan.Board?.LowerLeft ?? plan.Min, hi = plan.Board?.UpperRight ?? plan.Max;
            double minX = Math.Min(Math.Min(plan.Min.X, lo.X), 0) - 6, minY = Math.Min(Math.Min(plan.Min.Y, lo.Y), 0) - 6;
            double maxX = Math.Max(Math.Max(plan.Max.X, hi.X), 0) + 6, maxY = Math.Max(Math.Max(plan.Max.Y, hi.Y), 0) + 6;
            double k = Math.Min(8, 1600 / Math.Max(maxX - minX, maxY - minY));
            int legend = 26 * plan.Jobs.Count + 40;
            CultureInfo ci = CultureInfo.InvariantCulture;
            string X(double x) => ((x - minX) * k).ToString("0.#", ci);
            string Y(double y) => ((maxY - y) * k + legend).ToString("0.#", ci);
            double width = (maxX - minX) * k, height = (maxY - minY) * k + legend;

            var sb = new StringBuilder();
            sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width.ToString("0", ci)}\" height=\"{height.ToString("0", ci)}\" font-family=\"Arial, sans-serif\">");
            sb.Append("<rect width=\"100%\" height=\"100%\" fill=\"#ffffff\"/>");
            if (plan.Board.HasValue)
            {
                sb.Append($"<rect x=\"{X(lo.X)}\" y=\"{Y(hi.Y)}\" width=\"{((hi.X - lo.X) * k).ToString("0.#", ci)}\" height=\"{((hi.Y - lo.Y) * k).ToString("0.#", ci)}\" fill=\"#f3efe6\" stroke=\"#8d6e63\" stroke-width=\"2\"/>");
            }

            sb.Append($"<rect x=\"{X(plan.Min.X)}\" y=\"{Y(plan.Max.Y)}\" width=\"{(plan.WidthMm * k).ToString("0.#", ci)}\" height=\"{(plan.HeightMm * k).ToString("0.#", ci)}\" fill=\"#fafafa\" stroke=\"#bdbdbd\" stroke-dasharray=\"6 4\"/>");

            // Каждый файл — свой цвет у каждого сверла; путь от нуля по отверстиям в порядке файла.
            int jobIndex = 0;
            var jobColor = new Dictionary<DrillJob, string>();
            foreach ((GCodeFile file, GCodeRun run) in runs)
            {
                int offset = 0;
                foreach (DrillJob job in file.Jobs)
                {
                    string color = Palette[jobIndex++ % Palette.Length];
                    jobColor[job] = color;
                    var path = new StringBuilder($"M{X(0)},{Y(0)}");
                    for (int i = 0; i < job.Points.Count; i++)
                    {
                        Point2D at = run.Holes[offset + i].At;
                        path.Append($" L{X(at.X)},{Y(at.Y)}");
                    }

                    sb.Append($"<path d=\"{path}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"{Math.Max(0.6, k * 0.12).ToString("0.##", ci)}\" stroke-opacity=\"0.75\"/>");
                    for (int i = 0; i < job.Points.Count; i++)
                    {
                        Point2D at = run.Holes[offset + i].At;
                        sb.Append($"<circle cx=\"{X(at.X)}\" cy=\"{Y(at.Y)}\" r=\"{(job.DrillMm / 2 * k).ToString("0.##", ci)}\" fill=\"{color}\" fill-opacity=\"0.35\" stroke=\"{color}\" stroke-width=\"0.6\"/>");
                    }

                    offset += job.Points.Count;
                }
            }

            // Ноль станка.
            sb.Append($"<line x1=\"{X(-4)}\" y1=\"{Y(0)}\" x2=\"{X(4)}\" y2=\"{Y(0)}\" stroke=\"#212121\" stroke-width=\"1.5\"/>");
            sb.Append($"<line x1=\"{X(0)}\" y1=\"{Y(-4)}\" x2=\"{X(0)}\" y2=\"{Y(4)}\" stroke=\"#212121\" stroke-width=\"1.5\"/>");
            sb.Append($"<text x=\"{X(1)}\" y=\"{Y(-5)}\" font-size=\"13\" fill=\"#212121\">X0 Y0</text>");

            // Подписи сверху.
            sb.Append($"<text x=\"12\" y=\"22\" font-size=\"15\" font-weight=\"bold\" fill=\"#212121\">{plan.Holes} отверстий, {plan.WidthMm:0} × {plan.HeightMm:0} мм, глубина {settings.DepthMm:0.##} мм, ≈ {plan.Seconds / 60:0} мин</text>");
            int row = 0;
            foreach (DrillJob job in plan.Jobs)
            {
                string color = jobColor.TryGetValue(job, out string? c) ? c : "#000";
                double y = 44 + 26 * row++;
                string sizes = string.Join(", ", job.Sizes);
                string tpl = job.Color.Length > 0 ? $" · шаблон «{job.Color}»" : string.Empty;
                sb.Append($"<circle cx=\"20\" cy=\"{(y - 5).ToString("0", ci)}\" r=\"7\" fill=\"{color}\"/>");
                sb.Append($"<text x=\"34\" y=\"{y.ToString("0", ci)}\" font-size=\"14\" fill=\"#212121\">T{job.Tool} · сверло {job.DrillMm:0.0#} мм · {sizes}{tpl} · {job.Points.Count} отв. · путь {job.TravelMm / 1000:0.0#} м · ≈ {job.Seconds / 60:0} мин</text>");
            }

            sb.Append("</svg>");
            return sb.ToString();
        }

        private static List<PlacedStone> Stones(MethodKind kind, Curve shape, double d, MethodParameters p) =>
            MethodRunner.Run(kind, new[] { shape }, d, p).Stones.ToList();

        private static Curve Heart(double scale)
        {
            var pts = new List<Point2D>();
            for (int i = 0; i < 200; i++)
            {
                double t = 2 * Math.PI * i / 200;
                double x = 16 * Math.Pow(Math.Sin(t), 3);
                double y = 13 * Math.Cos(t) - 5 * Math.Cos(2 * t) - 2 * Math.Cos(3 * t) - Math.Cos(4 * t);
                pts.Add(new Point2D(x * scale, y * scale));
            }

            return Curve.FromPolyline(pts, isClosed: true);
        }

        private static Curve Circle(double cx, double cy, double r) =>
            Curve.FromPolyline(Enumerable.Range(0, 160).Select(i => new Point2D(cx + r * Math.Cos(2 * Math.PI * i / 160), cy + r * Math.Sin(2 * Math.PI * i / 160))).ToList(), isClosed: true);
    }
}
