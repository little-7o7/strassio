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
                    rowCurve = Curve.FromPolyline(offsetPoints, flat.IsClosed);
                }

                foreach (PlacedStone s in LineScatterer.Scatter(rowCurve, row.ScatterOptions))
                {
                    result.Add(new PlacedStone(s.Center, s.DiameterMm, s.IsCorner, rowIndex));
                }
            }

            return result;
        }
    }
}
