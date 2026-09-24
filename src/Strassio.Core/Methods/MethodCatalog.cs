using System.Collections.Generic;

namespace Strassio.Core.Methods
{
    /// <summary>Методы, которые уже есть в докере (docs/SPEC.md, разделы 4 и 5).</summary>
    public enum MethodKind
    {
        /// <summary>По линии — один ряд.</summary>
        L1,

        /// <summary>Вокруг линии — несколько рядов.</summary>
        L2,

        /// <summary>По смещённой линии — один ряд на расстоянии от линии.</summary>
        L3,

        /// <summary>Каллиграфия — число рядов меняется вдоль линии.</summary>
        L4,

        /// <summary>Переход размера вдоль линии.</summary>
        L5,

        /// <summary>Чередование размеров по шаблону.</summary>
        L6,

        /// <summary>Пунктир — группы по N камней, пропуск M.</summary>
        L7,

        /// <summary>Акценты — крупный камень на концах и/или в углах.</summary>
        L8,

        /// <summary>Обводка всего дизайна рядом страз на отступе (раздел 8).</summary>
        Outline,

        /// <summary>Сетка.</summary>
        F1,

        /// <summary>Соты.</summary>
        F2,

        /// <summary>Контурная.</summary>
        F3,

        /// <summary>Комбинированная: ряды по краю + сетка/соты внутри.</summary>
        F4,

        /// <summary>Кант: только ряды по краю.</summary>
        F5,

        /// <summary>По центральной линии.</summary>
        F6,

        /// <summary>Переход между двумя кривыми.</summary>
        F7,

        /// <summary>По направляющей линии.</summary>
        F8,

        /// <summary>От центра — кольца или спираль.</summary>
        F9,

        /// <summary>Случайная плотная.</summary>
        F10,

        /// <summary>Градиент размера.</summary>
        F11,

        /// <summary>Добивка щелей мелкими камнями.</summary>
        F12,
    }

    /// <summary>Метод в списке докера: ключ текста, вкладка, нужна ли замкнутая фигура и какие поля показывать.</summary>
    public sealed class MethodInfo
    {
        internal MethodInfo(MethodKind kind, bool isFill, bool needsClosed, params MethodField[] fields)
        {
            Kind = kind;
            IsFill = isFill;
            NeedsClosed = needsClosed;
            Fields = fields;
        }

        /// <summary>Нужна вторая линия: F7 — вторая кривая перехода, F8 — направляющая (выделить обе фигуры).</summary>
        public bool NeedsGuide { get; private set; }

        /// <summary>Берёт все выделенные фигуры сразу (обводка всего дизайна), а не одну.</summary>
        public bool UsesWholeSelection { get; private set; }

        internal MethodInfo WithWholeSelection()
        {
            UsesWholeSelection = true;
            return this;
        }

        internal MethodInfo WithGuide()
        {
            NeedsGuide = true;
            return this;
        }

        public MethodKind Kind { get; }

        /// <summary>Ключ названия в файле языка: "method.l1". Подсказка — "method.l1.tooltip".</summary>
        public string Key => "method." + Kind.ToString().ToLowerInvariant();

        /// <summary>Вкладка «Заливка» (true) или «Линия» (false).</summary>
        public bool IsFill { get; }

        public bool NeedsClosed { get; }

        /// <summary>Поля параметров в том порядке, в каком они стоят в докере.</summary>
        public IReadOnlyList<MethodField> Fields { get; }
    }

    /// <summary>Все методы и все поля параметров. Добавить поле методу — одна строка здесь плюс тексты в lang/*.json.</summary>
    public static class MethodCatalog
    {
        public static readonly MethodField Gap = MethodField.Number(
            "gap", FieldKind.Length, 0, 20, p => p.GapMm, (p, v) => p.GapMm = v);

        public static readonly MethodField Step = MethodField.Choice(
            "step", new[] { MethodChoices.StepFit, MethodChoices.StepExact, MethodChoices.StepCount },
            p => p.StepMode, (p, v) => p.StepMode = v);

        public static readonly MethodField ExactStep = MethodField.Number(
            "exactStep", FieldKind.Length, 0.1, 200, p => p.ExactStepMm, (p, v) => p.ExactStepMm = v,
            p => p.StepMode == MethodChoices.StepExact);

        public static readonly MethodField ExactCount = MethodField.Number(
            "exactCount", FieldKind.Integer, 1, 100000, p => p.ExactCount, (p, v) => p.ExactCount = (int)v,
            p => p.StepMode == MethodChoices.StepCount);

        public static readonly MethodField StartOffset = MethodField.Number(
            "startOffset", FieldKind.Length, 0, 10000, p => p.StartOffsetMm, (p, v) => p.StartOffsetMm = v);

        public static readonly MethodField EndMargin = MethodField.Number(
            "endMargin", FieldKind.Length, 0, 10000, p => p.EndMarginMm, (p, v) => p.EndMarginMm = v);

        public static readonly MethodField Reverse = MethodField.Toggle(
            "reverse", p => p.Reverse, (p, v) => p.Reverse = v);

        public static readonly MethodField CornerAngle = MethodField.Number(
            "cornerAngle", FieldKind.Angle, 1, 179, p => p.CornerAngleDeg, (p, v) => p.CornerAngleDeg = v);

        public static readonly MethodField RowCount = MethodField.Number(
            "rowCount", FieldKind.Integer, 1, 30, p => p.RowCount, (p, v) => p.RowCount = (int)v);

        public static readonly MethodField RowGap = MethodField.Number(
            "rowGap", FieldKind.Length, 0, 20, p => p.RowGapMm, (p, v) => p.RowGapMm = v);

        public static readonly MethodField RowSide = MethodField.Choice(
            "rowSide", new[] { MethodChoices.SideBoth, MethodChoices.SideOutside, MethodChoices.SideInside },
            p => p.RowSide, (p, v) => p.RowSide = v);

        public static readonly MethodField Stagger = MethodField.Toggle(
            "stagger", p => p.Stagger, (p, v) => p.Stagger = v);

        public static readonly MethodField Offset = MethodField.Number(
            "offset", FieldKind.Length, 0.01, 1000, p => p.OffsetMm, (p, v) => p.OffsetMm = v);

        public static readonly MethodField OffsetSide = MethodField.Choice(
            "offsetSide", new[] { MethodChoices.SideOutside, MethodChoices.SideInside },
            p => p.OffsetSide, (p, v) => p.OffsetSide = v);

        /// <summary>
        /// Углы: и как ряд проходит угол (страза в вершине или вершина скруглена), и какими
        /// строятся углы смещённого ряда у методов L2/L3/L4.
        /// </summary>
        public static readonly MethodField Corners = MethodField.Choice(
            "corners", new[] { MethodChoices.CornersMixed, MethodChoices.CornersSharp, MethodChoices.CornersRound },
            p => p.Corners, (p, v) => p.Corners = v);

        public static readonly MethodField Intersections = MethodField.Choice(
            "intersections", new[] { MethodChoices.IntersectRemove, MethodChoices.IntersectShift, MethodChoices.IntersectShow },
            p => p.Intersections, (p, v) => p.Intersections = v);

        public static readonly MethodField Angle = MethodField.Number(
            "angle", FieldKind.Angle, -360, 360, p => p.AngleDeg, (p, v) => p.AngleDeg = v);

        public static readonly MethodField EdgeMargin = MethodField.Number(
            "edgeMargin", FieldKind.Length, 0, 100, p => p.EdgeMarginMm, (p, v) => p.EdgeMarginMm = v);

        public static readonly MethodField Rings = MethodField.Number(
            "rings", FieldKind.Integer, 1, 100, p => p.Rings, (p, v) => p.Rings = (int)v);

        public static readonly MethodField CenterPattern = MethodField.Choice(
            "centerPattern", new[] { MethodChoices.PatternAlong, MethodChoices.PatternHoneycomb, MethodChoices.PatternSquare },
            p => p.CenterPattern, (p, v) => p.CenterPattern = v);

        public static readonly MethodField EdgeSize = MethodField.SizeChoice(
            "edgeSize", allowSame: true, p => p.EdgeSize, (p, v) => p.EdgeSize = v);

        public static readonly MethodField WidthProfile = MethodField.Choice(
            "widthProfile", new[] { MethodChoices.ProfileMiddle, MethodChoices.ProfileGrow, MethodChoices.ProfileShrink },
            p => p.WidthProfile, (p, v) => p.WidthProfile = v);

        public static readonly MethodField FromSize = MethodField.SizeChoice(
            "fromSize", allowSame: true, p => p.FromSize, (p, v) => p.FromSize = v);

        public static readonly MethodField ToSize = MethodField.SizeChoice(
            "toSize", allowSame: true, p => p.ToSize, (p, v) => p.ToSize = v);

        public static readonly MethodField SizePattern = MethodField.TextField(
            "sizePattern", p => p.SizePattern, (p, v) => p.SizePattern = v);

        public static readonly MethodField DashCount = MethodField.Number(
            "dashCount", FieldKind.Integer, 1, 1000, p => p.DashCount, (p, v) => p.DashCount = (int)v);

        public static readonly MethodField SkipCount = MethodField.Number(
            "skipCount", FieldKind.Integer, 1, 1000, p => p.SkipCount, (p, v) => p.SkipCount = (int)v);

        public static readonly MethodField AccentSize = MethodField.SizeChoice(
            "accentSize", allowSame: false, p => p.AccentSize, (p, v) => p.AccentSize = v);

        public static readonly MethodField AccentWhere = MethodField.Choice(
            "accentWhere", new[] { MethodChoices.AccentBoth, MethodChoices.AccentEnds, MethodChoices.AccentCorners },
            p => p.AccentWhere, (p, v) => p.AccentWhere = v);

        public static readonly MethodField AutoGrid = MethodField.Choice(
            "autoGrid", new[] { MethodChoices.AutoOff, MethodChoices.AutoShift, MethodChoices.AutoShiftAngle },
            p => p.AutoGrid, (p, v) => p.AutoGrid = v);

        public static readonly MethodField CenterMode = MethodField.Choice(
            "centerMode", new[] { MethodChoices.CenterRings, MethodChoices.CenterSpiral },
            p => p.CenterMode, (p, v) => p.CenterMode = v);

        public static readonly MethodField Variant = MethodField.Number(
            "variant", FieldKind.Integer, 1, 9999, p => p.Variant, (p, v) => p.Variant = (int)v);

        public static readonly MethodField MixSizes = MethodField.TextField(
            "mixSizes", p => p.MixSizes, (p, v) => p.MixSizes = v, allowEmpty: true);

        public static readonly MethodField GradientDirection = MethodField.Choice(
            "gradientDirection", new[] { MethodChoices.GradientCenter, MethodChoices.GradientHorizontal },
            p => p.GradientDirection, (p, v) => p.GradientDirection = v);

        public static readonly MethodField FillSize = MethodField.SizeChoice(
            "fillSize", allowSame: false, p => p.FillSize, (p, v) => p.FillSize = v, smallest: true);

        public static readonly IReadOnlyList<MethodInfo> All = new[]
        {
            new MethodInfo(MethodKind.L1, isFill: false, needsClosed: false,
                Gap, Step, ExactStep, ExactCount, StartOffset, EndMargin, Reverse, Corners, CornerAngle),
            new MethodInfo(MethodKind.L2, isFill: false, needsClosed: false,
                Gap, RowCount, RowGap, RowSide, EdgeSize, Stagger, Corners, Intersections, CornerAngle),
            new MethodInfo(MethodKind.L3, isFill: false, needsClosed: false,
                Gap, Offset, OffsetSide, RowCount, RowGap, Corners, CornerAngle),
            new MethodInfo(MethodKind.L4, isFill: false, needsClosed: false,
                Gap, RowCount, RowGap, WidthProfile, Corners, CornerAngle),
            new MethodInfo(MethodKind.L5, isFill: false, needsClosed: false, Gap, FromSize, ToSize, Corners, CornerAngle),
            new MethodInfo(MethodKind.L6, isFill: false, needsClosed: false, Gap, SizePattern, Corners, CornerAngle),
            new MethodInfo(MethodKind.L7, isFill: false, needsClosed: false, Gap, DashCount, SkipCount, Corners, CornerAngle),
            new MethodInfo(MethodKind.L8, isFill: false, needsClosed: false, Gap, AccentSize, AccentWhere, Corners, CornerAngle),
            new MethodInfo(MethodKind.Outline, isFill: false, needsClosed: true, Gap, EdgeMargin, Corners, CornerAngle).WithWholeSelection(),
            new MethodInfo(MethodKind.F1, isFill: true, needsClosed: true, Gap, Angle, EdgeMargin, AutoGrid),
            new MethodInfo(MethodKind.F2, isFill: true, needsClosed: true, Gap, Angle, EdgeMargin, AutoGrid),
            new MethodInfo(MethodKind.F3, isFill: true, needsClosed: true, Gap, EdgeMargin, CenterPattern),
            new MethodInfo(MethodKind.F4, isFill: true, needsClosed: true, Gap, Rings, EdgeMargin, CenterPattern),
            new MethodInfo(MethodKind.F5, isFill: true, needsClosed: true, Gap, Rings, EdgeMargin),
            new MethodInfo(MethodKind.F6, isFill: true, needsClosed: true, Gap, EdgeMargin),
            new MethodInfo(MethodKind.F7, isFill: true, needsClosed: false, Gap, RowGap).WithGuide(),
            new MethodInfo(MethodKind.F8, isFill: true, needsClosed: true, Gap, RowGap, EdgeMargin).WithGuide(),
            new MethodInfo(MethodKind.F9, isFill: true, needsClosed: true, Gap, CenterMode, EdgeMargin),
            new MethodInfo(MethodKind.F10, isFill: true, needsClosed: true, Gap, MixSizes, Variant, EdgeMargin),
            new MethodInfo(MethodKind.F11, isFill: true, needsClosed: true, Gap, FromSize, ToSize, GradientDirection, EdgeMargin),
            new MethodInfo(MethodKind.F12, isFill: true, needsClosed: true, Gap, FillSize, EdgeMargin),
        };

        public static MethodInfo Get(MethodKind kind)
        {
            foreach (MethodInfo info in All)
            {
                if (info.Kind == kind)
                {
                    return info;
                }
            }

            throw new KeyNotFoundException(kind.ToString());
        }
    }
}
