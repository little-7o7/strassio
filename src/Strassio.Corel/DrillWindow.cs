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
using System.Windows.Threading;
using Strassio.Core.Cnc;
using Strassio.Core.Localization;
using Strassio.Corel.Themes;
using CorelApplication = Corel.Interop.VGCore.Application;

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
    /// Окно «Сверление на станке» (docs/SPEC.md, раздел 11.1): свёрла пользователя, сверло для каждого
    /// размера камня, глубина и скорости, ноль станка; ниже — сразу видно, сколько свёрл и файлов
    /// получится и примерно сколько времени. «Сохранить» пишет файлы G-code (.tap), «Показать путь» —
    /// рисует путь сверла в CorelDRAW. Строится кодом, как NameDialog; все длины — в мм (станок в мм).
    /// </summary>
    internal sealed class DrillWindow : Window
    {
        private readonly IReadOnlyList<DrillHole> holes;
        private readonly Localizer loc;
        private readonly CultureInfo culture = CultureInfo.CurrentCulture;
        private readonly string baseName;
        private readonly string documentFolder;
        private readonly DispatcherTimer timer;

        private readonly TextBox drillsBox;
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
        private readonly DrillBoard? page;
        private readonly ComboBox fileModeCombo;
        private readonly CheckBox colorBox;
        private readonly CheckBox cycleBox;
        private readonly CheckBox numbersBox;
        private readonly CheckBox commentsBox;
        private readonly CheckBox percentBox;
        private readonly List<SizeRow> sizeRows = new List<SizeRow>();
        private readonly TextBlock summary;
        private readonly StackPanel jobsPanel;
        private readonly TextBlock warnings;
        private readonly Button saveButton;
        private readonly Button drawButton;

        /// <param name="page">Страница CorelDRAW (мм, координаты документа) — доска, если ноль станка на доске.</param>
        public DrillWindow(CorelApplication? app, IReadOnlyList<DrillHole> holes, DrillSettings settings, bool fromSelection, string baseName, string documentFolder, DrillBoard? page)
        {
            this.holes = holes;
            this.page = page;
            this.baseName = baseName;
            this.documentFolder = documentFolder;
            Settings = settings.Clone();
            loc = PluginContext.Instance.Localizer;

            Title = loc["drill.title"];
            Width = 580;
            SizeToContent = SizeToContent.Height;
            MaxHeight = Math.Max(400, SystemParameters.WorkArea.Height - 40);
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

            var root = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };

            // Откуда отверстия.
            root.Children.Add(Muted(loc.Format(fromSelection ? "drill.source.selection" : "drill.source.page", holes.Count.ToString("N0", culture)), 10));

            // Свёрла пользователя.
            root.Children.Add(Section("drill.section.drills"));
            drillsBox = new TextBox { Text = DrillText.FormatList(Settings.DrillsMm, culture), ToolTip = loc["drill.drills.tooltip"] };
            root.Children.Add(Row(loc["drill.drills"], drillsBox));
            root.Children.Add(Muted(loc["drill.drills.hint"], 8));

            // Сверло для каждого размера.
            var sizesGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            sizesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sizesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            sizesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            int row = 0;
            void AddCell(UIElement element, int column)
            {
                Grid.SetRow(element, row);
                Grid.SetColumn(element, column);
                sizesGrid.Children.Add(element);
            }

            sizesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddCell(Muted(loc["drill.sizes.size"], 4), 0);
            AddCell(Muted(loc["drill.sizes.drill"], 4), 1);
            row++;
            foreach (var size in holes
                .GroupBy(h => h.Size, StringComparer.OrdinalIgnoreCase)
                .Select(g => (Name: g.Key, Hole: g.Average(h => h.DiameterMm), Count: g.Count()))
                .OrderBy(g => g.Hole))
            {
                sizesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                string name = size.Name.Length > 0 ? size.Name : loc["drill.sizes.noName"];
                var label = new TextBlock
                {
                    Text = loc.Format("drill.sizes.row", name, Mm(size.Hole), size.Count.ToString("N0", culture)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 2, 8, 2),
                };
                double? rule = size.Name.Length > 0 ? Settings.RuleFor(size.Name) : null;
                var box = new TextBox
                {
                    Text = rule.HasValue ? DrillText.Format(rule.Value, culture) : string.Empty,
                    IsEnabled = size.Name.Length > 0,
                    Margin = new Thickness(0, 2, 8, 2),
                    ToolTip = loc["drill.sizes.tooltip"],
                };
                var auto = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 0, 2) };
                auto.SetResourceReference(TextBlock.ForegroundProperty, "Strassio.MutedForeground");
                AddCell(label, 0);
                AddCell(box, 1);
                AddCell(auto, 2);
                sizeRows.Add(new SizeRow(size.Name, size.Hole, box, auto));
                row++;
            }

            root.Children.Add(sizesGrid);

            // Станок.
            root.Children.Add(Section("drill.section.machine"));
            depthBox = NumberBox(Settings.DepthMm, "drill.depth.tooltip");
            retractBox = NumberBox(Settings.RetractMm, "drill.retract.tooltip");
            clearanceBox = NumberBox(Settings.ClearanceMm, "drill.clearance.tooltip");
            feedBox = NumberBox(Settings.PlungeFeed, "drill.feed.tooltip");
            peckBox = NumberBox(Settings.PeckMm, "drill.peck.tooltip");
            rpmBox = NumberBox(Settings.SpindleRpm, "drill.rpm.tooltip");
            root.Children.Add(Row(loc["drill.depth"], depthBox));
            root.Children.Add(Row(loc["drill.retract"], retractBox));
            root.Children.Add(Row(loc["drill.clearance"], clearanceBox));
            root.Children.Add(Row(loc["drill.feed"], feedBox));
            root.Children.Add(Row(loc["drill.peck"], peckBox));
            root.Children.Add(Row(loc["drill.rpm"], rpmBox));

            // Ноль станка (home) — как при создании модели в ArtCAM: угол или центр доски; или место на рисунке.
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
                (Settings.Origin, Settings.Origin == DrillOrigin.Board ? Settings.Home : BoardCorner.LowerLeft),
                "drill.origin.tooltip");
            root.Children.Add(Row(loc["drill.origin"], originCombo));

            // Размер доски: пусто — как страница CorelDRAW.
            boardWidthBox = new TextBox { Text = Settings.BoardWidthMm > 0 ? DrillText.Format(Settings.BoardWidthMm, culture) : string.Empty, ToolTip = loc["drill.board.tooltip"] };
            boardHeightBox = new TextBox { Text = Settings.BoardHeightMm > 0 ? DrillText.Format(Settings.BoardHeightMm, culture) : string.Empty, ToolTip = loc["drill.board.tooltip"], Margin = new Thickness(6, 0, 0, 0) };
            var asPage = new Button { Content = loc["drill.board.page"], ToolTip = loc["drill.board.page.tooltip"], Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
            asPage.Click += (s, e) =>
            {
                if (this.page.HasValue)
                {
                    boardWidthBox.Text = DrillText.Format(this.page.Value.WidthMm, culture);
                    boardHeightBox.Text = DrillText.Format(this.page.Value.HeightMm, culture);
                }
            };
            asPage.IsEnabled = page.HasValue;
            var sizeGrid = new Grid();
            sizeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sizeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sizeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(boardHeightBox, 1);
            Grid.SetColumn(asPage, 2);
            sizeGrid.Children.Add(boardWidthBox);
            sizeGrid.Children.Add(boardHeightBox);
            sizeGrid.Children.Add(asPage);
            boardRow = Row(loc["drill.board"], sizeGrid);
            root.Children.Add(boardRow);
            boardHint = Muted(page.HasValue
                ? loc.Format("drill.board.hint", Mm(page.Value.WidthMm), Mm(page.Value.HeightMm))
                : loc["drill.board.hintNoPage"], 8);
            root.Children.Add(boardHint);

            // Файлы.
            root.Children.Add(Section("drill.section.files"));
            fileModeCombo = Combo(
                new[] { (DrillFileMode.FilePerDrill, "drill.files.perDrill"), (DrillFileMode.OneFileToolChange, "drill.files.toolChange") },
                Settings.FileMode, "drill.files.tooltip");
            root.Children.Add(Row(loc["drill.files"], fileModeCombo));
            colorBox = Check("drill.byColor", "drill.byColor.tooltip", Settings.SplitByColor);
            cycleBox = Check("drill.cycle", "drill.cycle.tooltip", Settings.CannedCycle);
            numbersBox = Check("drill.lineNumbers", "drill.lineNumbers.tooltip", Settings.LineNumbers);
            commentsBox = Check("drill.comments", "drill.comments.tooltip", Settings.Comments);
            percentBox = Check("drill.percent", "drill.percent.tooltip", Settings.Percent);
            foreach (CheckBox box in new[] { colorBox, cycleBox, numbersBox, commentsBox, percentBox })
            {
                root.Children.Add(box);
            }

            // Что получится.
            root.Children.Add(Section("drill.section.result"));
            summary = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
            jobsPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
            warnings = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
            warnings.SetResourceReference(TextBlock.ForegroundProperty, "Strassio.Error");
            root.Children.Add(summary);
            root.Children.Add(jobsPanel);
            root.Children.Add(warnings);

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
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(saveButton);
            buttons.Children.Add(drawButton);
            buttons.Children.Add(close);
            root.Children.Add(buttons);

            Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

            // Пересчёт — через долю секунды после последнего изменения, чтобы не считать на каждую букву.
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                Recalculate();
            };
            foreach (TextBox box in new[] { drillsBox, depthBox, retractBox, clearanceBox, feedBox, peckBox, rpmBox, boardWidthBox, boardHeightBox }.Concat(sizeRows.Select(r => r.Box)))
            {
                box.TextChanged += (s, e) => Schedule();
            }

            foreach (ComboBox combo in new[] { originCombo, fileModeCombo })
            {
                combo.SelectionChanged += (s, e) => Schedule();
            }

            foreach (CheckBox box in new[] { colorBox, cycleBox, numbersBox, commentsBox, percentBox })
            {
                box.Checked += (s, e) => Schedule();
                box.Unchecked += (s, e) => Schedule();
            }

            ThemeManager.Attach(this, app);
            Closed += (s, e) =>
            {
                timer.Stop();
                ThemeManager.Detach(this);
            };
            Loaded += (s, e) => Recalculate();
        }

        /// <summary>Последние правильные настройки из окна (сохраняются, даже если окно просто закрыли).</summary>
        public DrillSettings Settings { get; private set; }

        public DrillPlan? Plan { get; private set; }

        public DrillWindowResult Result { get; private set; }

        public IReadOnlyList<GCodeFile> SavedFiles { get; private set; } = Array.Empty<GCodeFile>();

        public string SavedFolder { get; private set; } = string.Empty;

        private void Schedule()
        {
            timer.Stop();
            timer.Start();
        }

        /// <summary>Настройки из полей; null и текст ошибки — какое-то поле не читается.</summary>
        private DrillSettings? ReadSettings(out string error)
        {
            error = string.Empty;
            DrillSettings s = Settings.Clone();
            List<double>? drills = DrillText.ParseList(drillsBox.Text);
            if (drills == null)
            {
                error = loc.Format("drill.error.field", loc["drill.drills"]);
                return null;
            }

            s.DrillsMm = drills;
            foreach ((TextBox box, string label, Action<double> set) in new (TextBox, string, Action<double>)[]
            {
                (depthBox, "drill.depth", v => s.DepthMm = v),
                (retractBox, "drill.retract", v => s.RetractMm = v),
                (clearanceBox, "drill.clearance", v => s.ClearanceMm = v),
                (feedBox, "drill.feed", v => s.PlungeFeed = v),
                (peckBox, "drill.peck", v => s.PeckMm = v),
                (rpmBox, "drill.rpm", v => s.SpindleRpm = (int)Math.Round(v)),
            })
            {
                if (!DrillText.TryParseMm(box.Text, out double value))
                {
                    error = loc.Format("drill.error.field", loc[label]);
                    return null;
                }

                set(value);
            }

            // Свои свёрла для размеров: пустое поле — правило убрать (берётся ближайшее из списка).
            foreach (SizeRow size in sizeRows.Where(r => r.Name.Length > 0))
            {
                s.Rules.RemoveAll(r => string.Equals(r.Size, size.Name, StringComparison.OrdinalIgnoreCase));
                string text = size.Box.Text.Trim();
                if (text.Length == 0)
                {
                    continue;
                }

                if (!DrillText.TryParseMm(text, out double drill) || drill <= 0)
                {
                    error = loc.Format("drill.error.size", size.Name);
                    return null;
                }

                s.Rules.Add(new DrillRule { Size = size.Name, DrillMm = drill });
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

            s.FileMode = (DrillFileMode)((Option)fileModeCombo.SelectedItem).Value;
            s.SplitByColor = colorBox.IsChecked == true;
            s.CannedCycle = cycleBox.IsChecked == true;
            s.LineNumbers = numbersBox.IsChecked == true;
            s.Comments = commentsBox.IsChecked == true;
            s.Percent = percentBox.IsChecked == true;
            return s;
        }

        private void Recalculate()
        {
            bool board = originCombo.SelectedItem is Option o && (((DrillOrigin Origin, BoardCorner Home))o.Value).Origin == DrillOrigin.Board;
            boardRow.IsEnabled = board;
            boardHint.Visibility = board ? Visibility.Visible : Visibility.Collapsed;
            DrillSettings? s = ReadSettings(out string error);
            IReadOnlyList<string> problems = s?.Problems() ?? Array.Empty<string>();
            if (s == null || problems.Count > 0)
            {
                Plan = null;
                summary.Text = string.Empty;
                jobsPanel.Children.Clear();
                warnings.Text = s == null ? error : string.Join("\n", problems.Select(p => loc[p]));
                saveButton.IsEnabled = drawButton.IsEnabled = false;
                return;
            }

            Settings = s;
            foreach (SizeRow size in sizeRows)
            {
                double drill = DrillPlanner.ChooseDrill(size.Name, size.Hole, s);
                bool own = size.Name.Length > 0 && s.RuleFor(size.Name).HasValue;
                size.Auto.Text = loc.Format(own ? "drill.sizes.own" : "drill.sizes.auto", Mm(drill));
            }

            DrillPlan plan = DrillPlanner.Plan(holes, s, page: page);
            Plan = plan;
            int files = s.FileMode == DrillFileMode.FilePerDrill ? plan.Jobs.Count : Math.Max(1, plan.Colors.Count);
            summary.Text = loc.Format(
                "drill.summary",
                plan.Holes.ToString("N0", culture), Mm(plan.WidthMm), Mm(plan.HeightMm), plan.Jobs.Count, files, Minutes(plan.Seconds));
            if (plan.Board.HasValue)
            {
                summary.Text += "\n" + loc.Format("drill.summary.board", Mm(plan.Board.Value.WidthMm), Mm(plan.Board.Value.HeightMm), loc[OriginKey(s)]);
            }

            jobsPanel.Children.Clear();
            foreach (DrillJob job in plan.Jobs)
            {
                string sizes = string.Join(", ", job.Sizes.Select(x => x.Length > 0 ? x : loc["drill.sizes.noName"]));
                string text = loc.Format("drill.job", job.Tool, Mm(job.DrillMm), sizes, job.Points.Count.ToString("N0", culture), Minutes(job.Seconds));
                if (job.Color.Length > 0)
                {
                    text = loc.Format("drill.job.color", job.Color) + " " + text;
                }

                jobsPanel.Children.Add(new TextBlock { Text = "• " + text, TextWrapping = TextWrapping.Wrap });
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

            if (s.DrillsMm.Count == 0)
            {
                notes.Add(loc["drill.warn.noDrills"]);
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

        /// <summary>Выбор файла, самопроверка каждого файла, запись. Имя из окна — основа для имён по свёрлам.</summary>
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
                GCodeRun run = GCodeCheck.Run(file.Text);
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
            Settings = s;
            SavedFiles = files;
            SavedFolder = chosenFolder;
            Result = DrillWindowResult.Saved;
            DialogResult = true;
        }

        private string Mm(double value) => DrillText.Format(value, culture);

        private string Minutes(double seconds) => seconds < 90
            ? loc.Format("drill.seconds", Math.Max(1, (int)Math.Round(seconds)))
            : loc.Format("drill.minutes", ((int)Math.Round(seconds / 60)).ToString("N0", culture));

        private TextBox NumberBox(double value, string tooltipKey) =>
            new TextBox { Text = DrillText.Format(value, culture), ToolTip = loc[tooltipKey] };

        private CheckBox Check(string key, string tooltipKey, bool value) =>
            new CheckBox { Content = loc[key], IsChecked = value, ToolTip = loc[tooltipKey], Margin = new Thickness(0, 0, 0, 6) };

        private ComboBox Combo<T>(IEnumerable<(T Value, string Key)> options, T selected, string tooltipKey)
            where T : struct
        {
            List<Option> items = options.Select(o => new Option(o.Value, loc[o.Key])).ToList();
            var combo = new ComboBox { ItemsSource = items, DisplayMemberPath = nameof(Option.Text), ToolTip = loc[tooltipKey] };
            combo.SelectedItem = items.FirstOrDefault(i => i.Value.Equals(selected)) ?? items[0];
            return combo;
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
            public SizeRow(string name, double hole, TextBox box, TextBlock auto)
            {
                Name = name;
                Hole = hole;
                Box = box;
                Auto = auto;
            }

            public string Name { get; }

            public double Hole { get; }

            public TextBox Box { get; }

            public TextBlock Auto { get; }
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
