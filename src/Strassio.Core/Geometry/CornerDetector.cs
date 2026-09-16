using System;
using System.Collections.Generic;

namespace Strassio.Core.Geometry
{
    /// <summary>
    /// Находит острые углы на разбитой в полилинию кривой — места, где камень должен встать
    /// точно в вершину (docs/SPEC.md, раздел 4 «Углы» и раздел 6.1).
    /// </summary>
    public static class CornerDetector
    {
        /// <summary>
        /// Расстояния от начала кривой (мм) до вершин, где кривая поворачивает на угол
        /// thresholdDeg или больше. Для замкнутой кривой стык (последняя точка = первая)
        /// проверяется как один угол и возвращается расстоянием 0.
        /// </summary>
        public static List<double> FindSharpCornerDistances(FlattenedCurve curve, double thresholdDeg)
        {
            var result = new List<double>();
            IReadOnlyList<FlattenedPoint> pts = curve.Points;
            int n = pts.Count;
            double thresholdRad = thresholdDeg * Math.PI / 180.0;

            // Индекс n-1 не проверяем: для разомкнутой кривой это конец (не угол по определению),
            // для замкнутой — дубликат точки 0 (стык проверяется через индекс 0, см. ниже).
            for (int i = 0; i < n - 1; i++)
            {
                if (!pts[i].IsSegmentJoint)
                {
                    continue;
                }

                Point2D prevPos, curPos = pts[i].Position, nextPos;

                if (i == 0)
                {
                    if (!curve.IsClosed)
                    {
                        continue; // начало разомкнутой кривой — не угол
                    }

                    prevPos = pts[n - 2].Position; // точка перед дублирующей точкой замыкания
                    nextPos = pts[1].Position;
                }
                else
                {
                    prevPos = pts[i - 1].Position;
                    nextPos = pts[i + 1].Position;
                }

                Point2D inDir = (curPos - prevPos).Normalized();
                Point2D outDir = (nextPos - curPos).Normalized();

                // Нулевой длины участок направления не задаёт — такая точка не угол (защита на случай,
                // если FlattenedCurve собрали вручную, минуя чистку в CurveFlattener).
                if (inDir == Point2D.Zero || outDir == Point2D.Zero)
                {
                    continue;
                }

                double cos = Math.Max(-1, Math.Min(1, inDir.Dot(outDir)));
                double turnRad = Math.Acos(cos);

                if (turnRad >= thresholdRad)
                {
                    result.Add(curve.ArcLengths[i]);
                }
            }

            return result;
        }
    }
}
