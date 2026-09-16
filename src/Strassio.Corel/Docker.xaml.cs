using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Corel.Interop.VGCore;
using Strassio.Core.Geometry;
using Strassio.Core.Placement;
using CorelApplication = Corel.Interop.VGCore.Application;
using CoreCurve = Strassio.Core.Geometry.Curve;

namespace Strassio.Corel
{
    /// <summary>
    /// Докер Strassio. CorelDRAW создаёт этот UserControl сам (см. AppUI.xslt, itemData type="wpfhost")
    /// и передаёт в конструктор свой объект Application — никакой COM-регистрации не нужно.
    /// </summary>
    public partial class Docker : UserControl
    {
        private readonly CorelApplication app;

        public Docker(object app)
        {
            InitializeComponent();
            this.app = app as CorelApplication;
        }

        // Нужен WPF-дизайнеру и на случай ошибки приведения app в конструкторе выше.
        public Docker()
        {
            InitializeComponent();
        }

        private void CreateTestStone_Click(object sender, RoutedEventArgs e)
        {
            if (app == null)
            {
                StatusText.Text = "Нет связи с CorelDRAW (app == null).";
                return;
            }

            Document doc = app.ActiveDocument;
            if (doc == null)
            {
                StatusText.Text = "Нет открытого документа. Создайте новый документ и попробуйте снова.";
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

                doc.BeginCommandGroup("Strassio: тестовая страза ss6");
                try
                {
                    Layer layer = doc.ActiveLayer;
                    const double diameterMm = 2.4; // ss6, см. docs/SPEC.md, раздел 3.1
                    double radius = diameterMm / 2.0;

                    Shape stone = layer.CreateEllipse2(0, 0, radius, radius);
                    stone.Name = "ss6 Тест";
                    stone.Fill.UniformColor = app.CreateRGBColor(229, 57, 53); // #E53935, см. SPEC 3.2
                    stone.Outline.SetNoOutline();
                }
                finally
                {
                    doc.EndCommandGroup();
                }

                StatusText.Text = "Готово: страза ss6 создана в точке (0, 0) мм. Ctrl+Z — отменить.";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка: " + ex.Message;
            }
            finally
            {
                doc.Unit = prevUnit;
                app.EventsEnabled = prevEventsEnabled;
                app.Optimization = prevOptimization;
                app.Refresh();
            }
        }

        /// <summary>Тестовая кнопка: L1 «по линии» на выделенной кривой (разомкнутой или замкнутой).</summary>
        private void ScatterSelectedCurve_Click(object sender, RoutedEventArgs e) =>
            CreateFromSelectedCurve(
                "Strassio: L1 по выделенной кривой", "Strassio: L1 по линии",
                contours => LineScatterer.Scatter(
                    OuterContour(contours),
                    new LineScatterOptions { StoneDiameterMm = 2.4, GapMm = 0.2, Mode = StepMode.FitEven }),
                requireClosed: false);

        /// <summary>Тестовая кнопка: L2 «вокруг линии» — 3 ряда (центр ss8 крупнее, края ss6) на замкнутой фигуре.</summary>
        private void RingSelectedShape_Click(object sender, RoutedEventArgs e) =>
            CreateFromSelectedCurve(
                "Strassio: L2 по выделенной фигуре", "Strassio: L2 кольца",
                contours =>
                {
                    var rows = new[]
                    {
                        new RowSpec
                        {
                            OffsetMm = -3.4,
                            ScatterOptions = new LineScatterOptions { StoneDiameterMm = 2.4, GapMm = 0.2, Mode = StepMode.FitEven },
                        },
                        new RowSpec
                        {
                            OffsetMm = 0,
                            ScatterOptions = new LineScatterOptions { StoneDiameterMm = 3.2, GapMm = 0.2, Mode = StepMode.FitEven },
                        },
                        new RowSpec
                        {
                            OffsetMm = 3.4,
                            ScatterOptions = new LineScatterOptions { StoneDiameterMm = 2.4, GapMm = 0.2, Mode = StepMode.FitEven },
                        },
                    };
                    return IntersectionFixer.RemoveOverlaps(RingScatterer.Scatter(OuterContour(contours), rows));
                },
                requireClosed: true);

        /// <summary>Тестовая кнопка: F2 «соты» на замкнутой фигуре. Отверстия (буква «О», кольцо) остаются пустыми.</summary>
        private void FillHoneycombSelectedShape_Click(object sender, RoutedEventArgs e) =>
            CreateFromSelectedCurve(
                "Strassio: заливка сотами по выделенной фигуре", "Strassio: заливка (соты)",
                contours => GridFiller.Fill(
                    contours,
                    new GridFillOptions { StoneDiameterMm = 2.4, GapMm = 0.2, Pattern = GridPattern.Honeycomb }),
                requireClosed: true);

        /// <summary>Тестовая кнопка: F3 «контурная» на замкнутой фигуре — ряды от края внутрь, центр добит сеткой.</summary>
        private void ContourFillSelectedShape_Click(object sender, RoutedEventArgs e) =>
            CreateFromSelectedCurve(
                "Strassio: контурная заливка по выделенной фигуре", "Strassio: заливка (контурная)",
                contours => ContourFiller.Fill(
                    OuterContour(contours), new ContourFillOptions { StoneDiameterMm = 2.4, GapMm = 0.2 }),
                requireClosed: true);

        /// <summary>
        /// Общая часть всех тестовых кнопок Этапа 1: читает кривую выделенной фигуры (узлы и
        /// контрольные точки Безье — один раз, дальше вся геометрия считается в Strassio.Core, см.
        /// CLAUDE.md), прогоняет её через переданный метод расстановки и создаёт круги одной группой
        /// отмены. Параметры — заглушка (ss6/ss8, подгонка); выбор размера/цвета/метода появится
        /// в докере на Этапе 2.
        /// </summary>
        private void CreateFromSelectedCurve(
            string commandGroupName, string resultGroupCaption,
            Func<IReadOnlyList<CoreCurve>, IReadOnlyList<PlacedStone>> scatter, bool requireClosed)
        {
            if (app == null)
            {
                StatusText.Text = "Нет связи с CorelDRAW (app == null).";
                return;
            }

            Document doc = app.ActiveDocument;
            if (doc == null)
            {
                StatusText.Text = "Нет открытого документа.";
                return;
            }

            Shape selected = doc.ActiveShape;
            if (selected == null)
            {
                StatusText.Text = "Сначала выделите фигуру.";
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

                global::Corel.Interop.VGCore.Curve corelCurve = selected.Curve;
                if (corelCurve == null || corelCurve.SubPaths.Count == 0)
                {
                    StatusText.Text = "У выделенной фигуры нет кривой.";
                    return;
                }

                List<CoreCurve> contours = ReadAllSubPaths(corelCurve);
                if (contours.Count == 0)
                {
                    StatusText.Text = "У выделенной фигуры нет пригодных контуров.";
                    return;
                }

                if (requireClosed && !OuterContour(contours).IsClosed)
                {
                    StatusText.Text = "Этому методу нужна замкнутая фигура (эллипс, прямоугольник, замкнутая кривая).";
                    return;
                }

                IReadOnlyList<PlacedStone> stones = scatter(contours);
                if (stones.Count == 0)
                {
                    StatusText.Text = "Не расставлено ни одной стразы — фигура слишком маленькая?";
                    return;
                }

                doc.BeginCommandGroup(commandGroupName);
                try
                {
                    Layer layer = doc.ActiveLayer;
                    var created = new object[stones.Count];

                    for (int i = 0; i < stones.Count; i++)
                    {
                        PlacedStone stone = stones[i];
                        double radius = stone.DiameterMm / 2.0;
                        Shape circle = layer.CreateEllipse2(stone.Center.X, stone.Center.Y, radius, radius);
                        circle.Fill.UniformColor = app.CreateRGBColor(229, 57, 53); // #E53935, см. SPEC 3.2
                        circle.Outline.SetNoOutline();
                        circle.Name = "ss6 Красный";
                        created[i] = circle;
                    }

                    ShapeRange range = doc.CreateShapeRangeFromArray(ref created);
                    Shape group = range.Group();
                    group.Name = resultGroupCaption;
                }
                finally
                {
                    doc.EndCommandGroup();
                }

                StatusText.Text = $"Готово: {stones.Count} страз. Ctrl+Z — отменить.";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка: " + ex.Message;
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
                CoreCurve contour = TryReadSubPath(subPaths[i]);
                if (contour != null)
                {
                    contours.Add(contour);
                }
            }

            return contours;
        }

        /// <summary>Подпуть без сегментов (мусорная точка) кривой не является — такой просто пропускаем.</summary>
        private static CoreCurve TryReadSubPath(SubPath subPath)
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
        /// Внешний контур фигуры — самый большой по габаритам. Методы по линии (L1-L3) и контурная
        /// заливка работают именно по нему, дырки им пока не нужны.
        /// </summary>
        private static CoreCurve OuterContour(IReadOnlyList<CoreCurve> contours)
        {
            CoreCurve best = contours[0];
            double bestSize = -1;

            foreach (CoreCurve contour in contours)
            {
                (double width, double height) = CurveMetrics.BoundingSize(CurveFlattener.Flatten(contour));
                double size = width * height;
                if (size > bestSize)
                {
                    bestSize = size;
                    best = contour;
                }
            }

            return best;
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
