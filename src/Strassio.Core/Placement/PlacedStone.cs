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

        public PlacedStone(Point2D center, double diameterMm, bool isCorner)
        {
            Center = center;
            DiameterMm = diameterMm;
            IsCorner = isCorner;
        }
    }
}
