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

        /// <summary>L2: размер крайних рядов (название из таблицы камней); пусто — как основной камень.</summary>
        [DataMember(Name = "edgeSize")]
        public string EdgeSize { get; set; } = string.Empty;

        /// <summary>L5: с какого размера начинать (название из таблицы); пусто — основной камень.</summary>
        [DataMember(Name = "fromSize")]
        public string FromSize { get; set; } = string.Empty;

        /// <summary>L5: каким размером заканчивать; все размеры таблицы между ними идут по порядку.</summary>
        [DataMember(Name = "toSize")]
        public string ToSize { get; set; } = string.Empty;

        /// <summary>L6: шаблон размеров, например «ss6, ss6, ss10» — повторяется вдоль линии.</summary>
        [DataMember(Name = "sizePattern")]
        public string SizePattern { get; set; } = string.Empty;

        /// <summary>L7: страз в одной группе пунктира.</summary>
        [DataMember(Name = "dashCount")]
        public int DashCount { get; set; }

        /// <summary>L7: пропуск между группами — сколько мест под стразы оставить пустыми.</summary>
        [DataMember(Name = "skipCount")]
        public int SkipCount { get; set; }

        /// <summary>L8: размер акцентов (название из таблицы).</summary>
        [DataMember(Name = "accentSize")]
        public string AccentSize { get; set; } = string.Empty;

        /// <summary>L8: где акценты — на концах, в углах или и там, и там.</summary>
        [DataMember(Name = "accentWhere")]
        public string AccentWhere { get; set; } = MethodChoices.AccentBoth;

        /// <summary>L4: где линия шире всего — в середине или к концу.</summary>
        [DataMember(Name = "widthProfile")]
        public string WidthProfile { get; set; } = MethodChoices.ProfileMiddle;

        /// <summary>F1 и F2: автоподбор сетки — выкл, сдвиг, сдвиг и поворот.</summary>
        [DataMember(Name = "autoGrid")]
        public string AutoGrid { get; set; } = MethodChoices.AutoOff;

        /// <summary>F9: кольца или спираль.</summary>
        [DataMember(Name = "centerMode")]
        public string CenterMode { get; set; } = MethodChoices.CenterRings;

        /// <summary>F10: номер варианта случайной раскладки (тот же номер — та же раскладка).</summary>
        [DataMember(Name = "variant")]
        public int Variant { get; set; }

        /// <summary>F10: смешать размеры, например «ss6, ss10»; пусто — только основной камень.</summary>
        [DataMember(Name = "mixSizes")]
        public string MixSizes { get; set; } = string.Empty;

        /// <summary>F11: откуда крупные камни — из центра или слева.</summary>
        [DataMember(Name = "gradientDirection")]
        public string GradientDirection { get; set; } = MethodChoices.GradientCenter;

        /// <summary>F12: размер мелких камней для щелей.</summary>
        [DataMember(Name = "fillSize")]
        public string FillSize { get; set; } = string.Empty;

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
            EdgeSize = string.Empty;
            FromSize = string.Empty;
            ToSize = string.Empty;
            SizePattern = string.Empty;
            DashCount = 3;
            SkipCount = 1;
            AccentSize = string.Empty;
            AccentWhere = MethodChoices.AccentBoth;
            WidthProfile = MethodChoices.ProfileMiddle;
            AutoGrid = MethodChoices.AutoOff;
            CenterMode = MethodChoices.CenterRings;
            Variant = 1;
            MixSizes = string.Empty;
            GradientDirection = MethodChoices.GradientCenter;
            FillSize = string.Empty;
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

        public const string AccentEnds = "ends";
        public const string AccentCorners = "corners";
        public const string AccentBoth = "both";

        public const string ProfileMiddle = "middle";
        public const string ProfileGrow = "grow";
        public const string ProfileShrink = "shrink";

        public const string AutoOff = "off";
        public const string AutoShift = "shift";
        public const string AutoShiftAngle = "shiftAngle";

        public const string CenterRings = "rings";
        public const string CenterSpiral = "spiral";

        public const string GradientCenter = "center";
        public const string GradientHorizontal = "horizontal";

        /// <summary>Для поля «размер»: как у основного камня.</summary>
        public const string SameSize = "";
    }
}
