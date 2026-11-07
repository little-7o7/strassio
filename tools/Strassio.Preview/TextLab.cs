using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Strassio.Core.Geometry;
using Strassio.Core.Methods;
using Strassio.Core.Placement;
using Strassio.Core.Vector;

namespace Strassio.Preview
{
    /// <summary>
    /// «Надпись стразами»: три способа (обвести, по середине, залить) на одной надписи и отчёт проверки —
    /// тонкие места и тесные пары букв помечены красными кругами. Надпись — печатные буквы «TOAK» или
    /// буквы из JSON шрифта (пути после имени сценария). Картинки — в out/preview/text/.
    /// </summary>
    internal static class TextLab
    {
        private static readonly Dictionary<string, double> Table = new Dictionary<string, double>
        {
            ["ss6"] = 2.0, ["ss10"] = 2.8, ["ss16"] = 3.9,
        };

        public static void Render(string repoRoot, IReadOnlyList<string> glyphFiles)
        {
            string dir = Path.Combine(repoRoot, "out", "preview", "text");
            Directory.CreateDirectory(dir);

            var texts = new List<(string Name, Curve[] Contours)> { ("toak", BlockText()) };
            texts.AddRange(glyphFiles.Select(f => (Path.GetFileNameWithoutExtension(f), StrokeLab.LoadGlyphs(f).SelectMany(g => g.Contours).ToArray())));

            foreach ((string name, Curve[] contours) in texts)
            {
                foreach (TextMode mode in new[] { TextMode.Outline, TextMode.Middle, TextMode.Fill })
                {
                    var user = new MethodParameters { GapMm = 0.3, ThinSize = "ss6" };
                    TextReport report = TextCheck.Analyze(contours, mode, 2.8, 2.0, user.GapMm, user.EdgeMarginMm);
                    IReadOnlyList<PlacedStone> stones = MethodRunner.Run(
                        TextCheck.MethodFor(mode), contours, 2.8, TextCheck.ParametersFor(mode, user), Table).Stones;
                    string file = $"{name}-{mode.ToString().ToLowerInvariant()}.svg";
                    File.WriteAllText(Path.Combine(dir, file), StrokeLab.Svg(contours, stones, report.ThinSpots.Concat(report.CloseSpots).ToList()));
                    Console.WriteLine(
                        $"{file,-22} {stones.Count,4} страз; букв {report.LetterCount}, штрих {report.StrokeMm:0.0} мм (нужно {report.NeededMm:0.0}" +
                        $"{(report.IsThin ? $", увеличить в {report.Scale:0.00} раза" : string.Empty)}), тонких мест {report.ThinSpots.Count}, " +
                        $"ближе всего буквы {report.ClosestMm:0.0} мм{(report.IsTight ? $", раздвинуть на {report.ExtraSpacingMm:0.0}" : string.Empty)}, налезают {report.Overlaps}");
                }
            }

            Console.WriteLine($"SVG сохранены: {dir}");
        }

        /// <summary>Печатные «TOAK» высотой 30 мм, штрих 5 мм, буквы на разном расстоянии (у «A» и «K» почти вплотную).</summary>
        private static Curve[] BlockText()
        {
            Curve P(double dx, params double[] xy) => Curve.FromPolyline(
                Enumerable.Range(0, xy.Length / 2).Select(i => new Point2D(xy[2 * i] + dx, xy[2 * i + 1])).ToList(), true);
            Curve Ring(double cx, double cy, double rx, double ry)
            {
                var pts = new List<Point2D>();
                for (int i = 0; i < 96; i++)
                {
                    double a = 2 * Math.PI * i / 96;
                    pts.Add(new Point2D(cx + rx * Math.Cos(a), cy + ry * Math.Sin(a)));
                }

                return Curve.FromPolyline(pts, true);
            }

            return new[]
            {
                P(0, 0, 25, 9.5, 25, 9.5, 0, 14.5, 0, 14.5, 25, 0, 25, 0, 30, 24, 30, 24, 25, 0, 25),
                Ring(39, 15, 11, 15), Ring(39, 15, 6, 10),
                P(52, 0, 0, 5, 0, 6.6, 6, 17.4, 6, 19, 0, 24, 0, 15.5, 30, 8.5, 30),
                P(52, 8.2, 11, 15.8, 11, 12, 24),
                P(76.5, 0, 0, 5, 0, 5, 12, 14, 0, 20, 0, 9, 15, 20, 30, 14, 30, 5, 18, 5, 30, 0, 30),
            };
        }
    }
}
