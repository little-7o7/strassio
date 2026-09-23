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
    /// Замечание автора «пропускает стразы»: метод «Кант» (F5 — ряды по краю фигуры), квадрат,
    /// 3 ряда, зазор 0. Рисуем то же самое и считаем числами, где в ряду пропуски: для каждой
    /// стразы ищем ближайшую соседку, и если до неё больше полутора шагов — это дыра.
    /// </summary>
    internal static class KantLab
    {
        private const double Diameter = 2.4;
        private const double Scale = 9;
        private const double PadMm = 4;

        public static void Render(string repoRoot)
        {
            string outDir = Path.Combine(repoRoot, "out", "preview");
            Directory.CreateDirectory(outDir);

            var cases = new (string Title, Curve Shape, double Gap)[]
            {
                ("Квадрат 40 мм, зазор 0", Square(40), 0),
                ("Квадрат 40 мм, зазор 0,2", Square(40), 0.2),
                ("Звезда, зазор 0", Star(), 0),
            };

            var sb = new StringBuilder();
            double cellW = 56 * Scale;
            double cellH = 56 * Scale + 40;
            sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{N(cellW * cases.Length)}\" height=\"{N(cellH + 26)}\" font-family=\"Segoe UI, Arial, sans-serif\">");
            sb.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"white\"/>");

            Console.WriteLine("Кант (3 ряда по краю):");
            for (int i = 0; i < cases.Length; i++)
            {
                (string title, Curve shape, double gap) = cases[i];
                List<PlacedStone> stones = Run(shape, gap);
                (int holes, double worst) = Holes(stones, Diameter + gap);

                double left = i * cellW;
                sb.AppendLine($"<text x=\"{N(left + cellW / 2)}\" y=\"20\" text-anchor=\"middle\" font-size=\"15\" font-weight=\"600\" fill=\"#222\">{title}</text>");
                sb.AppendLine($"<g transform=\"translate({N(left + PadMm * Scale)},{N(26 + PadMm * Scale)})\">");

                foreach (PlacedStone s in stones)
                {
                    sb.AppendLine($"<circle cx=\"{N(s.Center.X * Scale)}\" cy=\"{N(s.Center.Y * Scale)}\" r=\"{N(s.DiameterMm / 2 * Scale)}\" fill=\"#43A047\" fill-opacity=\"0.75\" stroke=\"#333\" stroke-width=\"0.7\"/>");
                }

                sb.AppendLine("</g>");
                string note = $"{stones.Count} страз, пропусков {holes}, самый одинокий камень — сосед в {worst:0.00} мм";
                sb.AppendLine($"<text x=\"{N(left + cellW / 2)}\" y=\"{N(cellH + 16)}\" text-anchor=\"middle\" font-size=\"12\" fill=\"#444\">{note}</text>");
                Console.WriteLine($"  {title,-26} — {note}");
            }

            Diagnose("квадрат", Square(40));
            Diagnose("звезда", Star());

            sb.AppendLine("</svg>");
            string outPath = Path.Combine(outDir, "kant.svg");
            File.WriteAllText(outPath, sb.ToString());
            Console.WriteLine("SVG: " + outPath);
        }

        /// <summary>
        /// Почему кольцо строится тем или иным способом: куда уходит смещение контура и насколько
        /// точно его точки ложатся на нужную глубину (по тому же полю расстояний, что и в ядре).
        /// </summary>
        private static void Diagnose(string name, Curve shape)
        {
            FlattenedCurve flat = CurveFlattener.Flatten(shape, 0.02);
            SignedDistanceField field = SignedDistanceField.Build(new[] { flat }, Diameter / 8.0);

            double MeanDepth(double sign)
            {
                List<Point2D> probe = CurveOffsetter.Offset(flat, sign * 0.2, 0.02, roundOuterCorners: false);
                if (probe.Count == 0)
                {
                    return double.NaN;
                }

                double sum = 0;
                foreach (Point2D p in probe)
                {
                    sum += field.ValueAt(p);
                }

                return sum / probe.Count;
            }

            double plus = MeanDepth(1);
            double minus = MeanDepth(-1);
            double inward = plus > minus ? 1 : -1;
            double level = Diameter / 2;

            List<Point2D> loop = CurveOffsetter.Offset(flat, inward * level, 0.02, roundOuterCorners: false);
            double worst = 0;
            foreach (Point2D p in loop)
            {
                worst = Math.Max(worst, Math.Abs(field.ValueAt(p) - level));
            }

            Console.WriteLine($"  {name}: проба внутрь {plus:0.000}, наружу {minus:0.000}; точек смещения {loop.Count}, " +
                $"худшее отклонение глубины {worst:0.000} мм (допуск {field.CellSizeMm + Diameter * 0.1:0.000})");
        }

        private static List<PlacedStone> Run(Curve shape, double gap) =>
            ContourFiller.Fill(new[] { shape }, new ContourFillOptions
            {
                StoneDiameterMm = Diameter,
                GapMm = gap,
                MaxRings = 3,
                FillCenter = false,
            });

        /// <summary>Пропуск — страза, у которой ближайшая соседка дальше полутора шагов ряда.</summary>
        private static (int Holes, double Worst) Holes(IReadOnlyList<PlacedStone> stones, double step)
        {
            int holes = 0;
            double worst = 0;

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

                worst = Math.Max(worst, nearest);
                if (nearest > step * 1.5)
                {
                    holes++;
                }
            }

            return (holes, worst);
        }

        private static Curve Square(double side) => Curve.FromPolyline(
            new[] { new Point2D(0, 0), new Point2D(side, 0), new Point2D(side, side), new Point2D(0, side) },
            isClosed: true);

        private static Curve Star()
        {
            var pts = new List<Point2D>();
            for (int i = 0; i < 10; i++)
            {
                double a = Math.PI / 2 + i * Math.PI / 5;
                double r = i % 2 == 0 ? 24 : 10;
                pts.Add(new Point2D(24 + r * Math.Cos(a), 24 - r * Math.Sin(a)));
            }

            return Curve.FromPolyline(pts, isClosed: true);
        }

        private static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
