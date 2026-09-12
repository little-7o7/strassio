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

        /// <summary>
        /// Тестовая кнопка Этапа 1: читает кривую выделенной фигуры (узлы и контрольные точки Безье —
        /// один раз, дальше вся геометрия считается в Strassio.Core, см. CLAUDE.md), расставляет по ней
        /// стразы методом L1 «по линии» и создаёт круги одной группой. Параметры — заглушка (ss6,
        /// подгонка); выбор размера/цвета/метода появится в докере на Этапе 2.
        /// </summary>
        private void ScatterSelectedCurve_Click(object sender, RoutedEventArgs e)
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
                StatusText.Text = "Сначала выделите одну линию (кривую).";
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
                    StatusText.Text = "У выделенной фигуры нет кривой (это не линия?).";
                    return;
                }

                CoreCurve coreCurve = ReadSubPath(corelCurve.SubPaths[1]);

                var options = new LineScatterOptions
                {
                    StoneDiameterMm = 2.4, // ss6, см. docs/SPEC.md, раздел 3.1
                    GapMm = 0.2,
                    Mode = StepMode.FitEven,
                };

                IReadOnlyList<PlacedStone> stones = LineScatterer.Scatter(coreCurve, options);
                if (stones.Count == 0)
                {
                    StatusText.Text = "Метод L1 не расставил ни одной стразы — кривая слишком короткая?";
                    return;
                }

                doc.BeginCommandGroup("Strassio: L1 по выделенной кривой");
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
                    group.Name = "Strassio: L1 по линии";
                }
                finally
                {
                    doc.EndCommandGroup();
                }

                StatusText.Text = $"Готово: {stones.Count} страз по выделенной кривой. Ctrl+Z — отменить.";
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
