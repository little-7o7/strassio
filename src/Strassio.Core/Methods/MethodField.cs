using System;
using System.Collections.Generic;

namespace Strassio.Core.Methods
{
    /// <summary>Что за поле: от этого зависит, как докер его рисует и как читает введённое.</summary>
    public enum FieldKind
    {
        /// <summary>Длина — показывается и вводится в мм или дюймах (настройка «Единицы»).</summary>
        Length,

        /// <summary>Целое число (количество рядов, страз).</summary>
        Integer,

        /// <summary>Угол в градусах.</summary>
        Angle,

        /// <summary>Выпадающий список вариантов.</summary>
        Choice,

        /// <summary>Галочка.</summary>
        Toggle,

        /// <summary>Размер из таблицы камней (список строит докер — таблица у каждого своя).</summary>
        Size,

        /// <summary>Строка, например шаблон размеров «ss6, ss6, ss10».</summary>
        Text,
    }

    /// <summary>
    /// Одно поле параметров в докере (docs/SPEC.md, раздел 2.2, пункт 5): подпись и подсказка
    /// (ключи файлов языков), границы, варианты, и как прочитать/записать значение в
    /// <see cref="MethodParameters"/>. Докер ничего не знает о конкретных полях — он просто
    /// рисует список полей выбранного метода (<see cref="MethodCatalog"/>).
    /// </summary>
    public sealed class MethodField
    {
        private readonly Func<MethodParameters, double>? getNumber;
        private readonly Action<MethodParameters, double>? setNumber;
        private readonly Func<MethodParameters, string>? getChoice;
        private readonly Action<MethodParameters, string>? setChoice;
        private readonly Func<MethodParameters, bool>? getToggle;
        private readonly Action<MethodParameters, bool>? setToggle;
        private readonly Func<MethodParameters, bool>? visibleWhen;

        private MethodField(
            string id, FieldKind kind, double min, double max, IReadOnlyList<string> choices,
            Func<MethodParameters, double>? getNumber, Action<MethodParameters, double>? setNumber,
            Func<MethodParameters, string>? getChoice, Action<MethodParameters, string>? setChoice,
            Func<MethodParameters, bool>? getToggle, Action<MethodParameters, bool>? setToggle,
            Func<MethodParameters, bool>? visibleWhen)
        {
            Id = id;
            Kind = kind;
            Min = min;
            Max = max;
            Choices = choices;
            this.getNumber = getNumber;
            this.setNumber = setNumber;
            this.getChoice = getChoice;
            this.setChoice = setChoice;
            this.getToggle = getToggle;
            this.setToggle = setToggle;
            this.visibleWhen = visibleWhen;
        }

        /// <summary>Короткое имя поля, например "gap".</summary>
        public string Id { get; }

        public FieldKind Kind { get; }

        /// <summary>Наименьшее допустимое значение (для длин — в мм).</summary>
        public double Min { get; }

        /// <summary>Наибольшее допустимое значение (для длин — в мм).</summary>
        public double Max { get; }

        /// <summary>Варианты для <see cref="FieldKind.Choice"/> — слова из <see cref="MethodChoices"/>.</summary>
        public IReadOnlyList<string> Choices { get; }

        /// <summary>Ключ подписи в файле языка: "param.gap".</summary>
        public string LabelKey => "param." + Id;

        /// <summary>Ключ подсказки: "param.gap.tooltip".</summary>
        public string TooltipKey => LabelKey + ".tooltip";

        /// <summary>Ключ текста варианта выбора: "param.step.fit".</summary>
        public string ChoiceKey(string choice) => LabelKey + "." + choice;

        /// <summary>
        /// Показывать ли поле при текущих значениях. Например, «Шаг» нужен, только когда выбран
        /// режим «точный шаг».
        /// </summary>
        public bool IsVisible(MethodParameters p) => visibleWhen == null || visibleWhen(p);

        public double GetNumber(MethodParameters p) => getNumber!(p);

        public string GetChoice(MethodParameters p) => getChoice!(p);

        public bool GetToggle(MethodParameters p) => getToggle!(p);

        /// <summary>Подходит ли число этому полю: в границах, а у целых — без дробной части.</summary>
        public bool Accepts(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < Min - 1e-9 || value > Max + 1e-9)
            {
                return false;
            }

            return Kind != FieldKind.Integer || Math.Abs(value - Math.Round(value)) < 1e-9;
        }

        /// <summary>Записывает число; false — число не подходит (<see cref="Accepts"/>), параметры не меняются.</summary>
        public bool TrySetNumber(MethodParameters p, double value)
        {
            if (!Accepts(value))
            {
                return false;
            }

            setNumber!(p, Kind == FieldKind.Integer ? Math.Round(value) : value);
            return true;
        }

        /// <summary>Для <see cref="FieldKind.Size"/>: есть ли вариант «как основной камень» (пустое значение).</summary>
        public bool AllowsSameSize { get; private set; }

        /// <summary>Записывает вариант; незнакомое слово не записывается. Для размеров — любое название из таблицы.</summary>
        public bool TrySetChoice(MethodParameters p, string choice)
        {
            if (Kind == FieldKind.Size || Kind == FieldKind.Text)
            {
                if (Kind == FieldKind.Size && choice.Length == 0 && !AllowsSameSize)
                {
                    return false;
                }

                setChoice!(p, choice.Trim());
                return true;
            }

            foreach (string c in Choices)
            {
                if (c == choice)
                {
                    setChoice!(p, choice);
                    return true;
                }
            }

            return false;
        }

        public void SetToggle(MethodParameters p, bool value) => setToggle!(p, value);

        internal static MethodField Number(
            string id, FieldKind kind, double min, double max,
            Func<MethodParameters, double> get, Action<MethodParameters, double> set,
            Func<MethodParameters, bool>? visibleWhen = null) =>
            new MethodField(id, kind, min, max, Array.Empty<string>(), get, set, null, null, null, null, visibleWhen);

        internal static MethodField Choice(
            string id, string[] choices,
            Func<MethodParameters, string> get, Action<MethodParameters, string> set) =>
            new MethodField(id, FieldKind.Choice, 0, 0, choices, null, null, get, set, null, null, null);

        internal static MethodField SizeChoice(
            string id, bool allowSame, Func<MethodParameters, string> get, Action<MethodParameters, string> set) =>
            new MethodField(id, FieldKind.Size, 0, 0, Array.Empty<string>(), null, null, get, set, null, null, null)
            {
                AllowsSameSize = allowSame,
            };

        internal static MethodField TextField(string id, Func<MethodParameters, string> get, Action<MethodParameters, string> set) =>
            new MethodField(id, FieldKind.Text, 0, 0, Array.Empty<string>(), null, null, get, set, null, null, null);

        internal static MethodField Toggle(
            string id, Func<MethodParameters, bool> get, Action<MethodParameters, bool> set) =>
            new MethodField(id, FieldKind.Toggle, 0, 0, Array.Empty<string>(), null, null, null, null, get, set, null);
    }
}
