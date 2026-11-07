using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Strassio.Core.Geometry;
using Strassio.Core.Methods;
using Strassio.Core.Motifs;
using Strassio.Core.Placement;
using Strassio.Core.Vector;

namespace Strassio.Preview
{
    /// <summary>
    /// Монограммы: буквы из JSON шрифта (пути после имени сценария; без них — печатные буквы из прямоугольников)
    /// в каждой рамке. Буквы залиты камнем ss10, рамка — один ряд ss16. Картинки — в out/preview/monogram/.
    /// </summary>
    internal static class MonogramLab
    {
        private const double LetterD = 2.8, FrameD = 3.9, Gap = 0.3;

        private static readonly Dictionary<string, double> Table = new Dictionary<string, double>
        {
            ["ss6"] = 2.0, ["ss10"] = LetterD, ["ss16"] = FrameD,
        };

        public static void Render(string repoRoot, IReadOnlyList<string> glyphFiles)
        {
            string dir = Path.Combine(repoRoot, "out", "preview", "monogram");
            Directory.CreateDirectory(dir);

            var sets = new List<(string Name, List<IReadOnlyList<Curve>> Letters)>();
            foreach (string file in glyphFiles)
            {
                sets.Add((Path.GetFileNameWithoutExtension(file), StrokeLab.LoadGlyphs(file).Select(g => (IReadOnlyList<Curve>)g.Contours).ToList()));
            }

            if (sets.Count == 0)
            {
                sets.Add(("block", BlockLetters()));
            }

            foreach ((string name, List<IReadOnlyList<Curve>> letters) in sets)
            {
                foreach (MonogramFrame frame in Enum.GetValues(typeof(MonogramFrame)))
                {
                    var options = new MonogramOptions
                    {
                        Frame = frame,
                        LetterHeightMm = 30,
                        LetterGapMm = 2,
                        FrameGapMm = FrameD / 2 + LetterD + Gap,
                    };
                    MonogramLayout m = Monogram.Arrange(letters, options, Point2D.Zero);
                    var user = new MethodParameters { GapMm = Gap };
                    List<Curve> contours = m.Letters.SelectMany(l => l).ToList();
                    IReadOnlyList<PlacedStone> letterStones = MethodRunner.Run(
                        TextCheck.MethodFor(TextMode.Fill), contours, LetterD, TextCheck.ParametersFor(TextMode.Fill, user), Table).Stones;
                    IReadOnlyList<PlacedStone> frameStones = m.Frame == null
                        ? Array.Empty<PlacedStone>()
                        : MethodRunner.Run(MethodKind.L1, new[] { m.Frame }, FrameD, user, Table).Stones;

                    string file = $"{name}-{frame.ToString().ToLowerInvariant()}.svg";
                    File.WriteAllText(Path.Combine(dir, file), Svg(contours, m.Frame, letterStones, frameStones));
                    double closest = Closest(letterStones, frameStones);
                    Console.WriteLine(
                        $"{file,-24} {m.WidthMm,5:0.0} × {m.HeightMm,5:0.0} мм; буквы {letterStones.Count,4}, рамка {frameStones.Count,3} страз; " +
                        $"зазор буквы–рамка {(double.IsInfinity(closest) ? "—" : closest.ToString("0.00", CultureInfo.InvariantCulture))} мм");
                }
            }

            Console.WriteLine($"SVG сохранены: {dir}");
        }

        /// <summary>Самый маленький зазор между камнем буквы и камнем рамки (край к краю).</summary>
        private static double Closest(IReadOnlyList<PlacedStone> a, IReadOnlyList<PlacedStone> b)
        {
            double best = double.PositiveInfinity;
            foreach (PlacedStone s in a)
            {
                foreach (PlacedStone t in b)
                {
                    best = Math.Min(best, Point2D.Distance(s.Center, t.Center) - (s.DiameterMm + t.DiameterMm) / 2);
                }
            }

            return best;
        }

        /// <summary>«Н», «О», «Т» из прямоугольников — когда шрифт не передан.</summary>
        private static List<IReadOnlyList<Curve>> BlockLetters()
        {
            Curve R(double x, double y, double w, double h) => Curve.FromPolyline(
                new[] { new Point2D(x, y), new Point2D(x + w, y), new Point2D(x + w, y + h), new Point2D(x, y + h) }, true);
            Curve P(params double[] xy) => Curve.FromPolyline(
                Enumerable.Range(0, xy.Length / 2).Select(i => new Point2D(xy[2 * i], xy[2 * i + 1])).ToList(), true);
            return new List<IReadOnlyList<Curve>>
            {
                new[] { P(0, 0, 6, 0, 6, 12, 14, 12, 14, 0, 20, 0, 20, 30, 14, 30, 14, 18, 6, 18, 6, 30, 0, 30) },
                new[] { R(0, 0, 22, 30), R(6, 6, 10, 18) },
                new[] { P(7, 0, 13, 0, 13, 24, 20, 24, 20, 30, 0, 30, 0, 24, 7, 24) },
            };
        }

        private static string Svg(IReadOnlyList<Curve> letters, Curve? frame, IReadOnlyList<PlacedStone> letterStones, IReadOnlyList<PlacedStone> frameStones)
        {
            List<List<Point2D>> outlines = letters.Select(c => CurveFlattener.Flatten(c).Points.Select(p => p.Position).ToList()).ToList();
            List<Point2D> frameLine = frame == null ? new List<Point2D>() : CurveFlattener.Flatten(frame).Points.Select(p => p.Position).ToList();
            IEnumerable<Point2D> all = outlines.SelectMany(o => o).Concat(frameLine);
            double pad = FrameD;
            double minX = all.Min(p => p.X) - pad, maxX = all.Max(p => p.X) + pad, minY = all.Min(p => p.Y) - pad, maxY = all.Max(p => p.Y) + pad;
            const double k = 8;
            var ci = CultureInfo.InvariantCulture;
            string X(double x) => ((x - minX) * k).ToString("0.#", ci);
            string Y(double y) => ((maxY - y) * k).ToString("0.#", ci);

            var sb = new StringBuilder();
            sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{X(maxX)}\" height=\"{Y(minY)}\">");
            sb.Append("<rect width=\"100%\" height=\"100%\" fill=\"white\"/>");
            sb.Append("<path fill=\"#F3F1EC\" fill-rule=\"evenodd\" stroke=\"#9E9E9E\" stroke-width=\"1\" d=\"");
            foreach (List<Point2D> o in outlines)
            {
                sb.Append('M').Append(string.Join(" L", o.Select(p => X(p.X) + "," + Y(p.Y)))).Append('Z');
            }

            sb.Append("\"/>");
            if (frameLine.Count > 1)
            {
                sb.Append("<path fill=\"none\" stroke=\"#BDBDBD\" stroke-width=\"1\" d=\"M")
                  .Append(string.Join(" L", frameLine.Select(p => X(p.X) + "," + Y(p.Y)))).Append("Z\"/>");
            }

            void Stones(IEnumerable<PlacedStone> stones, string fill)
            {
                foreach (PlacedStone s in stones)
                {
                    sb.Append($"<circle cx=\"{X(s.Center.X)}\" cy=\"{Y(s.Center.Y)}\" r=\"{(s.DiameterMm / 2 * k).ToString("0.#", ci)}\" fill=\"{fill}\" stroke=\"#37474F\" stroke-width=\"0.7\"/>");
                }
            }

            Stones(letterStones, "#E91E63");
            Stones(frameStones, "#FFC107");
            sb.Append("</svg>");
            return sb.ToString();
        }
    }
}
