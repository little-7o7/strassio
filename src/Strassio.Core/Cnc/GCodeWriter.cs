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
                w.Comment("X0 Y0: " + OriginText(s.Origin) + ". Z0: top of material");
                w.Comment("Design " + Num(plan.WidthMm, "0.0") + " x " + Num(plan.HeightMm, "0.0") + " mm, from X"
                    + Num(plan.Min.X, "0.0") + " Y" + Num(plan.Min.Y, "0.0") + " to X" + Num(plan.Max.X, "0.0") + " Y" + Num(plan.Max.Y, "0.0"));
                w.Comment("Depth " + Num(s.DepthMm, "0.###") + " mm, retract " + Num(s.RetractMm, "0.###")
                    + " mm, plunge F" + Num(s.PlungeFeed, "0.#") + (s.PeckMm > 0 ? ", peck " + Num(s.PeckMm, "0.###") + " mm" : string.Empty));
            }

            w.Line("G21");
            w.Line("G90");
            w.Line("G17");
            w.Line("G0 Z" + Z(s.ClearanceMm));

            bool toolChange = s.FileMode == DrillFileMode.OneFileToolChange;
            for (int k = 0; k < jobs.Count; k++)
            {
                DrillJob job = jobs[k];
                if (s.Comments)
                {
                    string sizes = Ascii.Comment(string.Join(", ", job.Sizes.Where(x => x.Length > 0)));
                    string color = job.Color.Length > 0 ? ", " + Ascii.Comment(job.Color) : string.Empty;
                    w.Comment("Drill " + Num(job.DrillMm, "0.###") + " mm, T" + job.Tool.ToString(CultureInfo.InvariantCulture)
                        + (sizes.Length > 0 ? ", stones " + sizes : string.Empty) + color + ", "
                        + job.Points.Count.ToString(CultureInfo.InvariantCulture) + " holes");
                }

                if (toolChange)
                {
                    if (k > 0)
                    {
                        w.Line("G0 Z" + Z(s.ClearanceMm));
                        if (s.SpindleRpm > 0)
                        {
                            w.Line("M5");
                        }
                    }

                    w.Line("T" + job.Tool.ToString(CultureInfo.InvariantCulture) + " M6");
                }

                if (s.SpindleRpm > 0 && (k == 0 || toolChange))
                {
                    w.Line("M3 S" + s.SpindleRpm.ToString(CultureInfo.InvariantCulture));
                }

                if (s.CannedCycle)
                {
                    Canned(w, job.Points, s);
                }
                else
                {
                    Simple(w, job.Points, s);
                }
            }

            w.Line("G0 Z" + Z(s.ClearanceMm));
            if (s.SpindleRpm > 0)
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

        /// <summary>Каждое отверстие — подвод, опускание на подаче (с выводом сверла, если задан шаг), подъём.</summary>
        private static void Simple(Writer w, IReadOnlyList<Point2D> points, DrillSettings s)
        {
            string retract = Z(s.RetractMm);
            string feed = " F" + Num(s.PlungeFeed, "0.#");
            List<double> levels = Levels(s);
            bool first = true;
            foreach (Point2D p in points)
            {
                w.Line("G0 " + XY(p));
                if (first)
                {
                    w.Line("G0 Z" + retract);
                }

                for (int i = 0; i < levels.Count; i++)
                {
                    if (i > 0)
                    {
                        // Обратно в отверстие холостым ходом — чуть выше того места, где остановились.
                        w.Line("G0 Z" + Z(-levels[i - 1] + 0.3));
                    }

                    w.Line("G1 Z" + Z(-levels[i]) + (first ? feed : string.Empty));
                    first = false;
                    w.Line("G0 Z" + retract);
                }
            }
        }

        /// <summary>Цикл G81 (или G83 с шагом): первая строка с параметрами, дальше только X Y.</summary>
        private static void Canned(Writer w, IReadOnlyList<Point2D> points, DrillSettings s)
        {
            if (points.Count == 0)
            {
                return;
            }

            string cycle = s.PeckMm > 0 && s.PeckMm < s.DepthMm
                ? "G83 " + XY(points[0]) + " Z" + Z(-s.DepthMm) + " R" + Z(s.RetractMm) + " Q" + Z(s.PeckMm)
                : "G81 " + XY(points[0]) + " Z" + Z(-s.DepthMm) + " R" + Z(s.RetractMm);
            w.Line("G0 " + XY(points[0]));
            w.Line("G99 " + cycle + " F" + Num(s.PlungeFeed, "0.#"));
            for (int i = 1; i < points.Count; i++)
            {
                w.Line(XY(points[i]));
            }

            w.Line("G80");
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

        private static string OriginText(DrillOrigin origin) => origin switch
        {
            DrillOrigin.DesignCenter => "center of design",
            DrillOrigin.Document => "CorelDRAW document origin",
            _ => "lower left corner of design",
        };

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

            public void Raw(string line) => text.Append(line).Append(NewLine);

            public override string ToString() => text.ToString();
        }
    }
}
