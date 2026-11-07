using System;
using System.Collections.Generic;
using System.Linq;
using Strassio.Core.Geometry;

namespace Strassio.Core.Placement
{
    /// <summary>
    /// Расстояние от точки до выделенного рисунка: до края замкнутой фигуры снаружи и до края ближайшей
    /// стразы. Нужно раскладкам «вокруг мотива» (брызги, штрихи вокруг).
    /// </summary>
    internal sealed class DesignDistance
    {
        private readonly SignedDistanceField? field;
        private readonly Scatter.StoneIndex stones;

        public DesignDistance(IReadOnlyList<Curve> shapes, IReadOnlyList<PlacedStone> design, double cellMm, double reachMm)
        {
            List<FlattenedCurve> closed = shapes.Select(c => CurveFlattener.Flatten(c)).Where(f => f.IsClosed && f.TotalLength > 1e-6).ToList();
            field = closed.Count == 0 ? null : SignedDistanceField.Build(closed, cellMm, paddingMm: reachMm, union: true);
            stones = new Scatter.StoneIndex(design, reachMm);

            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (Point2D p in design.Select(s => s.Center).Concat(closed.SelectMany(f => f.Points.Select(q => q.Position))))
            {
                minX = Math.Min(minX, p.X);
                minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X);
                maxY = Math.Max(maxY, p.Y);
            }

            Min = minX <= maxX ? new Point2D(minX, minY) : new Point2D(0, 0);
            Max = minX <= maxX ? new Point2D(maxX, maxY) : new Point2D(0, 0);
        }

        /// <summary>Габариты рисунка (по центрам страз и точкам фигур).</summary>
        public Point2D Min { get; }

        public Point2D Max { get; }

        /// <summary>Расстояние до края рисунка; null — точка внутри фигуры рисунка.</summary>
        public double? At(Point2D p)
        {
            double best = double.MaxValue;
            if (field != null)
            {
                double v = field.ValueAt(p);
                if (v > 0)
                {
                    return null;
                }

                if (!double.IsNegativeInfinity(v))
                {
                    best = -v;
                }
            }

            return Math.Min(best, stones.EdgeDistance(p));
        }
    }
}
