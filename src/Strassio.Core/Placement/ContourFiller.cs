using System;
using System.Collections.Generic;
using System.Linq;
using Strassio.Core.Geometry;

namespace Strassio.Core.Placement
{
    /// <summary>
    /// Методы F3 «контурная», F4 «комбинированная» и F5 «кант» (docs/SPEC.md, раздел 5): ряды от
    /// края внутрь, повторяя форму. F3 — рядов сколько поместится, центр добивается сеткой;
    /// F5 «кант» — только MaxRings рядов, FillCenter=false, внутри пусто; F4 — MaxRings рядов
    /// по краю + сетка/соты внутри (FillCenter=true).
    /// </summary>
    public static class ContourFiller
    {
        public static List<PlacedStone> Fill(Curve boundary, ContourFillOptions options)
        {
            FlattenedCurve flat = CurveFlattener.Flatten(boundary, options.FlattenToleranceMm);
            double signedArea = CurveMetrics.SignedArea(flat);
            if (Math.Abs(signedArea) < 1e-9)
            {
                return new List<PlacedStone>();
            }

            // Положительный signed area — контур обходится против часовой стрелки, тогда
            // положительное расстояние в CurveOffsetter уводит внутрь (см. CurveOffsetterTests).
            double inwardSign = signedArea > 0 ? 1 : -1;

            double radius = options.StoneDiameterMm / 2;
            double rowSpacing = options.StoneDiameterMm + options.GapMm;
            double firstOffset = radius + options.MarginFromEdgeMm;
            double minAreaForRing = Math.PI * radius * radius * 0.5; // грубый порог «кольцо ещё имеет смысл»

            var result = new List<PlacedStone>();
            var scatterOptions = new LineScatterOptions
            {
                StoneDiameterMm = options.StoneDiameterMm,
                GapMm = options.GapMm,
                Mode = StepMode.FitEven,
                CornerAngleThresholdDeg = options.CornerAngleThresholdDeg,
                FlattenToleranceMm = options.FlattenToleranceMm,
            };

            Curve? lastRingBoundary = null;
            int ringCount = 0;

            // Верхняя граница числа колец с запасом: даже для тонкой длинной формы кольца не могут
            // осмысленно идти дальше половины меньшей стороны её ограничивающего прямоугольника.
            (double width, double height) = CurveMetrics.BoundingSize(flat);
            int safetyMaxRings = Math.Max(4, (int)(Math.Min(width, height) / (2 * rowSpacing)) + 4);

            for (int ring = 0; ring < safetyMaxRings; ring++)
            {
                if (options.MaxRings.HasValue && ring >= options.MaxRings.Value)
                {
                    break;
                }

                double distance = inwardSign * (firstOffset + ring * rowSpacing);
                List<Point2D> offsetPoints = CurveOffsetter.Offset(
                    flat, distance, options.FlattenToleranceMm, roundOuterCorners: options.CornerStyle == CornerStyle.Round);

                var offsetFlat = new FlattenedCurve(
                    offsetPoints.Select(p => new FlattenedPoint(p, true)).ToList(), flat.IsClosed);

                (double ringWidth, double ringHeight) = CurveMetrics.BoundingSize(offsetFlat);
                double ringArea = Math.Abs(CurveMetrics.SignedArea(offsetFlat));

                // Схлопнувшаяся или самопересёкшаяся (после слишком сильного сжатия) форма: либо
                // площадь слишком мала, либо ограничивающий прямоугольник уже не годится в стразу —
                // проверяем оба признака, площадь у самопересекающейся фигуры не всегда падает сама.
                if (ringArea < minAreaForRing || Math.Min(ringWidth, ringHeight) < options.StoneDiameterMm)
                {
                    break;
                }

                Curve ringCurve = Curve.FromPolyline(offsetPoints, flat.IsClosed);

                foreach (PlacedStone s in LineScatterer.Scatter(ringCurve, scatterOptions))
                {
                    result.Add(new PlacedStone(s.Center, s.DiameterMm, s.IsCorner, ring));
                }

                lastRingBoundary = ringCurve;
                ringCount = ring + 1;
            }

            if (options.FillCenter)
            {
                // Добиваем то, что осталось внутри последнего кольца (а если колец не было вовсе —
                // всю форму) обычной заливкой; IntersectionFixer уберёт то, что перекрылось с рядами
                // (кольца добавлены в список раньше — при равенстве прочих признаков приоритет у них).
                Curve fillBoundary = lastRingBoundary ?? boundary;
                var gridOptions = new GridFillOptions
                {
                    StoneDiameterMm = options.StoneDiameterMm,
                    GapMm = options.GapMm,
                    Pattern = options.CenterPattern,
                    MarginFromEdgeMm = 0,
                    FlattenToleranceMm = options.FlattenToleranceMm,
                };

                result.AddRange(GridFiller.Fill(fillBoundary, gridOptions));

                // У сетки фиксированный шаг — иногда маленький остаток в самой середине формы (после
                // всех колец) оказывается ровно между узлами сетки, и в центре остаётся крошечная
                // дыра (замечено на квадрате). Пробуем дополнительно поставить стразу прямо в центр
                // тяжести остатка — если она умещается, она просто добавляется в список кандидатов,
                // а IntersectionFixer сам решит, нужна ли она (уберёт, если там и так плотно).
                FlattenedCurve fillFlat = CurveFlattener.Flatten(fillBoundary, options.FlattenToleranceMm);
                Point2D centroid = Centroid(fillFlat);
                double requiredClearance = radius + options.MarginFromEdgeMm;
                if (PointInPolygon.IsInside(fillFlat, centroid) &&
                    PointInPolygon.DistanceToBoundary(fillFlat, centroid) >= requiredClearance)
                {
                    result.Add(new PlacedStone(centroid, options.StoneDiameterMm, false, rowId: -1));
                }
            }

            return IntersectionFixer.RemoveOverlaps(result);
        }

        private static Point2D Centroid(FlattenedCurve flat)
        {
            double sx = 0, sy = 0;
            int n = flat.Points.Count - 1; // последняя точка дублирует первую у замкнутой кривой
            for (int i = 0; i < n; i++)
            {
                sx += flat.Points[i].Position.X;
                sy += flat.Points[i].Position.Y;
            }

            return new Point2D(sx / n, sy / n);
        }

    }
}
