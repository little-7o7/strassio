namespace Strassio.Core.Placement
{
    /// <summary>Параметры методов F3 «контурная», F4 «комбинированная», F5 «кант» (docs/SPEC.md, раздел 5).</summary>
    public sealed class ContourFillOptions
    {
        public double StoneDiameterMm { get; set; }

        /// <summary>Зазор между соседними стразами (край в край), мм — и вдоль ряда, и между рядами.</summary>
        public double GapMm { get; set; }

        /// <summary>Дополнительный отступ от края формы (сверх собственного радиуса стразы), мм.</summary>
        public double MarginFromEdgeMm { get; set; }

        /// <summary>
        /// Сколько рядов от края — null (по умолчанию) значит «сколько поместится» (F3 «контурная»);
        /// число N — только N рядов по краю (F5 «кант», при FillCenter=false, или начало F4).
        /// </summary>
        public int? MaxRings { get; set; }

        /// <summary>
        /// Добивать ли оставшийся центр сеткой после рядов по краю — F3/F4 (true) или F5 «кант»,
        /// где внутри должно остаться пусто (false).
        /// </summary>
        public bool FillCenter { get; set; } = true;

        public GridPattern CenterPattern { get; set; } = GridPattern.Honeycomb;

        /// <summary>
        /// Класть ли по самой середине формы один ровный ряд — «прожилку» (как жилка листа).
        ///
        /// Ряды идут от краёв внутрь и в середине сходятся под углом друг к другу: там получается
        /// беспорядок (на сравнении автора с ручной работой — белый завиток). Прожилка заменяет
        /// эту тесноту одной чистой линией. Взамен рядом с ней могут остаться просветы, поэтому
        /// по умолчанию выключено — включается осознанно.
        /// </summary>
        public bool MidribAlongSkeleton { get; set; }

        public CornerStyle CornerStyle { get; set; } = CornerStyle.Round;

        public double CornerAngleThresholdDeg { get; set; } = 30;

        public double FlattenToleranceMm { get; set; } = 0.02;
    }
}
