using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Strassio.Core.Geometry;
using Strassio.Core.Methods;
using Strassio.Core.Placement;

namespace Strassio.Preview
{
    /// <summary>
    /// «Растворение» (F14) и «Россыпь» (брызги вокруг дизайна). Картинки — в out/preview/scatter/.
    /// </summary>
    internal static class ScatterLab
    {
        public static void Render(string repoRoot)
        {
            string dir = Path.Combine(repoRoot, "out", "preview", "scatter");
            Directory.CreateDirectory(dir);

            Curve heart = Heart(1.6);
            Curve rect = Curve.FromPolyline(new List<Point2D> { new(0, 0), new(70, 0), new(70, 32), new(0, 32) }, true);
            Curve circle = Circle(18);

            Dissolve(dir, "dissolve-heart-edge", heart, FadeDirection.Edge, 2.8, 2.6, "#EC407A");
            Dissolve(dir, "dissolve-rect-right", rect, FadeDirection.Right, 2.4, 5.5, "#7E57C2");
            Dissolve(dir, "dissolve-circle-center", circle, FadeDirection.Center, 2.4, 3.5, "#26A69A");

            // Россыпь вокруг сердца, выложенного кантом в два ряда.
            Curve small = Heart(1.0);
            var p = new MethodParameters { GapMm = 0.2, Rings = 2 };
            List<PlacedStone> design = MethodRunner.Run(MethodKind.F5, new[] { small }, 2.8, p).Stones.ToList();
            var watch = Stopwatch.StartNew();
            List<PlacedStone> splash = Scatter.Splash(Array.Empty<Curve>(), design, Array.Empty<PlacedStone>(), new[] { 2.8, 2.4, 2.0, 1.6 }, 0.6, 14, 5, 1);
            long ms = watch.ElapsedMilliseconds;
            var stones = design.Select(s => (s.Center, s.DiameterMm, "#E53935"))
                .Concat(splash.Select(s => (s.Center, s.DiameterMm, Shade(s.DiameterMm)))).ToList();
            File.WriteAllText(Path.Combine(dir, "splash-heart.svg"), MotifLab.Svg(new[] { small }, stones));
            Console.WriteLine($"splash-heart           дизайн {design.Count,4}, брызг {splash.Count,4}, {ms} мс");

            // Россыпь вокруг формы без страз (звезда) — от края самой формы.
            Curve star = Star();
            watch.Restart();
            List<PlacedStone> around = Scatter.Splash(new[] { star }, Array.Empty<PlacedStone>(), Array.Empty<PlacedStone>(), new[] { 2.4, 2.0, 1.6 }, 0.5, 10, 4, 3);
            ms = watch.ElapsedMilliseconds;
            File.WriteAllText(Path.Combine(dir, "splash-star.svg"),
                MotifLab.Svg(new[] { star }, around.Select(s => (s.Center, s.DiameterMm, "#FFB300")).ToList()));
            Console.WriteLine($"splash-star            брызг {around.Count,4}, {ms} мс");

            Console.WriteLine($"SVG сохранены: {dir}");
        }

        private static void Dissolve(string dir, string name, Curve shape, FadeDirection direction, double d, double fade, string rgb)
        {
            var watch = Stopwatch.StartNew();
            List<PlacedStone> stones = Scatter.Dissolve(new[] { shape }, d, 0.2, fade, direction, 0, 1);
            long ms = watch.ElapsedMilliseconds;
            File.WriteAllText(Path.Combine(dir, name + ".svg"), MotifLab.Svg(new[] { shape }, stones.Select(s => (s.Center, s.DiameterMm, rgb)).ToList()));
            Console.WriteLine($"{name,-22} {stones.Count,4} страз, {ms} мс");
        }

        private static string Shade(double d) => d >= 2.8 ? "#EF5350" : d >= 2.4 ? "#F48FB1" : d >= 2.0 ? "#F8BBD0" : "#FCE4EC";

        private static Curve Heart(double k)
        {
            var pts = new List<Point2D>();
            for (int i = 0; i < 200; i++)
            {
                double t = 2 * Math.PI * i / 200;
                double x = 16 * Math.Pow(Math.Sin(t), 3);
                double y = 13 * Math.Cos(t) - 5 * Math.Cos(2 * t) - 2 * Math.Cos(3 * t) - Math.Cos(4 * t);
                pts.Add(new Point2D(x * k, y * k));
            }

            return Curve.FromPolyline(pts, true);
        }

        private static Curve Circle(double r) => Curve.FromPolyline(
            Enumerable.Range(0, 160).Select(i => new Point2D(r * Math.Cos(2 * Math.PI * i / 160), r * Math.Sin(2 * Math.PI * i / 160))).ToList(), true);

        private static Curve Star()
        {
            var pts = new List<Point2D>();
            for (int i = 0; i < 10; i++)
            {
                double a = Math.PI / 2 + Math.PI * i / 5;
                double r = i % 2 == 0 ? 20 : 8;
                pts.Add(new Point2D(r * Math.Cos(a), r * Math.Sin(a)));
            }

            return Curve.FromPolyline(pts, true);
        }
    }
}
