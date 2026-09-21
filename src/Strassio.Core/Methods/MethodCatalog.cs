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

        public static readonly MethodField Corners = MethodField.Choice(
            "corners", new[] { MethodChoices.CornersRound, MethodChoices.CornersSharp },
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
            "centerPattern", new[] { MethodChoices.PatternHoneycomb, MethodChoices.PatternSquare },
            p => p.CenterPattern, (p, v) => p.CenterPattern = v);

        public static readonly IReadOnlyList<MethodInfo> All = new[]
        {
            new MethodInfo(MethodKind.L1, isFill: false, needsClosed: false,
                Gap, Step, ExactStep, ExactCount, StartOffset, EndMargin, Reverse, CornerAngle),
            new MethodInfo(MethodKind.L2, isFill: false, needsClosed: false,
                Gap, RowCount, RowGap, RowSide, Stagger, Corners, Intersections, CornerAngle),
            new MethodInfo(MethodKind.L3, isFill: false, needsClosed: false,
                Gap, Offset, OffsetSide, Corners, CornerAngle),
            new MethodInfo(MethodKind.F1, isFill: true, needsClosed: true, Gap, Angle, EdgeMargin),
            new MethodInfo(MethodKind.F2, isFill: true, needsClosed: true, Gap, Angle, EdgeMargin),
            new MethodInfo(MethodKind.F3, isFill: true, needsClosed: true, Gap, EdgeMargin, CenterPattern),
            new MethodInfo(MethodKind.F4, isFill: true, needsClosed: true, Gap, Rings, EdgeMargin, CenterPattern),
            new MethodInfo(MethodKind.F5, isFill: true, needsClosed: true, Gap, Rings, EdgeMargin),
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
