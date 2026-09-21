using System.Runtime.Serialization;

namespace Strassio.Core.Methods
{
    /// <summary>
    /// Параметры методов расстановки (docs/SPEC.md, разделы 4 и 5), которые пользователь меняет
    /// в докере. Один общий набор на все методы: зазор, например, одинаково нужен и линии, и
    /// заливке — пользователь не должен вводить его заново при смене метода. Хранится в
    /// settings.json (поле "method"), поэтому всё запоминается между запусками.
    ///
    /// Все длины — в миллиметрах (CLAUDE.md, правило 6). Варианты выбора хранятся словами
    /// (<see cref="MethodChoices"/>), как и остальные перечисления в settings.json.
    /// </summary>
    [DataContract]
    public sealed class MethodParameters
    {
        public MethodParameters()
        {
            SetDefaults();
        }

        /// <summary>Зазор между соседними стразами (край в край).</summary>
        [DataMember(Name = "gapMm")]
        public double GapMm { get; set; }

        /// <summary>Шаг по линии: <see cref="MethodChoices.StepFit"/>, <see cref="MethodChoices.StepExact"/> или <see cref="MethodChoices.StepCount"/>.</summary>
        [DataMember(Name = "step")]
        public string StepMode { get; set; } = MethodChoices.StepFit;

        /// <summary>Точный шаг (центр к центру) для режима «точный шаг».</summary>
        [DataMember(Name = "exactStepMm")]
        public double ExactStepMm { get; set; }

        /// <summary>Сколько страз поставить в режиме «точное количество».</summary>
        [DataMember(Name = "exactCount")]
        public int ExactCount { get; set; }

        /// <summary>Отступ первой стразы от начала линии.</summary>
        [DataMember(Name = "startOffsetMm")]
        public double StartOffsetMm { get; set; }

        /// <summary>Отступ от концов незамкнутой линии.</summary>
        [DataMember(Name = "endMarginMm")]
        public double EndMarginMm { get; set; }

        /// <summary>Идти по линии в обратную сторону.</summary>
        [DataMember(Name = "reverse")]
        public bool Reverse { get; set; }

        /// <summary>Порог угла: линия поворачивает на столько градусов или больше — в вершину ставится страза.</summary>
        [DataMember(Name = "cornerAngleDeg")]
        public double CornerAngleDeg { get; set; }

        /// <summary>L2: сколько всего рядов.</summary>
        [DataMember(Name = "rowCount")]
        public int RowCount { get; set; }

        /// <summary>L2: зазор между рядами (край в край).</summary>
        [DataMember(Name = "rowGapMm")]
        public double RowGapMm { get; set; }

        /// <summary>L2: ряды в обе стороны, только наружу или только внутрь.</summary>
        [DataMember(Name = "rowSide")]
        public string RowSide { get; set; } = MethodChoices.SideBoth;

        /// <summary>L2: шахматный сдвиг — каждый второй ряд сдвинут на полшага.</summary>
        [DataMember(Name = "stagger")]
        public bool Stagger { get; set; }

        /// <summary>L3: расстояние от линии до ряда (от линии до центра страз).</summary>
        [DataMember(Name = "offsetMm")]
        public double OffsetMm { get; set; }

        /// <summary>L3: наружу или внутрь.</summary>
        [DataMember(Name = "offsetSide")]
        public string OffsetSide { get; set; } = MethodChoices.SideOutside;

        /// <summary>L2 и L3: углы смещённого ряда — круглые или острые.</summary>
        [DataMember(Name = "corners")]
        public string Corners { get; set; } = MethodChoices.CornersRound;

        /// <summary>L2: что делать со стразами, которые накладываются друг на друга (раздел 6.4).</summary>
        [DataMember(Name = "intersections")]
        public string Intersections { get; set; } = MethodChoices.IntersectRemove;

        /// <summary>F1 и F2: угол поворота сетки, градусы.</summary>
        [DataMember(Name = "angleDeg")]
        public double AngleDeg { get; set; }

        /// <summary>Заливка: отступ страз от края фигуры.</summary>
        [DataMember(Name = "edgeMarginMm")]
        public double EdgeMarginMm { get; set; }

        /// <summary>F4 и F5: сколько рядов по краю.</summary>
        [DataMember(Name = "rings")]
        public int Rings { get; set; }

        /// <summary>F3 и F4: чем добивать середину — сотами или сеткой.</summary>
        [DataMember(Name = "centerPattern")]
        public string CenterPattern { get; set; } = MethodChoices.PatternHoneycomb;

        public MethodParameters Clone() => (MethodParameters)MemberwiseClone();

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) => SetDefaults();

        private void SetDefaults()
        {
            GapMm = 0.2;
            StepMode = MethodChoices.StepFit;
            ExactStepMm = 2.6;
            ExactCount = 20;
            StartOffsetMm = 0;
            EndMarginMm = 0;
            Reverse = false;
            CornerAngleDeg = 30;
            RowCount = 3;
            RowGapMm = 0.2;
            RowSide = MethodChoices.SideBoth;
            Stagger = false;
            OffsetMm = 2;
            OffsetSide = MethodChoices.SideOutside;
            Corners = MethodChoices.CornersRound;
            Intersections = MethodChoices.IntersectRemove;
            AngleDeg = 0;
            EdgeMarginMm = 0;
            Rings = 2;
            CenterPattern = MethodChoices.PatternHoneycomb;
        }
    }

    /// <summary>Слова, которыми варианты выбора хранятся в settings.json. Они же — хвосты ключей текстов в файлах языков.</summary>
    public static class MethodChoices
    {
        public const string StepFit = "fit";
        public const string StepExact = "step";
        public const string StepCount = "count";

        public const string SideBoth = "both";
        public const string SideOutside = "outside";
        public const string SideInside = "inside";

        public const string CornersRound = "round";
        public const string CornersSharp = "sharp";

        public const string IntersectRemove = "remove";
        public const string IntersectShift = "shift";
        public const string IntersectShow = "show";

        public const string PatternHoneycomb = "honeycomb";
        public const string PatternSquare = "square";
    }
}
