using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Strassio.Core.Geometry;
using Strassio.Core.Methods;
using Strassio.Core.Placement;

namespace Strassio.Preview
{
    /// <summary>
    /// Замечание автора со скриншота: «наружная линия — углы не получаются, надо подгонять углы,
    /// чтобы не было пусто». Рамка, метод «вокруг линии» (3 ряда, шахматный сдвиг, зазор 0) —
    /// то же, что у автора. Рисуем один и тот же случай при трёх значениях выбора «Углы».
    /// </summary>
    internal static class RowCornerLab
    {
        private const double Diameter = 2.4;
        private const double Scale = 9;          // пикселей на миллиметр
        private const double PadMm = 8;

        private static readonly (string Title, string Corners)[] Columns =
        {
            ("Смешанно", MethodChoices.CornersMixed),
            ("Острые", MethodChoices.CornersSharp),
            ("Круглые", MethodChoices.CornersRound),
        };

        public static void Render(string repoRoot)
        {
            string outDir = Path.Combine(repoRoot, "out", "preview");
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, "rows.svg");

            // Рамка 60 × 44 мм — как на скриншоте автора.
            var rect = Curve.FromPolyline(
                new[] { new Point2D(0, 0), new Point2D(60, 0), new Point2D(60, 44), new Point2D(0, 44) },
                isClosed: true);

            double cellW = (60 + 2 * PadMm) * Scale;
            double cellH = (44 + 2 * PadMm) * Scale + 34;
            var sb = new StringBuilder();
            sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{N(cellW * Columns.Length)}\" height=\"{N(cellH + 26)}\" font-family=\"Segoe UI, Arial, sans-serif\">");
            sb.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"white\"/>");

            for (int c = 0; c < Columns.Length; c++)
            {
                IReadOnlyList<PlacedStone> stones = Run(rect, Columns[c].Corners);
                double left = c * cellW;

                sb.AppendLine($"<text x=\"{N(left + cellW / 2)}\" y=\"20\" text-anchor=\"middle\" font-size=\"15\" font-weight=\"600\" fill=\"#222\">Углы: {Columns[c].Title}</text>");
                sb.AppendLine($"<g transform=\"translate({N(left + PadMm * Scale)},{N(26 + PadMm * Scale)})\">");
                sb.AppendLine("<rect x=\"0\" y=\"0\" width=\"" + N(60 * Scale) + "\" height=\"" + N(44 * Scale) + "\" fill=\"none\" stroke=\"#dddddd\" stroke-dasharray=\"4 3\"/>");

                foreach (PlacedStone s in stones)
                {
                    string fill = s.IsCorner ? "#E53935" : "#43A047";
                    sb.AppendLine($"<circle cx=\"{N(s.Center.X * Scale)}\" cy=\"{N(s.Center.Y * Scale)}\" r=\"{N(s.DiameterMm / 2 * Scale)}\" fill=\"{fill}\" fill-opacity=\"0.75\" stroke=\"#333\" stroke-width=\"0.7\"/>");
                }

                sb.AppendLine("</g>");
                sb.AppendLine($"<text x=\"{N(left + cellW / 2)}\" y=\"{N(cellH + 18)}\" text-anchor=\"middle\" font-size=\"12\" fill=\"#444\">{Report(stones)}</text>");
            }

            sb.AppendLine("</svg>");
            File.WriteAllText(outPath, sb.ToString());

            Console.WriteLine("Углы рядов: " + outPath);
            foreach ((string title, string corners) in Columns)
            {
                Console.WriteLine($"  {title,-10} — {Report(Run(rect, corners))}");
            }
        }

        private static IReadOnlyList<PlacedStone> Run(Curve rect, string corners)
        {
            var p = new MethodParameters
            {
                GapMm = 0,
                RowCount = 3,
                RowGapMm = 0.2,
                RowSide = MethodChoices.SideBoth,
                Stagger = true,
                Corners = corners,
                CornerAngleDeg = 30,
            };

            return MethodRunner.Run(MethodKind.L2, new[] { rect }, Diameter, p, null).Stones;
        }

        /// <summary>Сколько страз и какая самая большая дыра между соседями — по ней видно «пусто» в углах.</summary>
        private static string Report(IReadOnlyList<PlacedStone> stones)
        {
            double worst = 0;
            for (int i = 0; i < stones.Count; i++)
            {
                double nearest = double.MaxValue;
                for (int j = 0; j < stones.Count; j++)
                {
                    if (i != j)
                    {
                        double edge = Point2D.Distance(stones[i].Center, stones[j].Center)
                            - (stones[i].DiameterMm + stones[j].DiameterMm) / 2;
                        nearest = Math.Min(nearest, edge);
                    }
                }

                if (nearest != double.MaxValue)
                {
                    worst = Math.Max(worst, nearest);
                }
            }

            return $"{stones.Count} страз, самый одинокий камень — сосед в {worst:0.00} мм";
        }

        private static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
