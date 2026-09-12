using Strassio.Core.Geometry;

namespace Strassio.Core.Placement
{
    /// <summary>Одна страза, поставленная алгоритмом: центр круга и диаметр.</summary>
    public readonly struct PlacedStone
    {
        public Point2D Center { get; }
        public double DiameterMm { get; }

        /// <summary>true, если страза стоит точно в вершине острого угла (докладывается наружу для «живых» страз и отладки).</summary>
        public bool IsCorner { get; }

        /// <summary>
        /// Номер ряда, к которому относится страза (0, если ряд один, например у L1) — чтобы
        /// IntersectionFixer мог убирать соседей аккуратно, не оставляя «одиночек» рваным краем
        /// (раздел 6.3 ТЗ). Не влияет на геометрию, только на порядок исправления пересечений.
        /// </summary>
        public int RowId { get; }

        public PlacedStone(Point2D center, double diameterMm, bool isCorner, int rowId = 0)
        {
            Center = center;
            DiameterMm = diameterMm;
            IsCorner = isCorner;
            RowId = rowId;
        }
    }
}
