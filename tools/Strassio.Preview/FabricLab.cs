using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Strassio.Core.Geometry;
using Strassio.Core.Methods;
using Strassio.Core.Motifs;
using Strassio.Core.Placement;

namespace Strassio.Preview
{
    /// <summary>
    /// Ткани с повторяющимся рисунком: заливки «Лучи» (F15) и «Штрихи» (F16) и раппорт — плитка, повторённая
    /// по ткани без наложений на стыках. Сюжеты — по мотивам фото работ автора. Картинки — в out/preview/fabric/.
    /// </summary>
    internal static class FabricLab
    {
        private const double Gap = 0.2;

        public static void Render(string repoRoot)
        {
            string dir = Path.Combine(repoRoot, "out", "preview", "fabric");
            Directory.CreateDirectory(dir);

            // 1. Лучи в круге и в «веере» (полукруг), технический вид.
            Curve circle = Circle(0, 0, 16);
            List<PlacedStone> rays = Run(MethodKind.F15, circle, 2.0, new MethodParameters { GapMm = 0.3, RayGapMm = 1.0, CenterHoleMm = 3 });
            File.WriteAllText(Path.Combine(dir, "rays-circle.svg"), MotifLab.Svg(new[] { circle }, rays.Select(s => (s.Center, s.DiameterMm, "#26A69A")).ToList()));
            Console.WriteLine($"rays-circle            {rays.Count,5} страз");

            // 2. Штрихи, редеют вниз, технический вид.
            Curve rect = Rect(0, 0, 50, 40);
            List<PlacedStone> streaks = Run(MethodKind.F16, rect, 2.0, new MethodParameters
            {
                GapMm = 0.3, LineGapMm = 0.9, StreakLength = 8, StreakBreak = 3, StreakFade = MethodChoices.FadeDown, Variant = 3,
            });
            File.WriteAllText(Path.Combine(dir, "streaks-rect.svg"), MotifLab.Svg(new[] { rect }, streaks.Select(s => (s.Center, s.DiameterMm, "#5C6BC0")).ToList()));
            Console.WriteLine($"streaks-rect           {streaks.Count,5} страз");

            // 3. Волны в прямоугольнике, технический вид.
            List<PlacedStone> waves = Run(MethodKind.F17, rect, 1.4, new MethodParameters { GapMm = 0.25, LineGapMm = 0.8, WaveHeightMm = 2.5, WaveLengthMm = 24 });
            File.WriteAllText(Path.Combine(dir, "waves-rect.svg"), MotifLab.Svg(new[] { rect }, waves.Select(s => (s.Center, s.DiameterMm, "#26A69A")).ToList()));
            Console.WriteLine($"waves-rect             {waves.Count,5} страз");

            // 4. Штрихи вокруг мотива-луковки: у мотива гуще, дальше реже.
            Curve onion = Onion(new Point2D(0, -6), 7, 0);
            List<PlacedStone> onionStones = Run(MethodKind.F2, onion, 1.6, new MethodParameters { GapMm = 0.2 });
            List<PlacedStone> around = PatternFills.StreaksAround(new[] { onion }, onionStones, Array.Empty<PlacedStone>(), 1.4, 0.25, 1.0, 90, 10, 4, 16, 5);
            var aroundTile = new List<(PlacedStone, string)>();
            Add(aroundTile, onionStones, "#E53935");
            Add(aroundTile, around, "#E53935");
            File.WriteAllText(Path.Combine(dir, "streaks-around.svg"), Velvet(aroundTile.Select(t => (t.Item1.Center, t.Item1.DiameterMm, t.Item2)).ToList(), "#141016"));
            Console.WriteLine($"streaks-around         {around.Count,5} страз вокруг {onionStones.Count} страз мотива");

            // 5. Заливка мотивами: цветочки россыпью в круге.
            Motif flower = Motif.FromStones("flower", Run(MethodKind.F15, Circle(0, 0, 4.2), 1.4, new MethodParameters { GapMm = 0.2, RayGapMm = 0.4, CenterHoleMm = 0 })
                .Select(st => new MotifStone(st.Center.X, st.Center.Y, st.DiameterMm, "ss5", "gold", st.Center.Length < 1 ? "#FFB300" : "#F5F5F5")));
            Curve big = Circle(0, 0, 40);
            MotifLayout filled = MotifPlacer.Fill(new[] { big }, new[] { flower }, new MotifLayoutOptions { SpacingMm = 2.5, StoneGapMm = 0.2, Rotate = true }, 3);
            File.WriteAllText(Path.Combine(dir, "motif-fill.svg"), Velvet(filled.Stones.Select(st => (st.Center, st.Source.DiameterMm, st.Source.Rgb)).ToList(), "#1B3A5C"));
            Console.WriteLine($"motif-fill             {filled.Copies,5} мотивов, {filled.Stones.Count} страз");

            Fabric(dir, "fabric-sunflowers", SunflowerTile(), 46, 52, RepeatShift.HalfDrop, 5, 3, "#5EA7DB");
            Fabric(dir, "fabric-ogee", OgeeTile(), 56, 62, RepeatShift.Brick, 4, 3, "#141016");
            Fabric(dir, "fabric-flames", FlameTile(), 30, 60, RepeatShift.HalfDrop, 6, 2, "#2A0A0E");

            // 6. Раппорт из двух мотивов: каждое второе пламя отражено.
            Fabric(dir, "fabric-flames-mirror", FlameTile(), 30, 60, RepeatShift.None, 6, 2, "#2A0A0E", RepeatAlternate.MirrorX);

            // 7. Штрихи вокруг луковки как плитка ткани, полшага.
            Fabric(dir, "fabric-ikat", aroundTile, 40, 52, RepeatShift.HalfDrop, 5, 3, "#141016");

            // 8. Лист трафарета без шва из плитки подсолнухов: наибольший лист до 300 × 400 мм.
            Sheet(dir, SunflowerTile(), new RepeatOptions { StepXMm = 46, StepYMm = 52, Shift = RepeatShift.HalfDrop, GapMm = Gap }, 300, 400, "#5EA7DB");
            Console.WriteLine($"SVG сохранены: {dir}");
        }

        /// <summary>Раппорт плитки на ткани: копии сеткой, стыки очищены (<see cref="Repeat"/>).</summary>
        private static void Fabric(
            string dir, string name, List<(PlacedStone Stone, string Rgb)> tile, double stepX, double stepY, RepeatShift shift,
            int columns, int rows, string background, RepeatAlternate alternate = RepeatAlternate.None)
        {
            var options = new RepeatOptions
            {
                StepXMm = stepX, StepYMm = stepY, Shift = shift, Columns = columns, Rows = rows, GapMm = Gap, Alternate = alternate,
            };
            List<PlacedStone> tileStones = tile.Select(t => t.Stone).ToList();
            var watch = Stopwatch.StartNew();
            var drop = new HashSet<int>(Repeat.SeamConflicts(tileStones, options));
            long ms = watch.ElapsedMilliseconds;
            var clean = tile.Where((t, i) => !drop.Contains(i)).ToList();
            var all = new List<(Point2D P, double D, string Rgb)>();
            Point2D pivot = Repeat.PivotOf(tileStones, options);
            List<Point2D> offsets = Repeat.Offsets(options);
            for (int k = 0; k < offsets.Count; k++)
            {
                bool second = Repeat.IsAlternate(options, k);
                Point2D offset = offsets[k];
                all.AddRange(clean.Select(t => ((second ? Repeat.Transform(alternate, t.Stone.Center, pivot) : t.Stone.Center) + offset, t.Stone.DiameterMm, t.Rgb)));
            }

            File.WriteAllText(Path.Combine(dir, name + ".svg"), Velvet(all, background));
            File.WriteAllText(Path.Combine(dir, name + "-tile.svg"), Velvet(tile.Select(t => (t.Stone.Center, t.Stone.DiameterMm, t.Rgb)).ToList(), background));
            Console.WriteLine($"{name,-22} плитка {tile.Count,5}, на стыках убрано {drop.Count,3}, на ткани {all.Count,6} страз, {ms} мс");
        }

        /// <summary>
        /// Лист трафарета без шва: наибольший лист из целого числа периодов, плитка повторена с запасом, стразы
        /// с серединой снаружи листа убраны. Два листа рядом — продолжение узора, видно на картинке «-pair».
        /// </summary>
        private static void Sheet(
            string dir, List<(PlacedStone Stone, string Rgb)> tile, RepeatOptions options, double maxW, double maxH, string background)
        {
            List<PlacedStone> tileStones = tile.Select(t => t.Stone).ToList();
            RepeatSheet sheet = Repeat.SeamlessSheet(options, maxW, maxH);
            double minX = tileStones.Min(st => st.Center.X), maxY = tileStones.Max(st => st.Center.Y);
            List<SheetStone> placed = Repeat.FillSheet(tileStones, options, sheet, new Point2D(minX, maxY), out _);
            var onSheet = placed.Select(p => (p.Center, tile[p.Source].Stone.DiameterMm, tile[p.Source].Rgb)).ToList();
            File.WriteAllText(Path.Combine(dir, "sheet.svg"), Velvet(onSheet, background));

            // Два листа встык по ширине: узор не прерывается.
            var pair = onSheet.Concat(onSheet.Select(o => (o.Center + new Point2D(sheet.WidthMm, 0), o.DiameterMm, o.Rgb == "#FFC107" ? "#FF7043" : o.Rgb))).ToList();
            File.WriteAllText(Path.Combine(dir, "sheet-pair.svg"), Velvet(pair, background));
            Console.WriteLine($"sheet                  {sheet.WidthMm} × {sheet.HeightMm} мм, {onSheet.Count} страз на листе");
        }

        /// <summary>Два подсолнуха на стебле с листьями: лучи золотом, середина и листья голубым.</summary>
        internal static List<(PlacedStone, string)> SunflowerTile()
        {
            const string gold = "#E9B949", blue = "#2D6FA6";
            var result = new List<(PlacedStone, string)>();
            foreach ((double cx, double cy) in new[] { (0.0, 0.0), (17.0, 3.0) })
            {
                Add(result, Run(MethodKind.F15, Circle(cx, cy, 7.6), 1.4, new MethodParameters { GapMm = 0.2, RayGapMm = 0.5, CenterHoleMm = 2.6 }), gold);
                Add(result, Run(MethodKind.L1, Circle(cx, cy, 1.9), 1.6, new MethodParameters { GapMm = 0.25 }), blue);
                Add(result, Run(MethodKind.L1, Circle(cx, cy, 8.7), 1.6, new MethodParameters { GapMm = 0.35 }), blue);
            }

            // Стебли и листья — контуры одним рядом и жилка посередине.
            Add(result, Run(MethodKind.L1, Polyline(new[] { new Point2D(0, -9.8), new Point2D(5, -19), new Point2D(8, -29) }, false), 1.6, new MethodParameters { GapMm = 0.35 }), blue);
            Add(result, Run(MethodKind.L1, Polyline(new[] { new Point2D(17, -6.8), new Point2D(12, -19), new Point2D(8.6, -27.4) }, false), 1.6, new MethodParameters { GapMm = 0.35 }), blue);
            foreach ((Point2D at, double angle) in new[] { (new Point2D(-2, -24), 200.0), (new Point2D(17, -23), -20.0), (new Point2D(6.6, -35), 260.0) })
            {
                Add(result, Run(MethodKind.L1, Leaf(at, 15, 7, angle), 1.6, new MethodParameters { GapMm = 0.35 }), blue);
                Point2D tip = at + Rotate(new Point2D(5.2, 0), angle), tail = at - Rotate(new Point2D(5.2, 0), angle);
                Add(result, Run(MethodKind.L1, Polyline(new[] { tail, tip }, false), 1.6, new MethodParameters { GapMm = 0.35 }), blue);
            }

            return Packed(result);
        }

        /// <summary>Четыре луковки (огивы) крестом, залитые сотами, и вертикальные штрихи вокруг.</summary>
        private static List<(PlacedStone, string)> OgeeTile()
        {
            const string red = "#E53935";
            var result = new List<(PlacedStone, string)>();
            for (int k = 0; k < 4; k++)
            {
                double angle = 90 * k;
                Add(result, Run(MethodKind.F2, Onion(Rotate(new Point2D(0, 9.6), angle), 6.5, angle), 1.6, new MethodParameters { GapMm = 0.2 }), red);
            }

            // Штрихи вокруг мотива в прямоугольнике плитки, не налезая на мотив.
            List<PlacedStone> motif = result.Select(r => r.Item1).ToList();
            List<PlacedStone> rain = Run(MethodKind.F16, Rect(-28, -31, 56, 62), 1.4, new MethodParameters
            {
                GapMm = 0.25, LineGapMm = 1.2, StreakLength = 12, StreakBreak = 5, StreakFade = MethodChoices.FadeEdge, Variant = 7,
            });
            Add(result, rain.Where(s => motif.All(m => Point2D.Distance(m.Center, s.Center) >= (m.DiameterMm + s.DiameterMm) / 2 + 1.2)).ToList(), red);
            return result;
        }

        /// <summary>Изогнутый язычок пламени, залитый сотами, и редкая россыпь одиночных камней по фону.</summary>
        private static List<(PlacedStone, string)> FlameTile()
        {
            const string red = "#E0353B";
            var result = new List<(PlacedStone, string)>();
            Add(result, Run(MethodKind.F2, Flame(), 1.6, new MethodParameters { GapMm = 0.2 }), red);

            List<PlacedStone> flame = result.Select(r => r.Item1).ToList();
            List<PlacedStone> dust = Run(MethodKind.F10, Rect(-15, -30, 30, 60), 1.4, new MethodParameters { GapMm = 7, Variant = 2 });
            Add(result, dust.Where(s => flame.All(m => Point2D.Distance(m.Center, s.Center) >= (m.DiameterMm + s.DiameterMm) / 2 + 1.5)).ToList(), "#F06A6E");
            return result;
        }

        /// <summary>Выкинуть стразы, налезающие на уже положенные (как «не налезать на готовые» в CorelDRAW).</summary>
        private static List<(PlacedStone, string)> Packed(List<(PlacedStone Stone, string Rgb)> stones)
        {
            var kept = new List<(PlacedStone, string)>();
            foreach ((PlacedStone s, string rgb) in stones)
            {
                if (kept.All(k => Point2D.Distance(k.Item1.Center, s.Center) >= (k.Item1.DiameterMm + s.DiameterMm) / 2 + Gap - 0.01))
                {
                    kept.Add((s, rgb));
                }
            }

            return kept;
        }

        private static void Add(List<(PlacedStone, string)> to, IEnumerable<PlacedStone> stones, string rgb) =>
            to.AddRange(stones.Select(s => (s, rgb)));

        private static List<PlacedStone> Run(MethodKind kind, Curve shape, double d, MethodParameters p) =>
            MethodRunner.Run(kind, new[] { shape }, d, p).Stones.ToList();

        /// <summary>Бархат: тёмный (или цветной) фон, стразы с бликом.</summary>
        private static string Velvet(IReadOnlyList<(Point2D P, double D, string Rgb)> stones, string background)
        {
            double minX = stones.Min(s => s.P.X - s.D) - 3, maxX = stones.Max(s => s.P.X + s.D) + 3;
            double minY = stones.Min(s => s.P.Y - s.D) - 3, maxY = stones.Max(s => s.P.Y + s.D) + 3;
            const double k = 6;
            CultureInfo ci = CultureInfo.InvariantCulture;
            string X(double x) => ((x - minX) * k).ToString("0.#", ci);
            string Y(double y) => ((maxY - y) * k).ToString("0.#", ci);

            var sb = new StringBuilder();
            sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{X(maxX)}\" height=\"{Y(minY)}\">");
            sb.Append("<defs><radialGradient id=\"g\" cx=\"35%\" cy=\"35%\" r=\"70%\"><stop offset=\"0\" stop-color=\"#fff\" stop-opacity=\"0.75\"/>")
                .Append("<stop offset=\"0.45\" stop-color=\"#fff\" stop-opacity=\"0\"/></radialGradient></defs>");
            sb.Append($"<rect width=\"100%\" height=\"100%\" fill=\"{background}\"/>");
            foreach ((Point2D p, double d, string rgb) in stones)
            {
                string r = (d / 2 * k).ToString("0.#", ci);
                sb.Append($"<circle cx=\"{X(p.X)}\" cy=\"{Y(p.Y)}\" r=\"{r}\" fill=\"{rgb}\"/>");
                sb.Append($"<circle cx=\"{X(p.X)}\" cy=\"{Y(p.Y)}\" r=\"{r}\" fill=\"url(#g)\"/>");
            }

            sb.Append("</svg>");
            return sb.ToString();
        }

        private static Curve Circle(double cx, double cy, double r) => Polyline(
            Enumerable.Range(0, 160).Select(i => new Point2D(cx + r * Math.Cos(2 * Math.PI * i / 160), cy + r * Math.Sin(2 * Math.PI * i / 160))), true);

        private static Curve Rect(double x, double y, double w, double h) =>
            Polyline(new[] { new Point2D(x, y), new Point2D(x + w, y), new Point2D(x + w, y + h), new Point2D(x, y + h) }, true);

        /// <summary>Листик-«линза» длиной <paramref name="length"/> и шириной <paramref name="width"/>, повёрнутый на угол.</summary>
        private static Curve Leaf(Point2D at, double length, double width, double angleDeg)
        {
            var pts = new List<Point2D>();
            for (int i = 0; i <= 40; i++)
            {
                double x = -length / 2 + length * i / 40;
                pts.Add(new Point2D(x, width / 2 * (1 - Math.Pow(2 * x / length, 2))));
            }

            for (int i = 39; i > 0; i--)
            {
                double x = -length / 2 + length * i / 40;
                pts.Add(new Point2D(x, -width / 2 * (1 - Math.Pow(2 * x / length, 2))));
            }

            return Polyline(pts.Select(p => at + Rotate(p, angleDeg)), true);
        }

        /// <summary>
        /// Луковка (огива): круглое донышко радиуса <paramref name="r"/> и вытянутый острый носик. Носик
        /// смотрит от середины мотива: при угле 0 — вверх.
        /// </summary>
        private static Curve Onion(Point2D at, double r, double angleDeg)
        {
            var pts = new List<Point2D>();
            for (int i = 0; i <= 60; i++)
            {
                double a = Math.PI * 0.75 + 1.5 * Math.PI * i / 60;
                pts.Add(new Point2D(r * Math.Cos(a), r * Math.Sin(a)));
            }

            // Правый бок носика: от точки круга под 45° вогнутой дугой к острию, потом зеркально.
            Point2D start = new Point2D(r * Math.Cos(Math.PI / 4), r * Math.Sin(Math.PI / 4)), tip = new Point2D(0, 2.1 * r);
            Point2D control = new Point2D(r * 1.05, r * 1.25);
            for (int i = 1; i < 20; i++)
            {
                double t = i / 20.0;
                pts.Add(start * ((1 - t) * (1 - t)) + control * (2 * t * (1 - t)) + tip * (t * t));
            }

            pts.Add(tip);
            for (int i = 19; i >= 1; i--)
            {
                double t = i / 20.0;
                Point2D q = start * ((1 - t) * (1 - t)) + control * (2 * t * (1 - t)) + tip * (t * t);
                pts.Add(new Point2D(-q.X, q.Y));
            }

            return Polyline(pts.Select(p => at + Rotate(p, angleDeg)), true);
        }

        /// <summary>S-образный язычок пламени: широкий в середине и острый на концах.</summary>
        private static Curve Flame()
        {
            var left = new List<Point2D>();
            var right = new List<Point2D>();
            for (int i = 0; i <= 60; i++)
            {
                double t = i / 60.0;
                double y = -26 + 52 * t;
                double x = 6 * Math.Sin(2 * Math.PI * t * 0.9);
                double half = 4.4 * Math.Pow(Math.Sin(Math.PI * t), 1.3);
                left.Add(new Point2D(x - half, y));
                right.Add(new Point2D(x + half, y));
            }

            right.Reverse();
            return Polyline(left.Concat(right.Skip(1).Take(right.Count - 2)), true);
        }

        private static Point2D Rotate(Point2D p, double angleDeg)
        {
            double a = angleDeg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
            return new Point2D(p.X * c - p.Y * s, p.X * s + p.Y * c);
        }

        private static Curve Polyline(IEnumerable<Point2D> points, bool closed) => Curve.FromPolyline(points.ToList(), closed);
    }
}
