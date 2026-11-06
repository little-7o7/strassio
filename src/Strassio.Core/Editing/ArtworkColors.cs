using System;
using System.Collections.Generic;
using Strassio.Core.Geometry;
using Strassio.Core.Stones;

namespace Strassio.Core.Editing
{
    /// <summary>
    /// Цветная фигура рисунка для «Перекрасить по рисунку»: замкнутые контуры, цвет заливки и порядок
    /// по высоте (больше — выше, как у <see cref="DocStone.Order"/>). Контуры с отверстиями — по
    /// чётно-нечётному правилу, как в заливке формы: центр в дырке буквы «О» в фигуру не попадает.
    /// </summary>
    public sealed class ColorRegion
    {
        private readonly List<FlattenedCurve> contours = new List<FlattenedCurve>();

        public ColorRegion(IReadOnlyList<Curve> contours, byte red, byte green, byte blue, int order)
        {
            Red = red;
            Green = green;
            Blue = blue;
            Order = order;

            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (Curve curve in contours)
            {
                // Незамкнутая линия ничего не заливает — она не цветная область.
                if (!curve.IsClosed || curve.Segments.Count == 0)
                {
                    continue;
                }

                FlattenedCurve flat = CurveFlattener.Flatten(curve, 0.05);
                if (flat.Points.Count < 3)
                {
                    continue;
                }

                this.contours.Add(flat);
                foreach (FlattenedPoint p in flat.Points)
                {
                    minX = Math.Min(minX, p.Position.X);
                    minY = Math.Min(minY, p.Position.Y);
                    maxX = Math.Max(maxX, p.Position.X);
                    maxY = Math.Max(maxY, p.Position.Y);
                }
            }

            Min = new Point2D(minX, minY);
            Max = new Point2D(maxX, maxY);
        }

        public byte Red { get; }

        public byte Green { get; }

        public byte Blue { get; }

        /// <summary>Порядок по высоте: больше — выше (нарисована поверх).</summary>
        public int Order { get; }

        /// <summary>Есть хоть один замкнутый контур — фигура что-то закрашивает.</summary>
        public bool IsEmpty => contours.Count == 0;

        public Point2D Min { get; }

        public Point2D Max { get; }

        /// <summary>Точка внутри фигуры: внутри нечётного числа её контуров.</summary>
        public bool Contains(Point2D point)
        {
            if (IsEmpty || point.X < Min.X || point.X > Max.X || point.Y < Min.Y || point.Y > Max.Y)
            {
                return false;
            }

            bool inside = false;
            foreach (FlattenedCurve contour in contours)
            {
                if (PointInPolygon.IsInside(contour, point))
                {
                    inside = !inside;
                }
            }

            return inside;
        }
    }

    /// <summary>
    /// «Перекрасить по рисунку» (docs/SPEC.md, раздел 3.4): каждая страза берёт цвет той фигуры рисунка,
    /// в которую попадает её центр (если фигуры перекрываются — самой верхней), а цвет камня — ближайший
    /// к цвету фигуры из таблицы камней. Так многоцветный рисунок выкладывается одной сплошной заливкой:
    /// на границе двух цветов нет ни шва, ни дыры, ни наложения.
    /// </summary>
    public static class ArtworkColors
    {
        /// <summary>
        /// Номер фигуры (в списке <paramref name="regions"/>) для каждой стразы: самая верхняя из тех,
        /// куда попал центр. -1 — центр не попал ни в одну фигуру, цвет стразы не меняется.
        /// </summary>
        public static int[] RegionOf(IReadOnlyList<Point2D> centers, IReadOnlyList<ColorRegion> regions)
        {
            var result = new int[centers.Count];
            for (int i = 0; i < centers.Count; i++)
            {
                int best = -1;
                for (int r = 0; r < regions.Count; r++)
                {
                    if ((best < 0 || regions[r].Order > regions[best].Order) && regions[r].Contains(centers[i]))
                    {
                        best = r;
                    }
                }

                result[i] = best;
            }

            return result;
        }

        /// <summary>
        /// Ближайший цвет палитры — как его видит глаз (разница в пространстве CIE Lab), а не по сумме
        /// RGB: тёмно-красный ближе к красному, чем к чёрному. -1 — в палитре нет ни одного цвета.
        /// </summary>
        public static int Nearest(byte red, byte green, byte blue, IReadOnlyList<StoneColor> palette)
        {
            (double L, double A, double B) target = ToLab(red, green, blue);
            int best = -1;
            double bestDistance = double.MaxValue;
            for (int i = 0; i < palette.Count; i++)
            {
                if (!palette[i].TryGetRgb(out byte r, out byte g, out byte b))
                {
                    continue;
                }

                double distance = Distance(target, ToLab(r, g, b));
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>Насколько два цвета различаются на глаз (ΔE по CIE76). Меньше 2,3 — почти не отличить.</summary>
        public static double Difference(byte r1, byte g1, byte b1, byte r2, byte g2, byte b2) =>
            Distance(ToLab(r1, g1, b1), ToLab(r2, g2, b2));

        private static double Distance((double L, double A, double B) x, (double L, double A, double B) y)
        {
            double dl = x.L - y.L, da = x.A - y.A, db = x.B - y.B;
            return Math.Sqrt(dl * dl + da * da + db * db);
        }

        /// <summary>sRGB → CIE Lab (белая точка D65).</summary>
        private static (double L, double A, double B) ToLab(byte red, byte green, byte blue)
        {
            double r = Linear(red), g = Linear(green), b = Linear(blue);
            double x = (0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047;
            double y = 0.2126 * r + 0.7152 * g + 0.0722 * b;
            double z = (0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883;
            double fx = F(x), fy = F(y), fz = F(z);
            return (116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
        }

        private static double Linear(byte channel)
        {
            double c = channel / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        private static double F(double t) => t > 0.008856 ? Math.Pow(t, 1.0 / 3) : 7.787 * t + 16.0 / 116;
    }
}
