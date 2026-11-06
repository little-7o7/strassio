using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Strassio.Core.Geometry;
using Strassio.Core.Motifs;

namespace Strassio.Preview
{
    /// <summary>
    /// Мотивы (раздел 9): цветы и листики по волне, розетка на круге, рамка с отдельными углами, рамка-сердце
    /// и мандала (повтор выделенных страз вокруг центра). Картинки — в out/preview/motif/.
    /// </summary>
    internal static class MotifLab
    {
        private const double Gap = 0.2;

        public static void Render(string repoRoot)
        {
            string dir = Path.Combine(repoRoot, "out", "preview", "motif");
            Directory.CreateDirectory(dir);

            Motif flower = Flower();
            Motif leaf = Leaf();
            Motif drop = Drop();

            // 1. Волна: цветок и листик по очереди, мотивы поворачиваются по линии.
            Curve wave = Polyline(Enumerable.Range(0, 241).Select(i => new Point2D(i * 0.5, 9 * Math.Sin(i * 0.5 * 2 * Math.PI / 60))), false);
            Write(dir, "wave", new[] { wave }, MotifPlacer.AlongLine(wave, new[] { flower, leaf }, new MotifLayoutOptions { SpacingMm = 1.2 }));

            // 2. Розетка: капли «верхом» наружу на большом круге и цветы на малом.
            Curve outer = Circle(0, 0, 17), inner = Circle(0, 0, 8.6);
            MotifLayout petals = MotifPlacer.AlongLine(outer, new[] { drop }, new MotifLayoutOptions { Count = 16 });
            MotifLayout middle = MotifPlacer.AlongLine(inner, new[] { flower }, new MotifLayoutOptions { Count = 5 });
            Write(dir, "rosette", new[] { outer, inner }, petals, middle, Single(new MotifStone(0, 0, 3.9, "ss16", "", "#FFD54F")));

            // 3. Рамка: цветы в углах, листики по сторонам.
            Curve rect = Polyline(new[] { new Point2D(0, 0), new Point2D(90, 0), new Point2D(90, 55), new Point2D(0, 55) }, true);
            Write(dir, "frame", new[] { rect }, MotifPlacer.Frame(rect, new[] { leaf }, flower, new MotifLayoutOptions { SpacingMm = 0.8 }));

            // 4. Сердце рамкой: острый низ и ямка сверху — углы.
            Curve heart = Heart();
            Write(dir, "heart-frame", new[] { heart }, MotifPlacer.Frame(heart, new[] { drop }, flower, new MotifLayoutOptions { SpacingMm = 0.6 }));

            // 5. Мандала: один завиток из пяти страз — и «Повторить по кругу» 10 раз.
            var arm = new List<(Point2D P, double D, string Rgb)>
            {
                (Polar(5.2, 0), 2.0, "#F8BBD0"), (Polar(7.6, 0.16), 2.4, "#F48FB1"), (Polar(10.2, 0.30), 2.8, "#EC407A"),
                (Polar(12.9, 0.42), 2.8, "#AD1457"), (Polar(15.4, 0.50), 2.0, "#880E4F"),
            };
            List<RotatedCopy> copies = MotifPlacer.Around(arm.Select(a => a.P).ToList(), arm.Select(a => a.D).ToList(), Point2D.Zero, 10, Gap);
            var mandala = arm.Select(a => (a.P, a.D, a.Rgb)).Concat(copies.Select(c => (c.Center, arm[c.Source].D, arm[c.Source].Rgb))).ToList();
            mandala.Add((Point2D.Zero, 3.9, "#FFD54F"));
            File.WriteAllText(Path.Combine(dir, "mandala.svg"), Svg(Array.Empty<Curve>(), mandala));
            Console.WriteLine($"mandala      {mandala.Count,4} страз (завиток из {arm.Count}, копий {copies.Count})");

            Console.WriteLine($"SVG сохранены: {dir}");
        }

        /// <summary>Цветок: жёлтая серединка ss12 и шесть розовых лепестков.</summary>
        internal static Motif Flower()
        {
            const double d = 2.8;
            var stones = new List<MotifStone> { new(0, 0, d, "ss12", "Жёлтый", "#FFD54F") };
            for (int i = 0; i < 6; i++)
            {
                double a = Math.PI / 3 * i + Math.PI / 6;
                stones.Add(new MotifStone((d + Gap) * Math.Cos(a), (d + Gap) * Math.Sin(a), d, "ss12", "Розовый", "#F48FB1"));
            }

            return Motif.FromStones("Цветок", stones);
        }

        /// <summary>Листик вдоль линии: светлые кончики и зелёное тело.</summary>
        internal static Motif Leaf() => Motif.FromStones("Листик", new[]
        {
            new MotifStone(-4.0, 0, 1.8, "ss6", "Салатовый", "#AED581"),
            new MotifStone(-1.3, 1.35, 2.4, "ss10", "Зелёный", "#43A047"),
            new MotifStone(-1.3, -1.35, 2.4, "ss10", "Зелёный", "#43A047"),
            new MotifStone(1.3, 1.35, 2.4, "ss10", "Зелёный", "#43A047"),
            new MotifStone(1.3, -1.35, 2.4, "ss10", "Зелёный", "#43A047"),
            new MotifStone(4.0, 0, 1.8, "ss6", "Салатовый", "#AED581"),
        });

        /// <summary>Капля «вверх»: крупный тёмный камень внизу, к верху мельче и светлее.</summary>
        internal static Motif Drop() => Motif.FromStones("Капля", new[]
        {
            new MotifStone(0, 0, 3.0, "ss12", "Рубин", "#C62828"),
            new MotifStone(0, 3.0, 2.4, "ss10", "Розовый", "#EF5350"),
            new MotifStone(0, 5.5, 1.8, "ss6", "Светло-розовый", "#FFCDD2"),
        });

        private static (Point2D, double, string) Single(MotifStone s) => (new Point2D(s.X, s.Y), s.DiameterMm, s.Rgb);

        private static void Write(string dir, string name, IReadOnlyList<Curve> contours, params object[] parts)
        {
            var stones = new List<(Point2D, double, string)>();
            int copies = 0, dropped = 0;
            foreach (object part in parts)
            {
                if (part is MotifLayout layout)
                {
                    stones.AddRange(layout.Stones.Select(s => (s.Center, s.Source.DiameterMm, s.Source.Rgb)));
                    copies += layout.Copies;
                    dropped += layout.Dropped;
                }
                else if (part is ValueTuple<Point2D, double, string> one)
                {
                    stones.Add(one);
                }
            }

            File.WriteAllText(Path.Combine(dir, name + ".svg"), Svg(contours, stones));
            Console.WriteLine($"{name,-12} {stones.Count,4} страз, мотивов {copies,3}, пропущено камней {dropped}");
        }

        private static Point2D Polar(double r, double a) => new Point2D(r * Math.Cos(a), r * Math.Sin(a));

        private static Curve Polyline(IEnumerable<Point2D> points, bool closed) => Curve.FromPolyline(points.ToList(), closed);

        private static Curve Circle(double cx, double cy, double r) =>
            Polyline(Enumerable.Range(0, 160).Select(i => new Point2D(cx + r * Math.Cos(2 * Math.PI * i / 160), cy + r * Math.Sin(2 * Math.PI * i / 160))), true);

        /// <summary>Классическое сердце: острый низ и ямка сверху — это углы рамки.</summary>
        private static Curve Heart()
        {
            var pts = new List<Point2D>();
            for (int i = 0; i < 200; i++)
            {
                double t = 2 * Math.PI * i / 200;
                double x = 16 * Math.Pow(Math.Sin(t), 3);
                double y = 13 * Math.Cos(t) - 5 * Math.Cos(2 * t) - 2 * Math.Cos(3 * t) - Math.Cos(4 * t);
                pts.Add(new Point2D(x * 1.8, y * 1.8));
            }

            return Curve.FromPolyline(pts, true);
        }

        private static string Svg(IReadOnlyList<Curve> contours, IReadOnlyList<(Point2D P, double D, string Rgb)> stones)
        {
            List<List<Point2D>> outlines = contours.Select(c => CurveFlattener.Flatten(c).Points.Select(p => p.Position).ToList()).ToList();
            IEnumerable<Point2D> all = outlines.SelectMany(o => o).Concat(stones.SelectMany(s => new[]
            {
                s.P + new Point2D(s.D / 2, s.D / 2), s.P - new Point2D(s.D / 2, s.D / 2),
            }));
            double minX = all.Min(p => p.X) - 3, maxX = all.Max(p => p.X) + 3, minY = all.Min(p => p.Y) - 3, maxY = all.Max(p => p.Y) + 3;
            const double k = 10;
            CultureInfo ci = CultureInfo.InvariantCulture;
            string X(double x) => ((x - minX) * k).ToString("0.#", ci);
            string Y(double y) => ((maxY - y) * k).ToString("0.#", ci);

            var sb = new StringBuilder();
            sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{X(maxX)}\" height=\"{Y(minY)}\">");
            sb.Append("<rect width=\"100%\" height=\"100%\" fill=\"white\"/>");
            foreach (List<Point2D> o in outlines)
            {
                sb.Append("<path fill=\"none\" stroke=\"#BDBDBD\" stroke-width=\"1\" stroke-dasharray=\"6 4\" d=\"M")
                    .Append(string.Join(" L", o.Select(p => X(p.X) + "," + Y(p.Y)))).Append("\"/>");
            }

            foreach ((Point2D p, double d, string rgb) in stones)
            {
                sb.Append($"<circle cx=\"{X(p.X)}\" cy=\"{Y(p.Y)}\" r=\"{(d / 2 * k).ToString("0.#", ci)}\" fill=\"{rgb}\" stroke=\"#37474F\" stroke-width=\"0.8\"/>");
            }

            sb.Append("</svg>");
            return sb.ToString();
        }
    }
}
