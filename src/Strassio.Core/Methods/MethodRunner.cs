using System;
using System.Collections.Generic;
using System.Linq;
using Strassio.Core.Geometry;
using Strassio.Core.Placement;

namespace Strassio.Core.Methods
{
    /// <summary>Результат метода: стразы и (для «только показать») номера тех, что накладываются.</summary>
    public sealed class MethodResult
    {
        public MethodResult(IReadOnlyList<PlacedStone> stones, IReadOnlyList<int> conflictIndices)
        {
            Stones = stones;
            ConflictIndices = conflictIndices;
        }

        public IReadOnlyList<PlacedStone> Stones { get; }

        /// <summary>Стразы с наложением — при действии «только показать» (раздел 6.4); иначе пусто.</summary>
        public IReadOnlyList<int> ConflictIndices { get; }
    }

    /// <summary>
    /// Запускает метод расстановки с параметрами из докера: переводит <see cref="MethodParameters"/>
    /// в опции алгоритмов Placement и вызывает нужный. Одна точка входа и для CorelDRAW, и для
    /// Preview, и для тестов.
    /// </summary>
    public static class MethodRunner
    {
        /// <param name="contours">Все контуры фигуры (внешний и отверстия), мм.</param>
        /// <param name="stoneDiameterMm">Диаметр выбранного камня.</param>
        public static MethodResult Run(
            MethodKind kind, IReadOnlyList<Curve> contours, double stoneDiameterMm, MethodParameters p)
        {
            if (contours == null || contours.Count == 0)
            {
                throw new ArgumentException("Нужен хотя бы один контур.", nameof(contours));
            }

            if (p == null)
            {
                throw new ArgumentNullException(nameof(p));
            }

            double d = stoneDiameterMm;
            switch (kind)
            {
                case MethodKind.L1:
                    return Plain(LineScatterer.Scatter(OuterContour(contours), LineOptions(d, p)));

                case MethodKind.L2:
                    return AroundLine(OuterContour(contours), d, p);

                case MethodKind.L3:
                    return OffsetLine(OuterContour(contours), d, p);

                case MethodKind.F1:
                case MethodKind.F2:
                    return Plain(GridFiller.Fill(contours, new GridFillOptions
                    {
                        StoneDiameterMm = d,
                        GapMm = p.GapMm,
                        Pattern = kind == MethodKind.F1 ? GridPattern.Square : GridPattern.Honeycomb,
                        AngleDeg = p.AngleDeg,
                        MarginFromEdgeMm = p.EdgeMarginMm,
                    }));

                case MethodKind.F3:
                case MethodKind.F4:
                case MethodKind.F5:
                    return Plain(ContourFiller.Fill(contours, new ContourFillOptions
                    {
                        StoneDiameterMm = d,
                        GapMm = p.GapMm,
                        MarginFromEdgeMm = p.EdgeMarginMm,
                        MaxRings = kind == MethodKind.F3 ? (int?)null : p.Rings,
                        FillCenter = kind != MethodKind.F5,
                        CenterPattern = p.CenterPattern == MethodChoices.PatternSquare ? GridPattern.Square : GridPattern.Honeycomb,
                    }));

                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        /// <summary>
        /// Внешний контур фигуры — самый большой по габаритам. Методы по линии (L1-L3) работают
        /// именно по нему, отверстия им не нужны.
        /// </summary>
        public static Curve OuterContour(IReadOnlyList<Curve> contours)
        {
            Curve best = contours[0];
            double bestSize = -1;

            foreach (Curve contour in contours)
            {
                (double width, double height) = CurveMetrics.BoundingSize(CurveFlattener.Flatten(contour));
                double size = width * height;
                if (size > bestSize)
                {
                    bestSize = size;
                    best = contour;
                }
            }

            return best;
        }

        /// <summary>
        /// Знак смещения «наружу». У замкнутого контура «внутрь» — когда знак смещения совпадает со
        /// знаком площади (см. ContourFiller), значит наружу — противоположный. У незамкнутой линии
        /// «наружу» — просто одна из сторон (положительное смещение), «внутрь» — другая.
        /// </summary>
        internal static double OutwardSign(Curve curve)
        {
            FlattenedCurve flat = CurveFlattener.Flatten(curve);
            if (!flat.IsClosed)
            {
                return 1;
            }

            double area = CurveMetrics.SignedArea(flat);
            return area > 0 ? -1 : 1;
        }

        private static MethodResult Plain(IReadOnlyList<PlacedStone> stones) =>
            new MethodResult(stones, Array.Empty<int>());

        private static LineScatterOptions LineOptions(double d, MethodParameters p)
        {
            var options = new LineScatterOptions
            {
                StoneDiameterMm = d,
                GapMm = p.GapMm,
                StartOffsetMm = p.StartOffsetMm,
                EndMarginMm = p.EndMarginMm,
                Reverse = p.Reverse,
                CornerAngleThresholdDeg = p.CornerAngleDeg,
            };

            switch (p.StepMode)
            {
                case MethodChoices.StepExact:
                    options.Mode = StepMode.ExactStep;
                    options.ExactStepMm = p.ExactStepMm;
                    break;
                case MethodChoices.StepCount:
                    options.Mode = StepMode.ExactCount;
                    options.ExactCount = p.ExactCount;
                    break;
                default:
                    options.Mode = StepMode.FitEven;
                    break;
            }

            return options;
        }

        /// <summary>Опции ряда для L2/L3: зазор и порог угла из параметров, шаг — подгонка.</summary>
        private static LineScatterOptions RowOptions(double d, MethodParameters p, double startOffsetMm) =>
            new LineScatterOptions
            {
                StoneDiameterMm = d,
                GapMm = p.GapMm,
                Mode = StepMode.FitEven,
                StartOffsetMm = startOffsetMm,
                CornerAngleThresholdDeg = p.CornerAngleDeg,
            };

        private static CornerStyle Corners(MethodParameters p) =>
            p.Corners == MethodChoices.CornersSharp ? CornerStyle.Sharp : CornerStyle.Round;

        /// <summary>
        /// L2 «вокруг линии». Ряды идут через «зазор между рядами»; в обе стороны — симметрично
        /// относительно линии (при нечётном числе рядов средний лежит на самой линии). Ряд ближе
        /// к линии идёт в списке раньше — при наложениях он главнее (раздел 6.3).
        /// </summary>
        private static MethodResult AroundLine(Curve curve, double d, MethodParameters p)
        {
            double rowStep = d + p.RowGapMm;
            double outward = OutwardSign(curve);
            int count = Math.Max(1, p.RowCount);

            var rows = new List<(double Offset, int Position)>();
            for (int i = 0; i < count; i++)
            {
                double offset;
                switch (p.RowSide)
                {
                    case MethodChoices.SideOutside:
                        offset = outward * i * rowStep;
                        break;
                    case MethodChoices.SideInside:
                        offset = -outward * i * rowStep;
                        break;
                    default:
                        offset = (i - (count - 1) / 2.0) * rowStep;
                        break;
                }

                rows.Add((offset, i));
            }

            double halfStep = (d + p.GapMm) / 2;
            RowSpec[] specs = rows
                .OrderBy(r => Math.Abs(r.Offset))
                .Select(r => new RowSpec
                {
                    OffsetMm = r.Offset,
                    CornerStyle = Corners(p),
                    ScatterOptions = RowOptions(d, p, p.Stagger && r.Position % 2 == 1 ? halfStep : 0),
                })
                .ToArray();

            IReadOnlyList<PlacedStone> stones = RingScatterer.Scatter(curve, specs);
            if (count == 1)
            {
                return Plain(stones);
            }

            IntersectionAction action =
                p.Intersections == MethodChoices.IntersectShift ? IntersectionAction.Shift :
                p.Intersections == MethodChoices.IntersectShow ? IntersectionAction.ShowOnly :
                IntersectionAction.Remove;

            IntersectionFixResult fixedResult = IntersectionFixer.Fix(stones, new IntersectionFixOptions
            {
                Action = action,

                // Наложение — когда стразы ближе заданных зазоров. Половина меньшего зазора — запас на
                // неточность смещённых кривых (при зазоре 0,2 мм это прежние 0,1 мм).
                MinGapMm = Math.Min(p.GapMm, p.RowGapMm) / 2,
            });

            // Номера налезающих страз нужны только для «только показать»: при «удалить»/«сдвинуть» их
            // уже нет или они перестали налезать.
            return new MethodResult(
                fixedResult.Stones,
                action == IntersectionAction.ShowOnly ? fixedResult.ConflictIndices : Array.Empty<int>());
        }

        /// <summary>L3 «по смещённой линии»: один ряд на заданном расстоянии наружу или внутрь, без наложений.</summary>
        private static MethodResult OffsetLine(Curve curve, double d, MethodParameters p)
        {
            double sign = OutwardSign(curve) * (p.OffsetSide == MethodChoices.SideInside ? -1 : 1);
            var row = new RowSpec
            {
                OffsetMm = sign * p.OffsetMm,
                CornerStyle = Corners(p),
                ScatterOptions = RowOptions(d, p, 0),
            };
            IReadOnlyList<PlacedStone> stones = RingScatterer.Scatter(curve, new[] { row });

            // У острых углов смещённая линия круто изгибается, и соседние стразы ряда могут налезть
            // друг на друга (звезда, сердце — см. Preview "methods"). Лишние убираем, соседей раздвигаем.
            return Plain(IntersectionFixer.Fix(stones, new IntersectionFixOptions { MinGapMm = p.GapMm / 2 }).Stones);
        }
    }
}
