using System;
using System.Collections.Generic;
using System.Linq;
using Strassio.Core.Geometry;
using Strassio.Core.Placement;

namespace Strassio.Core.Motifs
{
    /// <summary>Какие стразы остаются при обрезке по рамке.</summary>
    public enum FrameTrimMode
    {
        /// <summary>Только те, что целиком внутри рамки: камень не выходит за край.</summary>
        WholeStone,

        /// <summary>Те, у которых середина внутри: соседние листы встык делят стразы на краю без дыр и без двойных.</summary>
        Center,
    }

    /// <summary>
    /// Обрезка по рамке (вкладка «Мотивы», раздел «Раппорт»): стразы снаружи рамки ткани или листа
    /// трафарета убираются. Рамка — замкнутые контуры; отверстия в ней (чётно-нечётно) тоже считаются «снаружи».
    /// </summary>
    public static class FrameTrim
    {
        /// <summary>Номера страз, которые надо убрать.</summary>
        public static List<int> Outside(IReadOnlyList<PlacedStone> stones, IReadOnlyList<Curve> frame, FrameTrimMode mode)
        {
            List<FlattenedCurve> loops = frame.Select(c => CurveFlattener.Flatten(c)).Where(f => f.IsClosed && f.Points.Count > 2).ToList();
            var result = new List<int>();
            if (loops.Count == 0)
            {
                return result;
            }

            for (int i = 0; i < stones.Count; i++)
            {
                Point2D p = stones[i].Center;
                bool inside = loops.Count(l => PointInPolygon.IsInside(l, p)) % 2 == 1;
                if (inside && mode == FrameTrimMode.WholeStone)
                {
                    // Край камня не дальше края рамки (с допуском на округление координат).
                    inside = loops.Min(l => DistanceToLoop(l, p)) >= stones[i].DiameterMm / 2 - 0.01;
                }

                if (!inside)
                {
                    result.Add(i);
                }
            }

            return result;
        }

        private static double DistanceToLoop(FlattenedCurve loop, Point2D p)
        {
            double best = double.MaxValue;
            IReadOnlyList<FlattenedPoint> pts = loop.Points;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                Point2D a = pts[i].Position, ab = pts[i + 1].Position - a;
                double len2 = ab.X * ab.X + ab.Y * ab.Y;
                double t = len2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / len2));
                best = Math.Min(best, Point2D.Distance(p, a + ab * t));
            }

            return best;
        }
    }
}
