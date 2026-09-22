#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Corel.Interop.VGCore;
using Strassio.Core.Geometry;
using Strassio.Core.Methods;
using Strassio.Core.Placement;
using Strassio.Core.Stones;
using Color = System.Windows.Media.Color;
using CorelApplication = Corel.Interop.VGCore.Application;
using CoreCurve = Strassio.Core.Geometry.Curve;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace Strassio.Corel
{
    /// <summary>
    /// «Предпросмотр» (docs/SPEC.md, раздел 2.2, пункт 6): тот же расчёт, что и «Создать», но
    /// результат рисуется картинкой в докере, а документ CorelDRAW не меняется. Картинка остаётся,
    /// пока пользователь не нажмёт «Предпросмотр» снова, «Создать» или не сменит метод.
    /// </summary>
    public partial class Docker
    {
        private void Preview_Click(object sender, RoutedEventArgs e)
        {
            if (!(SizeCombo.SelectedItem is SizeOption size) || !(ColorList.SelectedItem is ColorOption color))
            {
                SetStatus("status.noStone");
                return;
            }

            if (!(MethodCombo.SelectedItem is MethodOption method) || !CommitAllParams())
            {
                return;
            }

            if (!TryGetSelection(out CorelApplication _, out Document doc, out Shape selected))
            {
                HidePreview();
                return;
            }

            cdrUnit prevUnit = doc.Unit;
            List<CoreCurve>? contours;
            try
            {
                doc.Unit = cdrUnit.cdrMillimeter;
                contours = ReadContours(selected, method);
            }
            catch (Exception ex)
            {
                SetStatus("status.error", ex.Message);
                return;
            }
            finally
            {
                doc.Unit = prevUnit;
            }

            if (contours == null)
            {
                HidePreview();
                return;
            }

            MethodResult result;
            try
            {
                result = MethodRunner.Run(method.Info.Kind, contours, size.Size.DiameterMm, Params, SizeTable);
            }
            catch (Exception ex)
            {
                SetStatus("status.error", ex.Message);
                return;
            }

            if (result.Stones.Count == 0)
            {
                HidePreview();
                SetStatus("status.noStones");
                return;
            }

            PreviewImage.Source = BuildPreview(contours, result, color.Color);
            PreviewPanel.Visibility = Visibility.Visible;
            if (result.ConflictIndices.Count > 0)
            {
                SetStatus("status.previewConflicts", result.Stones.Count, result.ConflictIndices.Count);
            }
            else
            {
                SetStatus("status.preview", result.Stones.Count);
            }
        }

        private void HidePreview()
        {
            PreviewPanel.Visibility = Visibility.Collapsed;
            PreviewImage.Source = null;
        }

        /// <summary>
        /// Картинка: контуры фигуры тонкой линией, стразы кругами цвета камня. Все круги одного вида —
        /// одна геометрия (StreamGeometry), поэтому и 10 000 страз рисуются быстро. Ось Y
        /// переворачивается: в CorelDRAW она смотрит вверх, на экране — вниз.
        /// </summary>
        private static DrawingImage BuildPreview(IReadOnlyList<CoreCurve> contours, MethodResult result, StoneColor color)
        {
            var group = new DrawingGroup();

            double maxDiameter = result.Stones.Max(s => s.DiameterMm);
            var outlinePen = new Pen(new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)), maxDiameter / 12);
            outlinePen.Freeze();
            foreach (CoreCurve contour in contours)
            {
                group.Children.Add(new GeometryDrawing(null, outlinePen, Polyline(contour)));
            }

            var conflicts = new HashSet<int>(result.ConflictIndices);
            color.TryGetRgb(out byte r, out byte g, out byte b);
            var fill = new SolidColorBrush(Color.FromRgb(r, g, b));
            fill.Freeze();

            // Светлые камни (кристалл) на светлом фоне не видны без обводки — даём всем тонкую тёмную.
            var stonePen = new Pen(new SolidColorBrush(Color.FromArgb(0x99, 0x33, 0x33, 0x33)), maxDiameter / 20);
            stonePen.Freeze();
            var conflictPen = new Pen(new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)), maxDiameter / 6);
            conflictPen.Freeze();

            group.Children.Add(new GeometryDrawing(fill, stonePen, Circles(result.Stones, i => !conflicts.Contains(i))));
            if (conflicts.Count > 0)
            {
                group.Children.Add(new GeometryDrawing(fill, conflictPen, Circles(result.Stones, conflicts.Contains)));
            }

            group.Freeze();
            var image = new DrawingImage(group);
            image.Freeze();
            return image;
        }

        /// <summary>
        /// Картинка-схема метода для списка «Метод»: тот же метод на маленькой фигуре
        /// (Strassio.Core.Methods.MethodSamples). Цвета одинаково читаются в светлой и тёмной теме.
        /// Если что-то пошло не так — метод просто остаётся без картинки.
        /// </summary>
        private static ImageSource? BuildMethodIcon(MethodKind kind)
        {
            try
            {
                MethodResult result = MethodSamples.Run(kind, out MethodSample sample);
                var group = new DrawingGroup();

                var outlinePen = new Pen(new SolidColorBrush(Color.FromArgb(0xB0, 0x9E, 0x9E, 0x9E)), 0.6);
                outlinePen.Freeze();
                group.Children.Add(new GeometryDrawing(null, outlinePen, Polyline(sample.Shape)));

                var fill = new SolidColorBrush(Color.FromRgb(0x3D, 0x8E, 0xE0));
                fill.Freeze();
                group.Children.Add(new GeometryDrawing(fill, null, Circles(result.Stones, i => true)));

                group.Freeze();
                var image = new DrawingImage(group);
                image.Freeze();
                return image;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static StreamGeometry Polyline(CoreCurve curve)
        {
            FlattenedCurve flat = CurveFlattener.Flatten(curve);
            var geometry = new StreamGeometry();
            using (StreamGeometryContext ctx = geometry.Open())
            {
                List<Point> points = flat.Points.Select(p => new Point(p.Position.X, -p.Position.Y)).ToList();
                if (points.Count > 0)
                {
                    ctx.BeginFigure(points[0], isFilled: false, isClosed: flat.IsClosed);
                    ctx.PolyLineTo(points.Skip(1).ToList(), isStroked: true, isSmoothJoin: true);
                }
            }

            geometry.Freeze();
            return geometry;
        }

        private static StreamGeometry Circles(IReadOnlyList<PlacedStone> stones, Func<int, bool> include)
        {
            var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
            using (StreamGeometryContext ctx = geometry.Open())
            {
                for (int i = 0; i < stones.Count; i++)
                {
                    if (!include(i))
                    {
                        continue;
                    }

                    double radius = stones[i].DiameterMm / 2;
                    double x = stones[i].Center.X;
                    double y = -stones[i].Center.Y;
                    var size = new Size(radius, radius);

                    // Круг — две полуокружности.
                    ctx.BeginFigure(new Point(x - radius, y), isFilled: true, isClosed: true);
                    ctx.ArcTo(new Point(x + radius, y), size, 0, false, SweepDirection.Clockwise, true, false);
                    ctx.ArcTo(new Point(x - radius, y), size, 0, false, SweepDirection.Clockwise, true, false);
                }
            }

            geometry.Freeze();
            return geometry;
        }
    }
}
