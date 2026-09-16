using System.Collections.Generic;
using Strassio.Core.Geometry;

namespace Strassio.Core.Placement
{
    /// <summary>
    /// Методы L2 «вокруг линии» и L3 «по смещённой кривой» (docs/SPEC.md, раздел 4): один или
    /// несколько рядов, каждый — своя смещённая копия исходной кривой со своими параметрами
    /// расстановки. L3 — это вызов с одним рядом; L2 — с несколькими (в одну или обе стороны,
    /// с шахматным сдвигом через RowSpec.ScatterOptions.StartOffsetMm, с разным размером камня
    /// для каждого ряда через RowSpec.ScatterOptions.StoneDiameterMm).
    /// </summary>
    public static class RingScatterer
    {
        public static IReadOnlyList<PlacedStone> Scatter(Curve curve, IReadOnlyList<RowSpec> rows)
        {
            var result = new List<PlacedStone>();
            FlattenedCurve originalFlat = CurveFlattener.Flatten(curve);
            double originalArea = originalFlat.IsClosed ? CurveMetrics.SignedArea(originalFlat) : 0;
            (double originalWidth, double originalHeight) = CurveMetrics.BoundingSize(originalFlat);

            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                RowSpec row = rows[rowIndex];
                Curve rowCurve = curve;

                if (System.Math.Abs(row.OffsetMm) > 1e-9)
                {
                    FlattenedCurve flat = CurveFlattener.Flatten(curve, row.ScatterOptions.FlattenToleranceMm);
                    List<Point2D> offsetPoints = CurveOffsetter.Offset(
                        flat, row.OffsetMm, row.ScatterOptions.FlattenToleranceMm,
                        roundOuterCorners: row.CornerStyle == CornerStyle.Round);

                    bool isValid = IsValidRing(
                        offsetPoints, flat.IsClosed, originalArea, row.ScatterOptions.StoneDiameterMm);

                    // Смещение внутрь больше половины меньшей стороны исходной фигуры — линии смещения
                    // пересекаются "зеркально" и образуют геометрически опрятный, но бессмысленный
                    // контур (тот же трюк, что и площадь/знак не ловят: см. тест
                    // OversizedInwardOffset_OnSmallShape_SkipsRowInsteadOfGarbage). Проверяем расстояние
                    // смещения напрямую относительно ИСХОДНОЙ (не смещённой) фигуры. "Внутрь" — когда
                    // знак смещения совпадает со знаком площади контура (см. пояснение в ContourFiller).
                    bool isInward = flat.IsClosed && originalArea != 0 &&
                        System.Math.Sign(row.OffsetMm) == System.Math.Sign(originalArea);
                    if (isInward && System.Math.Abs(row.OffsetMm) * 2 >= System.Math.Min(originalWidth, originalHeight))
                    {
                        isValid = false;
                    }

                    if (flat.IsClosed && !isValid)
                    {
                        // Смещение больше, чем позволяет размер самой формы — контур схлопнулся или
                        // вывернулся наизнанку (например, смещение внутрь больше половины ширины
                        // маленькой фигуры). Пропускаем этот ряд целиком, а не расставляем стразы
                        // по бессмысленной кривой — иначе получится каша из наложенных кругов.
                        continue;
                    }

                    rowCurve = Curve.FromPolyline(offsetPoints, flat.IsClosed);
                }

                foreach (PlacedStone s in LineScatterer.Scatter(rowCurve, row.ScatterOptions))
                {
                    result.Add(new PlacedStone(s.Center, s.DiameterMm, s.IsCorner, rowIndex));
                }
            }

            return result;
        }

        private static bool IsValidRing(List<Point2D> offsetPoints, bool isClosed, double originalSignedArea, double stoneDiameterMm)
        {
            var offsetFlat = new FlattenedCurve(
                offsetPoints.ConvertAll(p => new FlattenedPoint(p, true)), isClosed);

            double area = CurveMetrics.SignedArea(offsetFlat);
            if (originalSignedArea != 0 && System.Math.Sign(area) != System.Math.Sign(originalSignedArea))
            {
                return false; // контур вывернулся наизнанку
            }

            (double width, double height) = CurveMetrics.BoundingSize(offsetFlat);
            return System.Math.Min(width, height) >= stoneDiameterMm;
        }
    }
}
