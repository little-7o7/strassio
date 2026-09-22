#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Strassio.Core.Localization;
using Strassio.Core.Settings;
using Strassio.Core.Stones;
using Strassio.Corel.Themes;
using CorelApplication = Corel.Interop.VGCore.Application;
using WinForms = System.Windows.Forms;

namespace Strassio.Corel
{
    /// <summary>
    /// Редактор таблицы камней (docs/SPEC.md, раздел 3.1): добавить, изменить, удалить размер и цвет,
    /// поменять порядок. Правит копию таблицы; по «ОК» проверяет её (StoneTableRules.Validate) и,
    /// если ошибок нет, сохраняет в stones.json — докер сразу показывает новые размеры и цвета.
    /// </summary>
    public partial class StonesWindow : Window
    {
        private readonly PluginContext context = PluginContext.Instance;
        private readonly StoneTable table;
        private StoneSet set;
        private readonly ObservableCollection<SizeRow> sizeRows = new ObservableCollection<SizeRow>();
        private readonly ObservableCollection<ColorRow> colorRows = new ObservableCollection<ColorRow>();
        private bool loading;

        public StonesWindow(CorelApplication? app, string? selectSizeName)
        {
            InitializeComponent();
            DataContext = context.Localizer;
            SetOwner(app);
            ThemeManager.Attach(this, app);
            Closed += (s, e) => ThemeManager.Detach(this);

            table = StoneTableRules.Clone(context.Stones);
            if (table.Sets.Count == 0)
            {
                table.Sets.Add(new StoneSet { Name = Loc["stones.default.set"] });
            }

            set = StoneTableRules.FindSet(table, context.Settings.ActiveSet) ?? table.Sets[0];
            RebuildSets();
            SizeList.ItemsSource = sizeRows;
            ColorList.ItemsSource = colorRows;

            string unit = Loc[Units == LengthUnit.Inch ? "unit.in" : "unit.mm"];
            DiameterCaption.Text = Loc.Format("stones.diameter", unit);

            RebuildSizes(set.Sizes.FirstOrDefault(s => s.Name == selectSizeName) ?? set.Sizes.FirstOrDefault());
        }

        private Localizer Loc => context.Localizer;

        private LengthUnit Units => context.Settings.Units;

        private StoneSize? SelectedSize => (SizeList.SelectedItem as SizeRow)?.Size;

        private StoneColor? SelectedColor => (ColorList.SelectedItem as ColorRow)?.Color;

        private void SetOwner(CorelApplication? app)
        {
            try
            {
                if (app != null)
                {
                    new WindowInteropHelper(this).Owner = new IntPtr(app.AppWindow.Handle);
                }
            }
            catch (Exception)
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }

        // ---- Наборы ----------------------------------------------------------------------------

        private void RebuildSets()
        {
            loading = true;
            try
            {
                SetCombo.ItemsSource = null;
                SetCombo.ItemsSource = table.Sets;
                SetCombo.SelectedItem = set;
                RemoveSetButton.IsEnabled = table.Sets.Count > 1;
            }
            finally
            {
                loading = false;
            }
        }

        private void SetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!loading && SetCombo.SelectedItem is StoneSet chosen)
            {
                set = chosen;
                RebuildSizes(set.Sizes.FirstOrDefault());
            }
        }

        private void AddSet_Click(object sender, RoutedEventArgs e)
        {
            string? name = NameDialog.Ask(null, this, Loc["stones.addSet"], Loc["stones.setName"], StoneTableRules.UniqueName(table.Sets.Select(s => s.Name), Loc["stones.newSet"]));
            if (name == null)
            {
                return;
            }

            set = StoneTableRules.AddSet(table, set, name);
            RebuildSets();
            RebuildSizes(set.Sizes.FirstOrDefault());
        }

        private void RenameSet_Click(object sender, RoutedEventArgs e)
        {
            string? name = NameDialog.Ask(null, this, Loc["stones.renameSet"], Loc["stones.setName"], set.Name);
            if (name != null)
            {
                set.Name = name;
                RebuildSets();
            }
        }

        private void RemoveSet_Click(object sender, RoutedEventArgs e)
        {
            if (table.Sets.Count <= 1)
            {
                return;
            }

            int index = table.Sets.IndexOf(set);
            table.Sets.Remove(set);
            set = table.Sets[Math.Min(index, table.Sets.Count - 1)];
            RebuildSets();
            RebuildSizes(set.Sizes.FirstOrDefault());
        }

        // ---- Размеры ---------------------------------------------------------------------------

        private void RebuildSizes(StoneSize? select)
        {
            sizeRows.Clear();
            foreach (StoneSize size in set.Sizes)
            {
                sizeRows.Add(new SizeRow(size, SizeText(size)));
            }

            SizeList.SelectedItem = sizeRows.FirstOrDefault(r => ReferenceEquals(r.Size, select)) ?? sizeRows.FirstOrDefault();
            LoadSize();
        }

        private string SizeText(StoneSize size)
        {
            string unit = Loc[Units == LengthUnit.Inch ? "unit.in" : "unit.mm"];
            string diameter = double.IsNaN(size.DiameterMm)
                ? "?"
                : LengthUnits.Format(size.DiameterMm, Units, context.Settings.Decimals, CultureInfo.CurrentCulture);
            return Loc.Format("size.item", size.Name, diameter, unit);
        }

        /// <summary>Поля справа — из выбранного размера.</summary>
        private void LoadSize()
        {
            loading = true;
            try
            {
                StoneSize? size = SelectedSize;
                SizePanel.IsEnabled = size != null;
                RemoveSizeButton.IsEnabled = size != null;
                SizeNameBox.Text = size?.Name ?? string.Empty;
                DiameterBox.Text = size == null || double.IsNaN(size.DiameterMm)
                    ? string.Empty
                    : LengthUnits.Format(size.DiameterMm, Units, context.Settings.Decimals, CultureInfo.CurrentCulture);
                DiameterBox.ClearValue(BorderBrushProperty);
            }
            finally
            {
                loading = false;
            }

            RebuildColors(SelectedSize?.Colors.FirstOrDefault());
        }

        private void RefreshSelectedSizeRow()
        {
            if (SizeList.SelectedItem is SizeRow row)
            {
                row.Text = SizeText(row.Size);
            }
        }

        private void SizeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!loading)
            {
                LoadSize();
            }
        }

        private void SizeNameBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (loading || SelectedSize == null)
            {
                return;
            }

            SelectedSize.Name = SizeNameBox.Text;
            RefreshSelectedSizeRow();
        }

        private void DiameterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (loading || SelectedSize == null)
            {
                return;
            }

            // Неверное число — диаметр «не задан» (NaN): проверка по «ОК» не даст сохранить таблицу.
            if (LengthUnits.TryParse(DiameterBox.Text, Units, out double mm))
            {
                SelectedSize.DiameterMm = mm;
                DiameterBox.ClearValue(BorderBrushProperty);
            }
            else
            {
                SelectedSize.DiameterMm = double.NaN;
                DiameterBox.SetResourceReference(BorderBrushProperty, "Strassio.Error");
            }

            RefreshSelectedSizeRow();
        }

        private void AddSize_Click(object sender, RoutedEventArgs e)
        {
            StoneSize size = StoneTableRules.AddSize(set, SelectedSize, Loc["stones.newSize"]);
            RebuildSizes(size);
            SizeNameBox.Focus();
            SizeNameBox.SelectAll();
        }

        private void RemoveSize_Click(object sender, RoutedEventArgs e)
        {
            StoneSize? size = SelectedSize;
            if (size == null)
            {
                return;
            }

            int index = set.Sizes.IndexOf(size);
            set.Sizes.RemoveAt(index);
            RebuildSizes(set.Sizes.Count == 0 ? null : set.Sizes[Math.Min(index, set.Sizes.Count - 1)]);
        }

        private void SizeUp_Click(object sender, RoutedEventArgs e) => MoveSize(-1);

        private void SizeDown_Click(object sender, RoutedEventArgs e) => MoveSize(+1);

        private void MoveSize(int delta)
        {
            StoneSize? size = SelectedSize;
            if (size != null)
            {
                StoneTableRules.Move(set.Sizes, set.Sizes.IndexOf(size), delta);
                RebuildSizes(size);
            }
        }

        private void Sort_Click(object sender, RoutedEventArgs e)
        {
            StoneSize? size = SelectedSize;
            StoneTableRules.SortByDiameter(set);
            RebuildSizes(size);
        }

        // ---- Цвета выбранного размера ----------------------------------------------------------

        private void RebuildColors(StoneColor? select)
        {
            colorRows.Clear();
            foreach (StoneColor color in SelectedSize?.Colors ?? new List<StoneColor>())
            {
                colorRows.Add(new ColorRow(color));
            }

            ColorList.SelectedItem = colorRows.FirstOrDefault(r => ReferenceEquals(r.Color, select)) ?? colorRows.FirstOrDefault();
            LoadColor();
        }

        private void LoadColor()
        {
            loading = true;
            try
            {
                StoneColor? color = SelectedColor;
                ColorPanel.IsEnabled = color != null;
                RemoveColorButton.IsEnabled = color != null;
                ColorNameBox.Text = color?.Name ?? string.Empty;
                RgbBox.Text = color?.Rgb ?? string.Empty;
                RgbBox.ClearValue(BorderBrushProperty);
            }
            finally
            {
                loading = false;
            }
        }

        private void ColorList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!loading)
            {
                LoadColor();
            }
        }

        private void ColorNameBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (loading || !(ColorList.SelectedItem is ColorRow row))
            {
                return;
            }

            row.Color.Name = ColorNameBox.Text;
            row.Refresh();
        }

        private void RgbBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (loading || !(ColorList.SelectedItem is ColorRow row))
            {
                return;
            }

            // Неверную запись оставляем как есть — проверка по «ОК» покажет, какой цвет испорчен.
            if (StoneTableRules.TryNormalizeRgb(RgbBox.Text, out string rgb))
            {
                row.Color.Rgb = rgb;
                RgbBox.ClearValue(BorderBrushProperty);
            }
            else
            {
                row.Color.Rgb = RgbBox.Text;
                RgbBox.SetResourceReference(BorderBrushProperty, "Strassio.Error");
            }

            row.Refresh();
        }

        private void PickColor_Click(object sender, RoutedEventArgs e)
        {
            if (!(ColorList.SelectedItem is ColorRow row))
            {
                return;
            }

            using var dialog = new WinForms.ColorDialog { FullOpen = true, AnyColor = true };
            if (row.Color.TryGetRgb(out byte r, out byte g, out byte b))
            {
                dialog.Color = System.Drawing.Color.FromArgb(r, g, b);
            }

            if (dialog.ShowDialog() == WinForms.DialogResult.OK)
            {
                RgbBox.Text = StoneTableRules.ToHex(dialog.Color.R, dialog.Color.G, dialog.Color.B);
            }
        }

        private void AddColor_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedSize == null)
            {
                return;
            }

            StoneColor color = StoneTableRules.AddColor(SelectedSize, Loc["stones.newColor"]);
            RebuildColors(color);
            ColorNameBox.Focus();
            ColorNameBox.SelectAll();
        }

        private void RemoveColor_Click(object sender, RoutedEventArgs e)
        {
            StoneSize? size = SelectedSize;
            StoneColor? color = SelectedColor;
            if (size == null || color == null)
            {
                return;
            }

            int index = size.Colors.IndexOf(color);
            size.Colors.RemoveAt(index);
            RebuildColors(size.Colors.Count == 0 ? null : size.Colors[Math.Min(index, size.Colors.Count - 1)]);
        }

        private void ColorUp_Click(object sender, RoutedEventArgs e) => MoveColor(-1);

        private void ColorDown_Click(object sender, RoutedEventArgs e) => MoveColor(+1);

        private void MoveColor(int delta)
        {
            StoneSize? size = SelectedSize;
            StoneColor? color = SelectedColor;
            if (size != null && color != null)
            {
                StoneTableRules.Move(size.Colors, size.Colors.IndexOf(color), delta);
                RebuildColors(color);
            }
        }

        private void CopyColors_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedSize != null)
            {
                StoneTableRules.CopyColorsToAllSizes(set, SelectedSize);
                ErrorText.Text = Loc.Format("stones.copyColors.done", SelectedSize.Name);
                ErrorText.SetResourceReference(TextBlock.ForegroundProperty, "Strassio.MutedForeground");
                ErrorText.Visibility = Visibility.Visible;
            }
        }

        // ---- Сохранение ------------------------------------------------------------------------

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            IReadOnlyList<StoneProblem> problems = StoneTableRules.Validate(table);
            if (problems.Count > 0)
            {
                // Показываем до трёх ошибок сразу — чтобы не чинить по одной вслепую.
                ErrorText.Text = string.Join(
                    Environment.NewLine,
                    problems.Take(3).Select(p => Loc.Format(p.Key, Args(p))));
                ErrorText.SetResourceReference(TextBlock.ForegroundProperty, "Strassio.Error");
                ErrorText.Visibility = Visibility.Visible;
                return;
            }

            foreach (StoneSet each in table.Sets)
            {
                StoneTableRules.Tidy(each);
            }

            context.Settings.ActiveSet = set.Name;
            context.SaveSettingsQuietly();
            context.ReplaceStones(table);
            DialogResult = true;
        }

        /// <summary>Значения для текста ошибки; для диаметра добавляем допустимые границы в текущих единицах.</summary>
        private object[] Args(StoneProblem problem)
        {
            if (problem.Key != "stones.error.diameter")
            {
                return problem.Args;
            }

            int decimals = context.Settings.Decimals;
            string unit = Loc[Units == LengthUnit.Inch ? "unit.in" : "unit.mm"];
            return problem.Args.Concat(new object[]
            {
                LengthUnits.Format(StoneTableRules.MinDiameterMm, Units, decimals, CultureInfo.CurrentCulture),
                LengthUnits.Format(StoneTableRules.MaxDiameterMm, Units, decimals, CultureInfo.CurrentCulture),
                unit,
            }).ToArray();
        }

        /// <summary>Строка списка размеров; текст обновляется на лету, пока пользователь печатает.</summary>
        public sealed class SizeRow : INotifyPropertyChanged
        {
            private string text;

            public SizeRow(StoneSize size, string text)
            {
                Size = size;
                this.text = text;
            }

            public event PropertyChangedEventHandler? PropertyChanged;

            public StoneSize Size { get; }

            public string Text
            {
                get => text;
                set
                {
                    text = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
                }
            }
        }

        /// <summary>Строка списка цветов: кружок-образец и название.</summary>
        public sealed class ColorRow : INotifyPropertyChanged
        {
            public ColorRow(StoneColor color)
            {
                Color = color;
            }

            public event PropertyChangedEventHandler? PropertyChanged;

            public StoneColor Color { get; }

            public string Name => Color.Name;

            public Brush Brush
            {
                get
                {
                    var brush = Color.TryGetRgb(out byte r, out byte g, out byte b)
                        ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b))
                        : new SolidColorBrush(Colors.Transparent);
                    brush.Freeze();
                    return brush;
                }
            }

            public void Refresh()
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Brush)));
            }
        }
    }
}
