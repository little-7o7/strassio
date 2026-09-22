#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Corel.Interop.VGCore;
using Strassio.Core.Editing;
using Strassio.Core.Geometry;
using CoreCurve = Strassio.Core.Geometry.Curve;

namespace Strassio.Corel
{
    /// <summary>
    /// Вкладки «Правка» (docs/SPEC.md, раздел 7: режимы удаления, дубли, закрепление) и «Цвет»
    /// (раздел 3.4: перекраска, выделение по размеру и цвету). Что удалить и куда сдвинуть, считает
    /// Strassio.Core.Editing.StoneEditor; здесь — только чтение страз и изменение документа.
    ///
    /// Область: выделенные стразы, а если стразы не выделены — все стразы страницы. Для «по линии» и
    /// «внутри/снаружи формы» нужна выделенная линия или форма — «инструмент».
    /// </summary>
    public partial class Docker
    {
        private static readonly DeleteMode[] DeleteModes =
        {
            DeleteMode.KeepTop, DeleteMode.KeepBottom, DeleteMode.AlongLine,
            DeleteMode.InsideShape, DeleteMode.OutsideShape, DeleteMode.Shift,
        };

        /// <summary>Была ли последней вкладка «Заливка» (список методов на вкладках «Правка» и «Цвет» не виден).</summary>
        private bool methodListIsFill;

        private bool IsMethodTab => MethodTabs.SelectedItem == LineTab || MethodTabs.SelectedItem == FillTab || MethodTabs.SelectedItem == null;

        private List<string> KnownSizes => StoneNames.SizesOf(context.Stones).ToList();

        /// <summary>Показывает то, что нужно выбранной вкладке: методы и «Создать» — или панель правки/цвета.</summary>
        private void ShowTabPanels()
        {
            bool edit = MethodTabs.SelectedItem == EditTab;
            bool color = MethodTabs.SelectedItem == ColorTab;
            bool method = !edit && !color;

            MethodPanel.Visibility = method ? Visibility.Visible : Visibility.Collapsed;
            CreateRow.Visibility = MethodPanel.Visibility;
            EditPanel.Visibility = edit ? Visibility.Visible : Visibility.Collapsed;
            ColorPanel.Visibility = color ? Visibility.Visible : Visibility.Collapsed;
            if (!method)
            {
                HidePreview();
            }
        }

        /// <summary>Тексты, которые зависят от языка и выбранного камня (кнопки «Перекрасить в …», «Выделить …»).</summary>
        private void RebuildEditTexts()
        {
            DeleteMode? selected = (DeleteModeCombo.SelectedItem as DeleteModeOption)?.Mode;
            List<DeleteModeOption> modes = DeleteModes
                .Select(m => new DeleteModeOption(m, Loc["edit.mode." + ModeKey(m)]))
                .ToList();
            DeleteModeCombo.ItemsSource = modes;
            DeleteModeCombo.SelectedItem = modes.FirstOrDefault(m => m.Mode == selected) ?? modes[0];
            UpdateDeleteModeHint();

            string size = (SizeCombo.SelectedItem as SizeOption)?.Size.Name ?? "—";
            string colorName = (ColorList.SelectedItem as ColorOption)?.Name ?? "—";
            RecolorButton.Content = Loc.Format("color.recolor", colorName);
            SelectSizeButton.Content = Loc.Format("color.selectSize", size);
            SelectColorButton.Content = Loc.Format("color.selectColor", colorName);
            SelectBothButton.Content = Loc.Format("color.selectBoth", size, colorName);
        }

        private static string ModeKey(DeleteMode mode)
        {
            switch (mode)
            {
                case DeleteMode.KeepTop: return "keepTop";
                case DeleteMode.KeepBottom: return "keepBottom";
                case DeleteMode.AlongLine: return "alongLine";
                case DeleteMode.InsideShape: return "inside";
                case DeleteMode.OutsideShape: return "outside";
                default: return "shift";
            }
        }

        private void UpdateDeleteModeHint()
        {
            if (!(DeleteModeCombo.SelectedItem is DeleteModeOption option))
            {
                return;
            }

            DeleteModeHint.Text = Loc["edit.mode." + ModeKey(option.Mode) + ".tooltip"];
            bool byShape = option.Mode == DeleteMode.InsideShape || option.Mode == DeleteMode.OutsideShape;
            TouchingBox.Visibility = byShape ? Visibility.Visible : Visibility.Collapsed;
        }

        private void DeleteModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateDeleteModeHint();

        // ---- Правка ----------------------------------------------------------------------------

        private void ApplyDelete_Click(object sender, RoutedEventArgs e)
        {
            if (!(DeleteModeCombo.SelectedItem is DeleteModeOption option))
            {
                return;
            }

            RunInDocument("undo.edit", doc =>
            {
                StoneSelection selection = StoneShapes.FromSelection(doc, KnownSizes);
                List<StoneShape> stones = selection.Stones.Count > 0 ? selection.Stones : StoneShapes.FromPage(doc.ActivePage, KnownSizes);
                if (stones.Count == 0)
                {
                    SetStatus("edit.noStones");
                    return;
                }

                List<DocStone> docStones = stones.Select(s => s.Stone).ToList();
                EditResult result;
                switch (option.Mode)
                {
                    case DeleteMode.KeepTop:
                    case DeleteMode.KeepBottom:
                        result = StoneEditor.ResolveOverlaps(docStones, keepTop: option.Mode == DeleteMode.KeepTop);
                        break;

                    case DeleteMode.Shift:
                        result = StoneEditor.ShiftApart(docStones);
                        break;

                    case DeleteMode.AlongLine:
                        List<CoreCurve> cutters = ToolCurves(selection.Tools);
                        if (cutters.Count == 0)
                        {
                            SetStatus("edit.needLine");
                            return;
                        }

                        result = StoneEditor.DeleteAlongLines(docStones, cutters);
                        break;

                    default:
                        List<CoreCurve> shape = ToolCurves(selection.Tools).Where(c => c.IsClosed).ToList();
                        if (shape.Count == 0)
                        {
                            SetStatus("edit.needShape");
                            return;
                        }

                        result = StoneEditor.DeleteByShape(
                            docStones, shape, inside: option.Mode == DeleteMode.InsideShape, touchingToo: TouchingBox.IsChecked == true);
                        break;
                }

                Apply(stones, result);
                SetStatus(result.IsEmpty ? "edit.nothing" : "edit.done", result.Deleted.Count, result.Moved.Count);
            });
        }

        private void Duplicates_Click(object sender, RoutedEventArgs e)
        {
            RunInDocument("undo.duplicates", doc =>
            {
                StoneSelection selection = StoneShapes.FromSelection(doc, KnownSizes);
                List<StoneShape> stones = selection.Stones.Count > 0 ? selection.Stones : StoneShapes.FromPage(doc.ActivePage, KnownSizes);
                EditResult result = StoneEditor.FindDuplicates(stones.Select(s => s.Stone).ToList());
                Apply(stones, result);
                SetStatus("edit.duplicatesDone", result.Deleted.Count);
            });
        }

        private void Lock_Click(object sender, RoutedEventArgs e) => SetLocked(true);

        private void Unlock_Click(object sender, RoutedEventArgs e) => SetLocked(false);

        private void SetLocked(bool locked)
        {
            RunInDocument(locked ? "undo.lock" : "undo.unlock", doc =>
            {
                List<StoneShape> stones = StoneShapes.FromSelection(doc, KnownSizes).Stones;
                if (stones.Count == 0)
                {
                    SetStatus("edit.selectStones");
                    return;
                }

                foreach (StoneShape stone in stones)
                {
                    StoneShapes.SetLocked(stone.Shape, locked);
                }

                SetStatus(locked ? "edit.locked" : "edit.unlocked", stones.Count);
            });
        }

        /// <summary>Сначала двигаем, потом удаляем — номера в результате относятся к исходному списку.</summary>
        private static void Apply(List<StoneShape> stones, EditResult result)
        {
            foreach (StoneMove move in result.Moved)
            {
                Point2D from = stones[move.Index].Stone.Center;
                stones[move.Index].Shape.Move(move.NewCenter.X - from.X, move.NewCenter.Y - from.Y);
            }

            foreach (int index in result.Deleted)
            {
                stones[index].Shape.Delete();
            }
        }

        /// <summary>Все контуры выделенных линий и форм — «ножи» и «формы» для режимов удаления.</summary>
        private static List<CoreCurve> ToolCurves(IEnumerable<Shape> tools)
        {
            var curves = new List<CoreCurve>();
            foreach (Shape tool in tools)
            {
                global::Corel.Interop.VGCore.Curve? curve = GetCurveOf(tool);
                if (curve != null && curve.SubPaths.Count > 0)
                {
                    curves.AddRange(ReadAllSubPaths(curve));
                }
            }

            return curves;
        }

        // ---- Цвет ------------------------------------------------------------------------------

        private void Recolor_Click(object sender, RoutedEventArgs e)
        {
            if (!(ColorList.SelectedItem is ColorOption color))
            {
                SetStatus("status.noStone");
                return;
            }

            string fallbackSize = (SizeCombo.SelectedItem as SizeOption)?.Size.Name ?? string.Empty;
            RunInDocument("undo.recolor", doc =>
            {
                List<StoneShape> stones = StoneShapes.FromSelection(doc, KnownSizes).Stones;
                if (stones.Count == 0)
                {
                    SetStatus("edit.selectStones");
                    return;
                }

                color.Color.TryGetRgb(out byte r, out byte g, out byte b);
                object[] shapes = stones.Select(s => (object)s.Shape).ToArray();
                doc.CreateShapeRangeFromArray(ref shapes).ApplyUniformFill(app!.CreateRGBColor(r, g, b));
                foreach (StoneShape stone in stones)
                {
                    StoneShapes.Rename(stone.Shape, stone.Size.Length > 0 ? stone.Size : fallbackSize, color.Name);
                }

                SetStatus("color.recolored", stones.Count, color.Name);
            });
        }

        private void SelectSize_Click(object sender, RoutedEventArgs e) =>
            SelectBy((SizeCombo.SelectedItem as SizeOption)?.Size.Name, null);

        private void SelectColor_Click(object sender, RoutedEventArgs e) =>
            SelectBy(null, (ColorList.SelectedItem as ColorOption)?.Name);

        private void SelectBoth_Click(object sender, RoutedEventArgs e) =>
            SelectBy((SizeCombo.SelectedItem as SizeOption)?.Size.Name, (ColorList.SelectedItem as ColorOption)?.Name);

        /// <summary>Выделяет на странице все стразы нужного размера и/или цвета (выделение не отменяется Ctrl+Z — группа отмены не нужна).</summary>
        private void SelectBy(string? size, string? color)
        {
            if (size == null && color == null)
            {
                SetStatus("status.noStone");
                return;
            }

            RunInDocument(null, doc =>
            {
                List<StoneShape> matching = StoneShapes.FromPage(doc.ActivePage, KnownSizes)
                    .Where(s => StoneNames.Matches(s.Size, s.Color, size, color))
                    .ToList();

                doc.ClearSelection();
                if (matching.Count > 0)
                {
                    object[] shapes = matching.Select(s => (object)s.Shape).ToArray();
                    doc.CreateShapeRangeFromArray(ref shapes).CreateSelection();
                }

                SetStatus("color.selected", matching.Count);
            });
        }

        // ---- Общее -----------------------------------------------------------------------------

        /// <summary>
        /// Правило работы с документом (CLAUDE.md): на время операции — Optimization, без событий,
        /// единицы — миллиметры, всё одной группой отмены; в конце всё вернуть как было.
        /// <paramref name="undoKey"/> null — операция не меняет документ (выделение), группа отмены не нужна.
        /// </summary>
        private void RunInDocument(string? undoKey, Action<Document> action)
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

            bool prevOptimization = app.Optimization;
            bool prevEventsEnabled = app.EventsEnabled;
            cdrUnit prevUnit = doc.Unit;
            try
            {
                app.Optimization = true;
                app.EventsEnabled = false;
                doc.Unit = cdrUnit.cdrMillimeter;

                if (undoKey == null)
                {
                    action(doc);
                }
                else
                {
                    doc.BeginCommandGroup(Loc[undoKey]);
                    try
                    {
                        action(doc);
                    }
                    finally
                    {
                        doc.EndCommandGroup();
                    }
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

        /// <summary>Строка списка режимов удаления.</summary>
        public sealed class DeleteModeOption
        {
            internal DeleteModeOption(DeleteMode mode, string text)
            {
                Mode = mode;
                Text = text;
            }

            public DeleteMode Mode { get; }

            public string Text { get; }
        }
    }
}
