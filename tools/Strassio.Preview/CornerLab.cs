using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Strassio.Core.Geometry;
using Strassio.Core.Placement;

namespace Strassio.Preview
{
    /// <summary>
    /// Картинка для выбора автором (шаг 1 плана «углы»): как ряд страз проходит угол сейчас и какими
    /// угол может стать — «острый» или «круглый». Ничего в ядре не меняет: варианты А и Б считаются
    /// прямо здесь, чтобы автор посмотрел на результат и выбрал, прежде чем переделывать алгоритм.
    /// Значения взяты с реальных скриншотов автора: камень ss6 (2,4 мм), зазор 0.
    /// </summary>
    internal static class CornerLab
    {
        private const double Diameter = 2.4;
        private const double Gap = 0.0;
        private const double ArmMm = 14;

        /// <summary>Зазор у самого острия: меньше обычного, у острия он не бросается в глаза.</summary>
        private const double TipGap = 0.1;

        private const double Scale = 12;        // пикселей на миллиметр
        private const double CellWidthMm = 30;
        private const double CellHeightMm = 20;
        private const double CaptionPx = 30;

        private static readonly double[] Angles = { 90, 60, 36 };

        private static readonly string[] ColumnTitles =
        {
            "Сейчас",
            "А — острый угол",
            "Б — круглый, мягкий",
            "В — круглый, широкий",
        };

        public static void Render(string repoRoot)
        {
            string outDir = Path.Combine(repoRoot, "out", "preview");
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, "corners.svg");

            var ci = CultureInfo.InvariantCulture;
            double cellW = CellWidthMm * Scale;
            double cellH = CellHeightMm * Scale;
            double width = cellW * ColumnTitles.Length;
            double height = (cellH + CaptionPx) * Angles.Length + CaptionPx;

            var sb = new StringBuilder();
            sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width.ToString("0.##", ci)}\" height=\"{height.ToString("0.##", ci)}\" viewBox=\"0 0 {width.ToString("0.##", ci)} {height.ToString("0.##", ci)}\" font-family=\"Segoe UI, Arial, sans-serif\">");
            sb.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"white\"/>");

            for (int c = 0; c < ColumnTitles.Length; c++)
            {
                double x = c * cellW + cellW / 2;
                sb.AppendLine($"<text x=\"{x.ToString("0.##", ci)}\" y=\"20\" text-anchor=\"middle\" font-size=\"15\" font-weight=\"600\" fill=\"#222\">{ColumnTitles[c]}</text>");
            }

            for (int r = 0; r < Angles.Length; r++)
            {
                double angle = Angles[r];
                double rowTop = CaptionPx + r * (cellH + CaptionPx);

                for (int c = 0; c < ColumnTitles.Length; c++)
                {
                    double left = c * cellW;
                    IReadOnlyList<PlacedStone> stones = c switch
                    {
                        0 => AsNow(angle),
                        1 => Sharp(angle),
                        2 => Round(angle, false, out _),
                        _ => Round(angle, true, out _),
                    };

                    string note = ShortNote(stones);
                    if (c >= 2)
                    {
                        Round(angle, c == 3, out double cut);
                        note += $", вершина срезана {cut:0.0} мм";
                    }

                    DrawCell(sb, ci, left, rowTop, cellW, cellH, angle, stones, note);
                }
            }

            sb.AppendLine("</svg>");
            File.WriteAllText(outPath, sb.ToString());

            Console.WriteLine("Лаборатория углов: " + outPath);
            Console.WriteLine();
            foreach (double angle in Angles)
            {
                Console.WriteLine($"Угол {angle:0}°:");
                Console.WriteLine("  сейчас  — " + Report(AsNow(angle)));
                Console.WriteLine("  острый  — " + Report(Sharp(angle)));
                Console.WriteLine("  мягкий  — " + RoundNote(angle, false));
                Console.WriteLine("  широкий — " + RoundNote(angle, true));
            }
        }

        /// <summary>Как считает плагин сегодня — настоящий LineScatterer, режим «подгонка» (по умолчанию в докере).</summary>
        private static IReadOnlyList<PlacedStone> AsNow(double interiorAngleDeg)
        {
            (Point2D v, Point2D u1, Point2D u2) = Arms(interiorAngleDeg);
            var curve = Curve.FromPolyline(new[] { v + u1 * ArmMm, v, v + u2 * ArmMm }, isClosed: false);
            var options = new LineScatterOptions
            {
                StoneDiameterMm = Diameter,
                GapMm = Gap,
                Mode = StepMode.FitEven,
                CornerAngleThresholdDeg = 30,
            };

            return LineScatterer.Scatter(curve, options);
        }

        /// <summary>
        /// Вариант А. Страза стоит точно в вершине, соседние по обе стороны — вплотную к ней.
        /// На очень остром угле две первые соседки с разных сторон налезали бы друг на друга, поэтому
        /// их отодвигают от вершины ровно настолько, чтобы между ними остался маленький зазор TipGap;
        /// дальше по плечу идёт обычный шаг.
        /// </summary>
        private static IReadOnlyList<PlacedStone> Sharp(double interiorAngleDeg)
        {
            (Point2D v, Point2D u1, Point2D u2) = Arms(interiorAngleDeg);
            double step = Diameter + Gap;
            double halfRad = interiorAngleDeg / 2 * Math.PI / 180;

            // Расстояние между первыми соседками с разных сторон = 2 * t * sin(half). Нужно не меньше диаметра.
            double needed = (Diameter + TipGap) / (2 * Math.Sin(halfRad));
            double first = Math.Max(step, needed);

            var stones = new List<PlacedStone> { new PlacedStone(v, Diameter, true) };
            foreach (Point2D u in new[] { u1, u2 })
            {
                for (double t = first; t <= ArmMm + 1e-9; t += step)
                {
                    stones.Add(new PlacedStone(v + u * t, Diameter, false));
                }
            }

            return stones;
        }

        /// <summary>
        /// Варианты Б и В. Угол скруглён: стразы идут по дуге, вписанной в угол и касающейся обоих
        /// плеч, вплотную друг к другу; от точек касания дальше идут прямые участки обычным шагом.
        /// Радиус подбирается так, чтобы на дуге поместилось целое число страз вплотную. «Мягкий» —
        /// самый маленький возможный радиус (вершина почти на месте), «широкий» — примерно 60° дуги
        /// на стразу (угол заметно скруглён).
        /// </summary>
        private static IReadOnlyList<PlacedStone> Round(double interiorAngleDeg, bool wide, out double cutMm)
        {
            (Point2D v, Point2D u1, Point2D u2) = Arms(interiorAngleDeg);
            double step = Diameter + Gap;
            double halfRad = interiorAngleDeg / 2 * Math.PI / 180;
            double arcAngle = Math.PI - interiorAngleDeg * Math.PI / 180;

            int intervals = wide ? Math.Max(1, (int)Math.Round(arcAngle / (Math.PI / 3))) : 1;
            double radius = step / (2 * Math.Sin(arcAngle / (2 * intervals)));

            cutMm = radius / Math.Tan(halfRad);              // насколько дуга «срезает» вершину
            double centerDist = radius / Math.Sin(halfRad);   // центр дуги — на биссектрисе

            Point2D bisector = (u1 + u2).Normalized();
            Point2D center = v + bisector * centerDist;

            var stones = new List<PlacedStone>();

            Point2D start = v + u1 * cutMm;
            Point2D end = v + u2 * cutMm;
            double a0 = Math.Atan2(start.Y - center.Y, start.X - center.X);
            double a1 = Math.Atan2(end.Y - center.Y, end.X - center.X);

            // Идём по дуге короткой стороной.
            double sweep = a1 - a0;
            while (sweep > Math.PI)
            {
                sweep -= 2 * Math.PI;
            }

            while (sweep < -Math.PI)
            {
                sweep += 2 * Math.PI;
            }

            for (int i = 0; i <= intervals; i++)
            {
                double a = a0 + sweep * i / intervals;
                stones.Add(new PlacedStone(center + new Point2D(Math.Cos(a), Math.Sin(a)) * radius, Diameter, true));
            }

            foreach (Point2D u in new[] { u1, u2 })
            {
                for (double t = cutMm + step; t <= ArmMm + 1e-9; t += step)
                {
                    stones.Add(new PlacedStone(v + u * t, Diameter, false));
                }
            }

            return stones;
        }

        /// <summary>Вершина угла в начале координат, оба плеча идут вверх (в SVG вверх — это минус Y).</summary>
        private static (Point2D Vertex, Point2D Arm1, Point2D Arm2) Arms(double interiorAngleDeg)
        {
            double half = interiorAngleDeg / 2 * Math.PI / 180;
            return (Point2D.Zero,
                new Point2D(-Math.Sin(half), -Math.Cos(half)),
                new Point2D(Math.Sin(half), -Math.Cos(half)));
        }

        /// <summary>Короткая подпись под клеткой: главное, что видно глазу.</summary>
        private static string ShortNote(IReadOnlyList<PlacedStone> stones)
        {
            double worst = WorstGap(stones);
            return worst < 0.05 ? "стразы вплотную" : $"просвет до {worst:0.00} мм";
        }

        private static double WorstGap(IReadOnlyList<PlacedStone> stones)
        {
            double worstGap = 0;
            for (int i = 0; i < stones.Count; i++)
            {
                double nearest = double.MaxValue;
                for (int j = 0; j < stones.Count; j++)
                {
                    if (i != j)
                    {
                        nearest = Math.Min(nearest, Point2D.Distance(stones[i].Center, stones[j].Center));
                    }
                }

                if (nearest != double.MaxValue)
                {
                    worstGap = Math.Max(worstGap, nearest - Diameter);
                }
            }

            return worstGap;
        }

        /// <summary>Самая тесная и самая просторная пара соседних страз — главное, что видно глазу.</summary>
        private static string Report(IReadOnlyList<PlacedStone> stones)
        {
            double worstOverlap = 0;
            double worstGap = 0;

            for (int i = 0; i < stones.Count; i++)
            {
                double nearest = double.MaxValue;
                for (int j = 0; j < stones.Count; j++)
                {
                    if (i != j)
                    {
                        nearest = Math.Min(nearest, Point2D.Distance(stones[i].Center, stones[j].Center));
                    }
                }

                if (nearest == double.MaxValue)
                {
                    continue;
                }

                double edgeToEdge = nearest - Diameter;
                worstOverlap = Math.Min(worstOverlap, edgeToEdge);
                worstGap = Math.Max(worstGap, edgeToEdge);
            }

            string overlap = worstOverlap < -0.005 ? $"налезают на {-worstOverlap:0.00} мм" : "без наложений";
            return $"{stones.Count} страз, {overlap}, самый большой просвет {worstGap:0.00} мм";
        }

        private static string RoundNote(double angle, bool wide)
        {
            IReadOnlyList<PlacedStone> stones = Round(angle, wide, out double cut);
            return Report(stones) + $", вершина срезана на {cut:0.0} мм";
        }

        private static void DrawCell(
            StringBuilder sb, CultureInfo ci, double left, double top, double cellW, double cellH,
            double angle, IReadOnlyList<PlacedStone> stones, string note)
        {
            sb.AppendLine($"<g transform=\"translate({left.ToString("0.##", ci)},{top.ToString("0.##", ci)})\">");
            sb.AppendLine($"<rect x=\"1\" y=\"1\" width=\"{(cellW - 2).ToString("0.##", ci)}\" height=\"{(cellH - 2).ToString("0.##", ci)}\" fill=\"#fafafa\" stroke=\"#dddddd\"/>");

            // Вершина угла — внизу по центру клетки, плечи уходят вверх.
            double originX = cellW / 2;
            double originY = cellH - 2.5 * Scale;

            double Px(double mm) => originX + mm * Scale;
            double Py(double mm) => originY + mm * Scale;

            (Point2D v, Point2D u1, Point2D u2) = Arms(angle);
            Point2D a = v + u1 * ArmMm;
            Point2D b = v + u2 * ArmMm;
            sb.AppendLine($"<polyline points=\"{Px(a.X).ToString("0.##", ci)},{Py(a.Y).ToString("0.##", ci)} {Px(v.X).ToString("0.##", ci)},{Py(v.Y).ToString("0.##", ci)} {Px(b.X).ToString("0.##", ci)},{Py(b.Y).ToString("0.##", ci)}\" fill=\"none\" stroke=\"#bbbbbb\" stroke-width=\"1\" stroke-dasharray=\"4 3\"/>");

            foreach (PlacedStone s in stones)
            {
                double radius = Diameter / 2 * Scale;
                string fill = s.IsCorner ? "#E53935" : "#43A047";
                sb.AppendLine($"<circle cx=\"{Px(s.Center.X).ToString("0.##", ci)}\" cy=\"{Py(s.Center.Y).ToString("0.##", ci)}\" r=\"{radius.ToString("0.##", ci)}\" fill=\"{fill}\" fill-opacity=\"0.8\" stroke=\"#333333\" stroke-width=\"0.8\"/>");
            }

            sb.AppendLine($"<text x=\"8\" y=\"18\" font-size=\"13\" fill=\"#555\">Угол {angle:0}°</text>");
            sb.AppendLine($"<text x=\"{(cellW / 2).ToString("0.##", ci)}\" y=\"{(cellH + 19).ToString("0.##", ci)}\" text-anchor=\"middle\" font-size=\"12\" fill=\"#444\">{note}</text>");
            sb.AppendLine("</g>");
        }
    }
}
