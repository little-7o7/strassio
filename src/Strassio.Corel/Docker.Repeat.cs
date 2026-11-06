#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Corel.Interop.VGCore;
using Strassio.Core.Geometry;
using Strassio.Core.Motifs;
using Strassio.Core.Placement;
using Strassio.Core.Settings;

namespace Strassio.Corel
{
    /// <summary>
    /// Раппорт (вкладка «Мотивы», docs/SPEC.md, раздел 9): выделенные стразы — плитка — повторяются по ткани
    /// сеткой: прямо, со сдвигом столбцов на полшага вниз или рядов на полшага вбок. Шаг — размер рамки
    /// плитки (прямоугольник в выделении), а без рамки — габарит страз плюс зазор; можно ввести свой.
    /// Стразы, которые на стыке налезли бы на соседнюю копию, убираются из самой плитки, поэтому стыки
    /// чистые по всей ткани. Копирование — целыми группами, это быстро даже для тысяч страз.
    /// </summary>
    public partial class Docker
    {
        private const int DefaultRepeatColumns = 3;
        private const int DefaultRepeatRows = 3;

        /// <summary>Списки и поля раздела «Раппорт» на текущем языке.</summary>
        private void RebuildRepeat()
        {
            var shifts = new List<ChoiceOption>
            {
                new ChoiceOption(nameof(RepeatShift.None), Loc["motif.repeat.shift.none"]),
                new ChoiceOption(nameof(RepeatShift.HalfDrop), Loc["motif.repeat.shift.halfDrop"]),
                new ChoiceOption(nameof(RepeatShift.Brick), Loc["motif.repeat.shift.brick"]),
            };
            string? shift = (RepeatShiftCombo.SelectedItem as ChoiceOption)?.Value;
            RepeatShiftCombo.ItemsSource = shifts;
            RepeatShiftCombo.SelectedItem = shifts.FirstOrDefault(s => s.Value == shift) ?? shifts[1];

            if (string.IsNullOrWhiteSpace(RepeatColumnsBox.Text))
            {
                RepeatColumnsBox.Text = DefaultRepeatColumns.ToString(CultureInfo.CurrentCulture);
            }

            if (string.IsNullOrWhiteSpace(RepeatRowsBox.Text))
            {
                RepeatRowsBox.Text = DefaultRepeatRows.ToString(CultureInfo.CurrentCulture);
            }
        }

        private RepeatShift SelectedRepeatShift =>
            Enum.TryParse((RepeatShiftCombo.SelectedItem as ChoiceOption)?.Value, out RepeatShift shift) ? shift : RepeatShift.HalfDrop;

        /// <summary>Свой шаг из поля, мм; null — поле пустое или там не число (тогда шаг по рамке или по стразам).</summary>
        private double? TypedStep(TextBox box) =>
            LengthUnits.TryParse(box.Text, context.Settings.Units, out double mm) && mm > 0 && mm <= 10000 ? mm : (double?)null;

        private void Repeat_Click(object sender, RoutedEventArgs e)
        {
            int columns = ReadCount(RepeatColumnsBox, 1, 200, DefaultRepeatColumns);
            int rows = ReadCount(RepeatRowsBox, 1, 200, DefaultRepeatRows);
            RepeatShift shift = SelectedRepeatShift;
            double? typedX = TypedStep(RepeatStepXBox), typedY = TypedStep(RepeatStepYBox);
            double gap = Params.GapMm;

            RunInDocument("undo.repeat", doc =>
            {
                if (!context.CanCreate)
                {
                    throw new InvalidOperationException(Loc["license.needed"]);
                }

                StoneSelection selection = StoneShapes.FromSelection(doc, KnownSizes);
                List<StoneShape> stones = selection.Stones;
                if (stones.Count == 0)
                {
                    SetStatus("motif.repeat.select");
                    return;
                }

                // Рамка плитки — самая большая выделенная фигура, которая не страза. Её не копируем.
                Shape? frame = null;
                double frameW = 0, frameH = 0, frameArea = 0;
                foreach (Shape tool in selection.Tools)
                {
                    tool.GetBoundingBox(out _, out _, out double w, out double h, false);
                    if (w * h > frameArea)
                    {
                        frame = tool;
                        frameW = w;
                        frameH = h;
                        frameArea = w * h;
                    }
                }

                double minX = stones.Min(s => s.Stone.Center.X - s.Stone.DiameterMm / 2);
                double maxX = stones.Max(s => s.Stone.Center.X + s.Stone.DiameterMm / 2);
                double minY = stones.Min(s => s.Stone.Center.Y - s.Stone.DiameterMm / 2);
                double maxY = stones.Max(s => s.Stone.Center.Y + s.Stone.DiameterMm / 2);
                var options = new RepeatOptions
                {
                    StepXMm = typedX ?? (frame != null ? frameW : maxX - minX + gap),
                    StepYMm = typedY ?? (frame != null ? frameH : maxY - minY + gap),
                    Shift = shift,
                    Columns = columns,
                    Rows = rows,
                    GapMm = gap,
                };

                // Что копировать: выделенные фигуры верхнего уровня, кроме рамки и других не-страз.
                var tools = new HashSet<int>(selection.Tools.Select(t => t.StaticID));
                var tops = new List<Shape>();
                ShapeRange range = doc.SelectionRange;
                for (int i = 1; i <= range.Count; i++)
                {
                    Shape shape = range[i];
                    if (!tools.Contains(shape.StaticID))
                    {
                        tops.Add(shape);
                    }
                }

                // Стыки: стразы, которые налезут на соседнюю копию, убираются из плитки.
                List<int> conflicts = Repeat.SeamConflicts(
                    stones.Select(s => new PlacedStone(s.Stone.Center, s.Stone.DiameterMm, false)).ToList(), options);
                var deleted = new HashSet<int>();
                foreach (int i in conflicts)
                {
                    deleted.Add(stones[i].Shape.StaticID);
                    stones[i].Shape.Delete();
                }

                tops.RemoveAll(t => deleted.Contains(t.StaticID));
                var all = new List<object>(tops.Cast<object>());
                foreach (Point2D offset in Repeat.Offsets(options).Skip(1))
                {
                    foreach (Shape top in tops)
                    {
                        Shape copy = top.Duplicate(offset.X, offset.Y);

                        // Копия «живой» группы не должна пересобираться по исходной фигуре на месте оригинала.
                        LiveStones.Forget(copy);
                        all.Add(copy);
                    }
                }

                object[] shapes = all.ToArray();
                doc.ClearSelection();
                if (shapes.Length > 0)
                {
                    doc.CreateShapeRangeFromArray(ref shapes).CreateSelection();
                }

                int perTile = stones.Count - conflicts.Count;
                SetStatus(
                    "motif.repeat.done", columns * rows, perTile * columns * rows, conflicts.Count,
                    LengthUnits.Format(options.StepXMm, context.Settings.Units, Math.Max(1, context.Settings.Decimals), CultureInfo.CurrentCulture),
                    LengthUnits.Format(options.StepYMm, context.Settings.Units, Math.Max(1, context.Settings.Decimals), CultureInfo.CurrentCulture));
            });
        }
    }
}
