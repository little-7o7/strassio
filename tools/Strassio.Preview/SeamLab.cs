using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Strassio.Core.Geometry;
using Strassio.Core.Methods;
using Strassio.Core.Placement;

namespace Strassio.Preview
{
    /// <summary>
    /// Шов в контурной заливке (F3/F4, узор «вдоль формы»): где ряды вдоль формы упираются в крайнее кольцо,
    /// остаются дырки и тесные места. Считаем «дырки» — места внутри кольца, куда между камнями влезает
    /// круг больше 0,6 камня, — и рисуем, дырки красным. Картинки — в out/preview/seam/.
    /// </summary>
    internal static class SeamLab
    {
        private const double D = 2.4;

        public static void Render(string repoRoot)
        {
            string dir = Path.Combine(repoRoot, "out", "preview", "seam");
            Directory.CreateDirectory(dir);

            foreach ((string name, Curve shape) in Shapes())
            {
                var p = new MethodParameters { GapMm = 0.2, CenterPattern = MethodChoices.PatternAlong };
                // «Было» — как F3 раскладывал до сглаживания шва; «стало» — F3 сейчас.
                IReadOnlyList<PlacedStone> stones = AdvancedFillers.Lengthwise(new[] { shape }, D, p.GapMm, p.EdgeMarginMm)
                    ?? ContourFiller.Fill(new[] { shape }, new ContourFillOptions { StoneDiameterMm = D, GapMm = p.GapMm, FillCenter = true });
                List<Point2D> holes = Holes(shape, stones, out double area);
                File.WriteAllText(Path.Combine(dir, name + "-before.svg"), StrokeLab.Svg(new[] { shape }, stones, holes));
                var watch = System.Diagnostics.Stopwatch.StartNew();
                IReadOnlyList<PlacedStone> smooth = MethodRunner.Run(MethodKind.F3, new[] { shape }, D, p).Stones;
                long ms = watch.ElapsedMilliseconds;
                List<Point2D> after = Holes(shape, smooth, out double areaAfter);
                File.WriteAllText(Path.Combine(dir, name + "-after.svg"), StrokeLab.Svg(new[] { shape }, smooth, after));
                Console.WriteLine(
                    $"{name,-8} было {stones.Count,4} страз, дырок {holes.Count,3}, пусто {area,5:0.0} мм² → стало {smooth.Count,4}, " +
                    $"дырок {after.Count,3}, пусто {areaAfter,5:0.0} мм² (F3 целиком {ms} мс)");
            }

            Console.WriteLine($"SVG сохранены: {dir}");
        }

        internal static IEnumerable<(string, Curve)> Shapes()
        {
            yield return ("leaf", Leaf(new Point2D(0, 0), new Point2D(60, 0), 24));
            yield return ("heart", Heart());
            yield return ("oval", Oval(26, 13));
            yield return ("drop", Drop());
            yield return ("ribbon", Ribbon());
        }

        /// <summary>
        /// Дырки внутри крайнего кольца: точки сетки 0,2 мм глубже камня от края, где до ближайшего камня
        /// (по краю) больше 0,3 камня. Соседние точки одной дырки сливаются в одну пометку.
        /// </summary>
        internal static List<Point2D> Holes(Curve shape, IReadOnlyList<PlacedStone> stones, out double area)
        {
            var region = new ShapeRegion(new[] { shape }, D);
            var marks = new List<Point2D>();
            area = 0;
            const double cell = 0.2;
            for (double x = region.Min.X; x <= region.Max.X; x += cell)
            {
                for (double y = region.Min.Y; y <= region.Max.Y; y += cell)
                {
                    var q = new Point2D(x, y);
                    if (region.Depth(q) < D)
                    {
                        continue;
                    }

                    double clear = stones.Min(s => Point2D.Distance(s.Center, q) - s.DiameterMm / 2);
                    if (clear > 0.3 * D)
                    {
                        area += cell * cell;
                        if (marks.All(m => Point2D.Distance(m, q) > D))
                        {
                            marks.Add(q);
                        }
                    }
                }
            }

            return marks;
        }

        private static Curve Leaf(Point2D from, Point2D to, double width)
        {
            Point2D axis = to - from;
            double length = axis.Length;
            Point2D dir = axis / length;
            var nrm = new Point2D(-dir.Y, dir.X);
            var pts = new List<Point2D>();
            for (int i = 0; i <= 60; i++)
            {
                double t = i / 60.0;
                pts.Add(from + dir * (t * length) + nrm * (width / 2 * Math.Sin(Math.PI * t)));
            }

            for (int i = 59; i > 0; i--)
            {
                double t = i / 60.0;
                pts.Add(from + dir * (t * length) - nrm * (width / 2 * Math.Sin(Math.PI * t)));
            }

            return Curve.FromPolyline(pts, isClosed: true);
        }

        private static Curve Heart()
        {
            var pts = new List<Point2D>();
            for (int i = 0; i < 200; i++)
            {
                double t = 2 * Math.PI * i / 200;
                double x = 16 * Math.Pow(Math.Sin(t), 3);
                double y = 13 * Math.Cos(t) - 5 * Math.Cos(2 * t) - 2 * Math.Cos(3 * t) - Math.Cos(4 * t);
                pts.Add(new Point2D(x * 1.6, y * 1.6));
            }

            return Curve.FromPolyline(pts, isClosed: true);
        }

        private static Curve Oval(double rx, double ry) => Curve.FromPolyline(
            Enumerable.Range(0, 120).Select(i => new Point2D(rx * Math.Cos(i * Math.PI / 60), ry * Math.Sin(i * Math.PI / 60))).ToList(), true);

        /// <summary>Капля-лепесток: круглый низ, острый верх.</summary>
        private static Curve Drop()
        {
            var pts = new List<Point2D>();
            for (int i = 0; i < 120; i++)
            {
                double t = 2 * Math.PI * i / 120;
                pts.Add(new Point2D(26 * Math.Cos(t), 17 * Math.Sin(t) * Math.Pow(Math.Sin(t / 2), 1.5)));
            }

            return Curve.FromPolyline(pts, true);
        }

        /// <summary>Изогнутая лента шириной 14 мм с круглыми концами.</summary>
        private static Curve Ribbon()
        {
            var left = new List<Point2D>();
            var right = new List<Point2D>();
            for (int i = 0; i <= 80; i++)
            {
                double t = i / 80.0;
                var c = new Point2D(70 * t, 12 * Math.Sin(2 * Math.PI * t * 0.75));
                var c2 = new Point2D(70 * (t + 1e-3), 12 * Math.Sin(2 * Math.PI * (t + 1e-3) * 0.75));
                Point2D dir = (c2 - c).Normalized();
                var n = new Point2D(-dir.Y, dir.X);
                left.Add(c + n * 7);
                right.Add(c - n * 7);
            }

            right.Reverse();
            return Curve.FromPolyline(left.Concat(right).ToList(), true);
        }
    }
}
