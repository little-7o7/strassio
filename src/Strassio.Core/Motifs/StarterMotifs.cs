using System;
using System.Collections.Generic;
using System.Linq;
using Strassio.Core.Geometry;
using Strassio.Core.Methods;
using Strassio.Core.Placement;

namespace Strassio.Core.Motifs
{
    /// <summary>Камень для готовых мотивов: имя размера из таблицы пользователя и диаметр.</summary>
    public sealed class StarterSize
    {
        public StarterSize(string name, double diameterMm)
        {
            Name = name;
            DiameterMm = diameterMm;
        }

        public string Name { get; }

        public double DiameterMm { get; }
    }

    /// <summary>Цвет для готовых мотивов: имя из таблицы и «#RRGGBB».</summary>
    public sealed class StarterColor
    {
        public StarterColor(string name, string rgb)
        {
            Name = name;
            Rgb = rgb;
        }

        public string Name { get; }

        public string Rgb { get; }
    }

    /// <summary>Из чего собирать готовые мотивы: три размера (мелкий, основной, крупный) и два цвета.</summary>
    public sealed class StarterKit
    {
        public StarterKit(StarterSize small, StarterSize main, StarterSize large, StarterColor color, StarterColor accent, double gapMm)
        {
            Small = small;
            Main = main;
            Large = large;
            Color = color;
            Accent = accent;
            GapMm = gapMm;
        }

        public StarterSize Small { get; }

        public StarterSize Main { get; }

        public StarterSize Large { get; }

        public StarterColor Color { get; }

        public StarterColor Accent { get; }

        public double GapMm { get; }

        /// <summary>
        /// Набор из таблицы пользователя: основной — выбранный размер, мелкий и крупный — соседние по
        /// диаметру (если их нет — тот же основной). Акцент — другой цвет того же размера.
        /// </summary>
        public static StarterKit From(IReadOnlyList<StarterSize> sizes, StarterSize main, IReadOnlyList<StarterColor> colors, StarterColor color, double gapMm)
        {
            StarterSize small = sizes.Where(s => s.DiameterMm < main.DiameterMm - 1e-6).OrderByDescending(s => s.DiameterMm).FirstOrDefault() ?? main;
            StarterSize large = sizes.Where(s => s.DiameterMm > main.DiameterMm + 1e-6).OrderBy(s => s.DiameterMm).FirstOrDefault() ?? main;
            StarterColor accent = colors.FirstOrDefault(c => !string.Equals(c.Name, color.Name, StringComparison.CurrentCultureIgnoreCase)) ?? color;
            return new StarterKit(small, main, large, color, accent, Math.Max(0, gapMm));
        }
    }

    /// <summary>
    /// Готовые мотивы и рамки в комплекте (пункт TRW-2 прокачки): сердца, звёзды, цветы, листик, снежинка,
    /// бабочка, корона, рамки и каймы. Каждый строится из простых фигур теми же методами, что и всё
    /// остальное, под камни пользователя — поэтому размеры и цвета в мотивах всегда из его таблицы.
    /// Названия — ключи "motif.starter.*" в файлах языков.
    /// </summary>
    public static class StarterMotifs
    {
        /// <summary>Версия набора: когда добавятся новые мотивы, плагин один раз доложит их пользователю.</summary>
        public const int Version = 1;

        /// <summary>Ключи названий в порядке набора.</summary>
        public static readonly IReadOnlyList<string> Keys = new[]
        {
            "heartOutline", "heartFilled", "starOutline", "starFilled", "ring", "dot", "drop", "diamond", "crescent",
            "daisy", "flower6", "flowerRays", "sun", "leaf", "snowflake", "butterfly", "crown", "swirl", "bow",
            "frameCircle", "frameDouble", "frameOval", "frameRounded",
            "borderWave", "borderScallop", "borderZigzag", "borderChain",
        };

        /// <summary>Все мотивы набора; <paramref name="name"/> даёт название по ключу (без размера).</summary>
        public static List<Motif> Build(StarterKit kit, Func<string, string> name)
        {
            var result = new List<Motif>();
            foreach (string key in Keys)
            {
                List<Part> parts = Parts(key, kit);
                List<MotifStone> stones = Place(parts, kit);
                if (stones.Count > 0)
                {
                    result.Add(Motif.FromStones($"{name(key)} ({kit.Main.Name})", stones));
                }
            }

            return result;
        }

        /// <summary>Одна деталь мотива: фигура, метод, камень и цвет.</summary>
        private sealed class Part
        {
            public Part(IReadOnlyList<Curve> curves, MethodKind kind, StarterSize size, StarterColor color)
            {
                Curves = curves;
                Kind = kind;
                Size = size;
                Color = color;
            }

            public Part(Point2D single, StarterSize size, StarterColor color)
            {
                Curves = Array.Empty<Curve>();
                Single = single;
                Size = size;
                Color = color;
            }

            public IReadOnlyList<Curve> Curves { get; }

            public MethodKind Kind { get; }

            public Point2D? Single { get; }

            public StarterSize Size { get; }

            public StarterColor Color { get; }
        }

        /// <summary>
        /// Раскладывает детали по порядку. Камень, задевающий уже поставленный, пропускается — так стыки
        /// деталей (лепесток и середина, луч и круг) всегда чистые.
        /// </summary>
        private static List<MotifStone> Place(IReadOnlyList<Part> parts, StarterKit kit)
        {
            var stones = new List<MotifStone>();
            var p = new MethodParameters { GapMm = kit.GapMm };
            foreach (Part part in parts)
            {
                IEnumerable<PlacedStone> placed;
                if (part.Single != null)
                {
                    placed = new[] { new PlacedStone(part.Single.Value, part.Size.DiameterMm, false) };
                }
                else
                {
                    var table = new Dictionary<string, double> { [part.Size.Name] = part.Size.DiameterMm };
                    placed = MethodRunner.Run(part.Kind, part.Curves, part.Size.DiameterMm, p, table).Stones;
                }

                // Внутри детали шаг держит сам метод; проверяем только стык с прежними деталями.
                int before = stones.Count;
                foreach (PlacedStone s in placed)
                {
                    bool clash = false;
                    for (int i = 0; i < before && !clash; i++)
                    {
                        clash = Point2D.Distance(stones[i].Offset, s.Center) < (stones[i].DiameterMm + s.DiameterMm) / 2 + kit.GapMm - 1e-6;
                    }

                    if (!clash)
                    {
                        stones.Add(new MotifStone(s.Center.X, s.Center.Y, s.DiameterMm, part.Size.Name, part.Color.Name, part.Color.Rgb));
                    }
                }
            }

            return stones;
        }

        private static List<Part> Parts(string key, StarterKit k)
        {
            double u = k.Main.DiameterMm + k.GapMm; // шаг основного камня — мера всех мотивов
            StarterSize s = k.Small, m = k.Main, l = k.Large;
            StarterColor c = k.Color, a = k.Accent;
            Part Line(MethodKind kind, StarterSize size, StarterColor color, params Curve[] curves) => new Part(curves, kind, size, color);
            var o = Point2D.Zero;

            switch (key)
            {
                case "heartOutline":
                    return new List<Part> { Line(MethodKind.L1, m, c, Heart(12 * u)) };
                case "heartFilled":
                    return new List<Part> { Line(MethodKind.F3, m, c, Heart(10 * u)) };
                case "starOutline":
                    return new List<Part> { Line(MethodKind.L1, m, c, Star(5, 7 * u, 2.9 * u)) };
                case "starFilled":
                    return new List<Part> { Line(MethodKind.F3, m, c, Star(5, 6 * u, 2.6 * u)) };
                case "ring":
                    return new List<Part> { Line(MethodKind.L1, m, c, Ellipse(o, 5 * u, 5 * u, 0)) };
                case "dot":
                    return new List<Part> { new Part(o, l, a), Line(MethodKind.F9, m, c, Ellipse(o, 3.6 * u, 3.6 * u, 0)) };
                case "drop":
                    return new List<Part> { Line(MethodKind.L1, m, c, Drop(10 * u)) };
                case "diamond":
                    return new List<Part> { Line(MethodKind.F3, m, c, Polygon(true, P(0, 4.5 * u), P(3 * u, 0), P(0, -4.5 * u), P(-3 * u, 0))) };
                case "crescent":
                    return new List<Part> { Line(MethodKind.F3, m, a, Crescent(5 * u)) };

                case "daisy":
                {
                    var parts = new List<Part> { new Part(o, l, a) };
                    for (int i = 0; i < 5; i++)
                    {
                        double ang = Math.PI / 2 + i * 2 * Math.PI / 5;
                        parts.Add(Line(MethodKind.L1, m, c, Ellipse(Polar(3.9 * u, ang), 2.2 * u, 1.3 * u, ang)));
                    }

                    return parts;
                }

                case "flower6":
                {
                    var parts = new List<Part> { new Part(o, l, a) };
                    for (int i = 0; i < 6; i++)
                    {
                        double ang = Math.PI / 2 + i * Math.PI / 3;
                        parts.Add(Line(MethodKind.F3, m, c, Ellipse(Polar(2.7 * u, ang), 1.9 * u, 1.05 * u, ang)));
                    }

                    return parts;
                }

                case "flowerRays":
                {
                    var parts = new List<Part> { new Part(o, l, a) };
                    for (int i = 0; i < 8; i++)
                    {
                        double ang = Math.PI / 2 + i * Math.PI / 4;
                        parts.Add(new Part(Polar(4.6 * u, ang), m, a));
                        parts.Add(Line(MethodKind.L1, s, c, Polygon(false, Polar(1.4 * u, ang), Polar(3.8 * u, ang))));
                    }

                    return parts;
                }

                case "sun":
                {
                    var parts = new List<Part> { Line(MethodKind.F9, m, a, Ellipse(o, 2.6 * u, 2.6 * u, 0)) };
                    for (int i = 0; i < 12; i++)
                    {
                        double ang = i * Math.PI / 6;
                        parts.Add(Line(MethodKind.L1, i % 2 == 0 ? m : s, c, Polygon(false, Polar(3.7 * u, ang), Polar(i % 2 == 0 ? 6.2 * u : 5.2 * u, ang))));
                    }

                    return parts;
                }

                case "leaf":
                    return new List<Part>
                    {
                        Line(MethodKind.L1, m, c, Leaf(11 * u, 5 * u)),
                        Line(MethodKind.L1, s, a, Polygon(false, P(-4.2 * u, 0), P(4.2 * u, 0))),
                    };

                case "snowflake":
                {
                    var parts = new List<Part> { new Part(o, l, a) };
                    for (int i = 0; i < 6; i++)
                    {
                        double ang = Math.PI / 2 + i * Math.PI / 3;
                        parts.Add(Line(MethodKind.L1, m, c, Polygon(false, Polar(1.3 * u, ang), Polar(6 * u, ang))));
                        Point2D at = Polar(3.8 * u, ang);
                        parts.Add(Line(MethodKind.L1, s, c, Polygon(false, at + Polar(0.9 * u, ang + 0.9), at + Polar(2.2 * u, ang + 0.9))));
                        parts.Add(Line(MethodKind.L1, s, c, Polygon(false, at + Polar(0.9 * u, ang - 0.9), at + Polar(2.2 * u, ang - 0.9))));
                    }

                    return parts;
                }

                case "butterfly":
                    return new List<Part>
                    {
                        Line(MethodKind.L1, s, a, Polygon(false, P(0, -3.4 * u), P(0, 3.0 * u))),
                        Line(MethodKind.L1, m, c, Ellipse(P(3.2 * u, 2.4 * u), 3.0 * u, 2.1 * u, 0.55)),
                        Line(MethodKind.L1, m, c, Ellipse(P(-3.2 * u, 2.4 * u), 3.0 * u, 2.1 * u, -0.55)),
                        Line(MethodKind.L1, m, c, Ellipse(P(2.6 * u, -2.6 * u), 2.2 * u, 1.6 * u, -0.6)),
                        Line(MethodKind.L1, m, c, Ellipse(P(-2.6 * u, -2.6 * u), 2.2 * u, 1.6 * u, 0.6)),
                    };

                case "crown":
                    return new List<Part>
                    {
                        new Part(P(-6 * u, 4.6 * u), l, a), new Part(P(0, 5.6 * u), l, a), new Part(P(6 * u, 4.6 * u), l, a),
                        Line(MethodKind.L1, m, c, Polygon(true,
                            P(-5 * u, -3 * u), P(5 * u, -3 * u), P(6 * u, 3.6 * u), P(3 * u, 0.4 * u),
                            P(0, 4.4 * u), P(-3 * u, 0.4 * u), P(-6 * u, 3.6 * u))),
                    };

                case "swirl":
                    return new List<Part> { new Part(o, l, a), Line(MethodKind.L1, m, c, Spiral(1.5 * u, 6 * u, 2.25)) };

                case "bow":
                    return new List<Part>
                    {
                        Line(MethodKind.F9, m, a, Ellipse(o, 1.3 * u, 1.3 * u, 0)),
                        Line(MethodKind.L1, m, c, Polygon(true, P(2.2 * u, 0.9 * u), P(7 * u, 3.6 * u), P(7 * u, -3.6 * u), P(2.2 * u, -0.9 * u))),
                        Line(MethodKind.L1, m, c, Polygon(true, P(-2.2 * u, 0.9 * u), P(-7 * u, 3.6 * u), P(-7 * u, -3.6 * u), P(-2.2 * u, -0.9 * u))),
                        Line(MethodKind.L1, s, c, Polygon(false, P(1.2 * u, -2.0 * u), P(3.4 * u, -6 * u))),
                        Line(MethodKind.L1, s, c, Polygon(false, P(-1.2 * u, -2.0 * u), P(-3.4 * u, -6 * u))),
                    };

                case "frameCircle":
                    return new List<Part> { Line(MethodKind.L1, m, c, Ellipse(o, 20 * u, 20 * u, 0)) };
                case "frameDouble":
                    return new List<Part>
                    {
                        Line(MethodKind.L1, m, c, Ellipse(o, 20 * u, 20 * u, 0)),
                        Line(MethodKind.L1, s, a, Ellipse(o, 20 * u - (m.DiameterMm + s.DiameterMm) / 2 - 1.5 * k.GapMm - 0.4, 20 * u - (m.DiameterMm + s.DiameterMm) / 2 - 1.5 * k.GapMm - 0.4, 0)),
                    };
                case "frameOval":
                    return new List<Part> { Line(MethodKind.L1, m, c, Ellipse(o, 26 * u, 17 * u, 0)) };
                case "frameRounded":
                    return new List<Part> { Line(MethodKind.L1, m, c, RoundedRect(34 * u, 24 * u, 5 * u)) };

                case "borderWave":
                    return new List<Part>
                    {
                        Line(MethodKind.L1, m, c, Wave(16 * u, 1.6 * u)),
                    };
                case "borderScallop":
                    return new List<Part>
                    {
                        Line(MethodKind.L1, m, c, Arc(o, 4 * u, Math.PI, 2 * Math.PI)),
                        new Part(P(0, -1.2 * u), l, a),
                    };
                case "borderZigzag":
                    return new List<Part>
                    {
                        Line(MethodKind.L1, m, c, Polygon(false, P(-6 * u, -2 * u), P(-2 * u, 2 * u), P(2 * u, -2 * u), P(6 * u, 2 * u))),
                    };
                case "borderChain":
                    return new List<Part>
                    {
                        new Part(o, l, c),
                        new Part(P(-(l.DiameterMm + s.DiameterMm) / 2 - k.GapMm, 0), s, a),
                        new Part(P((l.DiameterMm + s.DiameterMm) / 2 + k.GapMm, 0), s, a),
                    };

                default:
                    return new List<Part>();
            }
        }

        // ---- Фигуры (все с центром в нуле, мм) ---------------------------------------------------

        private static Point2D P(double x, double y) => new Point2D(x, y);

        private static Point2D Polar(double r, double angle) => new Point2D(r * Math.Cos(angle), r * Math.Sin(angle));

        private static Curve Polygon(bool closed, params Point2D[] points) => Curve.FromPolyline(points, closed);

        private static Curve Sampled(Func<double, Point2D> f, int n, bool closed)
        {
            int count = closed ? n : n + 1;
            return Curve.FromPolyline(Enumerable.Range(0, count).Select(i => f((double)i / n)).ToList(), closed);
        }

        private static Curve Ellipse(Point2D c, double rx, double ry, double angle)
        {
            double cos = Math.Cos(angle), sin = Math.Sin(angle);
            return Sampled(t =>
            {
                double x = rx * Math.Cos(2 * Math.PI * t), y = ry * Math.Sin(2 * Math.PI * t);
                return new Point2D(c.X + x * cos - y * sin, c.Y + x * sin + y * cos);
            }, 120, true);
        }

        private static Curve Arc(Point2D c, double r, double from, double to) =>
            Sampled(t => c + Polar(r, from + (to - from) * t), 60, false);

        /// <summary>Сердце шириной <paramref name="w"/> (классическая кривая, середина габарита в нуле).</summary>
        private static Curve Heart(double w)
        {
            double k = w / 32.0;
            return Sampled(t =>
            {
                double a = 2 * Math.PI * t, sn = Math.Sin(a);
                return new Point2D(k * 16 * sn * sn * sn, k * (13 * Math.Cos(a) - 5 * Math.Cos(2 * a) - 2 * Math.Cos(3 * a) - Math.Cos(4 * a) + 2.5));
            }, 160, true);
        }

        private static Curve Star(int rays, double outer, double inner) =>
            Curve.FromPolyline(Enumerable.Range(0, 2 * rays).Select(i => Polar(i % 2 == 0 ? outer : inner, Math.PI / 2 + i * Math.PI / rays)).ToList(), true);

        /// <summary>Капля высотой <paramref name="h"/>: острый кончик вверху, круглый низ.</summary>
        private static Curve Drop(double h)
        {
            // Классическая капля x = cos t, y = sin t · sin(t/2), повёрнутая кончиком вверх.
            double halfW = 0.31 * h / 0.7698;
            return Sampled(t =>
            {
                double a = 2 * Math.PI * t;
                return new Point2D(Math.Sin(a) * Math.Sin(a / 2) * halfW, Math.Cos(a) * h / 2);
            }, 140, true);
        }

        /// <summary>Полумесяц: круг радиуса <paramref name="r"/> минус круг 0,8 r, сдвинутый вправо.</summary>
        private static Curve Crescent(double r)
        {
            double e = 0.45 * r, ri = 0.8 * r;
            double x = (r * r - ri * ri + e * e) / (2 * e);
            double outer0 = Math.Acos(x / r), inner0 = Math.Acos((x - e) / ri);
            var pts = new List<Point2D>();
            const int n = 80;
            for (int i = 0; i <= n; i++)
            {
                pts.Add(Polar(r, outer0 + (2 * Math.PI - 2 * outer0) * i / n));
            }

            for (int i = 1; i < n; i++)
            {
                pts.Add(new Point2D(e, 0) + Polar(ri, 2 * Math.PI - inner0 - (2 * Math.PI - 2 * inner0) * i / n));
            }

            return Curve.FromPolyline(pts, true);
        }

        /// <summary>Лист длиной <paramref name="len"/> и шириной <paramref name="width"/> вдоль оси X, острые концы.</summary>
        private static Curve Leaf(double len, double width) =>
            Sampled(t =>
            {
                bool upper = t < 0.5;
                double x = upper ? len / 2 - len * 2 * t : -len / 2 + len * (2 * t - 1);
                double y = width / 2 * (1 - 4 * x * x / (len * len));
                return new Point2D(x, upper ? y : -y);
            }, 140, true);

        private static Curve Spiral(double from, double to, double turns) =>
            Sampled(t => Polar(from + (to - from) * t, Math.PI / 2 - 2 * Math.PI * turns * t), 240, false);

        private static Curve Wave(double len, double amp) =>
            Sampled(t => new Point2D(-len / 2 + len * t, amp * Math.Sin(2 * Math.PI * t)), 120, false);

        private static Curve RoundedRect(double w, double h, double r)
        {
            var pts = new List<Point2D>();
            var corners = new[] { P(w / 2 - r, h / 2 - r), P(-w / 2 + r, h / 2 - r), P(-w / 2 + r, -h / 2 + r), P(w / 2 - r, -h / 2 + r) };
            for (int c = 0; c < 4; c++)
            {
                for (int i = 0; i <= 12; i++)
                {
                    pts.Add(corners[c] + Polar(r, c * Math.PI / 2 + i * Math.PI / 24));
                }
            }

            return Curve.FromPolyline(pts, true);
        }
    }
}
