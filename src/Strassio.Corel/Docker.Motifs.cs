#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Corel.Interop.VGCore;
using Strassio.Core.Editing;
using Strassio.Core.Geometry;
using Strassio.Core.Motifs;
using Strassio.Core.Placement;
using Strassio.Core.Settings;
using CoreCurve = Strassio.Core.Geometry.Curve;
using CorelApplication = Corel.Interop.VGCore.Application;
using OutlineStyle = Strassio.Core.Settings.OutlineStyle;

namespace Strassio.Corel
{
    /// <summary>
    /// Вкладка «Мотивы» (docs/SPEC.md, раздел 9; пункт 12 прокачки): выделенные стразы сохраняются как мотив
    /// (цветок, листик, завиток) и потом раскладываются по линии, по кругу (розетка), рамкой с отдельными
    /// углами или россыпью внутри фигуры. «Повторить по кругу» размножает выделенные стразы вокруг центра —
    /// мандала. Вокруг готового дизайна — россыпь камней или штрихи.
    /// Мотивы лежат файлами в папке настроек: %APPDATA%\Strassio\motifs.
    /// </summary>
    public partial class Docker
    {
        private const int DefaultAroundCount = 8;

        private List<Motif> motifs = new List<Motif>();

        private MotifLibrary MotifStore => new MotifLibrary(System.IO.Path.Combine(context.Store.Directory, "motifs"));

        private bool IsFrameMode => (MotifModeCombo.SelectedItem as ChoiceOption)?.Value == "frame";

        private bool IsFillMode => (MotifModeCombo.SelectedItem as ChoiceOption)?.Value == "fill";

        /// <summary>Списки вкладки на текущем языке; мотивы перечитываются из папки.</summary>
        private void RebuildMotifs()
        {
            var modes = new List<ChoiceOption>
            {
                new ChoiceOption("line", Loc["motif.mode.line"]),
                new ChoiceOption("frame", Loc["motif.mode.frame"]),
                new ChoiceOption("fill", Loc["motif.mode.fill"]),
            };
            string? mode = (MotifModeCombo.SelectedItem as ChoiceOption)?.Value;
            MotifModeCombo.ItemsSource = modes;
            MotifModeCombo.SelectedItem = modes.FirstOrDefault(m => m.Value == mode) ?? modes[0];

            try
            {
                motifs = MotifStore.LoadAll();
            }
            catch (Exception)
            {
                motifs = new List<Motif>();
            }

            string? first = (MotifCombo.SelectedItem as ChoiceOption)?.Value;
            string? second = (MotifSecondCombo.SelectedItem as ChoiceOption)?.Value;
            List<ChoiceOption> names = motifs.Select(m => new ChoiceOption(m.Name, m.Name + " · " + m.Stones.Count)).ToList();
            MotifCombo.ItemsSource = names;
            MotifCombo.SelectedItem = names.FirstOrDefault(n => n.Value == first) ?? names.FirstOrDefault();

            var seconds = new List<ChoiceOption> { new ChoiceOption(string.Empty, Loc["motif.none"]) };
            seconds.AddRange(names);
            MotifSecondCombo.ItemsSource = seconds;
            MotifSecondCombo.SelectedItem = seconds.FirstOrDefault(n => n.Value == second) ?? seconds[0];

            if (string.IsNullOrWhiteSpace(MotifCountBox.Text))
            {
                MotifCountBox.Text = "0";
            }

            if (string.IsNullOrWhiteSpace(MotifAroundBox.Text))
            {
                MotifAroundBox.Text = DefaultAroundCount.ToString(CultureInfo.CurrentCulture);
            }

            RefreshMotifSpacingBox();
            RefreshSplashWidthBox();
            UpdateMotifModeHint();
            RebuildRepeat();
        }

        private void MotifModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateMotifModeHint();

        private void UpdateMotifModeHint()
        {
            bool frame = IsFrameMode;
            MotifModeHint.Text = Loc[frame ? "motif.mode.frame.hint" : IsFillMode ? "motif.mode.fill.hint" : "motif.mode.line.hint"];
            MotifSecondCaption.Text = Loc[frame ? "motif.second.corner" : "motif.second.alternate"];
        }

        private double motifSpacingMm = 1.0;

        private double MotifSpacingMm
        {
            get
            {
                if (LengthUnits.TryParse(MotifSpacingBox.Text, context.Settings.Units, out double mm) && mm >= 0 && mm <= 100)
                {
                    motifSpacingMm = mm;
                }

                return motifSpacingMm;
            }
        }

        private void MotifSpacingBox_TextChanged(object sender, TextChangedEventArgs e) => _ = MotifSpacingMm;

        private void RefreshMotifSpacingBox() =>
            MotifSpacingBox.Text = LengthUnits.Format(motifSpacingMm, context.Settings.Units, Math.Max(2, context.Settings.Decimals), CultureInfo.CurrentCulture);

        private static int ReadCount(TextBox box, int min, int max, int fallback) =>
            int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int n) && n >= min && n <= max ? n : fallback;

        private Motif? SelectedMotif(ComboBox combo)
        {
            string? name = (combo.SelectedItem as ChoiceOption)?.Value;
            return string.IsNullOrEmpty(name) ? null : motifs.FirstOrDefault(m => m.Name == name);
        }

        // ---- Сохранить и удалить ------------------------------------------------------------------

        private void MotifSave_Click(object sender, RoutedEventArgs e)
        {
            var stones = new List<MotifStone>();
            string fallbackSize = (SizeCombo.SelectedItem as SizeOption)?.Size.Name ?? string.Empty;
            RunInDocument(null, doc =>
            {
                foreach (StoneShape s in StoneShapes.FromSelection(doc, KnownSizes).Stones)
                {
                    string rgb = TryUniformFill(s.Shape, out byte r, out byte g, out byte b) ? Hex(r, g, b) : "#C0C0C0";
                    string size = s.Size.Length > 0 ? s.Size : SizeNameFor(s.Stone.DiameterMm, fallbackSize);
                    stones.Add(new MotifStone(s.Stone.Center.X, s.Stone.Center.Y, s.Stone.DiameterMm, size, s.Color, rgb));
                }
            });

            if (stones.Count == 0)
            {
                SetStatus("motif.selectStones");
                return;
            }

            string suggested = Loc.Format("motif.defaultName", motifs.Count + 1);
            string? name = NameDialog.Ask(app, null, Loc["motif.save"], Loc["motif.name"], suggested);
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            try
            {
                MotifStore.Save(Motif.FromStones(name!.Trim(), stones));
                RebuildMotifs();
                MotifCombo.SelectedItem = (MotifCombo.ItemsSource as IEnumerable<ChoiceOption>)?.FirstOrDefault(o => o.Value == name.Trim());
                SetStatus("motif.saved", name.Trim(), stones.Count);
            }
            catch (Exception ex)
            {
                SetStatus("status.error", ex.Message);
            }
        }

        private void MotifDelete_Click(object sender, RoutedEventArgs e)
        {
            Motif? motif = SelectedMotif(MotifCombo);
            if (motif == null)
            {
                return;
            }

            try
            {
                MotifStore.Delete(motif.Name);
                RebuildMotifs();
                SetStatus("motif.deleted", motif.Name);
            }
            catch (Exception ex)
            {
                SetStatus("status.error", ex.Message);
            }
        }

        private void MotifFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string folder = MotifStore.Folder;
                System.IO.Directory.CreateDirectory(folder);
                System.Diagnostics.Process.Start("explorer.exe", "\"" + folder + "\"");
            }
            catch (Exception ex)
            {
                SetStatus("status.error", ex.Message);
            }
        }

        // ---- Разложить ---------------------------------------------------------------------------

        private void MotifPlace_Click(object sender, RoutedEventArgs e)
        {
            Motif? motif = SelectedMotif(MotifCombo);
            if (motif == null)
            {
                SetStatus("motif.noMotif");
                return;
            }

            Motif? second = SelectedMotif(MotifSecondCombo);
            bool frame = IsFrameMode, fill = IsFillMode;
            int variant = Environment.TickCount & 0x7FFFFFFF;
            var options = new MotifLayoutOptions
            {
                Count = ReadCount(MotifCountBox, 0, 1000, 0),
                SpacingMm = MotifSpacingMm,
                Rotate = MotifRotateBox.IsChecked == true,
                Flip = MotifFlipBox.IsChecked == true,
                StoneGapMm = Params.GapMm,
            };

            string caption = Loc.Format("undo.motif", motif.Name);
            RunInDocument(null, doc =>
            {
                List<CoreCurve> paths = ToolCurves(StoneShapes.FromSelection(doc, KnownSizes).Tools);
                if (fill)
                {
                    paths = paths.Where(c => c.IsClosed).ToList();
                }

                if (paths.Count == 0)
                {
                    SetStatus(fill ? "motif.selectShape" : "motif.selectPath");
                    return;
                }

                var placed = new List<MotifPlacement>();
                int copies = 0, dropped = 0;
                if (fill)
                {
                    // Все замкнутые фигуры — одна область (дырки в ней пустые). Каждое нажатие — новая россыпь.
                    MotifLayout layout = MotifPlacer.Fill(paths, second == null ? new[] { motif } : new[] { motif, second }, options, variant);
                    placed.AddRange(layout.Stones);
                    copies = layout.Copies;
                    dropped = layout.Dropped;
                    paths.Clear();
                }

                foreach (CoreCurve path in paths)
                {
                    MotifLayout layout = frame
                        ? MotifPlacer.Frame(path, new[] { motif }, second, options)
                        : MotifPlacer.AlongLine(path, second == null ? new[] { motif } : new[] { motif, second }, options);
                    placed.AddRange(layout.Stones);
                    copies += layout.Copies;
                    dropped += layout.Dropped;
                }

                if (placed.Count == 0)
                {
                    SetStatus("status.noStones");
                    return;
                }

                doc.BeginCommandGroup(caption);
                try
                {
                    Shape group = CreateMotifStones(doc, placed, caption);
                    doc.ClearSelection();
                    group.CreateSelection();
                }
                finally
                {
                    doc.EndCommandGroup();
                }

                if (dropped > 0)
                {
                    SetStatus("motif.placedDropped", copies, placed.Count, dropped);
                }
                else
                {
                    SetStatus("motif.placed", copies, placed.Count);
                }
            });
        }

        /// <summary>
        /// Круги разложенного мотива: у каждой стразы свой размер и цвет из мотива. Заливка — одним
        /// вызовом на каждый цвет, обводка — одним на все (меньше обращений к CorelDRAW).
        /// </summary>
        private Shape CreateMotifStones(Document doc, IReadOnlyList<MotifPlacement> stones, string caption)
        {
            if (!context.CanCreate)
            {
                throw new InvalidOperationException(Loc["license.needed"]);
            }

            CorelApplication corel = app!;
            Layer layer = doc.ActiveLayer;
            var created = new object[stones.Count];
            var byColor = new Dictionary<string, List<object>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < stones.Count; i++)
            {
                MotifPlacement stone = stones[i];
                MotifStone source = stone.Source;
                double radius = source.DiameterMm / 2.0;
                Shape circle = layer.CreateEllipse2(stone.Center.X, stone.Center.Y, radius, radius);
                circle.Name = StoneNames.Compose(source.Size, source.Color);
                StoneShapes.Mark(circle, source.Size, source.Color);
                created[i] = circle;
                if (!byColor.TryGetValue(source.Rgb, out List<object>? list))
                {
                    list = new List<object>();
                    byColor[source.Rgb] = list;
                }

                list.Add(circle);
            }

            foreach (KeyValuePair<string, List<object>> pair in byColor)
            {
                var color = new Strassio.Core.Stones.StoneColor { Rgb = pair.Key };
                if (!color.TryGetRgb(out byte r, out byte g, out byte b))
                {
                    r = g = b = 192;
                }

                object[] shapes = pair.Value.ToArray();
                doc.CreateShapeRangeFromArray(ref shapes).ApplyUniformFill(corel.CreateRGBColor(r, g, b));
            }

            ShapeRange range = doc.CreateShapeRangeFromArray(ref created);
            PluginSettings settings = context.Settings;
            if (settings.Outline == OutlineStyle.None)
            {
                range.SetOutlineProperties(Width: 0);
            }
            else
            {
                double width = settings.Outline == OutlineStyle.Hairline ? HairlineMm : settings.OutlineWidthMm;
                range.SetOutlineProperties(Width: width, Color: corel.CreateRGBColor(0, 0, 0));
            }

            Shape group = range.Group();
            group.Name = caption;
            return group;
        }

        // ---- Повторить по кругу ------------------------------------------------------------------

        private void MotifAround_Click(object sender, RoutedEventArgs e)
        {
            int count = ReadCount(MotifAroundBox, 2, 360, DefaultAroundCount);
            double gap = Params.GapMm;
            RunInDocument("undo.motifAround", doc =>
            {
                StoneSelection selection = StoneShapes.FromSelection(doc, KnownSizes);
                List<StoneShape> stones = selection.Stones;
                if (stones.Count == 0)
                {
                    SetStatus("motif.around.selectStones");
                    return;
                }

                if (selection.Tools.Count == 0)
                {
                    SetStatus("motif.around.needCenter");
                    return;
                }

                // Центр — середина первой выделенной фигуры, которая не страза (круг, крестик, что угодно).
                selection.Tools[0].GetBoundingBox(out double x, out double y, out double w, out double h, false);
                var pivot = new Point2D(x + w / 2, y + h / 2);

                List<RotatedCopy> copies = MotifPlacer.Around(
                    stones.Select(s => s.Stone.Center).ToList(), stones.Select(s => s.Stone.DiameterMm).ToList(), pivot, count, gap);
                var all = new List<object>(stones.Select(s => (object)s.Shape));
                foreach (RotatedCopy copy in copies)
                {
                    Point2D from = stones[copy.Source].Stone.Center;
                    all.Add(stones[copy.Source].Shape.Duplicate(copy.Center.X - from.X, copy.Center.Y - from.Y));
                }

                object[] shapes = all.ToArray();
                doc.ClearSelection();
                doc.CreateShapeRangeFromArray(ref shapes).CreateSelection();
                int skipped = stones.Count * (count - 1) - copies.Count;
                SetStatus("motif.around.done", count, copies.Count, skipped);
            });
        }

        // ---- Россыпь вокруг (брызги) --------------------------------------------------------------

        private double splashWidthMm = 12;

        private double SplashWidthMm
        {
            get
            {
                if (LengthUnits.TryParse(SplashWidthBox.Text, context.Settings.Units, out double mm) && mm > 0 && mm <= 200)
                {
                    splashWidthMm = mm;
                }

                return splashWidthMm;
            }
        }

        private void SplashWidthBox_TextChanged(object sender, TextChangedEventArgs e) => _ = SplashWidthMm;

        private void RefreshSplashWidthBox() =>
            SplashWidthBox.Text = LengthUnits.Format(splashWidthMm, context.Settings.Units, Math.Max(1, context.Settings.Decimals), CultureInfo.CurrentCulture);

        /// <summary>
        /// Случайные камни вокруг выделенного дизайна (стразы и/или фигуры): у дизайна — основной камень и
        /// почти вплотную, дальше — мельче и реже. Каждое нажатие — новая россыпь (Ctrl+Z и ещё раз).
        /// </summary>
        private void Splash_Click(object sender, RoutedEventArgs e)
        {
            if (!(SizeCombo.SelectedItem is SizeOption size) || !(ColorList.SelectedItem is ColorOption color))
            {
                SetStatus("status.noStone");
                return;
            }

            double d = size.Size.DiameterMm;
            double smallest = ThinDiameter(d);
            List<double> sizes = SizeTable.Values.Where(v => v <= d + 1e-6 && v >= smallest - 1e-6)
                .Distinct().OrderByDescending(v => v).ToList();
            if (sizes.Count == 0)
            {
                sizes.Add(d);
            }

            // Не больше четырёх размеров: крупный, мелкий и равномерно между ними.
            if (sizes.Count > 4)
            {
                sizes = new[] { 0, sizes.Count / 3, 2 * sizes.Count / 3, sizes.Count - 1 }.Select(i => sizes[i]).Distinct().ToList();
            }

            double width = SplashWidthMm;
            double gap = Math.Max(Params.GapMm, 0.2 * d);
            double fadeGap = d + width / 4;
            int variant = Environment.TickCount & 0x7FFFFFFF;
            string caption = Loc.Format("undo.splash", size.Size.Name, color.Name);
            RunInDocument(null, doc =>
            {
                if (!ReadDesignAround(doc, width + d, "motif.splash.select", out List<PlacedStone> design, out List<CoreCurve> shapes, out List<PlacedStone> obstacles))
                {
                    return;
                }

                List<PlacedStone> stones = Scatter.Splash(shapes, design, obstacles, sizes, gap, width, fadeGap, variant);
                if (stones.Count == 0)
                {
                    SetStatus("motif.splash.none");
                    return;
                }

                CreateAroundGroup(doc, stones, size, color, caption);
                SetStatus("motif.splash.done", stones.Count);
            });
        }

        /// <summary>
        /// Штрихи вокруг выделенного дизайна: прерывистые полоски выбранным камнем — у дизайна длинные и
        /// частые, дальше короче и реже, на ширине россыпи сходят на нет. Длина штрихов, разрывы, угол и
        /// расстояние между полосками — как у способа «Штрихи» в настройках. Каждое нажатие — новый узор.
        /// </summary>
        private void StreaksAround_Click(object sender, RoutedEventArgs e)
        {
            if (!(SizeCombo.SelectedItem is SizeOption size) || !(ColorList.SelectedItem is ColorOption color))
            {
                SetStatus("status.noStone");
                return;
            }

            double d = size.Size.DiameterMm;
            double width = SplashWidthMm;
            Strassio.Core.Methods.MethodParameters p = Params;
            int variant = Environment.TickCount & 0x7FFFFFFF;
            string caption = Loc.Format("undo.streaksAround", size.Size.Name, color.Name);
            RunInDocument(null, doc =>
            {
                if (!ReadDesignAround(doc, width + d, "motif.streaks.select", out List<PlacedStone> design, out List<CoreCurve> shapes, out List<PlacedStone> obstacles))
                {
                    return;
                }

                List<PlacedStone> stones = PatternFills.StreaksAround(
                    shapes, design, obstacles, d, p.GapMm, p.LineGapMm, p.StreakAngleDeg, p.StreakLength, p.StreakBreak, width, variant);
                if (stones.Count == 0)
                {
                    SetStatus("motif.streaks.none");
                    return;
                }

                CreateAroundGroup(doc, stones, size, color, caption);
                SetStatus("motif.streaks.done", stones.Count);
            });
        }

        /// <summary>
        /// Выделенный дизайн для россыпи и штрихов: его стразы, его замкнутые фигуры и готовые стразы на
        /// странице рядом (на них новые камни не налезают). Ничего не выделено — сообщение и false.
        /// </summary>
        private bool ReadDesignAround(
            Document doc, double reach, string selectKey, out List<PlacedStone> design, out List<CoreCurve> shapes, out List<PlacedStone> obstacles)
        {
            StoneSelection selection = StoneShapes.FromSelection(doc, KnownSizes);
            design = selection.Stones.Select(s => new PlacedStone(s.Stone.Center, s.Stone.DiameterMm, false)).ToList();
            shapes = ToolCurves(selection.Tools).Where(c => c.IsClosed).ToList();
            obstacles = new List<PlacedStone>();
            if (design.Count == 0 && shapes.Count == 0)
            {
                SetStatus(selectKey);
                return false;
            }

            IEnumerable<Point2D> points = design.Select(s => s.Center)
                .Concat(shapes.SelectMany(c => CurveFlattener.Flatten(c).Points.Select(pt => pt.Position)));
            double minX = points.Min(pt => pt.X) - reach, maxX = points.Max(pt => pt.X) + reach;
            double minY = points.Min(pt => pt.Y) - reach, maxY = points.Max(pt => pt.Y) + reach;
            obstacles = StoneShapes.ObstaclesNear(doc.ActivePage, KnownSizes, minX, minY, maxX, maxY);
            return true;
        }

        /// <summary>Новые камни вокруг дизайна — одной группой выбранного размера и цвета, одним шагом отмены.</summary>
        private void CreateAroundGroup(Document doc, List<PlacedStone> stones, SizeOption size, ColorOption color, string caption)
        {
            color.Color.TryGetRgb(out byte red, out byte green, out byte blue);
            doc.BeginCommandGroup(caption);
            try
            {
                Shape group = CreateStoneGroup(doc, stones, size.Size, color.Name, red, green, blue, Array.Empty<int>(), caption);
                doc.ClearSelection();
                group.CreateSelection();
            }
            finally
            {
                doc.EndCommandGroup();
            }
        }
    }
}
