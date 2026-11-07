using System;
using System.Collections.Generic;
using System.Linq;
using Strassio.Core.Geometry;

namespace Strassio.Core.Motifs
{
    /// <summary>Рамка вокруг монограммы.</summary>
    public enum MonogramFrame
    {
        /// <summary>Без рамки — только буквы.</summary>
        None,

        /// <summary>Круг.</summary>
        Circle,

        /// <summary>Овал по форме букв (вытянут по длинной стороне).</summary>
        Oval,

        /// <summary>Ромб.</summary>
        Diamond,

        /// <summary>Прямоугольник со скруглёнными углами.</summary>
        Rounded,

        /// <summary>Щит: ровный верх, бока сходятся книзу в острие.</summary>
        Shield,
    }

    public sealed class MonogramOptions
    {
        public MonogramFrame Frame { get; set; } = MonogramFrame.Circle;

        /// <summary>Высота обычной буквы, мм.</summary>
        public double LetterHeightMm { get; set; } = 40;

        /// <summary>Расстояние между буквами (между их рамками-габаритами), мм.</summary>
        public double LetterGapMm { get; set; } = 2;

        /// <summary>Во сколько раз средняя буква из трёх крупнее крайних (классическая монограмма: фамилия в середине).</summary>
        public double MiddleScale { get; set; } = 1.35;

        /// <summary>Сколько мм от букв до линии рамки (до середины ряда страз рамки).</summary>
        public double FrameGapMm { get; set; } = 4;
    }

    /// <summary>Готовая монограмма: буквы уже на своих местах и рамка вокруг них.</summary>
    public sealed class MonogramLayout
    {
        public MonogramLayout(IReadOnlyList<IReadOnlyList<Curve>> letters, Curve? frame, double widthMm, double heightMm)
        {
            Letters = letters;
            Frame = frame;
            WidthMm = widthMm;
            HeightMm = heightMm;
        }

        /// <summary>Контуры каждой буквы (у «О», «В» и т.п. несколько — с дырками).</summary>
        public IReadOnlyList<IReadOnlyList<Curve>> Letters { get; }

        /// <summary>Рамка — замкнутая кривая; null, если рамка не нужна.</summary>
        public Curve? Frame { get; }

        /// <summary>Габарит всей монограммы (с рамкой), мм.</summary>
        public double WidthMm { get; }

        public double HeightMm { get; }
    }

    /// <summary>
    /// Монограмма (пункт TRW-1 прокачки): 1–3 буквы одного шрифта выстраиваются в ряд, при трёх буквах
    /// средняя крупнее, и вокруг них строится рамка — круг, овал, ромб, скруглённый прямоугольник или щит —
    /// точно на заданном расстоянии от ближайшей точки букв. Буквы приходят уже кривыми (из CorelDRAW),
    /// сами шрифты Core не читает.
    /// </summary>
    public static class Monogram
    {
        private const double Kappa = 0.5522847498; // «круговой» множитель контрольных точек Безье

        /// <summary>Расставляет буквы с центром монограммы в <paramref name="center"/> и строит рамку.</summary>
        public static MonogramLayout Arrange(IReadOnlyList<IReadOnlyList<Curve>> letters, MonogramOptions options, Point2D center)
        {
            List<IReadOnlyList<Curve>> source = letters.Where(l => l != null && l.Count > 0).ToList();
            if (source.Count == 0)
            {
                throw new ArgumentException("Нужна хотя бы одна буква.", nameof(letters));
            }

            List<Box> boxes = source.Select(BoxOf).ToList();
            double tallest = boxes.Max(b => b.Height);
            if (tallest <= 1e-9)
            {
                throw new ArgumentException("У букв нет высоты.", nameof(letters));
            }

            // Общий масштаб для всех букв: строчные остаются ниже заглавных, как в шрифте.
            double k = options.LetterHeightMm / tallest;
            var scales = source.Select((_, i) => source.Count == 3 && i == 1 ? k * Math.Max(0.1, options.MiddleScale) : k).ToList();

            double gap = Math.Max(0, options.LetterGapMm);
            double totalWidth = boxes.Select((b, i) => b.Width * scales[i]).Sum() + gap * (source.Count - 1);
            double cursor = -totalWidth / 2;
            var placed = new List<IReadOnlyList<Curve>>();
            for (int i = 0; i < source.Count; i++)
            {
                Box b = boxes[i];
                double s = scales[i];
                // Левый край буквы — на курсоре, середина по высоте — на оси.
                double dx = cursor - b.MinX * s, dy = -(b.MinY + b.MaxY) / 2 * s;
                placed.Add(source[i].Select(c => Map(c, p => new Point2D(p.X * s + dx, p.Y * s + dy))).ToList());
                cursor += b.Width * s + gap;
            }

            Box content = BoxOf(placed.SelectMany(l => l).ToList());
            Curve? frame = options.Frame == MonogramFrame.None ? null : FitFrame(options.Frame, placed, content, Math.Max(0, options.FrameGapMm));

            Func<Point2D, Point2D> shift = p => new Point2D(p.X + center.X, p.Y + center.Y);
            List<IReadOnlyList<Curve>> finalLetters = placed.Select(l => (IReadOnlyList<Curve>)l.Select(c => Map(c, shift)).ToList()).ToList();
            Curve? finalFrame = frame == null ? null : Map(frame, shift);
            Box all = finalFrame == null ? BoxOf(finalLetters.SelectMany(l => l).ToList()) : BoxOf(new[] { finalFrame });
            return new MonogramLayout(finalLetters, finalFrame, all.Width, all.Height);
        }

        /// <summary>
        /// Рамка нужной формы, увеличенная ровно настолько, чтобы все буквы были внутри и не ближе
        /// <paramref name="gap"/> к её линии. Все рамки выпуклые, поэтому достаточно проверить вершины
        /// выпуклой оболочки букв: ближе всех к выпуклой рамке всегда одна из них.
        /// </summary>
        private static Curve FitFrame(MonogramFrame kind, IReadOnlyList<IReadOnlyList<Curve>> letters, Box content, double gap)
        {
            List<Point2D> points = letters.SelectMany(l => l).SelectMany(c => CurveFlattener.Flatten(c, 0.02).Points.Select(p => p.Position)).ToList();
            List<Point2D> hull = ConvexHull(points);

            double halfW = content.Width / 2 + gap, halfH = content.Height / 2 + gap;
            double aspect = Aspect(kind, halfW / Math.Max(halfH, 1e-9), content.Width / Math.Max(content.Height, 1e-9));
            Curve unit = UnitFrame(kind, aspect);

            bool Fits(double scale)
            {
                FlattenedCurve f = CurveFlattener.Flatten(Map(unit, p => p * scale), 0.01);
                foreach (Point2D p in hull)
                {
                    if (!PointInPolygon.IsInside(f, p) || PointInPolygon.DistanceToBoundary(f, p) < gap)
                    {
                        return false;
                    }
                }

                return true;
            }

            double lo = 0, hi = Math.Max(halfW, halfH) * 4 + 1;
            while (!Fits(hi))
            {
                hi *= 2;
            }

            for (int i = 0; i < 50 && hi - lo > 1e-4; i++)
            {
                double mid = (lo + hi) / 2;
                if (Fits(mid))
                {
                    hi = mid;
                }
                else
                {
                    lo = mid;
                }
            }

            return Map(unit, p => p * hi);
        }

        /// <summary>Ширина рамки к её высоте (у единичной рамки половина высоты = 1).</summary>
        private static double Aspect(MonogramFrame kind, double padded, double bare)
        {
            switch (kind)
            {
                case MonogramFrame.Circle:
                    return 1;
                case MonogramFrame.Oval:
                    // Овал, похожий на круг, выглядит как ошибка: вытягиваем хотя бы в 1,25 раза.
                    return padded >= 1 ? Math.Max(padded, 1.25) : Math.Min(padded, 0.8);
                case MonogramFrame.Diamond:
                    return Clamp(bare, 0.6, 1.7);
                case MonogramFrame.Shield:
                    return Clamp(padded, 0.75, 1.3);
                default:
                    return padded;
            }
        }

        /// <summary>Рамка с центром в нуле, половина высоты 1, половина ширины <paramref name="a"/>.</summary>
        private static Curve UnitFrame(MonogramFrame kind, double a)
        {
            var P = (Func<double, double, Point2D>)((x, y) => new Point2D(x, y));
            switch (kind)
            {
                case MonogramFrame.Diamond:
                    return Curve.FromPolyline(new[] { P(a, 0), P(0, 1), P(-a, 0), P(0, -1) }, true);

                case MonogramFrame.Rounded:
                {
                    double r = 0.3 * Math.Min(a, 1), c = r * (1 - Kappa);
                    var s = new List<CurveSegment>
                    {
                        CurveSegment.Line(P(a - r, 1), P(-a + r, 1)),
                        CurveSegment.Cubic(P(-a + r, 1), P(-a + c, 1), P(-a, 1 - c), P(-a, 1 - r)),
                        CurveSegment.Line(P(-a, 1 - r), P(-a, -1 + r)),
                        CurveSegment.Cubic(P(-a, -1 + r), P(-a, -1 + c), P(-a + c, -1), P(-a + r, -1)),
                        CurveSegment.Line(P(-a + r, -1), P(a - r, -1)),
                        CurveSegment.Cubic(P(a - r, -1), P(a - c, -1), P(a, -1 + c), P(a, -1 + r)),
                        CurveSegment.Line(P(a, -1 + r), P(a, 1 - r)),
                        CurveSegment.Cubic(P(a, 1 - r), P(a, 1 - c), P(a - c, 1), P(a - r, 1)),
                    };
                    return new Curve(s, true);
                }

                case MonogramFrame.Shield:
                {
                    // Ровный верх, прямые бока до середины, затем плавно к острию внизу. Выпуклый.
                    var s = new List<CurveSegment>
                    {
                        CurveSegment.Line(P(a, 1), P(-a, 1)),
                        CurveSegment.Line(P(-a, 1), P(-a, 0.15)),
                        CurveSegment.Cubic(P(-a, 0.15), P(-a, -0.55), P(-0.5 * a, -1.0), P(0, -1.4)),
                        CurveSegment.Cubic(P(0, -1.4), P(0.5 * a, -1.0), P(a, -0.55), P(a, 0.15)),
                        CurveSegment.Line(P(a, 0.15), P(a, 1)),
                    };
                    return new Curve(s, true);
                }

                default: // круг и овал — эллипс из четырёх дуг Безье
                {
                    double ka = Kappa * a, kb = Kappa;
                    var s = new List<CurveSegment>
                    {
                        CurveSegment.Cubic(P(a, 0), P(a, kb), P(ka, 1), P(0, 1)),
                        CurveSegment.Cubic(P(0, 1), P(-ka, 1), P(-a, kb), P(-a, 0)),
                        CurveSegment.Cubic(P(-a, 0), P(-a, -kb), P(-ka, -1), P(0, -1)),
                        CurveSegment.Cubic(P(0, -1), P(ka, -1), P(a, -kb), P(a, 0)),
                    };
                    return new Curve(s, true);
                }
            }
        }

        /// <summary>Та же кривая, все узлы и контрольные точки пропущены через <paramref name="f"/>.</summary>
        public static Curve Map(Curve curve, Func<Point2D, Point2D> f)
        {
            var segments = curve.Segments.Select(s => s.IsLine
                ? CurveSegment.Line(f(s.Start), f(s.End))
                : CurveSegment.Cubic(f(s.Start), f(s.Control1!.Value), f(s.Control2!.Value), f(s.End))).ToList();
            return new Curve(segments, curve.IsClosed);
        }

        /// <summary>Выпуклая оболочка (монотонная цепь Эндрю).</summary>
        private static List<Point2D> ConvexHull(List<Point2D> points)
        {
            List<Point2D> pts = points.OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
            if (pts.Count < 3)
            {
                return pts;
            }

            var hull = new Point2D[2 * pts.Count];
            int k = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                while (k >= 2 && (hull[k - 1] - hull[k - 2]).Cross(pts[i] - hull[k - 2]) <= 0)
                {
                    k--;
                }

                hull[k++] = pts[i];
            }

            for (int i = pts.Count - 2, lower = k + 1; i >= 0; i--)
            {
                while (k >= lower && (hull[k - 1] - hull[k - 2]).Cross(pts[i] - hull[k - 2]) <= 0)
                {
                    k--;
                }

                hull[k++] = pts[i];
            }

            return hull.Take(k - 1).ToList();
        }

        private static double Clamp(double v, double min, double max) => Math.Max(min, Math.Min(max, v));

        private static Box BoxOf(IReadOnlyList<Curve> curves)
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (Curve c in curves)
            {
                foreach (FlattenedPoint fp in CurveFlattener.Flatten(c).Points)
                {
                    Point2D p = fp.Position;
                    minX = Math.Min(minX, p.X);
                    minY = Math.Min(minY, p.Y);
                    maxX = Math.Max(maxX, p.X);
                    maxY = Math.Max(maxY, p.Y);
                }
            }

            return new Box(minX, minY, maxX, maxY);
        }

        private readonly struct Box
        {
            public Box(double minX, double minY, double maxX, double maxY)
            {
                MinX = minX;
                MinY = minY;
                MaxX = maxX;
                MaxY = maxY;
            }

            public double MinX { get; }
            public double MinY { get; }
            public double MaxX { get; }
            public double MaxY { get; }
            public double Width => MaxX - MinX;
            public double Height => MaxY - MinY;
        }
    }
}
