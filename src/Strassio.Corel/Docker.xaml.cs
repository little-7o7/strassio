#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Corel.Interop.VGCore;
using Strassio.Core.Geometry;
using Strassio.Core.Editing;
using Strassio.Core.Localization;
using Strassio.Core.Methods;
using Strassio.Core.Placement;
using Strassio.Core.Settings;
using Strassio.Core.Stones;
using Strassio.Corel.Themes;
using CorelApplication = Corel.Interop.VGCore.Application;
using CoreCurve = Strassio.Core.Geometry.Curve;
using OutlineStyle = Strassio.Core.Settings.OutlineStyle;

namespace Strassio.Corel
{
    /// <summary>
    /// Докер Strassio. CorelDRAW создаёт этот UserControl сам (см. AppUI.xslt, itemData type="wpfhost")
    /// и передаёт в конструктор свой объект Application — никакой COM-регистрации не нужно.
    /// Сверху вниз (docs/SPEC.md, раздел 2.2): шапка, выбор камня, вкладки методов, параметры
    /// метода (Docker.Params.cs), «Создать».
    /// </summary>
    public partial class Docker : UserControl
    {
        /// <summary>Толщина «волосяной» обводки CorelDRAW (0,003 дюйма), мм.</summary>
        private const double HairlineMm = 0.0762;

        /// <summary>Толщина красной обводки у налезающих страз при «только показать», мм.</summary>
        private const double ConflictOutlineMm = 0.3;

        private readonly CorelApplication? app;
        private readonly PluginContext context = PluginContext.Instance;
        private readonly MethodOption[] methods;
        private bool refreshing;

        // Последнее сообщение внизу докера — ключ и значения, чтобы перевести его при смене языка.
        private string statusKey = "status.ready";
        private object[] statusArgs = Array.Empty<object>();

        public Docker(object? app)
        {
            methods = MethodCatalog.All.Select(info => new MethodOption(info, BuildMethodIcon(info.Kind))).ToArray();

            refreshing = true;
            InitializeComponent();
            refreshing = false;
            this.app = app as CorelApplication;

            DataContext = context.Localizer;
            context.Localizer.PropertyChanged += Localizer_PropertyChanged;
            context.SettingsChanged += Context_SettingsChanged;
            Loaded += (s, e) => ThemeManager.Attach(this, this.app);
            Unloaded += (s, e) => ThemeManager.Detach(this);

            ApplyLayout();
            RebuildAll();
            SelectLastMethod();
        }

        // Нужен WPF-дизайнеру.
        public Docker()
            : this(null)
        {
        }

        private Localizer Loc => context.Localizer;

        private StoneSet? ActiveSet => StoneTableRules.FindSet(context.Stones, context.Settings.ActiveSet);

        /// <summary>Размеры таблицы «название → диаметр» — для методов с несколькими размерами (L2, L5, L6, L8).</summary>
        private Dictionary<string, double> SizeTable
        {
            get
            {
                var table = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (StoneSize s in ActiveSet?.Sizes ?? new List<StoneSize>())
                {
                    if (!string.IsNullOrWhiteSpace(s.Name) && !table.ContainsKey(s.Name.Trim()))
                    {
                        table[s.Name.Trim()] = s.DiameterMm;
                    }
                }

                return table;
            }
        }

        /// <summary>Название размера для камня этого диаметра (в методах с несколькими размерами); иначе — выбранный размер.</summary>
        private string SizeNameFor(double diameterMm, string fallback)
        {
            foreach (StoneSize s in ActiveSet?.Sizes ?? new List<StoneSize>())
            {
                if (Math.Abs(s.DiameterMm - diameterMm) < 0.005)
                {
                    return s.Name;
                }
            }

            return fallback;
        }

        private void Localizer_PropertyChanged(object? sender, PropertyChangedEventArgs e) => RebuildAll();

        private void Context_SettingsChanged(object? sender, EventArgs e)
        {
            ApplyLayout();
            RebuildAll();
            ThemeManager.Refresh();
        }

        /// <summary>Пересобирает то, что докер строит сам (списки размеров и методов), сохраняя выбор.</summary>
        private void RebuildAll()
        {
            refreshing = true;
            try
            {
                PluginSettings settings = context.Settings;
                string sizeName = (SizeCombo.SelectedItem as SizeOption)?.Size.Name ?? settings.DefaultSize;
                string unit = Loc[settings.Units == LengthUnit.Inch ? "unit.in" : "unit.mm"];

                List<SizeOption> sizes = (ActiveSet?.Sizes ?? new List<StoneSize>())
                    .Select(size => new SizeOption(size, Loc.Format(
                        "size.item",
                        size.Name,
                        LengthUnits.Format(size.DiameterMm, settings.Units, settings.Decimals, CultureInfo.CurrentCulture),
                        unit)))
                    .ToList();
                SizeCombo.ItemsSource = sizes;
                SizeCombo.SelectedItem = sizes.FirstOrDefault(o => o.Size.Name == sizeName) ?? sizes.FirstOrDefault();

                foreach (MethodOption method in methods)
                {
                    method.Text = Loc[method.Key];
                }

                RebuildMethods();
            }
            finally
            {
                refreshing = false;
            }

            RebuildColors();
            RebuildEditTexts();
            RefreshBridgeBox();
            RefreshVectorBoxes();
            RebuildSets();
            RebuildPresets();
            ShowTabPanels();
            LivePreviewBox.IsChecked = context.Settings.LivePreview;
            StatusText.Text = Loc.Format(statusKey, statusArgs);
        }

        private void SetStatus(string key, params object[] args)
        {
            statusKey = key;
            statusArgs = args;
            StatusText.Text = Loc.Format(key, args);
        }

        private void RebuildMethods()
        {
            string? selectedKey = (MethodCombo.SelectedItem as MethodOption)?.Key;
            // На вкладках «Правка» и «Цвет» список методов не виден — оставляем тот, что был.
            bool fill = IsMethodTab ? MethodTabs.SelectedItem == FillTab : methodListIsFill;
            methodListIsFill = fill;
            List<MethodOption> list = methods.Where(m => m.Info.IsFill == fill).ToList();
            MethodCombo.ItemsSource = null;
            MethodCombo.ItemsSource = list;
            MethodCombo.SelectedItem = list.FirstOrDefault(m => m.Key == selectedKey) ?? list.FirstOrDefault();
            UpdateMethodHint();
            RebuildParams();
        }

        /// <summary>При первом открытии докера — вкладка и метод, которыми пользовались в прошлый раз.</summary>
        private void SelectLastMethod()
        {
            MethodOption? last = methods.FirstOrDefault(m => m.Key == "method." + context.Settings.LastMethod);
            if (last == null)
            {
                return;
            }

            refreshing = true;
            try
            {
                MethodTabs.SelectedItem = last.Info.IsFill ? FillTab : LineTab;
                RebuildMethods();
                MethodCombo.SelectedItem = last;
                UpdateMethodHint();
                RebuildParams();
            }
            finally
            {
                refreshing = false;
            }
        }

        private void RebuildColors()
        {
            string colorName = (ColorList.SelectedItem as ColorOption)?.Name ?? context.Settings.DefaultColor;
            List<ColorOption> colors = ((SizeCombo.SelectedItem as SizeOption)?.Size.Colors ?? new List<StoneColor>())
                .Select(c => new ColorOption(c))
                .ToList();
            ColorList.ItemsSource = colors;
            ColorList.SelectedItem = colors.FirstOrDefault(c => c.Name == colorName) ?? colors.FirstOrDefault();
            ColorName.Text = (ColorList.SelectedItem as ColorOption)?.Name ?? string.Empty;
            RebuildMixColors();
        }

        private void UpdateMethodHint()
        {
            MethodHint.Text = MethodCombo.SelectedItem is MethodOption m ? Loc[m.Key + ".tooltip"] : string.Empty;
        }

        private void SizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!refreshing)
            {
                RebuildColors();
            }
        }

        private void ColorList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ColorName.Text = (ColorList.SelectedItem as ColorOption)?.Name ?? string.Empty;
            RebuildEditTexts();
            ScheduleLivePreview();
        }

        private void MethodTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Событие всплывает и от вложенных списков — реагируем только на смену самой вкладки.
            if (ReferenceEquals(e.OriginalSource, MethodTabs) && !refreshing)
            {
                ShowTabPanels();
                SetStatus(MethodTabs.SelectedItem == EditTab ? "edit.ready" : MethodTabs.SelectedItem == ColorTab ? "color.ready" :
                    MethodTabs.SelectedItem == VectorTab ? "vec.ready" : "status.ready");
                if (IsMethodTab)
                {
                    RebuildMethods();
                }
            }
        }

        private void MethodCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!refreshing)
            {
                if (LivePreviewBox.IsChecked != true)
                {
                    HidePreview();
                }

                UpdateMethodHint();
                RebuildParams();
                ScheduleLivePreview();
                if (MethodCombo.SelectedItem is MethodOption m)
                {
                    context.Settings.LastMethod = m.Info.Kind.ToString().ToLowerInvariant();
                    context.SaveSettingsQuietly();
                }
            }
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            new SettingsWindow(app).ShowDialog();
        }

        /// <summary>Редактор таблицы камней; по «ОК» списки размеров и цветов в докере обновятся сами.</summary>
        private void EditStones_Click(object sender, RoutedEventArgs e)
        {
            new StonesWindow(app, (SizeCombo.SelectedItem as SizeOption)?.Size.Name).ShowDialog();
        }

        private void Create_Click(object sender, RoutedEventArgs e)
        {
            if (!(SizeCombo.SelectedItem is SizeOption size) || !(ColorList.SelectedItem is ColorOption color))
            {
                SetStatus("status.noStone");
                return;
            }

            if (!(MethodCombo.SelectedItem is MethodOption method))
            {
                return;
            }

            if (!CommitAllParams())
            {
                return;
            }

            HidePreview();

            // Запоминаем последний выбранный камень — при следующем запуске он будет выбран сразу.
            context.Settings.DefaultSize = size.Size.Name;
            context.Settings.DefaultColor = color.Name;
            context.SaveSettingsQuietly();

            string caption = Loc.Format(method.Info.IsFill ? "undo.fill" : "undo.line", size.Size.Name, color.Name);
            CreateFromSelectedCurve(caption, size.Size, color.Color, method);
        }

        /// <summary>
        /// Читает кривую выделенной фигуры (узлы и контрольные точки Безье — один раз, дальше вся
        /// геометрия считается в Strassio.Core, см. CLAUDE.md), расставляет стразы выбранным методом
        /// и создаёт круги одной группой отмены.
        /// </summary>
        private void CreateFromSelectedCurve(string caption, StoneSize size, StoneColor color, MethodOption method)
        {
            if (!TryGetSelection(out CorelApplication corel, out Document doc, out Shape selected))
            {
                return;
            }

            CorelApplication app = corel;
            bool prevOptimization = app.Optimization;
            bool prevEventsEnabled = app.EventsEnabled;
            cdrUnit prevUnit = doc.Unit;

            try
            {
                app.Optimization = true;
                app.EventsEnabled = false;
                doc.Unit = cdrUnit.cdrMillimeter;

                List<CoreCurve>? contours = ReadContours(doc, selected, method, out List<CoreCurve>? guides);
                if (contours == null)
                {
                    return;
                }

                MethodResult result = MethodRunner.Run(method.Info.Kind, contours, size.DiameterMm, Params, SizeTable, guides);
                IReadOnlyList<PlacedStone> stones = result.Stones;
                if (stones.Count == 0)
                {
                    SetStatus("status.noStones");
                    return;
                }

                color.TryGetRgb(out byte red, out byte green, out byte blue);

                doc.BeginCommandGroup(caption);
                try
                {
                    Shape group = CreateStoneGroup(doc, stones, size, color.Name, red, green, blue, result.ConflictIndices, caption);

                    // «Живые» стразы (раздел 8): запоминаем на группе, из чего и чем она сделана.
                    LiveStones.Save(group, new LiveRecipe
                    {
                        Method = method.Info.Kind.ToString().ToLowerInvariant(),
                        Size = size.Name,
                        Color = color.Name,
                        Sources = new List<int>(lastSourceIds),
                        Parameters = Params.Clone(),
                    });
                }
                finally
                {
                    doc.EndCommandGroup();
                }

                RememberRecent(method, size.Name, color.Name);
                if (result.ConflictIndices.Count > 0)
                {
                    SetStatus("status.doneConflicts", stones.Count, result.ConflictIndices.Count);
                }
                else
                {
                    SetStatus("status.done", stones.Count);
                }
            }
            catch (Exception ex)
            {
                SetStatus("status.error", ex.Message);
            }
            finally
            {
                doc.Unit = prevUnit;
                app.EventsEnabled = prevEventsEnabled;
                app.Optimization = prevOptimization;
                app.Refresh();
            }
        }

        /// <summary>
        /// Создаёт круги-стразы одной группой (вызывать внутри группы отмены, единицы — мм). У каждого
        /// круга — имя «размер цвет» и невидимые метки; в методах с несколькими размерами имя размера
        /// своё у каждого камня. <paramref name="conflicts"/> — стразы «только показать»: красная обводка.
        /// </summary>
        private Shape CreateStoneGroup(
            Document doc, IReadOnlyList<PlacedStone> stones, StoneSize size, string colorName,
            byte red, byte green, byte blue, IReadOnlyList<int> conflicts, string caption)
        {
            CorelApplication corel = app!;
            PluginSettings settings = context.Settings;
            string stoneName = StoneNames.Compose(size.Name, colorName);
            Layer layer = doc.ActiveLayer;
            var created = new object[stones.Count];

            for (int i = 0; i < stones.Count; i++)
            {
                PlacedStone stone = stones[i];
                double radius = stone.DiameterMm / 2.0;
                Shape circle = layer.CreateEllipse2(stone.Center.X, stone.Center.Y, radius, radius);

                // В методах с несколькими размерами (L5, L6, L8, L2) у каждого камня своё имя размера.
                string sizeName = Math.Abs(stone.DiameterMm - size.DiameterMm) < 0.005 ? size.Name : SizeNameFor(stone.DiameterMm, size.Name);
                circle.Name = sizeName == size.Name ? stoneName : StoneNames.Compose(sizeName, colorName);
                StoneShapes.Mark(circle, sizeName, colorName);
                created[i] = circle;
            }

            // Заливка и обводка — одним вызовом на всю пачку, а не для каждого круга (меньше COM-вызовов).
            ShapeRange range = doc.CreateShapeRangeFromArray(ref created);
            range.ApplyUniformFill(corel.CreateRGBColor(red, green, blue));
            if (settings.Outline == OutlineStyle.None)
            {
                range.SetOutlineProperties(Width: 0);
            }
            else
            {
                double width = settings.Outline == OutlineStyle.Hairline ? HairlineMm : settings.OutlineWidthMm;
                range.SetOutlineProperties(Width: width, Color: corel.CreateRGBColor(0, 0, 0));
            }

            // «Только показать» (раздел 6.4): налезающие стразы остаются, но получают красную обводку.
            if (conflicts.Count > 0)
            {
                object[] marked = conflicts.Select(i => created[i]).ToArray();
                doc.CreateShapeRangeFromArray(ref marked)
                    .SetOutlineProperties(Width: ConflictOutlineMm, Color: corel.CreateRGBColor(229, 57, 53));
            }

            Shape group = range.Group();
            group.Name = caption;
            return group;
        }

        /// <summary>Есть CorelDRAW, открытый документ и выделенная фигура; иначе — сообщение внизу докера.</summary>
        private bool TryGetSelection(out CorelApplication corel, out Document doc, out Shape selected)
        {
            corel = null!;
            doc = null!;
            selected = null!;

            if (app == null)
            {
                SetStatus("status.noApp");
                return false;
            }

            corel = app;
            doc = app.ActiveDocument;
            if (doc == null)
            {
                SetStatus("status.noDocument");
                return false;
            }

            selected = doc.ActiveShape;
            if (selected == null)
            {
                SetStatus("status.noSelection");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Все контуры выделенной фигуры в мм (единицы документа — уже миллиметры, это делает
        /// вызывающий). null — прочитать не удалось или методу нужна замкнутая фигура; сообщение
        /// уже показано внизу докера.
        /// </summary>
        /// <summary>Номера (StaticID) фигур, прочитанных последним ReadContours: форма/линия, для F7/F8 — и вторая.</summary>
        private readonly List<int> lastSourceIds = new List<int>();

        private List<CoreCurve>? ReadContours(Document doc, Shape selected, MethodOption method, out List<CoreCurve>? guides)
        {
            guides = null;
            lastSourceIds.Clear();
            if (method.Info.NeedsGuide)
            {
                return ReadTwoShapes(doc, method, out guides);
            }

            if (method.Info.UsesWholeSelection)
            {
                return ReadWholeSelection(doc);
            }

            global::Corel.Interop.VGCore.Curve? corelCurve = GetCurveOf(selected);
            if (corelCurve == null || corelCurve.SubPaths.Count == 0)
            {
                SetStatus("status.cannotReadShape", selected.Type);
                return null;
            }

            List<CoreCurve> contours = ReadAllSubPaths(corelCurve);
            if (contours.Count == 0)
            {
                SetStatus("status.noContours");
                return null;
            }

            lastSourceIds.Add(selected.StaticID);
            if (method.Info.NeedsClosed && !MethodRunner.OuterContour(contours).IsClosed)
            {
                SetStatus("status.needClosed");
                return null;
            }

            return contours;
        }

        /// <summary>
        /// Все выделенные фигуры (обводка дизайна): контуры каждой, группы — насквозь. Сами стразы
        /// пропускаются — обводится рисунок, а не камни на нём.
        /// </summary>
        private List<CoreCurve>? ReadWholeSelection(Document doc)
        {
            var contours = new List<CoreCurve>();
            List<string> known = StoneNames.SizesOf(context.Stones).ToList();

            void Collect(Shape shape)
            {
                if (shape.Type == cdrShapeType.cdrGroupShape)
                {
                    Shapes children = shape.Shapes;
                    for (int i = 1; i <= children.Count; i++)
                    {
                        Collect(children[i]);
                    }

                    return;
                }

                if (StoneShapes.IsStone(shape, known))
                {
                    return;
                }

                global::Corel.Interop.VGCore.Curve? curve = GetCurveOf(shape);
                if (curve != null && curve.SubPaths.Count > 0)
                {
                    contours.AddRange(ReadAllSubPaths(curve).Where(c => c.IsClosed));
                    lastSourceIds.Add(shape.StaticID);
                }
            }

            ShapeRange selection = doc.SelectionRange;
            for (int i = 1; i <= selection.Count; i++)
            {
                Collect(selection[i]);
            }

            if (contours.Count == 0)
            {
                SetStatus("status.needClosed");
                return null;
            }

            return contours;
        }

        /// <summary>
        /// Две выделенные фигуры для F7 «переход между кривыми» (обе — линии перехода) и F8 «по
        /// направляющей» (замкнутая форма и линия-направляющая: форма — замкнутая и побольше).
        /// </summary>
        private List<CoreCurve>? ReadTwoShapes(Document doc, MethodOption method, out List<CoreCurve>? guides)
        {
            guides = null;
            string errorKey = method.Info.Kind == MethodKind.F7 ? "status.needTwoCurves" : "status.needShapeAndGuide";
            ShapeRange selection = doc.SelectionRange;
            if (selection.Count != 2)
            {
                SetStatus(errorKey);
                return null;
            }

            var read = new List<List<CoreCurve>>();
            for (int i = 1; i <= 2; i++)
            {
                global::Corel.Interop.VGCore.Curve? curve = GetCurveOf(selection[i]);
                List<CoreCurve> contours = curve == null || curve.SubPaths.Count == 0 ? new List<CoreCurve>() : ReadAllSubPaths(curve);
                if (contours.Count == 0)
                {
                    SetStatus(errorKey);
                    return null;
                }

                read.Add(contours);
            }

            if (method.Info.Kind == MethodKind.F7)
            {
                lastSourceIds.Add(selection[1].StaticID);
                lastSourceIds.Add(selection[2].StaticID);
                guides = new List<CoreCurve> { MethodRunner.OuterContour(read[1]) };
                return new List<CoreCurve> { MethodRunner.OuterContour(read[0]) };
            }

            // F8: форма — та, что замкнута (если замкнуты обе — та, что больше), направляющая — другая.
            double Area(List<CoreCurve> c)
            {
                CoreCurve outer = MethodRunner.OuterContour(c);
                if (!outer.IsClosed)
                {
                    return -1;
                }

                (double w, double h) = CurveMetrics.BoundingSize(CurveFlattener.Flatten(outer));
                return w * h;
            }

            int shapeIndex = Area(read[0]) >= Area(read[1]) ? 0 : 1;
            if (Area(read[shapeIndex]) < 0)
            {
                SetStatus(errorKey);
                return null;
            }

            lastSourceIds.Add(selection[shapeIndex + 1].StaticID);
            lastSourceIds.Add(selection[2 - shapeIndex].StaticID);
            guides = new List<CoreCurve> { MethodRunner.OuterContour(read[1 - shapeIndex]) };
            return read[shapeIndex];
        }

        /// <summary>
        /// Достаёт кривую любого объекта.
        ///
        /// Важно: свойство Shape.Curve есть ТОЛЬКО у объекта-кривой (cdrCurveShape). У прямоугольника,
        /// эллипса, многоугольника/звезды и текста его нет — из-за этого все методы, кроме «по линии»,
        /// не работали на обычных фигурах. У таких объектов есть Shape.DisplayCurve: та же форма,
        /// уже в виде кривой, причём сам документ при этом не меняется (в отличие от ConvertToCurves,
        /// который молча переделал бы фигуру пользователя).
        /// </summary>
        private static global::Corel.Interop.VGCore.Curve? GetCurveOf(Shape shape)
        {
            if (shape.Type == cdrShapeType.cdrCurveShape)
            {
                try
                {
                    global::Corel.Interop.VGCore.Curve curve = shape.Curve;
                    if (curve != null && curve.SubPaths.Count > 0)
                    {
                        return curve;
                    }
                }
                catch (Exception)
                {
                    // Ниже пробуем DisplayCurve — он работает у всех типов объектов.
                }
            }

            try
            {
                return shape.DisplayCurve;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Читает ВСЕ подпути фигуры. У буквы «О», кольца или любой фигуры с отверстием подпутей
        /// несколько: внешняя граница и дырки. Заливка (раздел 5 ТЗ) умеет работать со всеми сразу
        /// и оставляет отверстия пустыми.
        /// </summary>
        private static List<CoreCurve> ReadAllSubPaths(global::Corel.Interop.VGCore.Curve corelCurve)
        {
            var contours = new List<CoreCurve>();
            SubPaths subPaths = corelCurve.SubPaths;

            for (int i = 1; i <= subPaths.Count; i++)
            {
                CoreCurve? contour = TryReadSubPath(subPaths[i]);
                if (contour != null)
                {
                    contours.Add(contour);
                }
            }

            return contours;
        }

        /// <summary>Подпуть без сегментов (мусорная точка) кривой не является — такой просто пропускаем.</summary>
        private static CoreCurve? TryReadSubPath(SubPath subPath)
        {
            try
            {
                return ReadSubPath(subPath);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        /// <summary>
        /// Читает один подпуть CorelDRAW (узлы и контрольные точки Безье) в независимую от CorelDRAW
        /// кривую Strassio.Core — единственное место, где геометрия покидает мир COM.
        /// </summary>
        private static CoreCurve ReadSubPath(SubPath subPath)
        {
            Segments corelSegments = subPath.Segments;
            int count = corelSegments.Count;
            var segments = new List<CurveSegment>(count);

            for (int i = 1; i <= count; i++)
            {
                Segment seg = corelSegments[i];
                Node startNode = seg.StartNode;
                Node endNode = seg.EndNode;
                var start = new Point2D(startNode.PositionX, startNode.PositionY);
                var end = new Point2D(endNode.PositionX, endNode.PositionY);

                if (seg.Type == cdrSegmentType.cdrLineSegment)
                {
                    segments.Add(CurveSegment.Line(start, end));
                }
                else
                {
                    var control1 = new Point2D(seg.StartingControlPointX, seg.StartingControlPointY);
                    var control2 = new Point2D(seg.EndingControlPointX, seg.EndingControlPointY);
                    segments.Add(CurveSegment.Cubic(start, control1, control2, end));
                }
            }

            return new CoreCurve(segments, subPath.Closed);
        }
    }
}
