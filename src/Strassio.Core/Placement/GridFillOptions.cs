namespace Strassio.Core.Placement
{
    /// <summary>Рисунок сетки заливки (docs/SPEC.md, раздел 5).</summary>
    public enum GridPattern
    {
        /// <summary>F1. Квадратная сетка.</summary>
        Square,

        /// <summary>F2. Соты (шахматная, плотная гексагональная упаковка).</summary>
        Honeycomb,
    }

    /// <summary>Параметры методов F1 «сетка» и F2 «соты» (docs/SPEC.md, раздел 5).</summary>
    public sealed class GridFillOptions
    {
        public double StoneDiameterMm { get; set; }

        /// <summary>Зазор между соседними стразами (край в край), мм.</summary>
        public double GapMm { get; set; }

        public GridPattern Pattern { get; set; } = GridPattern.Square;

        /// <summary>Угол поворота сетки, градусы.</summary>
        public double AngleDeg { get; set; }

        /// <summary>Дополнительный отступ от края формы (сверх собственного радиуса стразы), мм.</summary>
        public double MarginFromEdgeMm { get; set; }

        /// <summary>Сдвиг сетки вдоль её строк (в повёрнутых координатах сетки), мм. Нужен автоподбору.</summary>
        public double OffsetXMm { get; set; }

        /// <summary>Сдвиг сетки поперёк строк, мм.</summary>
        public double OffsetYMm { get; set; }

        /// <summary>Точность разбивки кривых Безье в полилинию, мм.</summary>
        public double FlattenToleranceMm { get; set; } = 0.02;
    }
}
