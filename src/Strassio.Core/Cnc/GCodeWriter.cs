using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Strassio.Core.Geometry;

namespace Strassio.Core.Cnc
{
    /// <summary>Готовый файл программы: имя (без папки) и текст.</summary>
    public sealed class GCodeFile
    {
        public GCodeFile(string name, string text, IReadOnlyList<DrillJob> jobs)
        {
            Name = name;
            Text = text;
            Jobs = jobs;
        }

        public string Name { get; }

        public string Text { get; }

        public IReadOnlyList<DrillJob> Jobs { get; }

        public int Holes => Jobs.Sum(j => j.Points.Count);

        /// <summary>Самый большой круг дофрезеровки в файле, мм — сколько сверлу можно ходить вбок внутри отверстия.</summary>
        public double MillLimitMm => Jobs.SelectMany(j => j.MillRadii).DefaultIfEmpty(0).Max();
    }

    /// <summary>
    /// Пишет план сверления в G-code (docs/SPEC.md, раздел 11.1). По умолчанию — самые простые
    /// команды, которые понимает любой станок (Mach3, NcStudio, DSP-пульты, GRBL, LinuxCNC):
    /// G0/G1, миллиметры (G21), абсолютные координаты (G90), точка в числах, концы строк Windows.
    /// Пояснения — латиницей: многие пульты не показывают кириллицу.
    /// </summary>
    public static class GCodeWriter
    {
        private const string NewLine = "\r\n";

        /// <summary>Файлы для сохранения: по файлу на сверло или по файлу на цвет (со сменой сверла).</summary>
        public static IReadOnlyList<GCodeFile> Files(DrillPlan plan, DrillSettings settings, string baseName)
        {
            string safeBase = Ascii.FileName(baseName);
            if (safeBase.Length == 0)
            {
                safeBase = "strassio";
            }

            string ext = Extension(settings.Extension);
            bool byColor = plan.Colors.Count > 1 || (plan.Colors.Count == 1 && plan.Colors[0].Length > 0);
            var files = new List<GCodeFile>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string Unique(string name)
            {
                string candidate = name;
                for (int k = 2; !used.Add(candidate + ext); k++)
                {
                    candidate = name + "_" + k.ToString(CultureInfo.InvariantCulture);
                }

                return candidate + ext;
            }

            if (settings.FileMode == DrillFileMode.OneFileToolChange)
            {
                foreach (IGrouping<string, DrillJob> color in plan.Jobs.GroupBy(j => j.Color, StringComparer.OrdinalIgnoreCase))
                {
                    List<DrillJob> jobs = color.ToList();
                    string name = byColor ? safeBase + "_" + ColorPart(color.Key) : safeBase;
                    files.Add(new GCodeFile(Unique(name), Program(plan, jobs, settings), jobs));
                }

                return files;
            }

            // Файл на сверло: номер по порядку, диаметр и размеры камней в имени — «роза_1_D2.4_ss6.tap».
            int index = 0;
            foreach (DrillJob job in plan.Jobs)
            {
                index++;
                var name = new StringBuilder(safeBase);
                if (byColor)
                {
                    name.Append('_').Append(ColorPart(job.Color));
                }

                name.Append('_').Append(index.ToString(CultureInfo.InvariantCulture))
                    .Append("_D").Append(Num(job.DrillMm, "0.###"));
                string sizes = Ascii.FileName(string.Join("-", job.Sizes.Where(s => s.Length > 0).Take(4)));
                if (sizes.Length > 0)
                {
                    name.Append('_').Append(sizes);
                }

                files.Add(new GCodeFile(Unique(name.ToString()), Program(plan, new[] { job }, settings), new[] { job }));
            }

            return files;
        }

        /// <summary>Одна программа для нескольких свёрл подряд (между ними — смена сверла).</summary>
        public static string Program(DrillPlan plan, IReadOnlyList<DrillJob> jobs, DrillSettings s)
        {
            var w = new Writer(s.LineNumbers);
            if (s.Percent)
            {
                w.Raw("%");
            }

            if (s.Comments)
            {
                int holes = jobs.Sum(j => j.Points.Count);
                w.Comment("Strassio: " + holes.ToString(CultureInfo.InvariantCulture) + " holes, "
                    + jobs.Count.ToString(CultureInfo.InvariantCulture) + (jobs.Count == 1 ? " drill" : " drills"));
                w.Comment("X0 Y0: " + OriginText(s, plan) + ". Z0: top of material");
                w.Comment("Design " + Num(plan.WidthMm, "0.0") + " x " + Num(plan.HeightMm, "0.0") + " mm, from X"
                    + Num(plan.Min.X, "0.0") + " Y" + Num(plan.Min.Y, "0.0") + " to X" + Num(plan.Max.X, "0.0") + " Y" + Num(plan.Max.Y, "0.0"));
                if (plan.CopiesX * plan.CopiesY > 1)
                {
                    w.Comment("Copies " + plan.CopiesX.ToString(CultureInfo.InvariantCulture) + " x " + plan.CopiesY.ToString(CultureInfo.InvariantCulture)
                        + ", step X" + Num(plan.Pitch.X, "0.###") + " Y-" + Num(plan.Pitch.Y, "0.###") + " mm");
                }

                int marks = jobs.Sum(j => j.Marks.Count);
                if (marks > 0)
                {
                    w.Comment("Registration marks: " + marks.ToString(CultureInfo.InvariantCulture) + " holes, "
                        + Num(s.MarksOffsetMm, "0.###") + " mm outside the design corners");
                }

                w.Comment("Depth " + Num(s.DepthMm, "0.###") + " mm, retract " + Num(s.RetractMm, "0.###") + " mm"
                    + (s.PeckMm > 0 ? ", peck " + Num(s.PeckMm, "0.###") + " mm" : string.Empty));
            }

            w.Line("G21");
            w.Line("G90");
            w.Line("G17");
            w.Line("G0 Z" + Z(s.ClearanceMm));

            bool toolChange = s.FileMode == DrillFileMode.OneFileToolChange;
            bool spindle = s.SpindleRpm > 0;
            int rpmNow = 0;
            for (int k = 0; k < jobs.Count; k++)
            {
                DrillJob job = jobs[k];
                if (s.Comments)
                {
                    w.Comment(JobComment(job));
                }

                if (toolChange)
                {
                    if (k > 0)
                    {
                        w.Line("G0 Z" + Z(s.ClearanceMm));
                        if (spindle)
                        {
                            w.Line("M5");
                            rpmNow = 0;
                        }
                    }

                    w.Line("T" + job.Tool.ToString(CultureInfo.InvariantCulture) + " M6");

                    // После смены сверла высота — та, что оставил станок: сначала подъём, потом X/Y.
                    w.Line("G0 Z" + Z(s.ClearanceMm));
                }

                int rpm = job.Rpm > 0 ? job.Rpm : s.SpindleRpm;
                if (spindle && rpm != rpmNow)
                {
                    w.Line("M3 S" + rpm.ToString(CultureInfo.InvariantCulture));
                    rpmNow = rpm;
                }

                Holes(w, job, s);
            }

            w.Line("G0 Z" + Z(s.ClearanceMm));
            if (spindle)
            {
                w.Line("M5");
            }

            w.Line("G0 X0.000 Y0.000");
            w.Line("M30");
            if (s.Percent)
            {
                w.Raw("%");
            }

            return w.ToString();
        }

        /// <summary>«Drill 2.2 mm cutter, T1, F500 S18000, stones ss6, 120 holes, 120 milled to D2.4, 4 marks».</summary>
        private static string JobComment(DrillJob job)
        {
            var c = new StringBuilder("Drill ").Append(Num(job.DrillMm, "0.###")).Append(" mm");
            string name = Ascii.Comment(job.Name);
            if (name.Length > 0)
            {
                c.Append(' ').Append(name);
            }

            c.Append(", T").Append(job.Tool.ToString(CultureInfo.InvariantCulture)).Append(", F").Append(Num(job.Feed, "0.#"));
            if (job.Rpm > 0)
            {
                c.Append(" S").Append(job.Rpm.ToString(CultureInfo.InvariantCulture));
            }

            string sizes = Ascii.Comment(string.Join(", ", job.Sizes.Where(x => x.Length > 0)));
            if (sizes.Length > 0)
            {
                c.Append(", stones ").Append(sizes);
            }

            if (job.Color.Length > 0)
            {
                c.Append(", ").Append(Ascii.Comment(job.Color));
            }

            c.Append(", ").Append(job.Points.Count.ToString(CultureInfo.InvariantCulture)).Append(" holes");
            if (job.Milled > 0)
            {
                string to = string.Join("/", job.MillRadii.Where(r => r > 0).Select(r => Num(job.DrillMm + 2 * r, "0.##")).Distinct());
                c.Append(", ").Append(job.Milled.ToString(CultureInfo.InvariantCulture)).Append(" milled to D").Append(to);
            }

            if (job.Marks.Count > 0)
            {
                c.Append(", ").Append(job.Marks.Count.ToString(CultureInfo.InvariantCulture)).Append(" marks");
            }

            return c.ToString();
        }

        /// <summary>
        /// Отверстия сверла по порядку. Простое отверстие — подвод, опускание на подаче (с выводом сверла,
        /// если задан шаг), подъём; с циклами — G81/G83 (дальше только X Y). Отверстие с дофрезеровкой —
        /// опускание в центре, потом круги по G1 на полной глубине, обратно в центр и подъём.
        /// </summary>
        private static void Holes(Writer w, DrillJob job, DrillSettings s)
        {
            string retract = Z(s.RetractMm);
            List<double> levels = Levels(s);
            bool atRetract = false;
            bool cycle = false;
            for (int n = 0; n < job.Points.Count; n++)
            {
                Point2D p = job.Points[n];
                double mill = n < job.MillRadii.Count ? job.MillRadii[n] : 0;
                if (s.CannedCycle && mill <= 0)
                {
                    if (cycle)
                    {
                        w.Line(XY(p));
                    }
                    else
                    {
                        string words = s.PeckMm > 0 && s.PeckMm < s.DepthMm
                            ? "G83 " + XY(p) + " Z" + Z(-s.DepthMm) + " R" + retract + " Q" + Z(s.PeckMm)
                            : "G81 " + XY(p) + " Z" + Z(-s.DepthMm) + " R" + retract;
                        w.Line("G0 " + XY(p));
                        w.Line("G99 " + words + w.Feed(job.Feed, always: true));
                        cycle = true;
                        atRetract = true;
                    }

                    continue;
                }

                if (cycle)
                {
                    w.Line("G80");
                    cycle = false;
                }

                w.Line("G0 " + XY(p));
                if (!atRetract)
                {
                    w.Line("G0 Z" + retract);
                    atRetract = true;
                }

                for (int i = 0; i < levels.Count; i++)
                {
                    if (i > 0)
                    {
                        // Обратно в отверстие холостым ходом — чуть выше того места, где остановились.
                        w.Line("G0 Z" + Z(-levels[i - 1] + 0.3));
                    }

                    w.Line("G1 Z" + Z(-levels[i]) + w.Feed(job.Feed));
                    if (i < levels.Count - 1 || mill <= 0)
                    {
                        w.Line("G0 Z" + retract);
                    }
                }

                if (mill > 0)
                {
                    foreach (double ring in DrillPlanner.MillRings(mill, job.DrillMm))
                    {
                        int segments = DrillPlanner.MillSegments(ring);
                        w.Line("G1 " + XY(p + new Point2D(ring, 0)) + w.Feed(s.MillFeed));
                        for (int k = 1; k <= segments; k++)
                        {
                            double a = 2 * Math.PI * k / segments;
                            w.Line("G1 " + XY(p + new Point2D(ring * Math.Cos(a), ring * Math.Sin(a))));
                        }
                    }

                    w.Line("G1 " + XY(p));
                    w.Line("G0 Z" + retract);
                }
            }

            if (cycle)
            {
                w.Line("G80");
            }
        }

        /// <summary>Глубины проходов: шаг за шагом до полной глубины (без шага — сразу на всю).</summary>
        private static List<double> Levels(DrillSettings s)
        {
            var levels = new List<double>();
            if (s.PeckMm > 0 && s.PeckMm < s.DepthMm)
            {
                for (double z = s.PeckMm; z < s.DepthMm - 1e-6; z += s.PeckMm)
                {
                    levels.Add(z);
                }
            }

            levels.Add(s.DepthMm);
            return levels;
        }

        private static string OriginText(DrillSettings s, DrillPlan plan)
        {
            switch (s.Origin)
            {
                case DrillOrigin.Board:
                    string corner = s.Home switch
                    {
                        BoardCorner.UpperLeft => "upper left corner",
                        BoardCorner.LowerRight => "lower right corner",
                        BoardCorner.UpperRight => "upper right corner",
                        BoardCorner.Center => "center",
                        _ => "lower left corner",
                    };
                    string size = plan.Board.HasValue ? " " + Num(plan.Board.Value.WidthMm, "0.#") + " x " + Num(plan.Board.Value.HeightMm, "0.#") + " mm" : string.Empty;
                    return corner + " of board" + size;
                case DrillOrigin.DesignCenter:
                    return "center of design";
                case DrillOrigin.Document:
                    return "CorelDRAW document origin";
                default:
                    return "lower left corner of design";
            }
        }

        private static string ColorPart(string color)
        {
            string part = Ascii.FileName(color);
            return part.Length > 0 ? part : "color";
        }

        private static string Extension(string? ext)
        {
            string e = (ext ?? string.Empty).Trim();
            if (e.Length == 0)
            {
                return ".tap";
            }

            e = Ascii.FileName(e.TrimStart('.'));
            return e.Length > 0 ? "." + e : ".tap";
        }

        private static string XY(Point2D p) => "X" + Z(p.X) + " Y" + Z(p.Y);

        /// <summary>Координата: всегда 3 знака и точка («-0.000» не бывает).</summary>
        internal static string Z(double v)
        {
            double r = Math.Round(v, 3, MidpointRounding.AwayFromZero);
            if (r == 0)
            {
                r = 0;
            }

            return r.ToString("0.000", CultureInfo.InvariantCulture);
        }

        private static string Num(double v, string format) => Math.Round(v, 3).ToString(format, CultureInfo.InvariantCulture);

        private sealed class Writer
        {
            private readonly StringBuilder text = new StringBuilder();
            private readonly bool numbers;
            private int n;
            private string? current;

            public Writer(bool numbers)
            {
                this.numbers = numbers;
            }

            public void Line(string code)
            {
                if (numbers)
                {
                    n += 10;
                    text.Append('N').Append(n.ToString(CultureInfo.InvariantCulture)).Append(' ');
                }

                text.Append(code).Append(NewLine);
            }

            public void Comment(string comment) => Line("(" + comment + ")");

            /// <summary>« F500», если подача меняется (или <paramref name="always"/>), иначе пусто — F модальная.</summary>
            public string Feed(double feed, bool always = false)
            {
                string f = Num(feed, "0.#");
                if (!always && f == current)
                {
                    return string.Empty;
                }

                current = f;
                return " F" + f;
            }

            public void Raw(string line) => text.Append(line).Append(NewLine);

            public override string ToString() => text.ToString();
        }
    }
}
