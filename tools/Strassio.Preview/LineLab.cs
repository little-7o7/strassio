using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Strassio.Core.Geometry;
using Strassio.Core.Methods;
using Strassio.Core.Placement;

namespace Strassio.Preview
{
    /// <summary>
    /// Замеры методов по линии (L1–L8, обводка) на обязательных сценариях: звезда, сердце, зигзаг с острыми
    /// углами, тугая спираль, S-кривая, мелкий круг, волна, буквы. Для каждого — налезания, самый маленький
    /// и самый большой просвет между соседями по ряду, разброс и как далеко стразы ушли от линии.
    /// Таблица — в консоль, картинки — в out/preview/lines/.
    /// </summary>
    internal static class LineLab
    {
        private const double D = 2.4;

        private static readonly Dictionary<string, double> Sizes = new Dictionary<string, double>
        {
            ["ss5"] = 2.1, ["ss6"] = 2.4, ["ss10"] = 2.9, ["ss16"] = 3.9,
        };

        public static void Render(string repoRoot, string? only = null)
        {
            string dir = Path.Combine(repoRoot, "out", "preview", "lines");
            Directory.CreateDirectory(dir);

            var methods = new List<(string Name, MethodKind Kind, MethodParameters P)>
            {
                ("l1", MethodKind.L1, new MethodParameters()),
                ("l1-gap0", MethodKind.L1, new MethodParameters { GapMm = 0 }),
                ("l2", MethodKind.L2, new MethodParameters()),
                ("l3", MethodKind.L3, new MethodParameters()),
                ("l4", MethodKind.L4, new MethodParameters { RowCount = 4 }),
                ("l5", MethodKind.L5, new MethodParameters { FromSize = "ss16", ToSize = "ss5" }),
                ("l6", MethodKind.L6, new MethodParameters { SizePattern = "ss6, ss6, ss16" }),
                ("l7", MethodKind.L7, new MethodParameters()),
                ("l8", MethodKind.L8, new MethodParameters { AccentSize = "ss16" }),
                ("outline", MethodKind.Outline, new MethodParameters()),
            };

            Console.WriteLine($"{"метод",-9} {"фигура",-8} {"страз",5} {"налез",5} {"мин",6} {"макс",6} {"разброс",7} {"от линии",8}");
            foreach ((string shapeName, Curve curve) in Shapes())
            {
                FlattenedCurve flat = CurveFlattener.Flatten(curve);
                foreach ((string name, MethodKind kind, MethodParameters p) in methods)
                {
                    if (only != null && name != only)
                    {
                        continue;
                    }

                    MethodInfo info = MethodCatalog.Get(kind);
                    if (info.NeedsClosed && !flat.IsClosed)
                    {
                        continue;
                    }

                    IReadOnlyList<PlacedStone> stones = MethodRunner.Run(kind, new[] { curve }, D, p, Sizes).Stones;
                    Metrics m = Measure(stones, flat, p.GapMm, singleRow: kind != MethodKind.L2 && kind != MethodKind.L4);
                    Console.WriteLine(
                        $"{name,-9} {shapeName,-8} {stones.Count,5} {m.Overlaps,5} {F(m.MinGap),6} {F(m.MaxGap),6} {F(m.MaxGap - m.MinGap),7} {F(m.OffLine),8}");
                    File.WriteAllText(Path.Combine(dir, $"{name}-{shapeName}.svg"),
                        SvgWriter.Render(flat.Points.Select(pt => pt.Position).ToList(), flat.IsClosed, stones));
                }
            }

            Console.WriteLine($"SVG сохранены: {dir}");
        }

        private static string F(double v) => double.IsInfinity(v) || double.IsNaN(v) ? "-" : v.ToString("0.00", CultureInfo.InvariantCulture);

        private readonly struct Metrics
        {
            public Metrics(int overlaps, double minGap, double maxGap, double offLine)
            {
                Overlaps = overlaps;
                MinGap = minGap;
                MaxGap = maxGap;
                OffLine = offLine;
            }

            public int Overlaps { get; }

            public double MinGap { get; }

            public double MaxGap { get; }

            public double OffLine { get; }
        }

        /// <summary>
        /// Налезания — пары ближе «полсуммы диаметров + зазор − 0,05». Просветы — между соседями по ряду: для
        /// одного ряда стразы сортируются по месту на линии, для нескольких рядов — до ближайшего соседа.
        /// </summary>
        private static Metrics Measure(IReadOnlyList<PlacedStone> stones, FlattenedCurve flat, double gap, bool singleRow)
        {
            int overlaps = 0;
            for (int i = 0; i < stones.Count; i++)
            {
                for (int j = i + 1; j < stones.Count; j++)
                {
                    double need = (stones[i].DiameterMm + stones[j].DiameterMm) / 2 + Math.Max(0, gap) - 0.05;
                    if (Point2D.Distance(stones[i].Center, stones[j].Center) < need)
                    {
                        overlaps++;
                    }
                }
            }

            double minGap = double.PositiveInfinity, maxGap = double.NegativeInfinity, off = 0;
            if (singleRow)
            {
                List<(PlacedStone S, double At, double Off)> along = stones.Select(s =>
                {
                    (double at, double dist) = Project(flat, s.Center);
                    return (s, at, dist);
                }).OrderBy(t => t.at).ToList();
                off = along.Count == 0 ? 0 : along.Max(t => t.Off);
                for (int i = 0; i + 1 < along.Count; i++)
                {
                    Edge(along[i].S, along[i + 1].S);
                }

                if (flat.IsClosed && along.Count > 2)
                {
                    Edge(along[along.Count - 1].S, along[0].S);
                }
            }
            else
            {
                foreach (PlacedStone s in stones)
                {
                    double best = stones.Where(t => !t.Center.Equals(s.Center))
                        .Select(t => Point2D.Distance(t.Center, s.Center) - (t.DiameterMm + s.DiameterMm) / 2)
                        .DefaultIfEmpty(double.NaN).Min();
                    if (!double.IsNaN(best))
                    {
                        minGap = Math.Min(minGap, best);
                        maxGap = Math.Max(maxGap, best);
                    }
                }
            }

            return new Metrics(overlaps, minGap, maxGap, off);

            void Edge(PlacedStone a, PlacedStone b)
            {
                double g = Point2D.Distance(a.Center, b.Center) - (a.DiameterMm + b.DiameterMm) / 2;
                minGap = Math.Min(minGap, g);
                maxGap = Math.Max(maxGap, g);
            }
        }

        /// <summary>Место точки на линии (длина от начала до ближайшей точки) и расстояние до линии.</summary>
        private static (double At, double Distance) Project(FlattenedCurve flat, Point2D p)
        {
            IReadOnlyList<FlattenedPoint> pts = flat.Points;
            double best = double.MaxValue, at = 0;
            int n = flat.IsClosed ? pts.Count : pts.Count - 1;
            for (int i = 0; i < n; i++)
            {
                FlattenedPoint a = pts[i], b = pts[(i + 1) % pts.Count];
                Point2D ab = b.Position - a.Position;
                double len2 = ab.X * ab.X + ab.Y * ab.Y;
                double t = len2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, ((p - a.Position).X * ab.X + (p - a.Position).Y * ab.Y) / len2));
                double d = Point2D.Distance(p, a.Position + ab * t);
                if (d < best)
                {
                    best = d;
                    at = flat.ArcLengths[i] + t * Math.Sqrt(len2);
                }
            }

            return (at, best);
        }

        private static IEnumerable<(string, Curve)> Shapes()
        {
            // Звезда: пять острых лучей (36°).
            var star = new List<Point2D>();
            for (int i = 0; i < 10; i++)
            {
                double a = Math.PI / 2 + Math.PI * i / 5, r = i % 2 == 0 ? 25 : 10;
                star.Add(new Point2D(r * Math.Cos(a), r * Math.Sin(a)));
            }

            yield return ("star", Curve.FromPolyline(star, true));

            // Сердце кривыми Безье, как его рисуют в CorelDRAW: острый клюв внизу и ямка сверху.
            yield return ("heart", new Curve(new[]
            {
                CurveSegment.Cubic(new Point2D(0, -25), new Point2D(-12, -12), new Point2D(-28, 0), new Point2D(-22, 12)),
                CurveSegment.Cubic(new Point2D(-22, 12), new Point2D(-17, 24), new Point2D(-3, 22), new Point2D(0, 13)),
                CurveSegment.Cubic(new Point2D(0, 13), new Point2D(3, 22), new Point2D(17, 24), new Point2D(22, 12)),
                CurveSegment.Cubic(new Point2D(22, 12), new Point2D(28, 0), new Point2D(12, -12), new Point2D(0, -25)),
            }, true));

            // Зигзаг с углами около 25°.
            yield return ("zigzag", Curve.FromPolyline(new[]
            {
                new Point2D(0, 0), new Point2D(12, 26), new Point2D(24, 0), new Point2D(36, 26), new Point2D(48, 0),
            }));

            // Тугая спираль: витки через 5 мм, в середине радиус 2 мм.
            var spiral = new List<Point2D>();
            for (double t = 0; t <= 6 * Math.PI; t += 0.02)
            {
                double r = 2 + 5 * t / (2 * Math.PI);
                spiral.Add(new Point2D(r * Math.Cos(t), r * Math.Sin(t)));
            }

            yield return ("spiral", Curve.FromPolyline(spiral));

            // S-кривая двумя дугами Безье.
            yield return ("s-curve", new Curve(new[]
            {
                CurveSegment.Cubic(new Point2D(0, 0), new Point2D(0, 20), new Point2D(30, 20), new Point2D(30, 0)),
                CurveSegment.Cubic(new Point2D(30, 0), new Point2D(30, -20), new Point2D(60, -20), new Point2D(60, 0)),
            }, false));

            // Маленький круг (радиус 4 мм) — на нём хорда заметно короче дуги.
            yield return ("circle4", Curve.FromPolyline(
                Enumerable.Range(0, 120).Select(i => new Point2D(4 * Math.Cos(2 * Math.PI * i / 120), 4 * Math.Sin(2 * Math.PI * i / 120))).ToList(), true));

            // Волна-завиток: плавная линия с крутыми изгибами.
            var wave = new List<Point2D>();
            for (double x = 0; x <= 80; x += 0.25)
            {
                wave.Add(new Point2D(x, 6 * Math.Sin(x / 4)));
            }

            yield return ("wave", Curve.FromPolyline(wave));

            // Буква «S» толстым контуром и «O» (две кривые) — замкнутые с плавными и острыми местами.
            yield return ("letter-v", Curve.FromPolyline(new[]
            {
                new Point2D(0, 30), new Point2D(6, 30), new Point2D(12, 8), new Point2D(18, 30), new Point2D(24, 30), new Point2D(15, 0), new Point2D(9, 0),
            }, true));
        }
    }
}
