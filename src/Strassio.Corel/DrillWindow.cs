#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Strassio.Core.Cnc;
using Strassio.Core.Localization;
using Strassio.Core.Stones;
using Strassio.Corel.Themes;
using CorelApplication = Corel.Interop.VGCore.Application;
using Point2D = Strassio.Core.Geometry.Point2D;

namespace Strassio.Corel
{
    /// <summary>Чем закончилось окно «Сверление».</summary>
    internal enum DrillWindowResult
    {
        None,
        Saved,
        DrawPath,
    }

    /// <summary>
    /// Окно «Сверление на станке» (docs/SPEC.md, раздел 11.1). Слева — профиль станка, сверло для каждого
    /// размера камня (из каталога страз и свёрл), глубина и скорости, ноль и доска, повтор и метки,
    /// порядок обхода, формат файла. Справа — картинка пути на доске и что получится: сколько свёрл и
    /// файлов, примерно сколько времени, предупреждения. «Сохранить» пишет файлы G-code (.tap),
    /// «Показать путь» — рисует путь сверла в CorelDRAW. Строится кодом, как NameDialog; все длины — в мм.
    /// </summary>
    internal sealed class DrillWindow : Window
    {
        private static readonly Color[] JobColors =
        {
            Color.FromRgb(30, 136, 229), Color.FromRgb(229, 57, 53), Color.FromRgb(67, 160, 71),
            Color.FromRgb(251, 140, 0), Color.FromRgb(142, 36, 170), Color.FromRgb(0, 137, 123),
        };

        private readonly IReadOnlyList<DrillHole> holes;
        private readonly Localizer loc;
        private readonly CultureInfo culture = CultureInfo.CurrentCulture;
        private readonly string baseName;
        private readonly string documentFolder;
        private readonly string? setName;
        private readonly DrillBoard? page;
        private readonly Func<Window, StoneTable, StoneTable?>? openCatalog;
        private readonly DispatcherTimer timer;
        private readonly Dictionary<string, double> localDrills = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private bool loading;

        private readonly ComboBox profileCombo;
        private readonly Button removeProfileButton;
        private readonly Grid sizesGrid;
        private readonly List<SizeRow> sizeRows = new List<SizeRow>();
        private readonly TextBlock catalogHint;
        private readonly CheckBox millBox;
        private readonly TextBox millFeedBox;
        private readonly TextBox depthBox;
        private readonly TextBox retractBox;
        private readonly TextBox clearanceBox;
        private readonly TextBox feedBox;
        private readonly TextBox peckBox;
        private readonly TextBox rpmBox;
        private readonly ComboBox originCombo;
        private readonly TextBox boardWidthBox;
        private readonly TextBox boardHeightBox;
        private readonly Grid boardRow;
        private readonly TextBlock boardHint;
        private readonly TextBox repeatXBox;
        private readonly TextBox repeatYBox;
        private readonly TextBox gapBox;
        private readonly CheckBox marksBox;
        private readonly TextBox marksOffsetBox;
        private readonly ComboBox pathCombo;
        private readonly TextBox bridgeBox;
        private readonly ComboBox formatCombo;
        private readonly ComboBox fileModeCombo;
        private readonly CheckBox colorBox;
        private readonly CheckBox cycleBox;
        private readonly CheckBox numbersBox;
        private readonly CheckBox commentsBox;
        private readonly CheckBox percentBox;
        private readonly Image picture;
        private readonly TextBlock summary;
        private readonly StackPanel jobsPanel;
        private readonly TextBlock warnings;
        private readonly Button saveButton;
        private readonly Button drawButton;

        /// <param name="config">Профили станков (окно правит копию; итог — <see cref="Config"/>).</param>
        /// <param name="catalog">Каталог страз и свёрл (копия; итог — <see cref="Catalog"/>, если <see cref="CatalogChanged"/>).</param>
        /// <param name="setName">Набор таблицы камней, из которого берутся свёрла размеров.</param>
        /// <param name="page">Страница CorelDRAW (мм, координаты документа) — доска, если ноль станка на доске.</param>
        /// <param name="openCatalog">Открыть окно каталога поверх этого; возвращает сохранённый каталог или null.</param>
        public DrillWindow(
            CorelApplication? app, IReadOnlyList<DrillHole> holes, DrillConfig config, StoneTable catalog, string? setName,
            bool fromSelection, string baseName, string documentFolder, DrillBoard? page, Func<Window, StoneTable, StoneTable?>? openCatalog)
        {
            this.holes = holes;
            this.page = page;
            this.baseName = baseName;
            this.documentFolder = documentFolder;
            this.setName = setName;
            this.openCatalog = openCatalog;
            loc = PluginContext.Instance.Localizer;
            Config = config.Clone();
            Settings = Config.Profile();
            Catalog = StoneTableRules.Clone(catalog);

            Title = loc["drill.title"];
            Width = Math.Min(1060, Math.Max(760, SystemParameters.WorkArea.Width - 80));
            Height = Math.Max(480, SystemParameters.WorkArea.Height - 60);
            MinWidth = 720;
            MinHeight = 460;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/Strassio.Corel;component/Themes/Styles.xaml", UriKind.Relative),
            });
            SetResourceReference(BackgroundProperty, "Strassio.Background");
            SetResourceReference(ForegroundProperty, "Strassio.Foreground");
            try
            {
                if (app != null)
                {
                    new WindowInteropHelper(this).Owner = new IntPtr(app.AppWindow.Handle);
                    WindowStartupLocation = WindowStartupLocation.CenterOwner;
                }
            }
            catch (Exception)
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            var left = new StackPanel { Margin = new Thickness(16, 12, 12, 12) };

            // Откуда отверстия.
            left.Children.Add(Muted(loc.Format(fromSelection ? "drill.source.selection" : "drill.source.page", holes.Count.ToString("N0", culture)), 10));

            // Профиль станка: у каждого станка свои глубина, подачи, ноль и формат.
            left.Children.Add(Section("drill.section.profile"));
            profileCombo = new ComboBox { DisplayMemberPath = nameof(DrillSettings.Name), ToolTip = loc["drill.profile.tooltip"] };
            var newProfile = SmallButton("drill.profile.new", "drill.profile.new.tooltip");
            var renameProfile = SmallButton("drill.profile.rename", "drill.profile.rename.tooltip");
            removeProfileButton = SmallButton("drill.profile.remove", "drill.profile.remove.tooltip");
            newProfile.Click += (s, e) => AddProfile();
            renameProfile.Click += (s, e) => RenameProfile();
            removeProfileButton.Click += (s, e) => RemoveProfile();
            var profileRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var profileButtons = new StackPanel { Orientation = Orientation.Horizontal };
            profileButtons.Children.Add(newProfile);
            profileButtons.Children.Add(renameProfile);
            profileButtons.Children.Add(removeProfileButton);
            DockPanel.SetDock(profileButtons, Dock.Right);
            profileRow.Children.Add(profileButtons);
            profileRow.Children.Add(profileCombo);
            left.Children.Add(profileRow);

            // Свёрла для размеров — из каталога страз и свёрл.
            var drillsHeader = new DockPanel();
            var catalogButton = SmallButton("drill.catalog", "drill.catalog.tooltip");
            catalogButton.IsEnabled = openCatalog != null;
            catalogButton.Click += (s, e) => OpenCatalog();
            DockPanel.SetDock(catalogButton, Dock.Right);
            drillsHeader.Children.Add(catalogButton);
            drillsHeader.Children.Add(Section("drill.section.drills"));
            left.Children.Add(drillsHeader);
            sizesGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            left.Children.Add(sizesGrid);
            catalogHint = Muted(string.Empty, 8);
            left.Children.Add(catalogHint);
            millBox = Check("drill.mill", "drill.mill.tooltip");
            left.Children.Add(millBox);
            millFeedBox = NumberBox("drill.millFeed.tooltip");
            left.Children.Add(Row(loc["drill.millFeed"], millFeedBox));

            // Станок.
            left.Children.Add(Section("drill.section.machine"));
            depthBox = NumberBox("drill.depth.tooltip");
            retractBox = NumberBox("drill.retract.tooltip");
            clearanceBox = NumberBox("drill.clearance.tooltip");
            feedBox = NumberBox("drill.feed.tooltip");
            peckBox = NumberBox("drill.peck.tooltip");
            rpmBox = NumberBox("drill.rpm.tooltip");
            left.Children.Add(Row(loc["drill.depth"], depthBox));
            left.Children.Add(Row(loc["drill.retract"], retractBox));
            left.Children.Add(Row(loc["drill.clearance"], clearanceBox));
            left.Children.Add(Row(loc["drill.feed"], feedBox));
            left.Children.Add(Row(loc["drill.peck"], peckBox));
            left.Children.Add(Row(loc["drill.rpm"], rpmBox));

            // Ноль станка (home) — как при создании модели в ArtCAM: угол или центр доски; или место на рисунке.
            left.Children.Add(Section("drill.section.board"));
            originCombo = Combo(
                new[]
                {
                    ((DrillOrigin.Board, BoardCorner.LowerLeft), "drill.home.lowerLeft"),
                    ((DrillOrigin.Board, BoardCorner.UpperLeft), "drill.home.upperLeft"),
                    ((DrillOrigin.Board, BoardCorner.LowerRight), "drill.home.lowerRight"),
                    ((DrillOrigin.Board, BoardCorner.UpperRight), "drill.home.upperRight"),
                    ((DrillOrigin.Board, BoardCorner.Center), "drill.home.center"),
                    ((DrillOrigin.DesignLowerLeft, BoardCorner.LowerLeft), "drill.origin.lowerLeft"),
                    ((DrillOrigin.DesignCenter, BoardCorner.LowerLeft), "drill.origin.center"),
                    ((DrillOrigin.Document, BoardCorner.LowerLeft), "drill.origin.document"),
                },
                "drill.origin.tooltip");
            left.Children.Add(Row(loc["drill.origin"], originCombo));

            // Размер доски: пусто — как страница CorelDRAW.
            boardWidthBox = new TextBox { ToolTip = loc["drill.board.tooltip"] };
            boardHeightBox = new TextBox { ToolTip = loc["drill.board.tooltip"] };
            var asPage = SmallButton("drill.board.page", "drill.board.page.tooltip");
            asPage.Click += (s, e) =>
            {
                if (this.page.HasValue)
                {
                    boardWidthBox.Text = Mm(this.page.Value.WidthMm);
                    boardHeightBox.Text = Mm(this.page.Value.HeightMm);
                }
            };
            asPage.IsEnabled = page.HasValue;
            boardRow = Row(loc["drill.board"], Pair(boardWidthBox, boardHeightBox, asPage));
            left.Children.Add(boardRow);
            boardHint = Muted(page.HasValue
                ? loc.Format("drill.board.hint", Mm(page.Value.WidthMm), Mm(page.Value.HeightMm))
                : loc["drill.board.hintNoPage"], 8);
            left.Children.Add(boardHint);

            // Повтор рисунка на доске и метки совмещения.
            repeatXBox = new TextBox { ToolTip = loc["drill.repeat.tooltip"] };
            repeatYBox = new TextBox { ToolTip = loc["drill.repeat.tooltip"] };
            var fit = SmallButton("drill.repeat.fit", "drill.repeat.fit.tooltip");
            fit.Click += (s, e) => FitBoard();
            left.Children.Add(Row(loc["drill.repeat"], Pair(repeatXBox, repeatYBox, fit)));
            gapBox = NumberBox("drill.repeat.gap.tooltip");
            left.Children.Add(Row(loc["drill.repeat.gap"], gapBox));
            marksBox = Check("drill.marks", "drill.marks.tooltip");
            left.Children.Add(marksBox);
            marksOffsetBox = NumberBox("drill.marks.offset.tooltip");
            left.Children.Add(Row(loc["drill.marks.offset"], marksOffsetBox));

            // Порядок обхода и проверка перемычек.
            left.Children.Add(Section("drill.section.order"));
            pathCombo = Combo(
                new[]
                {
                    (DrillPathStyle.Fast, "drill.path.fast"),
                    (DrillPathStyle.Rows, "drill.path.rows"),
                    (DrillPathStyle.Snake, "drill.path.snake"),
                    (DrillPathStyle.Columns, "drill.path.columns"),
                    (DrillPathStyle.Groups, "drill.path.groups"),
                    (DrillPathStyle.Spread, "drill.path.spread"),
                },
                "drill.path.tooltip");
            left.Children.Add(Row(loc["drill.path"], pathCombo));
            bridgeBox = NumberBox("drill.bridge.tooltip");
            left.Children.Add(Row(loc["drill.bridge"], bridgeBox));

            // Файлы.
            left.Children.Add(Section("drill.section.files"));
            formatCombo = Combo(GCodeFormats.All.Select(f => (f, "drill.format." + f)), "drill.format.tooltip");
            left.Children.Add(Row(loc["drill.format"], formatCombo));
            fileModeCombo = Combo(
                new[] { (DrillFileMode.FilePerDrill, "drill.files.perDrill"), (DrillFileMode.OneFileToolChange, "drill.files.toolChange") },
                "drill.files.tooltip");
            left.Children.Add(Row(loc["drill.files"], fileModeCombo));
            colorBox = Check("drill.byColor", "drill.byColor.tooltip");
            cycleBox = Check("drill.cycle", "drill.cycle.tooltip");
            numbersBox = Check("drill.lineNumbers", "drill.lineNumbers.tooltip");
            commentsBox = Check("drill.comments", "drill.comments.tooltip");
            percentBox = Check("drill.percent", "drill.percent.tooltip");
            foreach (CheckBox box in new[] { colorBox, cycleBox, numbersBox, commentsBox, percentBox })
            {
                left.Children.Add(box);
            }

            // Справа: картинка, итог, кнопки.
            var right = new DockPanel { Margin = new Thickness(12, 12, 16, 12) };
            saveButton = new Button { Content = loc["drill.save"], IsDefault = true, MinWidth = 150, ToolTip = loc["drill.save.tooltip"] };
            saveButton.SetResourceReference(StyleProperty, "Strassio.PrimaryButton");
            saveButton.Click += (s, e) => Save();
            drawButton = new Button { Content = loc["drill.draw"], MinWidth = 120, Margin = new Thickness(6, 0, 0, 0), ToolTip = loc["drill.draw.tooltip"] };
            drawButton.Click += (s, e) =>
            {
                Result = DrillWindowResult.DrawPath;
                DialogResult = true;
            };
            var close = new Button { Content = loc["drill.close"], IsCancel = true, MinWidth = 80, Margin = new Thickness(6, 0, 0, 0) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            buttons.Children.Add(saveButton);
            buttons.Children.Add(drawButton);
            buttons.Children.Add(close);
            DockPanel.SetDock(buttons, Dock.Bottom);
            right.Children.Add(buttons);

            var pictureTitle = Section("drill.section.picture");
            DockPanel.SetDock(pictureTitle, Dock.Top);
            right.Children.Add(pictureTitle);
            picture = new Image { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both };
            var frame = new Border { Child = picture, Height = 300, Padding = new Thickness(6), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6) };
            frame.SetResourceReference(Border.BackgroundProperty, "Strassio.InputBackground");
            frame.SetResourceReference(Border.BorderBrushProperty, "Strassio.ControlBorder");
            DockPanel.SetDock(frame, Dock.Top);
            right.Children.Add(frame);
            var legend = Muted(loc["drill.picture.legend"], 10);
            legend.Margin = new Thickness(0, 4, 0, 10);
            DockPanel.SetDock(legend, Dock.Top);
            right.Children.Add(legend);

            var result = new StackPanel();
            result.Children.Add(Section("drill.section.result"));
            summary = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
            jobsPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
            warnings = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
            warnings.SetResourceReference(TextBlock.ForegroundProperty, "Strassio.Error");
            result.Children.Add(summary);
            result.Children.Add(jobsPanel);
            result.Children.Add(warnings);
            right.Children.Add(new ScrollViewer { Content = result, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 380 });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 320 });
            var leftScroll = new ScrollViewer { Content = left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            root.Children.Add(leftScroll);
            Grid.SetColumn(right, 1);
            root.Children.Add(right);
            Content = root;

            // Пересчёт — через долю секунды после последнего изменения, чтобы не считать на каждую букву.
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                Recalculate();
            };
            foreach (TextBox box in new[] { millFeedBox, depthBox, retractBox, clearanceBox, feedBox, peckBox, rpmBox, boardWidthBox, boardHeightBox, repeatXBox, repeatYBox, gapBox, marksOffsetBox, bridgeBox })
            {
                box.TextChanged += (s, e) => Schedule();
            }

            foreach (ComboBox combo in new[] { originCombo, fileModeCombo, pathCombo })
            {
                combo.SelectionChanged += (s, e) => Schedule();
            }

            foreach (CheckBox box in new[] { millBox, marksBox, colorBox, cycleBox, numbersBox, commentsBox, percentBox })
            {
                box.Checked += (s, e) => Schedule();
                box.Unchecked += (s, e) => Schedule();
            }

            formatCombo.SelectionChanged += (s, e) => ApplyFormat();
            profileCombo.SelectionChanged += (s, e) => SwitchProfile();

            RebuildProfiles();
            LoadFields(Settings);
            RebuildSizes();

            ThemeManager.Attach(this, app);
            Closed += (s, e) =>
            {
                timer.Stop();
                ThemeManager.Detach(this);
            };
            Loaded += (s, e) => Recalculate();
        }

        /// <summary>Профиль, с которым работали (последние правильные значения из окна).</summary>
        public DrillSettings Settings { get; private set; }

        /// <summary>Все профили станков — сохраняются, даже если окно просто закрыли.</summary>
        public DrillConfig Config { get; }

        /// <summary>Каталог страз и свёрл с выбранными в окне свёрлами размеров.</summary>
        public StoneTable Catalog { get; private set; }

        /// <summary>Каталог поменялся (свёрла размеров) — его нужно сохранить.</summary>
        public bool CatalogChanged { get; private set; }

        public DrillPlan? Plan { get; private set; }

        public DrillWindowResult Result { get; private set; }

        public IReadOnlyList<GCodeFile> SavedFiles { get; private set; } = Array.Empty<GCodeFile>();

        public string SavedFolder { get; private set; } = string.Empty;

        private StoneSet? ActiveSet => StoneTableRules.FindSet(Catalog, setName);

        private void Schedule()
        {
            if (loading)
            {
                return;
            }

            timer.Stop();
            timer.Start();
        }

        // ---- Профили ------------------------------------------------------------------------------

        private void RebuildProfiles()
        {
            loading = true;
            try
            {
                profileCombo.ItemsSource = null;
                profileCombo.ItemsSource = Config.Profiles;
                profileCombo.SelectedItem = Settings;
                removeProfileButton.IsEnabled = Config.Profiles.Count > 1;
            }
            finally
            {
                loading = false;
            }
        }

        /// <summary>Поля — в текущий профиль (если читаются), потом показать выбранный.</summary>
        private void SwitchProfile()
        {
            if (loading || !(profileCombo.SelectedItem is DrillSettings chosen) || ReferenceEquals(chosen, Settings))
            {
                return;
            }

            KeepFields();
            Settings = chosen;
            Config.Active = chosen.Name;
            LoadFields(chosen);
            Recalculate();
        }

        private void AddProfile()
        {
            KeepFields();
            string? name = NameDialog.Ask(null, this, loc["drill.profile.new"], loc["drill.profile.name"], StoneTableRules.UniqueName(Config.Profiles.Select(p => p.Name), loc["drill.profile.newName"]));
            if (name == null)
            {
                return;
            }

            Settings = Config.Add(Settings, name);
            RebuildProfiles();
            LoadFields(Settings);
            Recalculate();
        }

        private void RenameProfile()
        {
            string? name = NameDialog.Ask(null, this, loc["drill.profile.rename"], loc["drill.profile.name"], Settings.Name);
            if (name != null && !Config.Rename(Settings, name))
            {
                MessageBox.Show(this, loc["drill.profile.nameTaken"], Title, MessageBoxButton.OK, MessageBoxImage.Information);
            }

            RebuildProfiles();
        }

        private void RemoveProfile()
        {
            if (Config.Profiles.Count <= 1 || MessageBox.Show(
                    this, loc.Format("drill.profile.removeAsk", Settings.Name), Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            Config.Remove(Settings);
            Settings = Config.Profile();
            RebuildProfiles();
            LoadFields(Settings);
            Recalculate();
        }

        /// <summary>Значения полей — в текущий профиль, если все читаются (иначе профиль остаётся прежним).</summary>
        private void KeepFields()
        {
            DrillSettings? s = ReadSettings(out _);
            if (s != null && s.Problems().Count == 0)
            {
                Copy(s, Settings);
            }
        }

        private static void Copy(DrillSettings from, DrillSettings to)
        {
            string name = to.Name;
            foreach (System.Reflection.PropertyInfo p in typeof(DrillSettings).GetProperties().Where(p => p.CanRead && p.CanWrite))
            {
                p.SetValue(to, p.GetValue(from));
            }

            to.Name = name;
            to.DrillsMm = new List<double>(from.DrillsMm ?? new List<double>());
            to.Rules = new List<DrillRule>(from.Rules ?? new List<DrillRule>());
        }

        /// <summary>Поля окна — из профиля.</summary>
        private void LoadFields(DrillSettings s)
        {
            loading = true;
            try
            {
                millBox.IsChecked = s.MillSmaller;
                millFeedBox.Text = Mm(s.MillFeed);
                depthBox.Text = Mm(s.DepthMm);
                retractBox.Text = Mm(s.RetractMm);
                clearanceBox.Text = Mm(s.ClearanceMm);
                feedBox.Text = Mm(s.PlungeFeed);
                peckBox.Text = Mm(s.PeckMm);
                rpmBox.Text = s.SpindleRpm.ToString(culture);
                Select(originCombo, (s.Origin, s.Origin == DrillOrigin.Board ? s.Home : BoardCorner.LowerLeft));
                boardWidthBox.Text = s.BoardWidthMm > 0 ? Mm(s.BoardWidthMm) : string.Empty;
                boardHeightBox.Text = s.BoardHeightMm > 0 ? Mm(s.BoardHeightMm) : string.Empty;
                repeatXBox.Text = s.RepeatX.ToString(culture);
                repeatYBox.Text = s.RepeatY.ToString(culture);
                gapBox.Text = Mm(s.RepeatGapMm);
                marksBox.IsChecked = s.Marks;
                marksOffsetBox.Text = Mm(s.MarksOffsetMm);
                Select(pathCombo, s.PathStyle);
                bridgeBox.Text = Mm(s.MinBridgeMm);
                LoadFileFields(s);
            }
            finally
            {
                loading = false;
            }
        }

        private void LoadFileFields(DrillSettings s)
        {
            Select(formatCombo, GCodeFormats.Detect(s));
            Select(fileModeCombo, s.FileMode);
            colorBox.IsChecked = s.SplitByColor;
            cycleBox.IsChecked = s.CannedCycle;
            numbersBox.IsChecked = s.LineNumbers;
            commentsBox.IsChecked = s.Comments;
            percentBox.IsChecked = s.Percent;
        }

        /// <summary>Готовый формат файла — ставит галочки «как надо этому станку».</summary>
        private void ApplyFormat()
        {
            if (loading || !(formatCombo.SelectedItem is Option o) || (string)o.Value == GCodeFormats.Custom)
            {
                return;
            }

            DrillSettings s = ReadSettings(out _) ?? Settings.Clone();
            GCodeFormats.Apply(s, (string)o.Value);
            loading = true;
            try
            {
                LoadFileFields(s);
            }
            finally
            {
                loading = false;
            }

            Settings.Extension = s.Extension;
            Schedule();
        }

        // ---- Свёрла размеров -------------------------------------------------------------------

        /// <summary>Строка на каждый размер страз в рисунке: отверстие, сколько штук, сверло из каталога.</summary>
        private void RebuildSizes()
        {
            sizesGrid.Children.Clear();
            sizesGrid.RowDefinitions.Clear();
            sizesGrid.ColumnDefinitions.Clear();
            sizeRows.Clear();
            sizesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sizesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            int row = 0;
            void AddCell(UIElement element, int column, int span = 1)
            {
                Grid.SetRow(element, row);
                Grid.SetColumn(element, column);
                Grid.SetColumnSpan(element, span);
                sizesGrid.Children.Add(element);
            }

            StoneSet? set = ActiveSet;
            List<DrillTool> drills = Catalog.Drills.Where(d => d.DiameterMm > 0).ToList();
            foreach (var size in holes
                .GroupBy(h => h.Size, StringComparer.OrdinalIgnoreCase)
                .Select(g => (Name: g.Key, Hole: g.Average(h => h.DiameterMm), Count: g.Count()))
                .OrderBy(g => g.Hole))
            {
                StoneSize? tableSize = size.Name.Length == 0 ? null : set?.Sizes.FirstOrDefault(x => string.Equals((x.Name ?? string.Empty).Trim(), size.Name.Trim(), StringComparison.OrdinalIgnoreCase));
                double current = tableSize != null && tableSize.DrillMm > 0 ? tableSize.DrillMm : localDrills.TryGetValue(size.Name, out double local) ? local : 0;
                var options = new List<Option> { new Option(0.0, loc["drill.sizes.autoOption"]) };
                options.AddRange(drills.Select(d => new Option(d.DiameterMm, d.Name.Trim().Length > 0
                    ? loc.Format("catalog.drillOption.named", Mm(d.DiameterMm), d.Name.Trim())
                    : loc.Format("catalog.drillOption", Mm(d.DiameterMm)))));
                if (current > 0 && !drills.Any(d => Math.Abs(d.DiameterMm - current) <= DrillRack.SameDrillMm))
                {
                    options.Add(new Option(current, loc.Format("catalog.sizeDrill.missing", Mm(current))));
                }

                var combo = new ComboBox
                {
                    ItemsSource = options,
                    DisplayMemberPath = nameof(Option.Text),
                    IsEnabled = size.Name.Length > 0,
                    Margin = new Thickness(0, 2, 0, 2),
                    ToolTip = loc["drill.sizes.tooltip"],
                };
                combo.SelectedItem = options.FirstOrDefault(o => Math.Abs((double)o.Value - current) <= DrillRack.SameDrillMm) ?? options[0];

                sizesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                string name = size.Name.Length > 0 ? size.Name : loc["drill.sizes.noName"];
                AddCell(new TextBlock
                {
                    Text = loc.Format("drill.sizes.row", name, Mm(size.Hole), size.Count.ToString("N0", culture)),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 8, 2),
                }, 0);
                AddCell(combo, 1);
                row++;
                sizesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                TextBlock note = Muted(string.Empty, 4);
                AddCell(note, 0, 2);
                row++;

                var sizeRow = new SizeRow(size.Name, size.Hole, tableSize, combo, note);
                sizeRows.Add(sizeRow);
                combo.SelectionChanged += (s, e) =>
                {
                    double chosen = combo.SelectedItem is Option o ? (double)o.Value : 0;
                    if (sizeRow.TableSize != null)
                    {
                        sizeRow.TableSize.DrillMm = chosen;
                        CatalogChanged = true;
                    }
                    else if (chosen > 0)
                    {
                        localDrills[sizeRow.Name] = chosen;
                    }
                    else
                    {
                        localDrills.Remove(sizeRow.Name);
                    }

                    Schedule();
                };
            }

            catalogHint.Text = drills.Count == 0 ? loc["drill.catalog.empty"] : loc["drill.catalog.hint"];
        }

        /// <summary>Свёрла для расчёта: каталог, свёрла размеров набора и выбранные здесь для размеров не из таблицы.</summary>
        private DrillRack Rack()
        {
            var sizes = new List<KeyValuePair<string, double>>();
            StoneSet? set = ActiveSet;
            if (set != null)
            {
                sizes.AddRange(set.Sizes.Where(x => x.DrillMm > 0).Select(x => new KeyValuePair<string, double>(x.Name, x.DrillMm)));
            }

            sizes.AddRange(localDrills);
            return new DrillRack(Catalog.Drills, sizes);
        }

        private void OpenCatalog()
        {
            if (openCatalog == null)
            {
                return;
            }

            KeepFields();
            StoneTable? saved = openCatalog(this, Catalog);
            if (saved == null)
            {
                return;
            }

            // Каталог сохранён в окне каталога — дальше работаем с ним.
            Catalog = StoneTableRules.Clone(saved);
            CatalogChanged = false;
            RebuildSizes();
            Recalculate();
        }

        // ---- Чтение полей и расчёт ---------------------------------------------------------------

        /// <summary>Настройки из полей; null и текст ошибки — какое-то поле не читается.</summary>
        private DrillSettings? ReadSettings(out string error)
        {
            error = string.Empty;
            DrillSettings s = Settings.Clone();
            foreach ((TextBox box, string label, Action<double> set) in new (TextBox, string, Action<double>)[]
            {
                (millFeedBox, "drill.millFeed", v => s.MillFeed = v),
                (depthBox, "drill.depth", v => s.DepthMm = v),
                (retractBox, "drill.retract", v => s.RetractMm = v),
                (clearanceBox, "drill.clearance", v => s.ClearanceMm = v),
                (feedBox, "drill.feed", v => s.PlungeFeed = v),
                (peckBox, "drill.peck", v => s.PeckMm = v),
                (rpmBox, "drill.rpm", v => s.SpindleRpm = (int)Math.Round(v)),
                (repeatXBox, "drill.repeat", v => s.RepeatX = (int)Math.Round(v)),
                (repeatYBox, "drill.repeat", v => s.RepeatY = (int)Math.Round(v)),
                (gapBox, "drill.repeat.gap", v => s.RepeatGapMm = v),
                (marksOffsetBox, "drill.marks.offset", v => s.MarksOffsetMm = v),
                (bridgeBox, "drill.bridge", v => s.MinBridgeMm = v),
            })
            {
                if (!DrillText.TryParseMm(box.Text, out double value))
                {
                    error = loc.Format("drill.error.field", loc[label]);
                    return null;
                }

                set(value);
            }

            var home = ((DrillOrigin Origin, BoardCorner Home))((Option)originCombo.SelectedItem).Value;
            s.Origin = home.Origin;
            if (home.Origin == DrillOrigin.Board)
            {
                s.Home = home.Home;
            }

            // Размер доски: пустое поле — 0, то есть как страница.
            foreach ((TextBox box, Action<double> set) in new (TextBox, Action<double>)[]
            {
                (boardWidthBox, v => s.BoardWidthMm = v),
                (boardHeightBox, v => s.BoardHeightMm = v),
            })
            {
                string text = box.Text.Trim();
                double value = 0;
                if (text.Length > 0 && (!DrillText.TryParseMm(text, out value) || value < 0))
                {
                    error = loc["drill.error.board"];
                    return null;
                }

                set(value);
            }

            s.MillSmaller = millBox.IsChecked == true;
            s.Marks = marksBox.IsChecked == true;
            s.PathStyle = (DrillPathStyle)((Option)pathCombo.SelectedItem).Value;
            s.FileMode = (DrillFileMode)((Option)fileModeCombo.SelectedItem).Value;
            s.SplitByColor = colorBox.IsChecked == true;
            s.CannedCycle = cycleBox.IsChecked == true;
            s.LineNumbers = numbersBox.IsChecked == true;
            s.Comments = commentsBox.IsChecked == true;
            s.Percent = percentBox.IsChecked == true;
            s.Format = GCodeFormats.Detect(s);
            return s;
        }

        private void Recalculate()
        {
            bool board = originCombo.SelectedItem is Option o && (((DrillOrigin Origin, BoardCorner Home))o.Value).Origin == DrillOrigin.Board;
            boardRow.IsEnabled = board;
            boardHint.Visibility = board ? Visibility.Visible : Visibility.Collapsed;
            marksOffsetBox.IsEnabled = marksBox.IsChecked == true;
            millFeedBox.IsEnabled = millBox.IsChecked == true;
            DrillSettings? s = ReadSettings(out string error);
            IReadOnlyList<string> problems = s?.Problems() ?? Array.Empty<string>();
            if (s == null || problems.Count > 0)
            {
                Plan = null;
                summary.Text = string.Empty;
                jobsPanel.Children.Clear();
                picture.Source = null;
                warnings.Text = s == null ? error : string.Join("\n", problems.Select(p => loc[p]));
                saveButton.IsEnabled = drawButton.IsEnabled = false;
                return;
            }

            Copy(s, Settings);
            loading = true;
            try
            {
                Select(formatCombo, s.Format);
            }
            finally
            {
                loading = false;
            }

            DrillRack rack = Rack();
            foreach (SizeRow size in sizeRows)
            {
                double drill = DrillPlanner.ChooseDrill(size.Name, size.Hole, s, rack);
                bool own = rack.DrillForSize(size.Name).HasValue;
                string text = loc.Format(own ? "drill.sizes.own" : "drill.sizes.auto", Mm(drill));
                if (DrillPlanner.MillRadius(size.Hole, drill, s) > 0)
                {
                    text += " " + loc.Format("drill.sizes.mill", Mm(size.Hole));
                }

                size.Note.Text = text;
            }

            DrillPlan plan = DrillPlanner.Plan(holes, s, rack, page: page);
            Plan = plan;
            int files = s.FileMode == DrillFileMode.FilePerDrill ? plan.Jobs.Count : Math.Max(1, plan.Colors.Count);
            summary.Text = loc.Format(
                "drill.summary",
                plan.Holes.ToString("N0", culture), Mm(plan.WidthMm), Mm(plan.HeightMm), plan.Jobs.Count, files, Minutes(plan.Seconds));
            if (plan.CopiesX * plan.CopiesY > 1)
            {
                summary.Text += "\n" + loc.Format("drill.summary.copies", plan.CopiesX, plan.CopiesY);
            }

            if (plan.MarkHoles > 0)
            {
                summary.Text += "\n" + loc.Format("drill.summary.marks", plan.MarkHoles.ToString("N0", culture), Mm(s.MarksOffsetMm));
            }

            if (plan.Board.HasValue)
            {
                summary.Text += "\n" + loc.Format("drill.summary.board", Mm(plan.Board.Value.WidthMm), Mm(plan.Board.Value.HeightMm), loc[OriginKey(s)]);
            }

            jobsPanel.Children.Clear();
            int k = 0;
            foreach (DrillJob job in plan.Jobs)
            {
                string sizes = string.Join(", ", job.Sizes.Select(x => x.Length > 0 ? x : loc["drill.sizes.noName"]));
                string text = loc.Format("drill.job", job.Tool, Mm(job.DrillMm), sizes, job.Points.Count.ToString("N0", culture), Minutes(job.Seconds));
                if (job.Name.Trim().Length > 0)
                {
                    text = text.Replace(Mm(job.DrillMm) + " ", Mm(job.DrillMm) + " (" + job.Name.Trim() + ") ");
                }

                if (job.Milled > 0)
                {
                    text += " · " + loc.Format("drill.job.milled", job.Milled.ToString("N0", culture));
                }

                if (job.Color.Length > 0)
                {
                    text = loc.Format("drill.job.color", job.Color) + " " + text;
                }

                var line = new TextBlock { Text = "● " + text, TextWrapping = TextWrapping.Wrap };
                line.Inlines.Clear();
                line.Inlines.Add(new System.Windows.Documents.Run("● ") { Foreground = new SolidColorBrush(JobColors[k++ % JobColors.Length]) });
                line.Inlines.Add(new System.Windows.Documents.Run(text));
                jobsPanel.Children.Add(line);
            }

            var notes = new List<string>();
            foreach (DrillMismatch m in plan.Mismatches)
            {
                notes.Add(loc.Format("drill.warn.mismatch", m.Size.Length > 0 ? m.Size : loc["drill.sizes.noName"], Mm(m.HoleMm), Mm(m.DrillMm), m.Count.ToString("N0", culture)));
            }

            if (plan.Duplicates > 0)
            {
                notes.Add(loc.Format("drill.warn.duplicates", plan.Duplicates.ToString("N0", culture)));
            }

            if (Catalog.Drills.Count == 0)
            {
                notes.Add(loc["drill.warn.noDrills"]);
            }

            if (plan.Milled > 0)
            {
                notes.Add(loc.Format("drill.warn.milled", plan.Milled.ToString("N0", culture)));
            }

            if (plan.Bridges.Count > 0)
            {
                notes.Add(loc.Format("drill.warn.bridges", plan.Bridges.Count.ToString("N0", culture), Mm(Math.Round(plan.Bridges.ThinnestMm, 2)), Mm(s.MinBridgeMm)));
            }

            // Отверстия за доской — сверло ушло бы за край или в прижимы: сохранить нельзя, путь показать можно.
            bool outside = plan.OutsideBoard > 0;
            if (outside)
            {
                notes.Insert(0, loc.Format("drill.warn.outside", plan.OutsideBoard.ToString("N0", culture), Mm(Math.Round(plan.OverhangMm, 1))));
            }

            warnings.Text = string.Join("\n", notes);
            saveButton.IsEnabled = !plan.IsEmpty && !outside;
            drawButton.IsEnabled = !plan.IsEmpty;
            DrawPicture(plan);
        }

        /// <summary>«Заполнить доску»: сколько копий рисунка (с метками) влезает вправо и вниз.</summary>
        private void FitBoard()
        {
            DrillSettings? s = ReadSettings(out _);
            if (s == null)
            {
                return;
            }

            (int x, int y) = DrillPlanner.FitCopies(holes, s, Rack(), page);
            repeatXBox.Text = x.ToString(culture);
            repeatYBox.Text = y.ToString(culture);
        }

        // ---- Картинка пути -------------------------------------------------------------------------

        /// <summary>
        /// Доска, путь каждого сверла своим цветом, отверстия (как они будут вырезаны), метки кольцами,
        /// тонкие перемычки красным, крестик — ноль станка. Координаты станка, ось Y вверх.
        /// </summary>
        private void DrawPicture(DrillPlan plan)
        {
            if (plan.IsEmpty)
            {
                picture.Source = null;
                return;
            }

            var points = plan.Jobs.SelectMany(j => j.Points).ToList();
            double minX = Math.Min(0, Math.Min(plan.Min.X, points.Min(p => p.X)));
            double minY = Math.Min(0, Math.Min(plan.Min.Y, points.Min(p => p.Y)));
            double maxX = Math.Max(0, Math.Max(plan.Max.X, points.Max(p => p.X)));
            double maxY = Math.Max(0, Math.Max(plan.Max.Y, points.Max(p => p.Y)));
            if (plan.Board.HasValue)
            {
                minX = Math.Min(minX, plan.Board.Value.LowerLeft.X);
                minY = Math.Min(minY, plan.Board.Value.LowerLeft.Y);
                maxX = Math.Max(maxX, plan.Board.Value.UpperRight.X);
                maxY = Math.Max(maxY, plan.Board.Value.UpperRight.Y);
            }

            double extent = Math.Max(1, Math.Max(maxX - minX, maxY - minY));
            double px = extent / 600;
            Brush foreground = TryFindResource("Strassio.Foreground") as Brush ?? Brushes.Black;
            Brush muted = TryFindResource("Strassio.MutedForeground") as Brush ?? Brushes.Gray;
            Brush error = TryFindResource("Strassio.Error") as Brush ?? Brushes.Red;
            Brush boardFill = TryFindResource("Strassio.AccentSoft") as Brush ?? Brushes.AliceBlue;

            var group = new DrawingGroup();
            using (DrawingContext dc = group.Open())
            {
                // Невидимая рамка с полями — чтобы картинка не прилипала к краям.
                double pad = extent * 0.03;
                dc.DrawRectangle(Brushes.Transparent, null, new Rect(minX - pad, minY - pad, maxX - minX + 2 * pad, maxY - minY + 2 * pad));
                if (plan.Board.HasValue)
                {
                    DrillBoard b = plan.Board.Value;
                    dc.DrawRectangle(boardFill, new Pen(muted, 1.5 * px), new Rect(b.LowerLeft.X, b.LowerLeft.Y, b.WidthMm, b.HeightMm));
                }

                int k = 0;
                foreach (DrillJob job in plan.Jobs)
                {
                    var color = JobColors[k++ % JobColors.Length];
                    var line = new SolidColorBrush(Color.FromArgb(150, color.R, color.G, color.B));
                    var dot = new SolidColorBrush(color);
                    var path = new StreamGeometry();
                    using (StreamGeometryContext g = path.Open())
                    {
                        g.BeginFigure(new Point(0, 0), false, false);
                        g.PolyLineTo(job.Points.Select(p => new Point(p.X, p.Y)).ToList(), true, true);
                    }

                    path.Freeze();
                    dc.DrawGeometry(null, new Pen(line, 0.8 * px), path);

                    var dots = new StreamGeometry();
                    using (StreamGeometryContext g = dots.Open())
                    {
                        for (int i = 0; i < job.Points.Count; i++)
                        {
                            double r = Math.Max(px, (job.DrillMm + 2 * job.MillRadii[i]) / 2);
                            Circle(g, job.Points[i], r);
                        }
                    }

                    dots.Freeze();
                    dc.DrawGeometry(dot, null, dots);

                    if (job.Marks.Count > 0)
                    {
                        var rings = new StreamGeometry();
                        using (StreamGeometryContext g = rings.Open())
                        {
                            foreach (Point2D m in job.Marks)
                            {
                                Circle(g, m, Math.Max(4 * px, job.DrillMm * 1.6));
                            }
                        }

                        rings.Freeze();
                        dc.DrawGeometry(null, new Pen(foreground, 1.2 * px), rings);
                    }
                }

                if (plan.Bridges.Spots.Count > 0)
                {
                    var spots = new StreamGeometry();
                    using (StreamGeometryContext g = spots.Open())
                    {
                        foreach (Point2D p in plan.Bridges.Spots)
                        {
                            Circle(g, p, 3 * px);
                        }
                    }

                    spots.Freeze();
                    dc.DrawGeometry(error, null, spots);
                }

                // Крестик в нуле станка.
                double arm = 10 * px;
                var zero = new Pen(foreground, 1.5 * px);
                dc.DrawLine(zero, new Point(-arm, 0), new Point(arm, 0));
                dc.DrawLine(zero, new Point(0, -arm), new Point(0, arm));
            }

            // Ось Y станка — вверх, у экрана — вниз.
            group.Transform = new ScaleTransform(1, -1);
            group.Freeze();
            var image = new DrawingImage(group);
            image.Freeze();
            picture.Source = image;
        }

        private static void Circle(StreamGeometryContext g, Point2D c, double r)
        {
            g.BeginFigure(new Point(c.X + r, c.Y), true, true);
            g.ArcTo(new Point(c.X - r, c.Y), new Size(r, r), 0, false, SweepDirection.Clockwise, false, false);
            g.ArcTo(new Point(c.X + r, c.Y), new Size(r, r), 0, false, SweepDirection.Clockwise, false, false);
        }

        /// <summary>Ключ текста «где ноль станка» для настроек.</summary>
        public static string OriginKey(DrillSettings s)
        {
            switch (s.Origin)
            {
                case DrillOrigin.Board:
                    return s.Home switch
                    {
                        BoardCorner.UpperLeft => "drill.home.upperLeft",
                        BoardCorner.LowerRight => "drill.home.lowerRight",
                        BoardCorner.UpperRight => "drill.home.upperRight",
                        BoardCorner.Center => "drill.home.center",
                        _ => "drill.home.lowerLeft",
                    };
                case DrillOrigin.DesignCenter:
                    return "drill.origin.center";
                case DrillOrigin.Document:
                    return "drill.origin.document";
                default:
                    return "drill.origin.lowerLeft";
            }
        }

        // ---- Сохранение ------------------------------------------------------------------------------

        /// <summary>
        /// Проверка перемычек, выбор файла, самопроверка каждого файла, запись. Имя из окна — основа для
        /// имён по свёрлам.
        /// </summary>
        private void Save()
        {
            timer.Stop();
            Recalculate();
            DrillPlan? plan = Plan;
            if (plan == null || plan.IsEmpty || plan.OutsideBoard > 0)
            {
                return;
            }

            DrillSettings s = Settings;
            if (plan.Bridges.Count > 0 && MessageBox.Show(
                    this,
                    loc.Format("drill.bridges.ask", plan.Bridges.Count.ToString("N0", culture), Mm(Math.Round(plan.Bridges.ThinnestMm, 2)), Mm(s.MinBridgeMm)),
                    Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            string ext = string.IsNullOrWhiteSpace(s.Extension) ? ".tap" : s.Extension;
            string folder = Directory.Exists(s.LastFolder) ? s.LastFolder : Directory.Exists(documentFolder) ? documentFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = loc["drill.save.dialog"],
                Filter = loc["drill.save.filter"] + "|*.tap;*.nc;*.ngc;*.gcode;*.cnc|" + loc["drill.save.all"] + "|*.*",
                FileName = Ascii.FileName(baseName).Length > 0 ? Ascii.FileName(baseName) + ext : "strassio" + ext,
                InitialDirectory = folder,
                DefaultExt = ext,
                AddExtension = true,
                OverwritePrompt = false,
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            string chosenFolder = Path.GetDirectoryName(dialog.FileName) ?? folder;
            string chosenExt = Path.GetExtension(dialog.FileName);
            if (!string.IsNullOrEmpty(chosenExt))
            {
                s.Extension = chosenExt.ToLowerInvariant();
            }

            IReadOnlyList<GCodeFile> files = GCodeWriter.Files(plan, s, Path.GetFileNameWithoutExtension(dialog.FileName));

            // Самопроверка: файл «проигрывается» до записи. Если что-то опасно — не пишем ничего.
            foreach (GCodeFile file in files)
            {
                GCodeRun run = GCodeCheck.Run(file.Text, file.MillLimitMm);
                if (run.Problems.Count > 0 || run.Holes.Count != file.Holes)
                {
                    MessageBox.Show(this, loc.Format("drill.error.check", file.Name, string.Join("\n", run.Problems.Take(5))), Title, MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            string[] existing = files.Select(f => Path.Combine(chosenFolder, f.Name)).Where(File.Exists).ToArray();
            if (existing.Length > 0 && MessageBox.Show(
                    this, loc.Format("drill.overwrite", string.Join("\n", existing.Select(Path.GetFileName).Take(8))), Title,
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                foreach (GCodeFile file in files)
                {
                    File.WriteAllText(Path.Combine(chosenFolder, file.Name), file.Text, Encoding.ASCII);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, loc.Format("drill.error.write", ex.Message), Title, MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            s.LastFolder = chosenFolder;
            SavedFiles = files;
            SavedFolder = chosenFolder;
            Result = DrillWindowResult.Saved;
            DialogResult = true;
        }

        // ---- Мелочи ------------------------------------------------------------------------------------

        private string Mm(double value) => DrillText.Format(value, culture);

        private string Minutes(double seconds) => seconds < 90
            ? loc.Format("drill.seconds", Math.Max(1, (int)Math.Round(seconds)))
            : loc.Format("drill.minutes", ((int)Math.Round(seconds / 60)).ToString("N0", culture));

        private TextBox NumberBox(string tooltipKey) => new TextBox { ToolTip = loc[tooltipKey] };

        private CheckBox Check(string key, string tooltipKey) =>
            new CheckBox { Content = loc[key], ToolTip = loc[tooltipKey], Margin = new Thickness(0, 0, 0, 6) };

        private Button SmallButton(string key, string tooltipKey) =>
            new Button { Content = loc[key], ToolTip = loc[tooltipKey], Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };

        private ComboBox Combo<T>(IEnumerable<(T Value, string Key)> options, string tooltipKey)
        {
            List<Option> items = options.Select(o => new Option(o.Value!, loc[o.Key])).ToList();
            var combo = new ComboBox { ItemsSource = items, DisplayMemberPath = nameof(Option.Text), ToolTip = loc[tooltipKey] };
            combo.SelectedItem = items[0];
            return combo;
        }

        private static void Select(ComboBox combo, object value)
        {
            if (combo.ItemsSource is IEnumerable<Option> items)
            {
                combo.SelectedItem = items.FirstOrDefault(i => i.Value.Equals(value)) ?? items.First();
            }
        }

        /// <summary>Два поля рядом и кнопка справа (доска, копии).</summary>
        private static Grid Pair(TextBox first, TextBox second, Button button)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            second.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(second, 1);
            Grid.SetColumn(button, 2);
            grid.Children.Add(first);
            grid.Children.Add(second);
            grid.Children.Add(button);
            return grid;
        }

        private TextBlock Section(string key)
        {
            var text = new TextBlock { Text = loc[key] };
            text.SetResourceReference(StyleProperty, "Strassio.Section");
            return text;
        }

        private static TextBlock Muted(string text, double bottom)
        {
            var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, bottom) };
            block.SetResourceReference(StyleProperty, "Strassio.Muted");
            return block;
        }

        /// <summary>Подпись слева, поле справа.</summary>
        private static Grid Row(string label, FrameworkElement control)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 8, 0) };
            grid.Children.Add(text);
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
            return grid;
        }

        private sealed class SizeRow
        {
            public SizeRow(string name, double hole, StoneSize? tableSize, ComboBox combo, TextBlock note)
            {
                Name = name;
                Hole = hole;
                TableSize = tableSize;
                Combo = combo;
                Note = note;
            }

            public string Name { get; }

            public double Hole { get; }

            /// <summary>Размер в таблице камней (туда пишется выбранное сверло) или null.</summary>
            public StoneSize? TableSize { get; }

            public ComboBox Combo { get; }

            public TextBlock Note { get; }
        }

        /// <summary>Строка выпадающего списка: значение и текст.</summary>
        public sealed class Option
        {
            public Option(object value, string text)
            {
                Value = value;
                Text = text;
            }

            public object Value { get; }

            public string Text { get; }
        }
    }
}
