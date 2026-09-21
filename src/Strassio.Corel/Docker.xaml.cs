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
            methods = MethodCatalog.All.Select(info => new MethodOption(info)).ToArray();

            refreshing = true;
            InitializeComponent();
            refreshing = false;
            this.app = app as CorelApplication;

            DataContext = context.Localizer;
            context.Localizer.PropertyChanged += Localizer_PropertyChanged;
            context.SettingsChanged += Context_SettingsChanged;
            Loaded += (s, e) => ThemeManager.Attach(this, this.app);
            Unloaded += (s, e) => ThemeManager.Detach(this);

            RebuildAll();
            SelectLastMethod();
        }

        // Нужен WPF-дизайнеру.
        public Docker()
            : this(null)
        {
        }

        private Localizer Loc => context.Localizer;

        private StoneSet? ActiveSet => context.Stones.Sets.FirstOrDefault();

        private void Localizer_PropertyChanged(object? sender, PropertyChangedEventArgs e) => RebuildAll();

        private void Context_SettingsChanged(object? sender, EventArgs e)
        {
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
            bool fill = MethodTabs.SelectedItem == FillTab;
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
        }

        private void MethodTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Событие всплывает и от вложенных списков — реагируем только на смену самой вкладки.
            if (ReferenceEquals(e.OriginalSource, MethodTabs) && !refreshing)
            {
                RebuildMethods();
            }
        }

        private void MethodCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!refreshing)
            {
                UpdateMethodHint();
                RebuildParams();
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
            if (app == null)
            {
                SetStatus("status.noApp");
                return;
            }

            Document doc = app.ActiveDocument;
            if (doc == null)
            {
                SetStatus("status.noDocument");
                return;
            }

            Shape selected = doc.ActiveShape;
            if (selected == null)
            {
                SetStatus("status.noSelection");
                return;
            }

            bool prevOptimization = app.Optimization;
            bool prevEventsEnabled = app.EventsEnabled;
            cdrUnit prevUnit = doc.Unit;

            try
            {
                app.Optimization = true;
                app.EventsEnabled = false;
                doc.Unit = cdrUnit.cdrMillimeter;

                global::Corel.Interop.VGCore.Curve? corelCurve = GetCurveOf(selected);
                if (corelCurve == null || corelCurve.SubPaths.Count == 0)
                {
                    SetStatus("status.cannotReadShape", selected.Type);
                    return;
                }

                List<CoreCurve> contours = ReadAllSubPaths(corelCurve);
                if (contours.Count == 0)
                {
                    SetStatus("status.noContours");
                    return;
                }

                if (method.Info.NeedsClosed && !MethodRunner.OuterContour(contours).IsClosed)
                {
                    SetStatus("status.needClosed");
                    return;
                }

                MethodResult result = MethodRunner.Run(method.Info.Kind, contours, size.DiameterMm, Params);
                IReadOnlyList<PlacedStone> stones = result.Stones;
                if (stones.Count == 0)
                {
                    SetStatus("status.noStones");
                    return;
                }

                color.TryGetRgb(out byte red, out byte green, out byte blue);
                PluginSettings settings = context.Settings;
                string stoneName = size.Name + " " + color.Name;

                doc.BeginCommandGroup(caption);
                try
                {
                    Layer layer = doc.ActiveLayer;
                    var created = new object[stones.Count];

                    for (int i = 0; i < stones.Count; i++)
                    {
                        PlacedStone stone = stones[i];
                        double radius = stone.DiameterMm / 2.0;
                        Shape circle = layer.CreateEllipse2(stone.Center.X, stone.Center.Y, radius, radius);
                        circle.Name = stoneName;
                        created[i] = circle;
                    }

                    // Заливка и обводка — одним вызовом на всю пачку, а не для каждого круга (меньше COM-вызовов).
                    ShapeRange range = doc.CreateShapeRangeFromArray(ref created);
                    range.ApplyUniformFill(app.CreateRGBColor(red, green, blue));
                    if (settings.Outline == OutlineStyle.None)
                    {
                        range.SetOutlineProperties(Width: 0);
                    }
                    else
                    {
                        double width = settings.Outline == OutlineStyle.Hairline ? HairlineMm : settings.OutlineWidthMm;
                        range.SetOutlineProperties(Width: width, Color: app.CreateRGBColor(0, 0, 0));
                    }

                    // «Только показать» (раздел 6.4): налезающие стразы остаются, но получают красную обводку.
                    if (result.ConflictIndices.Count > 0)
                    {
                        object[] conflicts = result.ConflictIndices.Select(i => created[i]).ToArray();
                        doc.CreateShapeRangeFromArray(ref conflicts)
                            .SetOutlineProperties(Width: ConflictOutlineMm, Color: app.CreateRGBColor(229, 57, 53));
                    }

                    Shape group = range.Group();
                    group.Name = caption;
                }
                finally
                {
                    doc.EndCommandGroup();
                }

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
