#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Strassio.Core.Methods;
using Strassio.Core.Settings;

namespace Strassio.Corel
{
    /// <summary>
    /// Поля параметров выбранного метода (docs/SPEC.md, раздел 2.2, пункт 5). Какие поля показать —
    /// решает Strassio.Core.Methods.MethodCatalog; здесь они только рисуются и читаются.
    /// Значения сразу пишутся в настройки (PluginSettings.Method) и запоминаются между запусками.
    /// </summary>
    public partial class Docker
    {
        private readonly List<ParamRow> paramRows = new List<ParamRow>();

        private MethodParameters Params => context.Settings.Method;

        /// <summary>Строит поля заново: при смене метода, языка, единиц или знаков после запятой.</summary>
        private void RebuildParams()
        {
            ParamsGrid.Children.Clear();
            ParamsGrid.RowDefinitions.Clear();
            paramRows.Clear();

            IReadOnlyList<MethodField> fields = (MethodCombo.SelectedItem as MethodOption)?.Info.Fields ?? new MethodField[0];
            foreach (MethodField field in fields)
            {
                ParamsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                int row = ParamsGrid.RowDefinitions.Count - 1;
                paramRows.Add(field.Kind == FieldKind.Toggle ? AddToggle(field, row) : AddLabeled(field, row));
            }

            ParamsHeader.Visibility = fields.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateParamVisibility();
        }

        private ParamRow AddToggle(MethodField field, int row)
        {
            var box = new CheckBox
            {
                Content = Loc[field.LabelKey],
                ToolTip = Loc[field.TooltipKey],
                IsChecked = field.GetToggle(Params),
                Margin = new Thickness(0, 2, 0, 6),
            };
            box.Click += (s, e) =>
            {
                field.SetToggle(Params, box.IsChecked == true);
                context.SaveSettingsQuietly();
            };
            Place(box, row, 0, columnSpan: 2);
            return new ParamRow(field, null, box);
        }

        private ParamRow AddLabeled(MethodField field, int row)
        {
            var label = new TextBlock
            {
                Text = LabelText(field),
                ToolTip = Loc[field.TooltipKey],
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 8, 6),
            };
            label.SetResourceReference(StyleProperty, "Strassio.Caption");
            Place(label, row, 0);

            FrameworkElement input =
                field.Kind == FieldKind.Choice ? CreateChoice(field) :
                field.Kind == FieldKind.Size ? CreateSizeChoice(field) :
                field.Kind == FieldKind.Text ? CreateTextBox(field) :
                CreateNumberBox(field);
            input.ToolTip = Loc[field.TooltipKey];
            input.Margin = new Thickness(0, 0, 0, 6);
            Place(input, row, 1);
            return new ParamRow(field, label, input);
        }

        private ComboBox CreateChoice(MethodField field)
        {
            List<ChoiceOption> options = field.Choices
                .Select(c => new ChoiceOption(c, Loc[field.ChoiceKey(c)]))
                .ToList();
            var combo = new ComboBox { DisplayMemberPath = nameof(ChoiceOption.Text), ItemsSource = options };
            string current = field.GetChoice(Params);
            combo.SelectedItem = options.FirstOrDefault(o => o.Value == current) ?? options.FirstOrDefault();
            combo.SelectionChanged += (s, e) =>
            {
                if (combo.SelectedItem is ChoiceOption chosen && field.TrySetChoice(Params, chosen.Value))
                {
                    context.SaveSettingsQuietly();
                    UpdateParamVisibility();
                }
            };
            return combo;
        }

        /// <summary>
        /// Размер из таблицы камней (L2 крайние ряды, L5, L8). «Как основной камень» — пустое значение.
        /// Если поле пустое, а такого варианта нет — выбирается самый крупный размер таблицы.
        /// </summary>
        private ComboBox CreateSizeChoice(MethodField field)
        {
            PluginSettings settings = context.Settings;
            string unit = Loc[settings.Units == LengthUnit.Inch ? "unit.in" : "unit.mm"];
            var options = new List<ChoiceOption>();
            if (field.AllowsSameSize)
            {
                options.Add(new ChoiceOption(MethodChoices.SameSize, Loc["param.size.same"]));
            }

            foreach (Strassio.Core.Stones.StoneSize size in ActiveSet?.Sizes ?? new List<Strassio.Core.Stones.StoneSize>())
            {
                options.Add(new ChoiceOption(size.Name, Loc.Format(
                    "size.item", size.Name,
                    LengthUnits.Format(size.DiameterMm, settings.Units, settings.Decimals, CultureInfo.CurrentCulture), unit)));
            }

            var combo = new ComboBox { DisplayMemberPath = nameof(ChoiceOption.Text), ItemsSource = options };
            string current = field.GetChoice(Params);
            ChoiceOption? chosen = options.FirstOrDefault(o => string.Equals(o.Value, current, System.StringComparison.OrdinalIgnoreCase));
            if (chosen == null && !field.AllowsSameSize && ActiveSet != null && ActiveSet.Sizes.Count > 0)
            {
                // Пусто: для акцента — самый крупный размер, для щелей (F12) — самый мелкий.
                string pick = (field.DefaultsToSmallest
                    ? ActiveSet.Sizes.OrderBy(s => s.DiameterMm)
                    : ActiveSet.Sizes.OrderByDescending(s => s.DiameterMm)).First().Name;
                chosen = options.FirstOrDefault(o => o.Value == pick);
                field.TrySetChoice(Params, pick);
            }

            combo.SelectedItem = chosen ?? options.FirstOrDefault();
            combo.SelectionChanged += (s, e) =>
            {
                if (combo.SelectedItem is ChoiceOption picked && field.TrySetChoice(Params, picked.Value))
                {
                    context.SaveSettingsQuietly();
                }
            };
            return combo;
        }

        /// <summary>Строка — шаблон размеров L6 «ss6, ss6, ss10»; проверяется по таблице камней.</summary>
        private TextBox CreateTextBox(MethodField field)
        {
            var box = new TextBox { Text = field.GetChoice(Params) };
            box.LostKeyboardFocus += (s, e) => CommitText(field, box, reportError: true);
            box.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    CommitText(field, box, reportError: true);
                }
            };
            return box;
        }

        private bool CommitText(MethodField field, TextBox box, bool reportError)
        {
            string text = box.Text.Trim();
            IReadOnlyList<string> unknown = SizePatterns.Unknown(text, KnownSizes);
            bool empty = SizePatterns.Split(text).Length == 0;
            string? error = empty && !field.AllowsEmptyText ? "param.pattern.empty" : unknown.Count > 0 ? "param.pattern.invalid" : null;
            if (error == null && field.TrySetChoice(Params, text))
            {
                box.ClearValue(BorderBrushProperty);
                context.SaveSettingsQuietly();
                return true;
            }

            box.SetResourceReference(BorderBrushProperty, "Strassio.Error");
            if (reportError)
            {
                SetStatus(error ?? "param.pattern.empty", string.Join(", ", unknown));
            }

            return false;
        }

        private TextBox CreateNumberBox(MethodField field)
        {
            var box = new TextBox { Text = FormatNumber(field, field.GetNumber(Params)) };
            box.LostKeyboardFocus += (s, e) => CommitNumber(field, box, reportError: true);
            box.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    CommitNumber(field, box, reportError: true);
                }
            };
            return box;
        }

        /// <summary>
        /// Записывает введённое число в параметры. Неверное — не записывается: поле подсвечивается,
        /// внизу пишется, какие числа подходят. false — число не принято.
        /// </summary>
        private bool CommitNumber(MethodField field, TextBox box, bool reportError)
        {
            bool ok = TryParseNumber(field, box.Text, out double value) && field.TrySetNumber(Params, value);
            if (ok)
            {
                box.ClearValue(BorderBrushProperty);
                box.Text = FormatNumber(field, field.GetNumber(Params));
                context.SaveSettingsQuietly();
                return true;
            }

            box.SetResourceReference(BorderBrushProperty, "Strassio.Error");
            if (reportError)
            {
                SetStatus("param.invalid", Loc[field.LabelKey], FormatNumber(field, field.Min), FormatNumber(field, field.Max));
            }

            return false;
        }

        /// <summary>Перед «Создать»: принимает все видимые поля; первое неверное получает фокус.</summary>
        private bool CommitAllParams()
        {
            foreach (ParamRow row in paramRows)
            {
                if (!(row.Input is TextBox box) || row.Input.Visibility != Visibility.Visible)
                {
                    continue;
                }

                bool ok = row.Field.Kind == FieldKind.Text
                    ? CommitText(row.Field, box, reportError: true)
                    : CommitNumber(row.Field, box, reportError: true);
                if (!ok)
                {
                    box.Focus();
                    box.SelectAll();
                    return false;
                }
            }

            return true;
        }

        /// <summary>Прячет поля, которые не нужны при текущих значениях (например, «Шаг» без режима «точный шаг»).</summary>
        private void UpdateParamVisibility()
        {
            foreach (ParamRow row in paramRows)
            {
                Visibility visibility = row.Field.IsVisible(Params) ? Visibility.Visible : Visibility.Collapsed;
                row.Input.Visibility = visibility;
                if (row.Label != null)
                {
                    row.Label.Visibility = visibility;
                }
            }
        }

        private string LabelText(MethodField field)
        {
            switch (field.Kind)
            {
                case FieldKind.Length:
                    string unit = Loc[context.Settings.Units == LengthUnit.Inch ? "unit.in" : "unit.mm"];
                    return Loc.Format("param.label", Loc[field.LabelKey], unit);
                case FieldKind.Angle:
                    return Loc.Format("param.label", Loc[field.LabelKey], Loc["param.unit.deg"]);
                default:
                    return Loc[field.LabelKey];
            }
        }

        private string FormatNumber(MethodField field, double value)
        {
            PluginSettings settings = context.Settings;
            switch (field.Kind)
            {
                case FieldKind.Length:
                    return LengthUnits.Format(value, settings.Units, settings.Decimals, CultureInfo.CurrentCulture);
                case FieldKind.Integer:
                    return ((long)System.Math.Round(value)).ToString(CultureInfo.CurrentCulture);
                default:
                    return System.Math.Round(value, System.Math.Max(0, System.Math.Min(settings.Decimals, 10)))
                        .ToString("0.##########", CultureInfo.CurrentCulture);
            }
        }

        /// <summary>Длины — в текущих единицах (мм или дюймы) → мм; углы и количества — как есть. Запятая и точка — обе годятся.</summary>
        private bool TryParseNumber(MethodField field, string text, out double value)
        {
            LengthUnit unit = field.Kind == FieldKind.Length ? context.Settings.Units : LengthUnit.Millimeter;
            return LengthUnits.TryParse(text, unit, out value);
        }

        private void Place(UIElement element, int row, int column, int columnSpan = 1)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            Grid.SetColumnSpan(element, columnSpan);
            ParamsGrid.Children.Add(element);
        }

        private sealed class ParamRow
        {
            public ParamRow(MethodField field, TextBlock? label, FrameworkElement input)
            {
                Field = field;
                Label = label;
                Input = input;
            }

            public MethodField Field { get; }

            public TextBlock? Label { get; }

            public FrameworkElement Input { get; }
        }
    }
}
