using System.Collections.Generic;
using System.Linq;
using Strassio.Core.Geometry;

namespace Strassio.Core.Editing
{
    /// <summary>Тонкая перемычка: между стразами <see cref="First"/> и <see cref="Second"/> всего <see cref="GapMm"/> материала.</summary>
    public readonly struct ThinBridge
    {
        public ThinBridge(int first, int second, double gapMm, Point2D middle)
        {
            First = first;
            Second = second;
            GapMm = gapMm;
            Middle = middle;
        }

        public int First { get; }

        public int Second { get; }

        /// <summary>Сколько материала между краями отверстий, мм (меньше нуля — отверстия налезают).</summary>
        public double GapMm { get; }

        /// <summary>Середина перемычки — туда ставится пометка.</summary>
        public Point2D Middle { get; }
    }

    /// <summary>
    /// Проверка перемычек (docs/SPEC.md, раздел 11, [NEW]): места, где между отверстиями трафарета
    /// остаётся меньше заданного (например, 0,3 мм) — там материал порвётся при резке или при работе.
    /// </summary>
    public static class BridgeChecker
    {
        public static List<ThinBridge> Find(IReadOnlyList<DocStone> stones, double minBridgeMm)
        {
            var result = new List<ThinBridge>();
            if (stones.Count < 2)
            {
                return result;
            }

            double maxD = stones.Max(s => s.DiameterMm);
            var grid = new StoneGrid(maxD + minBridgeMm);
            var order = new List<int>();
            for (int i = 0; i < stones.Count; i++)
            {
                DocStone s = stones[i];
                foreach (int g in grid.Near(s.Center))
                {
                    DocStone other = stones[order[g]];
                    double gap = Point2D.Distance(s.Center, other.Center) - s.Radius - other.Radius;
                    if (gap < minBridgeMm - 1e-9)
                    {
                        Point2D a = other.Center + (s.Center - other.Center).Normalized() * other.Radius;
                        Point2D b = s.Center + (other.Center - s.Center).Normalized() * s.Radius;
                        result.Add(new ThinBridge(order[g], i, gap, (a + b) / 2));
                    }
                }

                grid.Add(s.Center, s.Radius);
                order.Add(i);
            }

            return result.OrderBy(b => b.GapMm).ToList();
        }
    }
}
