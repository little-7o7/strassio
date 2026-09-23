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
    /// Проверка углов (шаг 2 плана «углы»): как настоящий алгоритм проходит угол при трёх значениях
    /// CornerPlacement — «смешанно» (по умолчанию), «острый», «круглый». Всё считается настоящим
    /// LineScatterer, ничего не рисуется от руки. Камень ss6 (2,4 мм), зазор 0 — как на скриншотах автора.
    /// </summary>
    internal static class CornerLab
    {
        private const double Diameter = 2.4;
        private const double Gap = 0.0;
        private const double ArmMm = 14;

        private const double Scale = 11;        // пикселей на миллиметр
        private const double CellWidthMm = 30;
        private const double CellHeightMm = 20;
        private const double CaptionPx = 32;

        private static readonly double[] Angles = { 90, 60, 36, 15 };

        private static readonly (string Title, CornerPlacement Placement)[] Columns =
        {
            ("Смешанно (по умолчанию)", CornerPlacement.Mixed),
            ("Острый", CornerPlacement.Sharp),
            ("Круглый", CornerPlacement.Round),
        };

        public static void Render(string repoRoot)
        {
            string outDir = Path.Combine(repoRoot, "out", "preview");
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, "corners.svg");

            var ci = CultureInfo.InvariantCulture;
            double cellW = CellWidthMm * Scale;
            double cellH = CellHeightMm * Scale;
            double width = cellW * Columns.Length;
            double height = (cellH + CaptionPx) * Angles.Length + CaptionPx;

            var sb = new StringBuilder();
            sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{N(width)}\" height=\"{N(height)}\" viewBox=\"0 0 {N(width)} {N(height)}\" font-family=\"Segoe UI, Arial, sans-serif\">");
            sb.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"white\"/>");

            for (int c = 0; c < Columns.Length; c++)
            {
                double x = c * cellW + cellW / 2;
                sb.AppendLine($"<text x=\"{N(x)}\" y=\"21\" text-anchor=\"middle\" font-size=\"15\" font-weight=\"600\" fill=\"#222\">{Columns[c].Title}</text>");
            }

            for (int r = 0; r < Angles.Length; r++)
            {
                double angle = Angles[r];
                double rowTop = CaptionPx + r * (cellH + CaptionPx);

                for (int c = 0; c < Columns.Length; c++)
                {
                    IReadOnlyList<PlacedStone> stones = Run(angle, Columns[c].Placement);
                    DrawCell(sb, ci, c * cellW, rowTop, cellW, cellH, angle, stones, Note(stones));
                }
            }

            sb.AppendLine("</svg>");
            File.WriteAllText(outPath, sb.ToString());

            Console.WriteLine("Углы: " + outPath);
            Console.WriteLine();
            foreach (double angle in Angles)
            {
                Console.WriteLine($"Угол {angle:0}°:");
                foreach ((string title, CornerPlacement placement) in Columns)
                {
                    Console.WriteLine($"  {title,-24} — {Report(Run(angle, placement))}");
                }
            }
        }

        /// <summary>Уголок с заданным углом между сторонами, посчитанный настоящим алгоритмом плагина.</summary>
        private static IReadOnlyList<PlacedStone> Run(double interiorAngleDeg, CornerPlacement placement)
        {
            (Point2D v, Point2D u1, Point2D u2) = Arms(interiorAngleDeg);
            var curve = Curve.FromPolyline(new[] { v + u1 * ArmMm, v, v + u2 * ArmMm }, isClosed: false);
            var options = new LineScatterOptions
            {
                StoneDiameterMm = Diameter,
                GapMm = Gap,
                Mode = StepMode.FitEven,
                CornerAngleThresholdDeg = 30,
                CornerPlacement = placement,
            };

            return LineScatterer.Scatter(curve, options);
        }

        /// <summary>Вершина угла в начале координат, оба плеча идут вверх (в SVG вверх — это минус Y).</summary>
        private static (Point2D Vertex, Point2D Arm1, Point2D Arm2) Arms(double interiorAngleDeg)
        {
            double half = interiorAngleDeg / 2 * Math.PI / 180;
            return (Point2D.Zero,
                new Point2D(-Math.Sin(half), -Math.Cos(half)),
                new Point2D(Math.Sin(half), -Math.Cos(half)));
        }

        private static bool StoneAtVertex(IReadOnlyList<PlacedStone> stones)
        {
            foreach (PlacedStone s in stones)
            {
                if (Point2D.Distance(s.Center, Point2D.Zero) < 1e-6)
                {
                    return true;
                }
            }

            return false;
        }

        private static (double Closest, double Widest) Extremes(IReadOnlyList<PlacedStone> stones)
        {
            double closest = double.MaxValue;
            double widest = 0;

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
                    closest = Math.Min(closest, nearest);
                    widest = Math.Max(widest, nearest);
                }
            }

            return (closest == double.MaxValue ? 0 : closest, widest);
        }

        private static string Note(IReadOnlyList<PlacedStone> stones)
        {
            (double closest, double widest) = Extremes(stones);
            string tip = StoneAtVertex(stones) ? "страза в вершине" : "вершина скруглена";
            double gap = widest - Diameter;
            return gap < 0.02 ? $"{tip}, вплотную" : $"{tip}, просвет до {gap:0.00} мм";
        }

        private static string Report(IReadOnlyList<PlacedStone> stones)
        {
            (double closest, double widest) = Extremes(stones);
            string overlap = closest < Diameter - 0.005 ? $"НАЛОЖЕНИЕ {Diameter - closest:0.00} мм" : "без наложений";
            string tip = StoneAtVertex(stones) ? "страза в вершине" : "вершина скруглена";
            return $"{stones.Count} страз, {tip}, {overlap}, самый большой просвет {widest - Diameter:0.00} мм";
        }

        private static void DrawCell(
            StringBuilder sb, CultureInfo ci, double left, double top, double cellW, double cellH,
            double angle, IReadOnlyList<PlacedStone> stones, string note)
        {
            sb.AppendLine($"<g transform=\"translate({N(left)},{N(top)})\">");
            sb.AppendLine($"<rect x=\"1\" y=\"1\" width=\"{N(cellW - 2)}\" height=\"{N(cellH - 2)}\" fill=\"#fafafa\" stroke=\"#dddddd\"/>");

            // Вершина угла — внизу по центру клетки, стороны уходят вверх.
            double originX = cellW / 2;
            double originY = cellH - 2.5 * Scale;

            double Px(double mm) => originX + mm * Scale;
            double Py(double mm) => originY + mm * Scale;

            (Point2D v, Point2D u1, Point2D u2) = Arms(angle);
            Point2D a = v + u1 * ArmMm;
            Point2D b = v + u2 * ArmMm;
            sb.AppendLine($"<polyline points=\"{N(Px(a.X))},{N(Py(a.Y))} {N(Px(v.X))},{N(Py(v.Y))} {N(Px(b.X))},{N(Py(b.Y))}\" fill=\"none\" stroke=\"#bbbbbb\" stroke-width=\"1\" stroke-dasharray=\"4 3\"/>");

            foreach (PlacedStone s in stones)
            {
                string fill = s.IsCorner ? "#E53935" : "#43A047";
                sb.AppendLine($"<circle cx=\"{N(Px(s.Center.X))}\" cy=\"{N(Py(s.Center.Y))}\" r=\"{N(Diameter / 2 * Scale)}\" fill=\"{fill}\" fill-opacity=\"0.8\" stroke=\"#333333\" stroke-width=\"0.8\"/>");
            }

            sb.AppendLine($"<text x=\"8\" y=\"18\" font-size=\"13\" fill=\"#555\">Угол {angle:0}°</text>");
            sb.AppendLine($"<text x=\"{N(cellW / 2)}\" y=\"{N(cellH + 20)}\" text-anchor=\"middle\" font-size=\"12\" fill=\"#444\">{note}</text>");
            sb.AppendLine("</g>");
        }

        private static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
