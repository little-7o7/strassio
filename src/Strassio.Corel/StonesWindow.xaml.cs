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
using Strassio.Core.Cnc;
using Strassio.Core.Localization;
using Strassio.Core.Settings;
using Strassio.Core.Stones;
using Strassio.Corel.Themes;
using CorelApplication = Corel.Interop.VGCore.Application;
using WinForms = System.Windows.Forms;

namespace Strassio.Corel
{
    /// <summary>
    /// Каталог страз и свёрл (docs/SPEC.md, разделы 3.1 и 11.1): добавить, изменить, удалить размер,
    /// цвет и сверло, поменять порядок, выбрать сверло для размера. Правит копию таблицы; по «ОК»
    /// проверяет её (StoneTableRules.Validate) и, если ошибок нет, сохраняет в stones.json — докер сразу
    /// показывает новые размеры и цвета, а сверление — новые свёрла.
    /// </summary>
    public partial class StonesWindow : Window
    {
        private readonly PluginContext context = PluginContext.Instance;
        private readonly StoneTable table;
        private StoneSet set;
        private readonly ObservableCollection<SizeRow> sizeRows = new ObservableCollection<SizeRow>();
        private readonly ObservableCollection<ColorRow> colorRows = new ObservableCollection<ColorRow>();
        private readonly ObservableCollection<DrillRow> drillRows = new ObservableCollection<DrillRow>();

        /// <summary>Какое сверло каталога выбрано у размера: при смене диаметра сверла размер идёт следом.</summary>
        private readonly Dictionary<StoneSize, DrillTool> links = new Dictionary<StoneSize, DrillTool>();
        private bool loading;

        /// <param name="drillsTab">Открыть сразу вкладку «Свёрла» (из окна сверления).</param>
        /// <param name="source">Что править (по умолчанию — рабочая таблица плагина).</param>
        /// <param name="owner">Окно-владелец из Strassio (иначе — окно CorelDRAW).</param>
        public StonesWindow(CorelApplication? app, string? selectSizeName, bool drillsTab = false, StoneTable? source = null, Window? owner = null)
        {
            InitializeComponent();
            DataContext = context.Localizer;
            if (owner != null)
            {
                Owner = owner;
            }
            else
            {
                SetOwner(app);
            }

            ThemeManager.Attach(this, app);
            Closed += (s, e) => ThemeManager.Detach(this);

            table = StoneTableRules.Clone(source ?? context.Stones);
            foreach (StoneSize size in table.Sets.SelectMany(x => x.Sizes))
            {
                DrillTool? linked = size.DrillMm > 0 ? table.Drills.FirstOrDefault(d => Math.Abs(d.DiameterMm - size.DrillMm) <= DrillRack.SameDrillMm) : null;
                if (linked != null)
                {
                    links[size] = linked;
                }
            }

            DrillList.ItemsSource = drillRows;
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
            RebuildDrills(table.Drills.FirstOrDefault());
            if (drillsTab)
            {
                Tabs.SelectedItem = DrillsTab;
            }
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
                RefreshDrillSizes();
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

            LoadSizeDrill();
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
            ShowSizeDrillHint();
            RefreshDrillSizes();
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

        // ---- Сверло размера ---------------------------------------------------------------------

        /// <summary>Список «авто» + свёрла каталога (+ сверло не из каталога, если у размера такое).</summary>
        private void LoadSizeDrill()
        {
            loading = true;
            try
            {
                StoneSize? size = SelectedSize;
                var options = new List<DrillOption> { new DrillOption(null, 0, Loc["catalog.sizeDrill.auto"]) };
                foreach (DrillTool tool in table.Drills.Where(d => d.DiameterMm > 0))
                {
                    options.Add(new DrillOption(tool, tool.DiameterMm, DrillTitle(tool)));
                }

                DrillOption selected = options[0];
                if (size != null && links.TryGetValue(size, out DrillTool? linked))
                {
                    selected = options.FirstOrDefault(o => ReferenceEquals(o.Tool, linked)) ?? options[0];
                }
                else if (size != null && size.DrillMm > 0)
                {
                    selected = new DrillOption(null, size.DrillMm, Loc.Format("catalog.sizeDrill.missing", Mm(size.DrillMm)));
                    options.Add(selected);
                }

                SizeDrillCombo.ItemsSource = options;
                SizeDrillCombo.SelectedItem = selected;
            }
            finally
            {
                loading = false;
            }

            ShowSizeDrillHint();
        }

        private void SizeDrillCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (loading || SelectedSize == null || !(SizeDrillCombo.SelectedItem is DrillOption option))
            {
                return;
            }

            SelectedSize.DrillMm = option.Mm;
            if (option.Tool != null)
            {
                links[SelectedSize] = option.Tool;
            }
            else
            {
                links.Remove(SelectedSize);
            }

            ShowSizeDrillHint();
            RefreshDrillSizes();
        }

        /// <summary>Каким сверлом будет сверлиться размер и что из этого выйдет (как при сверлении).</summary>
        private void ShowSizeDrillHint()
        {
            StoneSize? size = SelectedSize;
            if (size == null || double.IsNaN(size.DiameterMm) || size.DiameterMm <= 0)
            {
                SizeDrillHint.Text = string.Empty;
                return;
            }

            var settings = new DrillSettings();
            double drill = DrillPlanner.ChooseDrill(size.Name, size.DiameterMm, settings, Rack());
            string text = size.DrillMm > 0 ? string.Empty : Loc.Format("catalog.hint.auto", Mm(drill)) + " ";
            if (table.Drills.Count == 0)
            {
                text = Loc["catalog.hint.noDrills"];
            }
            else if (DrillPlanner.MillRadius(size.DiameterMm, drill, settings) > 0)
            {
                text += Loc.Format("catalog.hint.mill", Mm(drill), Mm(size.DiameterMm));
            }
            else if (drill > size.DiameterMm + settings.MaxMismatchMm + 1e-9)
            {
                text += Loc.Format("catalog.hint.bigger", Mm(drill), Mm(Math.Round(drill - size.DiameterMm, 2)));
            }
            else
            {
                text += Loc.Format("catalog.hint.ok", Mm(drill));
            }

            SizeDrillHint.Text = text.Trim();
        }

        private DrillRack Rack() => new DrillRack(
            table.Drills,
            set.Sizes.Where(x => x.DrillMm > 0).Select(x => new KeyValuePair<string, double>(x.Name, x.DrillMm)));

        // ---- Свёрла ------------------------------------------------------------------------------

        private DrillTool? SelectedDrill => (DrillList.SelectedItem as DrillRow)?.Tool;

        private void RebuildDrills(DrillTool? select)
        {
            drillRows.Clear();
            for (int i = 0; i < table.Drills.Count; i++)
            {
                drillRows.Add(new DrillRow(table.Drills[i]));
            }

            RefreshDrillRows();
            DrillList.SelectedItem = drillRows.FirstOrDefault(r => ReferenceEquals(r.Tool, select)) ?? drillRows.FirstOrDefault();
            LoadDrill();
            LoadSizeDrill();
        }

        /// <summary>Подписи списка свёрл: диаметр, номер T, название; ниже — какие размеры им сверлятся.</summary>
        private void RefreshDrillRows()
        {
            for (int i = 0; i < drillRows.Count; i++)
            {
                DrillTool tool = drillRows[i].Tool;
                IReadOnlyList<string> sizes = tool.DiameterMm > 0 ? DrillCatalog.SizesOf(tool, set, table.Drills, true, new DrillSettings().MaxMismatchMm) : Array.Empty<string>();
                drillRows[i].Update(
                    Loc.Format("catalog.drillRow", tool.DiameterMm > 0 ? Mm(tool.DiameterMm) : "?", tool.Tool > 0 ? tool.Tool : i + 1, tool.Name?.Trim() ?? string.Empty).Trim().TrimEnd('·').Trim(),
                    sizes.Count > 0 ? Loc.Format("catalog.drillSizes", string.Join(", ", sizes)) : Loc["catalog.drillSizes.none"]);
            }
        }

        private void RefreshDrillSizes()
        {
            RefreshDrillRows();
            if (SelectedDrill != null)
            {
                int index = table.Drills.IndexOf(SelectedDrill);
                DrillSizesText.Text = index >= 0 ? drillRows[index].Sizes : string.Empty;
            }
        }

        private void LoadDrill()
        {
            loading = true;
            try
            {
                DrillTool? tool = SelectedDrill;
                DrillPanel.IsEnabled = tool != null;
                RemoveDrillButton.IsEnabled = tool != null;
                DrillDiameterBox.Text = tool == null || !(tool.DiameterMm > 0) ? string.Empty : Mm(tool.DiameterMm);
                DrillToolBox.Text = tool != null && tool.Tool > 0 ? tool.Tool.ToString(CultureInfo.CurrentCulture) : string.Empty;
                DrillNameBox.Text = tool?.Name ?? string.Empty;
                DrillFeedBox.Text = tool != null && tool.Feed > 0 ? Mm(tool.Feed) : string.Empty;
                DrillRpmBox.Text = tool != null && tool.Rpm > 0 ? tool.Rpm.ToString(CultureInfo.CurrentCulture) : string.Empty;
                foreach (TextBox box in new[] { DrillDiameterBox, DrillToolBox, DrillFeedBox, DrillRpmBox })
                {
                    box.ClearValue(BorderBrushProperty);
                }
            }
            finally
            {
                loading = false;
            }

            RefreshDrillSizes();
        }

        private void DrillList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!loading)
            {
                LoadDrill();
            }
        }

        /// <summary>Поля сверла: пустой номер, подача и обороты — «как в профиле станка» (0).</summary>
        private void DrillField_TextChanged(object sender, TextChangedEventArgs e)
        {
            DrillTool? tool = SelectedDrill;
            if (loading || tool == null)
            {
                return;
            }

            if (ReferenceEquals(sender, DrillDiameterBox))
            {
                bool ok = DrillText.TryParseMm(DrillDiameterBox.Text, out double d) && d > 0;
                tool.DiameterMm = ok ? d : double.NaN;
                Mark(DrillDiameterBox, ok);
                if (ok)
                {
                    // Размеры с этим сверлом идут следом за новым диаметром.
                    foreach (KeyValuePair<StoneSize, DrillTool> link in links.Where(l => ReferenceEquals(l.Value, tool)).ToList())
                    {
                        link.Key.DrillMm = d;
                    }
                }
            }
            else if (ReferenceEquals(sender, DrillToolBox))
            {
                string text = DrillToolBox.Text.Trim();
                bool ok = text.Length == 0 || (int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int t) && t >= 0);
                tool.Tool = ok && text.Length > 0 ? int.Parse(text, CultureInfo.CurrentCulture) : ok ? 0 : -1;
                Mark(DrillToolBox, ok);
            }
            else if (ReferenceEquals(sender, DrillNameBox))
            {
                tool.Name = DrillNameBox.Text;
            }
            else if (ReferenceEquals(sender, DrillFeedBox))
            {
                string text = DrillFeedBox.Text.Trim();
                bool ok = text.Length == 0 || (DrillText.TryParseMm(text, out double f) && f >= 0);
                tool.Feed = ok && text.Length > 0 && DrillText.TryParseMm(text, out double feed) ? feed : ok ? 0 : -1;
                Mark(DrillFeedBox, ok);
            }
            else if (ReferenceEquals(sender, DrillRpmBox))
            {
                string text = DrillRpmBox.Text.Trim();
                bool ok = text.Length == 0 || (DrillText.TryParseMm(text, out double r) && r >= 0);
                tool.Rpm = ok && text.Length > 0 && DrillText.TryParseMm(text, out double rpm) ? (int)Math.Round(rpm) : ok ? 0 : -1;
                Mark(DrillRpmBox, ok);
            }

            RefreshDrillSizes();
            LoadSizeDrill();
        }

        private static void Mark(TextBox box, bool ok)
        {
            if (ok)
            {
                box.ClearValue(BorderBrushProperty);
            }
            else
            {
                box.SetResourceReference(BorderBrushProperty, "Strassio.Error");
            }
        }

        private void AddDrill_Click(object sender, RoutedEventArgs e)
        {
            DrillTool? after = SelectedDrill;
            double basis = after != null && after.DiameterMm > 0 ? after.DiameterMm : table.Drills.Where(d => d.DiameterMm > 0).Select(d => d.DiameterMm).DefaultIfEmpty(2.1).Max();
            double d = Math.Round(basis + 0.1, 2);
            while (table.Drills.Any(x => Math.Abs(x.DiameterMm - d) <= DrillRack.SameDrillMm))
            {
                d = Math.Round(d + 0.1, 2);
            }

            var tool = new DrillTool { DiameterMm = d };
            int index = after == null ? table.Drills.Count : table.Drills.IndexOf(after) + 1;
            table.Drills.Insert(index, tool);
            RebuildDrills(tool);
            DrillDiameterBox.Focus();
            DrillDiameterBox.SelectAll();
        }

        /// <summary>Убрать сверло; размеры, которые им сверлились, становятся «авто».</summary>
        private void RemoveDrill_Click(object sender, RoutedEventArgs e)
        {
            DrillTool? tool = SelectedDrill;
            if (tool == null)
            {
                return;
            }

            foreach (KeyValuePair<StoneSize, DrillTool> link in links.Where(l => ReferenceEquals(l.Value, tool)).ToList())
            {
                link.Key.DrillMm = 0;
                links.Remove(link.Key);
            }

            int index = table.Drills.IndexOf(tool);
            table.Drills.RemoveAt(index);
            RebuildDrills(table.Drills.Count == 0 ? null : table.Drills[Math.Min(index, table.Drills.Count - 1)]);
        }

        private void DrillUp_Click(object sender, RoutedEventArgs e) => MoveDrill(-1);

        private void DrillDown_Click(object sender, RoutedEventArgs e) => MoveDrill(+1);

        private void MoveDrill(int delta)
        {
            DrillTool? tool = SelectedDrill;
            if (tool != null)
            {
                StoneTableRules.Move(table.Drills, table.Drills.IndexOf(tool), delta);
                RebuildDrills(tool);
            }
        }

        private void SortDrills_Click(object sender, RoutedEventArgs e)
        {
            DrillTool? tool = SelectedDrill;
            DrillCatalog.SortByDiameter(table.Drills);
            RebuildDrills(tool);
        }

        private string DrillTitle(DrillTool tool)
        {
            string name = tool.Name?.Trim() ?? string.Empty;
            return name.Length > 0 ? Loc.Format("catalog.drillOption.named", Mm(tool.DiameterMm), name) : Loc.Format("catalog.drillOption", Mm(tool.DiameterMm));
        }

        private static string Mm(double value) => DrillText.Format(value, CultureInfo.CurrentCulture);

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
                Tabs.SelectedItem = problems[0].Key.StartsWith("catalog.", StringComparison.Ordinal) ? DrillsTab : StonesTab;
                return;
            }

            foreach (StoneSet each in table.Sets)
            {
                StoneTableRules.Tidy(each);
            }

            foreach (DrillTool tool in table.Drills)
            {
                tool.Name = (tool.Name ?? string.Empty).Trim();
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

        /// <summary>Строка выбора сверла для размера: сверло каталога (или null — «авто» / не из каталога) и текст.</summary>
        public sealed class DrillOption
        {
            public DrillOption(DrillTool? tool, double mm, string text)
            {
                Tool = tool;
                Mm = mm;
                Text = text;
            }

            public DrillTool? Tool { get; }

            public double Mm { get; }

            public string Text { get; }
        }

        /// <summary>Строка списка свёрл: диаметр, номер, название и какие размеры сверлит.</summary>
        public sealed class DrillRow : INotifyPropertyChanged
        {
            public DrillRow(DrillTool tool)
            {
                Tool = tool;
            }

            public event PropertyChangedEventHandler? PropertyChanged;

            public DrillTool Tool { get; }

            public string Text { get; private set; } = string.Empty;

            public string Sizes { get; private set; } = string.Empty;

            public void Update(string text, string sizes)
            {
                Text = text;
                Sizes = sizes;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Sizes)));
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
