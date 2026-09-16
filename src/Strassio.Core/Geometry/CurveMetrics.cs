using System.Collections.Generic;

namespace Strassio.Core.Geometry
{
    /// <summary>
    /// Общие измерения замкнутой (разбитой в полилинию) кривой — площадь и габариты. Нужны, чтобы
    /// отличить осмысленный смещённый контур от схлопнувшегося/вывернутого наизнанку после слишком
    /// сильного сжатия (методы L2/L3, F3/F4/F5 — docs/SPEC.md, раздел 4-5).
    /// </summary>
    public static class CurveMetrics
    {
        /// <summary>Площадь со знаком (шнуровка Гаусса) — положительная у контура против часовой стрелки.</summary>
        public static double SignedArea(FlattenedCurve flat)
        {
            double sum = 0;
            IReadOnlyList<FlattenedPoint> pts = flat.Points;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                Point2D a = pts[i].Position;
                Point2D b = pts[i + 1].Position;
                sum += a.X * b.Y - b.X * a.Y;
            }

            return sum / 2;
        }

        /// <summary>Ширина и высота ограничивающего прямоугольника.</summary>
        public static (double Width, double Height) BoundingSize(FlattenedCurve flat)
        {
            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            foreach (FlattenedPoint p in flat.Points)
            {
                if (p.Position.X < minX) minX = p.Position.X;
                if (p.Position.X > maxX) maxX = p.Position.X;
                if (p.Position.Y < minY) minY = p.Position.Y;
                if (p.Position.Y > maxY) maxY = p.Position.Y;
            }

            return (maxX - minX, maxY - minY);
        }
    }
}
