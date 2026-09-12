using Strassio.Core.Geometry;

namespace Strassio.Core.Placement
{
    /// <summary>Как расставлена одна кривая-смещение относительно исходной, изнутри или снаружи.</summary>
    public enum CornerStyle
    {
        /// <summary>Снаружи угла — дуга (плавный обвод).</summary>
        Round,

        /// <summary>Снаружи угла — тоже срез (митр), как изнутри. Тот же предохранитель от «выброса» на острых углах.</summary>
        Sharp,
    }

    /// <summary>
    /// Один ряд метода L2 «вокруг линии» (docs/SPEC.md, раздел 4): своя смещённая копия исходной
    /// кривой (0 — сама исходная линия, центр ряда), свои параметры расстановки — можно задать
    /// разный размер камня для каждого ряда (центр ss10, края ss6 — раздел 4, L2).
    /// </summary>
    public sealed class RowSpec
    {
        /// <summary>Расстояние от исходной кривой, мм. 0 — по самой линии; знак определяет сторону (см. CurveOffsetter).</summary>
        public double OffsetMm { get; set; }

        public CornerStyle CornerStyle { get; set; } = CornerStyle.Round;

        public LineScatterOptions ScatterOptions { get; set; } = new LineScatterOptions();
    }
}
