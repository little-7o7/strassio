namespace Strassio.Core.Placement
{
    /// <summary>Параметры метода L1 «по линии» (docs/SPEC.md, разделы 3.1 и 4).</summary>
    public sealed class LineScatterOptions
    {
        /// <summary>Диаметр стразы, мм.</summary>
        public double StoneDiameterMm { get; set; }

        /// <summary>Зазор между соседними стразами (край в край), мм.</summary>
        public double GapMm { get; set; }

        public StepMode Mode { get; set; } = StepMode.FitEven;

        /// <summary>Шаг между центрами страз для StepMode.ExactStep, мм. Если не задан — берётся StoneDiameterMm + GapMm.</summary>
        public double? ExactStepMm { get; set; }

        /// <summary>Общее количество страз на всю кривую для StepMode.ExactCount.</summary>
        public int? ExactCount { get; set; }

        /// <summary>Отступ от начала кривой, мм. Для замкнутой кривой — сдвиг точки начала заполнения по контуру.</summary>
        public double StartOffsetMm { get; set; }

        /// <summary>Отступ от конца кривой, мм. Не используется для замкнутой кривой.</summary>
        public double EndMarginMm { get; set; }

        /// <summary>Направление: false — от начала кривой к концу, true — в обратную сторону.</summary>
        public bool Reverse { get; set; }

        /// <summary>Порог угла (градусы): кривая поворачивает на столько или больше — считаем угол острым и ставим стразу точно в вершину.</summary>
        public double CornerAngleThresholdDeg { get; set; } = 30;

        /// <summary>Точность разбивки кривых Безье в полилинию, мм.</summary>
        public double FlattenToleranceMm { get; set; } = 0.02;

        /// <summary>
        /// Насколько можно сместить одну стразу в сторону от линии (мм), чтобы развести её с соседней
        /// стразой по другую сторону очень острого угла — без этого они физически перекрывались бы,
        /// хотя каждая верно стоит на своём отрезке (раздел 6.1 ТЗ). Обычные и тупые углы не затрагивает.
        /// </summary>
        public double MaxCornerNudgeMm { get; set; } = 1.0;

        /// <summary>
        /// Минимальный зазор край-в-край (мм) между ближайшими стразами по разные стороны очень
        /// острого угла — может быть меньше обычного GapMm ряда: у самого острия небольшой зазор
        /// не бросается в глаза, а лишний сдвиг наоборот заметнее.
        /// </summary>
        public double CornerMinGapMm { get; set; } = 0.1;

        /// <summary>
        /// Сколько страз подряд с каждой стороны острого угла участвуют в плавном сдвиге (раздел 6.1
        /// ТЗ) — чем больше, тем более пологий и незаметный получается изгиб ряда у угла.
        /// </summary>
        public int CornerTaperCount { get; set; } = 4;
    }
}
