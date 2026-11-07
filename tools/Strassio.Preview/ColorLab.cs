using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Strassio.Core.Editing;
using Strassio.Core.Geometry;
using Strassio.Core.Methods;
using Strassio.Core.Placement;
using Strassio.Core.Stones;

namespace Strassio.Preview
{
    /// <summary>
    /// «Перекрасить по рисунку»: одна сплошная заливка на весь силуэт, поверх — цветные фигуры, и каждая
    /// страза берёт цвет фигуры под собой. Картинки — в out/preview/colors/, на тёмном фоне, как на ткани.
    /// </summary>
    internal static class ColorLab
    {
        private static readonly List<StoneColor> Palette = new List<StoneColor>
        {
            new StoneColor { Name = "Кристалл", Rgb = "#E8EEF5" },
            new StoneColor { Name = "Цитрин", Rgb = "#F2C200" },
            new StoneColor { Name = "Золото", Rgb = "#C9A03A" },
            new StoneColor { Name = "Красный", Rgb = "#E53935" },
            new StoneColor { Name = "Розовый", Rgb = "#F06292" },
            new StoneColor { Name = "Зелёный", Rgb = "#43A047" },
        };

        public static void Render(string repoRoot)
        {
            string dir = Path.Combine(repoRoot, "out", "preview", "colors");
            Directory.CreateDirectory(dir);

            // Ромашка: лепестки белые, середина жёлтая, по краю середины — кольцо золота.
            Curve flower = Flower(12, 13, 11);
            var flowerRegions = new List<ColorRegion>
            {
                new ColorRegion(new[] { flower }, 255, 255, 255, order: 0),
                new ColorRegion(new[] { Circle(0, 0, 10) }, 214, 168, 40, order: 1),
                new ColorRegion(new[] { Circle(0, 0, 7.8) }, 255, 214, 0, order: 2),
            };
            Save(dir, "chamomile", flower, MethodKind.F2, flowerRegions);

            // Сердце в косую полоску: три полосы под 30°, нарисованные поверх красного фона.
            Curve heart = Heart();
            var heartRegions = new List<ColorRegion> { new ColorRegion(new[] { heart }, 200, 20, 30, order: 0) };
            for (int i = 0; i < 6; i++)
            {
                byte[][] stripe = { new byte[] { 255, 120, 160 }, new byte[] { 255, 255, 255 } };
                byte[] c = stripe[i % 2];
                heartRegions.Add(new ColorRegion(new[] { Stripe(-40 + i * 13, 6, 30) }, c[0], c[1], c[2], order: 1 + i));
            }

            Save(dir, "heart-stripes", heart, MethodKind.F3, heartRegions);

            // «Весь дизайн за раз»: ромашка на стебле из четырёх фигур, у каждой свой цвет. Фигуры идут
            // сверху вниз: середина, лепестки, лист, стебель — нижние обходят верхние.
            var design = new List<(Curve Shape, byte R, byte G, byte B)>
            {
                (Circle(0, 0, 8), 255, 214, 0),
                (Flower(12, 11, 10), 255, 255, 255),
                (Leaf(new Point2D(2, 40), new Point2D(18, 26), 7), 40, 160, 60),
                (Stem(), 30, 120, 50),
            };
            foreach (MethodKind kind in new[] { MethodKind.F2, MethodKind.F3 })
            {
                DesignResult result = DesignRunner.Run(
                    kind, design.Select(d => (IReadOnlyList<Curve>)new[] { d.Shape }).ToList(), 2.4, new MethodParameters { GapMm = 0.2 });
                var stones = new List<PlacedStone>();
                var fills = new List<string>();
                for (int i = 0; i < design.Count; i++)
                {
                    StoneColor color = Palette[ArtworkColors.Nearest(design[i].R, design[i].G, design[i].B, Palette)];
                    stones.AddRange(result.PerShape[i].Stones);
                    fills.AddRange(result.PerShape[i].Stones.Select(_ => color.Rgb));
                }

                string name = "design-" + kind.ToString().ToLowerInvariant();
                string path = Path.Combine(dir, name + ".svg");
                File.WriteAllText(path, SvgWriter.RenderColored(stones, fills));
                Console.WriteLine($"{name}: {stones.Count} страз, убрано на стыках {result.Removed} → {path}");
            }

            // Смешение 70% кристалл / 30% золото в сердце: «случайно» и «равномерно» рядом.
            MethodResult mixed = MethodRunner.Run(MethodKind.F3, new[] { heart }, 2.4, new MethodParameters { GapMm = 0.2 });
            List<Point2D> centers = mixed.Stones.Select(s => s.Center).ToList();
            List<double> diameters = mixed.Stones.Select(s => s.DiameterMm).ToList();
            var shares = new[] { 70.0, 30.0 };
            string[] mixColors = { Palette[0].Rgb, Palette[2].Rgb };
            foreach ((string name, int[] colors) in new[]
            {
                ("mix-random", ColorMixer.Random(centers.Count, shares, 4)),
                ("mix-even", ColorMixer.Even(centers, diameters, shares, 4)),
            })
            {
                string path = Path.Combine(dir, name + ".svg");
                File.WriteAllText(path, SvgWriter.RenderColored(mixed.Stones, colors.Select(c => mixColors[c]).ToList()));
                Console.WriteLine($"{name}: {centers.Count} страз, золото касается золота {ColorMixer.SameColorTouches(centers, diameters, colors, 1)} раз → {path}");
            }

            // Крайние ряды золотом: ромашка «Комбинированной» и сердце сотами — один и два ряда от края.
            foreach ((string name, Curve shape, MethodKind kind, int rows) in new[]
            {
                ("edge-chamomile", flower, MethodKind.F4, 1),
                ("edge-heart", heart, MethodKind.F2, 2),
            })
            {
                MethodResult made = MethodRunner.Run(kind, new[] { shape }, 2.4, new MethodParameters { GapMm = 0.2, Rings = 2 });
                int[] row = EdgeRows.Find(made.Stones.Select(s => s.Center).ToList(), made.Stones.Select(s => s.DiameterMm).ToList(), rows);
                string path = Path.Combine(dir, name + ".svg");
                File.WriteAllText(path, SvgWriter.RenderColored(made.Stones, row.Select(r => r > 0 ? Palette[2].Rgb : Palette[0].Rgb).ToList()));
                Console.WriteLine($"{name}: {made.Stones.Count} страз, по краю {row.Count(r => r > 0)} → {path}");
            }

            // Омбре: сердце сотами розовый → красный слева направо и ромашка золото → цитрин → кристалл от центра.
            foreach ((string name, Curve shape, ColorMixer.OmbreDirection direction, string[] ombre) in new[]
            {
                ("ombre-heart", heart, ColorMixer.OmbreDirection.LeftToRight, new[] { Palette[4].Rgb, Palette[3].Rgb }),
                ("ombre-chamomile", flower, ColorMixer.OmbreDirection.CenterOut, new[] { Palette[2].Rgb, Palette[1].Rgb, Palette[0].Rgb }),
            })
            {
                MethodResult made = MethodRunner.Run(MethodKind.F2, new[] { shape }, 2.4, new MethodParameters { GapMm = 0.2 });
                int[] colors = ColorMixer.Ombre(
                    made.Stones.Select(s => s.Center).ToList(), made.Stones.Select(s => s.DiameterMm).ToList(), ombre.Length, direction, 1);
                string path = Path.Combine(dir, name + ".svg");
                File.WriteAllText(path, SvgWriter.RenderColored(made.Stones, colors.Select(c => ombre[c]).ToList()));
                Console.WriteLine($"{name}: {made.Stones.Count} страз → {path}");
            }

            // Симметрия: левая половина ромашки (золото) отражена вправо (кристалл).
            {
                MethodResult made = MethodRunner.Run(MethodKind.F4, new[] { flower }, 2.4, new MethodParameters { GapMm = 0.2, Rings = 2 });
                List<DocStone> half = made.Stones.Where(s => s.Center.X <= 0.6).Select(s => new DocStone(s.Center, s.DiameterMm, 0)).ToList();
                MirrorResult mirrored = Symmetry.Mirror(half, Symmetry.AxisAt(half, MirrorSide.Right).Point, new Point2D(0, 1), 0.2);
                Point2D[] moved = half.Select(s => s.Center).ToArray();
                foreach (StoneMove m in mirrored.Snapped)
                {
                    moved[m.Index] = m.NewCenter;
                }

                var stones = moved.Select(c => new PlacedStone(c, 2.4, false)).Concat(mirrored.Added.Select(a => new PlacedStone(a.Center, 2.4, false))).ToList();
                var fills = moved.Select(_ => Palette[2].Rgb).Concat(mirrored.Added.Select(_ => Palette[0].Rgb)).ToList();
                string path = Path.Combine(dir, "mirror-chamomile.svg");
                File.WriteAllText(path, SvgWriter.RenderColored(stones, fills));
                Console.WriteLine($"mirror-chamomile: половина {half.Count}, копий {mirrored.Added.Count}, на ось {mirrored.Snapped.Count}, пропущено {mirrored.Skipped} → {path}");
            }
        }

        /// <summary>Лист: две дуги от черешка до кончика, ширина посередине <paramref name="width"/>.</summary>
        private static Curve Leaf(Point2D from, Point2D to, double width)
        {
            Point2D axis = to - from;
            double length = axis.Length;
            Point2D dir = axis / length;
            var nrm = new Point2D(-dir.Y, dir.X);
            var pts = new List<Point2D>();
            for (int i = 0; i <= 40; i++)
            {
                double t = i / 40.0;
                pts.Add(from + dir * (t * length) + nrm * (width / 2 * Math.Sin(Math.PI * t)));
            }

            for (int i = 39; i > 0; i--)
            {
                double t = i / 40.0;
                pts.Add(from + dir * (t * length) - nrm * (width / 2 * Math.Sin(Math.PI * t)));
            }

            return Curve.FromPolyline(pts, isClosed: true);
        }

        /// <summary>Стебель: лёгкий изгиб от цветка вниз, ширина 4 мм.</summary>
        private static Curve Stem()
        {
            var left = new List<Point2D>();
            var right = new List<Point2D>();
            for (int i = 0; i <= 30; i++)
            {
                // В картинке Preview ось Y смотрит вниз — стебель идёт вниз от цветка.
                double y = 15 + 45.0 * i / 30;
                double x = 4 * Math.Sin((y - 15) / 45 * Math.PI);
                left.Add(new Point2D(x - 2, y));
                right.Add(new Point2D(x + 2, y));
            }

            right.Reverse();
            return Curve.FromPolyline(left.Concat(right).ToList(), isClosed: true);
        }

        private static void Save(string dir, string name, Curve shape, MethodKind method, IReadOnlyList<ColorRegion> regions)
        {
            var p = new MethodParameters { GapMm = 0.2 };
            MethodResult result = MethodRunner.Run(method, new[] { shape }, 2.4, p);
            int[] region = ArtworkColors.RegionOf(result.Stones.Select(s => s.Center).ToList(), regions);

            var fills = new List<string>();
            var used = new SortedSet<string>();
            for (int i = 0; i < result.Stones.Count; i++)
            {
                StoneColor color = Palette[0];
                if (region[i] >= 0)
                {
                    ColorRegion r = regions[region[i]];
                    color = Palette[ArtworkColors.Nearest(r.Red, r.Green, r.Blue, Palette)];
                }

                fills.Add(color.Rgb);
                used.Add(color.Name);
            }

            string path = Path.Combine(dir, name + ".svg");
            File.WriteAllText(path, SvgWriter.RenderColored(result.Stones, fills));
            Console.WriteLine($"{name}: {result.Stones.Count} страз, цвета: {string.Join(", ", used)} → {path}");
        }

        /// <summary>Цветок с круглыми лепестками: радиус волной, впадины между лепестками острее.</summary>
        private static Curve Flower(int petals, double inner, double petal)
        {
            var pts = new List<Point2D>();
            const int n = 720;
            for (int i = 0; i < n; i++)
            {
                double t = 2 * Math.PI * i / n;
                double r = inner + petal * Math.Pow(Math.Abs(Math.Cos(petals / 2.0 * t)), 0.6);
                pts.Add(new Point2D(r * Math.Cos(t), r * Math.Sin(t)));
            }

            return Curve.FromPolyline(pts, isClosed: true);
        }

        private static Curve Circle(double cx, double cy, double r) => Curve.FromPolyline(
            Enumerable.Range(0, 120).Select(i => new Point2D(cx + r * Math.Cos(i * Math.PI / 60), cy + r * Math.Sin(i * Math.PI / 60))).ToList(),
            isClosed: true);

        private static Curve Heart()
        {
            var pts = new List<Point2D>();
            for (int i = 0; i < 200; i++)
            {
                double t = 2 * Math.PI * i / 200;
                double x = 16 * Math.Pow(Math.Sin(t), 3);
                double y = 13 * Math.Cos(t) - 5 * Math.Cos(2 * t) - 2 * Math.Cos(3 * t) - Math.Cos(4 * t);
                pts.Add(new Point2D(x * 1.8, -y * 1.8));
            }

            return Curve.FromPolyline(pts, isClosed: true);
        }

        /// <summary>Длинная полоса шириной <paramref name="width"/>, повёрнутая на <paramref name="angleDeg"/>.</summary>
        private static Curve Stripe(double offset, double width, double angleDeg)
        {
            double a = angleDeg * Math.PI / 180;
            var dir = new Point2D(Math.Cos(a), Math.Sin(a));
            var nrm = new Point2D(-dir.Y, dir.X);
            Point2D P(double along, double across) =>
                new Point2D(dir.X * along + nrm.X * (offset + across), dir.Y * along + nrm.Y * (offset + across));
            return Curve.FromPolyline(new[] { P(-80, 0), P(80, 0), P(80, width), P(-80, width) }, isClosed: true);
        }
    }
}
