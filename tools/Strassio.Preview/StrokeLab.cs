using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Strassio.Core.Geometry;
using Strassio.Core.Placement;

namespace Strassio.Preview
{
    /// <summary>
    /// «По толщине штриха» (F13): мазки кистью, полумесяц, «широкое перо» и печатные буквы. Мелкие камни —
    /// голубые, средние — зелёные, крупные — фиолетовые. Картинки — в out/preview/stroke/.
    /// Можно добавить настоящие буквы из шрифта: JSON {"letters":[{"char":"L","contours":[[[x,y],…],…]}]}
    /// в мм — пути к файлам после имени сценария.
    /// </summary>
    internal static class StrokeLab
    {
        private static readonly double[] Sizes = { 2.0, 2.8, 3.9 };
        private static readonly string[] Colors = { "#81D4FA", "#66BB6A", "#7E57C2" };

        public static void Render(string repoRoot, IReadOnlyList<string> glyphFiles)
        {
            string dir = Path.Combine(repoRoot, "out", "preview", "stroke");
            Directory.CreateDirectory(dir);

            var shapes = new List<(string Name, Curve[] Contours)>
            {
                ("brush-s", new[] { Brush(t => new Point2D(40 * t, 8 * Math.Sin(2 * Math.PI * t)), t => 0.4 + 2.4 * Math.Sin(Math.PI * t)) }),
                ("crescent", new[] { Crescent() }),
                ("broad-nib-c", new[] { BroadNibArc() }),
                ("block-t", new[] { Polygon(0, 36, 13, 36, 13, 0, 17, 0, 17, 36, 30, 36, 30, 40, 0, 40) }),
                ("block-o", new[] { Ellipse(15, 20, 15, 20), Ellipse(15, 20, 11.2, 16.2) }),
            };

            foreach (string file in glyphFiles)
            {
                using JsonDocument json = JsonDocument.Parse(File.ReadAllText(file));
                int index = 0;
                foreach (JsonElement letter in json.RootElement.GetProperty("letters").EnumerateArray())
                {
                    Curve[] contours = letter.GetProperty("contours").EnumerateArray()
                        .Select(c => Curve.FromPolyline(c.EnumerateArray().Select(p => new Point2D(p[0].GetDouble(), p[1].GetDouble())).ToList(), true))
                        .ToArray();
                    shapes.Add(($"{Path.GetFileNameWithoutExtension(file)}-{index++}", contours));
                }
            }

            foreach ((string name, Curve[] contours) in shapes)
            {
                var watch = Stopwatch.StartNew();
                List<PlacedStone> stones = StrokeRow.Place(contours, Sizes, 0.3, 0);
                long ms = watch.ElapsedMilliseconds;
                File.WriteAllText(Path.Combine(dir, name + ".svg"), Svg(contours, stones));
                string counts = string.Join(", ", Sizes.Select(s => $"{s:0.0}×{stones.Count(x => x.DiameterMm == s)}"));
                Console.WriteLine($"{name,-16} {stones.Count,4} страз ({counts}), {ms} мс");
            }

            Console.WriteLine($"SVG сохранены: {dir}");
        }

        /// <summary>Мазок кистью вдоль линии <paramref name="path"/> с полутолщиной <paramref name="half"/> (t от 0 до 1).</summary>
        private static Curve Brush(Func<double, Point2D> path, Func<double, double> half)
        {
            const int n = 120;
            var left = new List<Point2D>();
            var right = new List<Point2D>();
            for (int i = 0; i <= n; i++)
            {
                double t = (double)i / n;
                Point2D p = path(t);
                Point2D tangent = (path(Math.Min(1, t + 1e-3)) - path(Math.Max(0, t - 1e-3))).Normalized();
                var normal = new Point2D(-tangent.Y, tangent.X);
                left.Add(p + normal * half(t));
                right.Add(p - normal * half(t));
            }

            right.Reverse();
            return Curve.FromPolyline(left.Concat(right.Skip(1).Take(right.Count - 2)).ToList(), true);
        }

        /// <summary>Полумесяц: внешняя дуга круга минус сдвинутый круг — острые рожки, толстая середина.</summary>
        private static Curve Crescent()
        {
            var pts = new List<Point2D>();
            const double r = 18, r2 = 15.5, shift = 5;
            double cut = Math.Acos((r * r + shift * shift - r2 * r2) / (2 * r * shift));
            for (int i = 0; i <= 80; i++)
            {
                double a = cut + (2 * Math.PI - 2 * cut) * i / 80;
                pts.Add(new Point2D(r * Math.Cos(a), r * Math.Sin(a)));
            }

            Point2D tip1 = pts[0], tip2 = pts[pts.Count - 1];
            double b1 = Math.Atan2(tip2.Y, tip2.X - shift), b0 = Math.Atan2(tip1.Y, tip1.X - shift);
            if (b1 < b0)
            {
                b1 += 2 * Math.PI;
            }

            for (int i = 79; i >= 1; i--)
            {
                double a = b0 + (b1 - b0) * i / 80;
                pts.Add(new Point2D(shift + r2 * Math.Cos(a), r2 * Math.Sin(a)));
            }

            return Curve.FromPolyline(pts, true);
        }

        /// <summary>Буква «С» широким пером под 45°: толщина зависит от направления штриха.</summary>
        private static Curve BroadNibArc() => Brush(
            t =>
            {
                double a = Math.PI * (0.25 + 1.5 * t);
                return new Point2D(15 * Math.Cos(a), 18 * Math.Sin(a));
            },
            t =>
            {
                double a = Math.PI * (0.25 + 1.5 * t);
                var dir = new Point2D(-15 * Math.Sin(a), 18 * Math.Cos(a)).Normalized();
                double across = Math.Abs(dir.X * Math.Cos(Math.PI / 4) - dir.Y * Math.Sin(Math.PI / 4));
                return 0.6 + 2.4 * across;
            });

        private static Curve Polygon(params double[] xy) => Curve.FromPolyline(
            Enumerable.Range(0, xy.Length / 2).Select(i => new Point2D(xy[2 * i], xy[2 * i + 1])).ToList(), true);

        private static Curve Ellipse(double cx, double cy, double rx, double ry)
        {
            var pts = new List<Point2D>();
            for (int i = 0; i < 120; i++)
            {
                double a = 2 * Math.PI * i / 120;
                pts.Add(new Point2D(cx + rx * Math.Cos(a), cy + ry * Math.Sin(a)));
            }

            return Curve.FromPolyline(pts, true);
        }

        private static string Svg(IReadOnlyList<Curve> contours, IReadOnlyList<PlacedStone> stones)
        {
            List<List<Point2D>> outlines = contours.Select(c => CurveFlattener.Flatten(c).Points.Select(p => p.Position).ToList()).ToList();
            IEnumerable<Point2D> all = outlines.SelectMany(o => o);
            double minX = all.Min(p => p.X) - 3, maxX = all.Max(p => p.X) + 3, minY = all.Min(p => p.Y) - 3, maxY = all.Max(p => p.Y) + 3;
            const double k = 12;
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
            foreach (PlacedStone s in stones)
            {
                int size = Array.FindIndex(Sizes, v => Math.Abs(v - s.DiameterMm) < 1e-6);
                string fill = Colors[Math.Max(0, size)];
                sb.Append($"<circle cx=\"{X(s.Center.X)}\" cy=\"{Y(s.Center.Y)}\" r=\"{(s.DiameterMm / 2 * k).ToString("0.#", ci)}\" fill=\"{fill}\" stroke=\"#37474F\" stroke-width=\"0.8\"/>");
            }

            sb.Append("</svg>");
            return sb.ToString();
        }
    }
}
