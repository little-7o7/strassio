using System;
using System.Collections.Generic;
using System.Globalization;
using Strassio.Core.Geometry;

namespace Strassio.Core.Cnc
{
    /// <summary>Что вышло при «прогоне» программы: где просверлено, как глубоко, что опасно.</summary>
    public sealed class GCodeRun
    {
        /// <summary>Просверленные отверстия: место, нижняя точка Z, номер инструмента.</summary>
        public List<(Point2D At, double BottomZ, int Tool)> Holes { get; } = new List<(Point2D, double, int)>();

        /// <summary>Холостой путь по X Y, мм.</summary>
        public double TravelMm { get; set; }

        /// <summary>Самая низкая точка Z за всю программу.</summary>
        public double MinZ { get; set; } = double.MaxValue;

        /// <summary>Опасные места: номер строки и что не так (по-английски, для разработчика).</summary>
        public List<string> Problems { get; } = new List<string>();

        public bool Ended { get; set; }
    }

    /// <summary>
    /// Самопроверка готового G-code (docs/SPEC.md, раздел 11.1): построчно «проигрывает» программу
    /// и проверяет, что сверло не ходит по X Y внутри материала, шпиндель крутится, пока сверло
    /// внизу, и программа заканчивается M30. Понимает только то, что пишет <see cref="GCodeWriter"/>
    /// (G0/G1, G81/G83, G80, G98/G99, T, M3/M5/M6/M30) — этого хватает, чтобы проверить свой файл
    /// перед сохранением и нарисовать путь в превью.
    /// </summary>
    public static class GCodeCheck
    {
        public static GCodeRun Run(string program)
        {
            var run = new GCodeRun();
            double x = 0, y = 0, z = double.NaN;
            int motion = 0;
            bool spindle = false;
            int tool = 0;
            bool retractToR = true;
            double cycleZ = 0, cycleR = 0, cycleQ = 0, initialZ = 0;
            bool inHole = false;
            double holeBottom = 0;

            string[] lines = program.Replace("\r\n", "\n").Split('\n');
            for (int ln = 0; ln < lines.Length; ln++)
            {
                string line = StripComments(lines[ln]).Trim().ToUpperInvariant();
                if (line.Length == 0 || line == "%")
                {
                    continue;
                }

                var words = new List<(char Letter, double Value)>();
                if (!Parse(line, words))
                {
                    run.Problems.Add($"line {ln + 1}: cannot read '{lines[ln].Trim()}'");
                    continue;
                }

                double? nx = null, ny = null, nz = null, r = null, q = null;
                bool changeTool = false;
                int? newTool = null;
                foreach ((char letter, double value) in words)
                {
                    switch (letter)
                    {
                        case 'N':
                        case 'F':
                        case 'S':
                            break;
                        case 'X': nx = value; break;
                        case 'Y': ny = value; break;
                        case 'Z': nz = value; break;
                        case 'R': r = value; break;
                        case 'Q': q = value; break;
                        case 'T': newTool = (int)value; break;
                        case 'G':
                            int g = (int)Math.Round(value * 10);
                            if (g == 0) motion = 0;
                            else if (g == 10) motion = 1;
                            else if (g == 810) motion = 81;
                            else if (g == 830) motion = 83;
                            else if (g == 800) motion = 0;
                            else if (g == 980) retractToR = false;
                            else if (g == 990) retractToR = true;
                            else if (g != 210 && g != 900 && g != 170)
                            {
                                run.Problems.Add($"line {ln + 1}: unexpected G{value.ToString(CultureInfo.InvariantCulture)}");
                            }

                            break;
                        case 'M':
                            int m = (int)value;
                            if (m == 3) spindle = true;
                            else if (m == 5) spindle = false;
                            else if (m == 6) changeTool = true;
                            else if (m == 30 || m == 2) run.Ended = true;
                            else run.Problems.Add($"line {ln + 1}: unexpected M{m}");
                            break;
                        default:
                            run.Problems.Add($"line {ln + 1}: unexpected word {letter}");
                            break;
                    }
                }

                if (newTool.HasValue)
                {
                    tool = newTool.Value;
                }

                if (changeTool && !double.IsNaN(z) && z <= 0)
                {
                    run.Problems.Add($"line {ln + 1}: tool change below safe height");
                }

                if (motion == 81 || motion == 83)
                {
                    if (nz.HasValue) cycleZ = nz.Value;
                    if (r.HasValue) cycleR = r.Value;
                    if (q.HasValue) cycleQ = q.Value;
                    bool cycleStart = words.Exists(w => w.Letter == 'G' && (Math.Abs(w.Value - 81) < 1e-9 || Math.Abs(w.Value - 83) < 1e-9));
                    if (cycleStart)
                    {
                        initialZ = z;
                    }

                    if (!nx.HasValue && !ny.HasValue && !cycleStart)
                    {
                        continue;
                    }

                    double tx = nx ?? x, ty = ny ?? y;
                    // Подвод на начальной высоте, потом на R, сверление до Z, возврат на R (G99) или начальную (G98).
                    if (double.IsNaN(z) || z < cycleR - 1e-9)
                    {
                        run.Problems.Add($"line {ln + 1}: cycle starts below R");
                    }

                    run.TravelMm += Point2D.Distance(new Point2D(x, y), new Point2D(tx, ty));
                    x = tx;
                    y = ty;
                    if (!spindle)
                    {
                        run.Problems.Add($"line {ln + 1}: drilling with spindle off");
                    }

                    if (motion == 83 && cycleQ <= 0)
                    {
                        run.Problems.Add($"line {ln + 1}: G83 without Q");
                    }

                    run.Holes.Add((new Point2D(x, y), cycleZ, tool));
                    run.MinZ = Math.Min(run.MinZ, cycleZ);
                    z = retractToR ? cycleR : Math.Max(initialZ, cycleR);
                    continue;
                }

                double toX = nx ?? x, toY = ny ?? y, toZ = nz ?? z;
                bool movesXY = Math.Abs(toX - x) > 1e-9 || Math.Abs(toY - y) > 1e-9;
                if (movesXY)
                {
                    // По X Y — только над материалом (Z0 — его верх), иначе сверло волочится и ломается.
                    if (double.IsNaN(z))
                    {
                        run.Problems.Add($"line {ln + 1}: X/Y move before Z is known");
                    }
                    else if (Math.Min(z, double.IsNaN(toZ) ? z : toZ) <= 1e-9)
                    {
                        run.Problems.Add($"line {ln + 1}: X/Y move at or below the top of material (Z {Fmt(Math.Min(z, toZ))})");
                    }

                    run.TravelMm += Point2D.Distance(new Point2D(x, y), new Point2D(toX, toY));
                    inHole = false;
                }

                if (!double.IsNaN(toZ) && toZ < -1e-9)
                {
                    // Холостым ходом вниз в материал — только обратно в уже просверленную часть отверстия.
                    if (motion == 0 && (double.IsNaN(z) || toZ < z - 1e-9) && (!inHole || toZ < holeBottom - 1e-9))
                    {
                        run.Problems.Add($"line {ln + 1}: rapid move into material (Z {Fmt(toZ)})");
                    }

                    if (!spindle)
                    {
                        run.Problems.Add($"line {ln + 1}: cutting with spindle off");
                    }

                    if (!inHole)
                    {
                        inHole = true;
                        holeBottom = toZ;
                        run.Holes.Add((new Point2D(toX, toY), toZ, tool));
                    }
                    else if (toZ < holeBottom)
                    {
                        holeBottom = toZ;
                        run.Holes[run.Holes.Count - 1] = (new Point2D(toX, toY), toZ, tool);
                    }
                }

                if (!double.IsNaN(toZ))
                {
                    run.MinZ = Math.Min(run.MinZ, toZ);
                }

                x = toX;
                y = toY;
                z = toZ;
            }

            if (!run.Ended)
            {
                run.Problems.Add("program does not end with M30");
            }

            if (spindle)
            {
                run.Problems.Add("spindle is still on at the end");
            }

            return run;
        }

        private static string Fmt(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        private static string StripComments(string line)
        {
            int semicolon = line.IndexOf(';');
            if (semicolon >= 0)
            {
                line = line.Substring(0, semicolon);
            }

            while (true)
            {
                int open = line.IndexOf('(');
                if (open < 0)
                {
                    return line;
                }

                int close = line.IndexOf(')', open);
                line = close < 0 ? line.Substring(0, open) : line.Substring(0, open) + line.Substring(close + 1);
            }
        }

        private static bool Parse(string line, List<(char, double)> words)
        {
            int i = 0;
            while (i < line.Length)
            {
                char c = line[i];
                if (c == ' ' || c == '\t')
                {
                    i++;
                    continue;
                }

                if (c < 'A' || c > 'Z')
                {
                    return false;
                }

                int start = ++i;
                while (i < line.Length && (char.IsDigit(line[i]) || line[i] == '.' || line[i] == '-' || line[i] == '+'))
                {
                    i++;
                }

                if (!double.TryParse(line.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                {
                    return false;
                }

                words.Add((c, value));
            }

            return true;
        }
    }
}
