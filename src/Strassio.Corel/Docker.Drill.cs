#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using Corel.Interop.VGCore;
using Strassio.Core.Cnc;
using Strassio.Core.Geometry;
using CoreCurve = Strassio.Core.Geometry.Curve;

namespace Strassio.Corel
{
    /// <summary>
    /// Сверление шаблона на станке (docs/SPEC.md, раздел 11.1): стразы → файлы G-code (.tap) по свёрлам.
    /// Отверстия читаются один раз (выделенные стразы, а если ничего не выделено — вся страница),
    /// дальше всё считает Strassio.Core; в документе меняется только «Показать путь».
    /// </summary>
    public partial class Docker
    {
        private void DrillExport_Click(object sender, RoutedEventArgs e)
        {
            List<DrillHole>? holes = null;
            bool fromSelection = false;
            string name = string.Empty;
            string folder = string.Empty;
            DrillBoard? page = null;
            RunInDocument(null, doc =>
            {
                StoneSelection selection = StoneShapes.FromSelection(doc, KnownSizes);
                fromSelection = selection.Stones.Count > 0;
                List<StoneShape> stones = fromSelection ? selection.Stones : StoneShapes.FromPage(doc.ActivePage, KnownSizes);
                if (stones.Count == 0)
                {
                    SetStatus("edit.noStones");
                    return;
                }

                holes = stones.Select(s => new DrillHole(s.Stone.Center, s.Stone.DiameterMm, s.Size, s.Color)).ToList();
                (name, folder) = DocumentName(doc);
                page = PageBoard(doc.ActivePage);
            });

            if (holes == null)
            {
                return;
            }

            DrillSettings settings = LoadDrillSettings();
            var window = new DrillWindow(app, holes, settings, fromSelection, name, folder, page);
            window.ShowDialog();
            SaveDrillSettings(window.Settings);

            if (window.Result == DrillWindowResult.Saved)
            {
                ReportSaved(window.SavedFiles, window.SavedFolder, window.Settings);
            }
            else if (window.Result == DrillWindowResult.DrawPath && window.Plan != null)
            {
                DrawDrillPath(window.Plan);
            }
        }

        private DrillSettings LoadDrillSettings()
        {
            try
            {
                return context.Store.LoadDrill();
            }
            catch (Exception)
            {
                return new DrillSettings();
            }
        }

        private void SaveDrillSettings(DrillSettings settings)
        {
            try
            {
                context.Store.SaveDrill(settings);
            }
            catch (Exception)
            {
                // Не сохранилось (папка только для чтения) — в следующий раз будут прежние значения.
            }
        }

        /// <summary>Страница как доска: левый нижний угол и размер, мм (единицы документа — мм, см. RunInDocument).</summary>
        private static DrillBoard? PageBoard(Page page)
        {
            try
            {
                double w = page.SizeWidth, h = page.SizeHeight;
                return w > 0 && h > 0 ? new DrillBoard(new Point2D(page.CenterX - w / 2, page.CenterY - h / 2), w, h) : (DrillBoard?)null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Имя документа без расширения и его папка (у несохранённого — заголовок окна и пустая папка).</summary>
        private static (string Name, string Folder) DocumentName(Document doc)
        {
            try
            {
                string full = doc.FullFileName;
                if (!string.IsNullOrEmpty(full))
                {
                    return (Path.GetFileNameWithoutExtension(full), Path.GetDirectoryName(full) ?? string.Empty);
                }
            }
            catch (Exception)
            {
                // Документ ещё не сохранён.
            }

            try
            {
                return (Path.GetFileNameWithoutExtension(doc.Title ?? string.Empty), string.Empty);
            }
            catch (Exception)
            {
                return (string.Empty, string.Empty);
            }
        }

        /// <summary>Что куда сохранено и в каком порядке ставить свёрла — в строке состояния и окном.</summary>
        private void ReportSaved(IReadOnlyList<GCodeFile> files, string folder, DrillSettings settings)
        {
            int holes = files.Sum(f => f.Holes);
            SetStatus("drill.saved", files.Count, holes.ToString("N0", CultureInfo.CurrentCulture), folder);

            CultureInfo culture = CultureInfo.CurrentCulture;
            var text = new StringBuilder(Loc.Format("drill.report.head", files.Count, folder)).Append("\n\n");
            int n = 0;
            foreach (GCodeFile file in files)
            {
                string drills = string.Join(", ", file.Jobs.Select(j => DrillText.Format(j.DrillMm, culture)));
                text.Append(++n).Append(". ").Append(file.Name).Append(" — ")
                    .Append(Loc.Format("drill.report.file", drills, file.Holes.ToString("N0", culture))).Append('\n');
            }

            text.Append('\n').Append(Loc.Format("drill.report.zero", Loc[DrillWindow.OriginKey(settings)], DrillText.Format(settings.DepthMm, culture)));
            if (settings.FileMode == DrillFileMode.FilePerDrill && files.Count > 1)
            {
                text.Append("\n\n").Append(Loc["drill.report.order"]);
            }

            MessageBox.Show(text.ToString(), Loc["drill.title"], MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Путь сверла на отдельном непечатаемом слое: по линии на сверло, от нуля станка по отверстиям в
        /// порядке файла. Одна группа отмены — Ctrl+Z убирает всё; слой можно просто удалить.
        /// </summary>
        private void DrawDrillPath(DrillPlan plan)
        {
            RunInDocument("undo.drillPath", doc =>
            {
                Page page = doc.ActivePage;
                string name = Loc["drill.pathLayer"];
                Layer? layer = FindLayer(page, name);
                if (layer == null)
                {
                    layer = page.CreateLayer(name);
                }

                try
                {
                    layer.Printable = false;
                }
                catch (Exception)
                {
                    // Старые версии могут не дать поменять — путь всё равно виден.
                }

                int[][] colors = { new[] { 30, 136, 229 }, new[] { 229, 57, 53 }, new[] { 67, 160, 71 }, new[] { 251, 140, 0 }, new[] { 142, 36, 170 }, new[] { 0, 137, 123 } };
                var created = new List<object>();
                int k = 0;
                foreach (DrillJob job in plan.Jobs)
                {
                    var points = new List<Point2D> { plan.Origin };
                    points.AddRange(job.Points.Select(p => p + plan.Origin));
                    if (points.Count < 2)
                    {
                        continue;
                    }

                    Shape shape = CreateCurveShape(doc, new[] { CoreCurve.FromPolyline(points, isClosed: false) }, null);
                    shape.Name = Loc.Format("drill.pathName", DrillText.Format(job.DrillMm, CultureInfo.CurrentCulture), job.Points.Count);
                    object[] one = { shape };
                    ShapeRange range = doc.CreateShapeRangeFromArray(ref one);
                    range.ApplyNoFill();
                    int[] c = colors[k++ % colors.Length];
                    range.SetOutlineProperties(Width: 0.15, Color: app!.CreateRGBColor(c[0], c[1], c[2]));
                    range.MoveToLayer(layer);
                    created.Add(shape);
                }

                // Рамка доски — проверить, что рисунок стоит на доске там, где нужно.
                if (plan.Board.HasValue)
                {
                    Point2D lo = plan.Board.Value.LowerLeft + plan.Origin, hi = plan.Board.Value.UpperRight + plan.Origin;
                    CoreCurve frame = CoreCurve.FromPolyline(new[] { lo, new Point2D(hi.X, lo.Y), hi, new Point2D(lo.X, hi.Y) }, isClosed: true);
                    Shape boardShape = CreateCurveShape(doc, new[] { frame }, null);
                    boardShape.Name = Loc["drill.boardName"];
                    object[] boardArr = { boardShape };
                    ShapeRange boardRange = doc.CreateShapeRangeFromArray(ref boardArr);
                    boardRange.ApplyNoFill();
                    boardRange.SetOutlineProperties(Width: 0.3, Color: app!.CreateRGBColor(120, 120, 120));
                    boardRange.MoveToLayer(layer);
                    created.Add(boardShape);
                }

                // Крестик в нуле станка.
                const double arm = 4;
                CoreCurve cross1 = CoreCurve.FromPolyline(new[] { plan.Origin - new Point2D(arm, 0), plan.Origin + new Point2D(arm, 0) }, isClosed: false);
                CoreCurve cross2 = CoreCurve.FromPolyline(new[] { plan.Origin - new Point2D(0, arm), plan.Origin + new Point2D(0, arm) }, isClosed: false);
                Shape zero = CreateCurveShape(doc, new[] { cross1, cross2 }, null);
                zero.Name = Loc["drill.zeroName"];
                object[] zeroArr = { zero };
                ShapeRange zeroRange = doc.CreateShapeRangeFromArray(ref zeroArr);
                zeroRange.SetOutlineProperties(Width: 0.3, Color: app!.CreateRGBColor(33, 33, 33));
                zeroRange.MoveToLayer(layer);
                created.Add(zero);

                doc.ClearSelection();
                object[] all = created.ToArray();
                doc.CreateShapeRangeFromArray(ref all).CreateSelection();
                SetStatus("drill.pathDone", plan.Jobs.Count, plan.Holes.ToString("N0", CultureInfo.CurrentCulture));
            });
        }
    }
}
