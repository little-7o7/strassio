using System;
using System.Collections.Generic;
using Strassio.Core.Geometry;

namespace Strassio.Core.Methods
{
    /// <summary>Маленький образец для картинки-схемы метода: фигура, камень и параметры.</summary>
    public sealed class MethodSample
    {
        internal MethodSample(Curve shape, double stoneDiameterMm, MethodParameters parameters)
        {
            Shape = shape;
            StoneDiameterMm = stoneDiameterMm;
            Parameters = parameters;
        }

        /// <summary>Фигура примерно 24×24 мм.</summary>
        public Curve Shape { get; }

        public double StoneDiameterMm { get; }

        public MethodParameters Parameters { get; }
    }

    /// <summary>
    /// Картинки-схемы методов в списке докера (docs/SPEC.md, раздел 2.2, пункт 4). Схема не рисуется
    /// вручную, а считается тем же методом на маленькой фигуре — поэтому она всегда честно показывает,
    /// что метод делает, и ни на чью не похожа. Размеры подобраны так, чтобы на значке 22 px было
    /// 8–40 кружков: меньше — не видно идеи, больше — каша.
    /// </summary>
    public static class MethodSamples
    {
        public static MethodSample For(MethodKind kind)
        {
            switch (kind)
            {
                case MethodKind.L1:
                    return new MethodSample(Wave(), 2.6, new MethodParameters { GapMm = 0.5 });
                case MethodKind.L2:
                    return new MethodSample(Wave(), 2.0, new MethodParameters { GapMm = 0.4, RowCount = 3, RowGapMm = 0.4 });
                case MethodKind.L3:
                    return new MethodSample(
                        Circle(11), 2.4,
                        new MethodParameters { GapMm = 0.8, OffsetMm = 4.5, OffsetSide = MethodChoices.SideInside });
                case MethodKind.F1:
                    return new MethodSample(Circle(11), 3.0, new MethodParameters { GapMm = 0.5 });
                case MethodKind.F2:
                    return new MethodSample(Circle(11), 3.0, new MethodParameters { GapMm = 0.5 });
                case MethodKind.F3:
                    return new MethodSample(Circle(11), 3.0, new MethodParameters { GapMm = 0.5 });
                case MethodKind.F4:
                    return new MethodSample(
                        Circle(11), 2.6, new MethodParameters { GapMm = 0.5, Rings = 1, CenterPattern = MethodChoices.PatternSquare });
                case MethodKind.F5:
                    return new MethodSample(Circle(11), 2.6, new MethodParameters { GapMm = 0.5, Rings = 2 });
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        /// <summary>Считает схему: те же стразы, что дал бы метод на этой фигуре.</summary>
        public static MethodResult Run(MethodKind kind, out MethodSample sample)
        {
            sample = For(kind);
            return MethodRunner.Run(kind, new[] { sample.Shape }, sample.StoneDiameterMm, sample.Parameters);
        }

        /// <summary>Пологая волна слева направо — для методов по линии.</summary>
        private static Curve Wave()
        {
            var points = new List<Point2D>();
            for (int i = 0; i <= 48; i++)
            {
                double x = i * 0.5;
                points.Add(new Point2D(x, 12 + 5 * Math.Sin(x / 24 * 2 * Math.PI)));
            }

            return Curve.FromPolyline(points, isClosed: false);
        }

        /// <summary>Круг с центром (12, 12) — для заливок и L3.</summary>
        private static Curve Circle(double radius)
        {
            var points = new List<Point2D>();
            const int n = 72;
            for (int i = 0; i < n; i++)
            {
                double a = 2 * Math.PI * i / n;
                points.Add(new Point2D(12 + radius * Math.Cos(a), 12 + radius * Math.Sin(a)));
            }

            return Curve.FromPolyline(points, isClosed: true);
        }
    }
}
