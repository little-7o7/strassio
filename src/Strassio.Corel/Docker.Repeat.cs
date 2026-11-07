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
using Strassio.Core.Stones;
using Clipboard = System.Windows.Clipboard;
using CoreCurve = Strassio.Core.Geometry.Curve;

namespace Strassio.Corel
{
    /// <summary>
    /// Раппорт (вкладка «Мотивы», docs/SPEC.md, раздел 9): выделенные стразы — плитка — повторяются по ткани
    /// сеткой: прямо, со сдвигом столбцов на полшага вниз или рядов на полшага вбок; каждая вторая копия
    /// может быть повёрнута или отражена (раппорт из двух мотивов). Шаг — размер рамки плитки (прямоугольник
    /// в выделении), а без рамки — габарит страз плюс зазор; можно ввести свой. Стразы, которые на стыке
    /// налезли бы на соседнюю копию, убираются из самой плитки, поэтому стыки чистые по всей ткани.
    /// Здесь же: лист трафарета без шва, сколько камней на метр ткани и обрезка по рамке.
    /// </summary>
    public partial class Docker
    {
        private const int DefaultRepeatColumns = 3;
        private const int DefaultRepeatRows = 3;

        /// <summary>Сколько места между плиткой и готовым листом, мм.</summary>
        private const double SheetSpacingMm = 20;

        private double sheetWidthMm = 300;
        private double sheetHeightMm = 400;
        private double fabricWidthMm = 1500;

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

            var alternates = new List<ChoiceOption>
            {
                new ChoiceOption(nameof(RepeatAlternate.None), Loc["motif.repeat.alternate.none"]),
                new ChoiceOption(nameof(RepeatAlternate.Rotate180), Loc["motif.repeat.alternate.rotate"]),
                new ChoiceOption(nameof(RepeatAlternate.MirrorX), Loc["motif.repeat.alternate.mirrorX"]),
                new ChoiceOption(nameof(RepeatAlternate.MirrorY), Loc["motif.repeat.alternate.mirrorY"]),
            };
            string? alternate = (RepeatAlternateCombo.SelectedItem as ChoiceOption)?.Value;
            RepeatAlternateCombo.ItemsSource = alternates;
            RepeatAlternateCombo.SelectedItem = alternates.FirstOrDefault(a => a.Value == alternate) ?? alternates[0];

            var trims = new List<ChoiceOption>
            {
                new ChoiceOption(nameof(FrameTrimMode.WholeStone), Loc["motif.trim.mode.whole"]),
                new ChoiceOption(nameof(FrameTrimMode.Center), Loc["motif.trim.mode.center"]),
            };
            string? trim = (TrimModeCombo.SelectedItem as ChoiceOption)?.Value;
            TrimModeCombo.ItemsSource = trims;
            TrimModeCombo.SelectedItem = trims.FirstOrDefault(t => t.Value == trim) ?? trims[0];

            if (string.IsNullOrWhiteSpace(RepeatColumnsBox.Text))
            {
                RepeatColumnsBox.Text = DefaultRepeatColumns.ToString(CultureInfo.CurrentCulture);
            }

            if (string.IsNullOrWhiteSpace(RepeatRowsBox.Text))
            {
                RepeatRowsBox.Text = DefaultRepeatRows.ToString(CultureInfo.CurrentCulture);
            }

            // Размеры — в текущих единицах (при смене мм/дюймов пересчитываются).
            SheetWidthBox.Text = FormatLength(sheetWidthMm);
            SheetHeightBox.Text = FormatLength(sheetHeightMm);
            FabricWidthBox.Text = FormatLength(fabricWidthMm);
        }

        private string FormatLength(double mm) =>
            LengthUnits.Format(mm, context.Settings.Units, Math.Max(1, context.Settings.Decimals), CultureInfo.CurrentCulture);

        private RepeatShift SelectedRepeatShift =>
            Enum.TryParse((RepeatShiftCombo.SelectedItem as ChoiceOption)?.Value, out RepeatShift shift) ? shift : RepeatShift.HalfDrop;

        private RepeatAlternate SelectedRepeatAlternate =>
            Enum.TryParse((RepeatAlternateCombo.SelectedItem as ChoiceOption)?.Value, out RepeatAlternate alternate) ? alternate : RepeatAlternate.None;

        private FrameTrimMode SelectedTrimMode =>
            Enum.TryParse((TrimModeCombo.SelectedItem as ChoiceOption)?.Value, out FrameTrimMode mode) ? mode : FrameTrimMode.WholeStone;

        /// <summary>Свой шаг из поля, мм; null — поле пустое или там не число (тогда шаг по рамке или по стразам).</summary>
        private double? TypedStep(TextBox box) =>
            LengthUnits.TryParse(box.Text, context.Settings.Units, out double mm) && mm > 0 && mm <= 10000 ? mm : (double?)null;

        /// <summary>Размер из поля, если там число в разумных пределах; иначе прежний.</summary>
        private double ReadLength(TextBox box, double min, double max, double fallback) =>
            LengthUnits.TryParse(box.Text, context.Settings.Units, out double mm) && mm >= min && mm <= max ? mm : fallback;

        private void SheetWidthBox_TextChanged(object sender, TextChangedEventArgs e) => sheetWidthMm = ReadLength(SheetWidthBox, 10, 5000, sheetWidthMm);

        private void SheetHeightBox_TextChanged(object sender, TextChangedEventArgs e) => sheetHeightMm = ReadLength(SheetHeightBox, 10, 5000, sheetHeightMm);

        private void FabricWidthBox_TextChanged(object sender, TextChangedEventArgs e) => fabricWidthMm = ReadLength(FabricWidthBox, 10, 10000, fabricWidthMm);

        /// <summary>Что введено в разделе «Раппорт»; шаг 0 — взять по рамке или по стразам.</summary>
        private RepeatOptions RepeatInput() => new RepeatOptions
        {
            StepXMm = TypedStep(RepeatStepXBox) ?? 0,
            StepYMm = TypedStep(RepeatStepYBox) ?? 0,
            Shift = SelectedRepeatShift,
            Alternate = SelectedRepeatAlternate,
            Columns = ReadCount(RepeatColumnsBox, 1, 200, DefaultRepeatColumns),
            Rows = ReadCount(RepeatRowsBox, 1, 200, DefaultRepeatRows),
            GapMm = Params.GapMm,
        };

        /// <summary>Плитка раппорта из выделения: стразы, рамка (если есть), шаг и габарит.</summary>
        private sealed class RepeatTile
        {
            public RepeatTile(StoneSelection selection, Shape? frame, RepeatOptions options, double left, double top, double right, double bottom)
            {
                Selection = selection;
                Frame = frame;
                Options = options;
                Placed = selection.Stones.Select(s => new PlacedStone(s.Stone.Center, s.Stone.DiameterMm, false)).ToList();
                Left = left;
                Top = top;
                Right = right;
                Bottom = bottom;
            }

            public StoneSelection Selection { get; }

            public List<StoneShape> Stones => Selection.Stones;

            public Shape? Frame { get; }

            public RepeatOptions Options { get; }

            public List<PlacedStone> Placed { get; }

            public double Left { get; }

            public double Top { get; }

            public double Right { get; }

            public double Bottom { get; }
        }

        /// <summary>
        /// Плитка из выделения. Рамка — самая большая выделенная фигура, которая не страза: её размер — шаг,
        /// её середина — центр поворота «второй» копии. Нет страз — сообщение и null.
        /// </summary>
        private RepeatTile? ReadRepeatTile(Document doc, RepeatOptions input)
        {
            StoneSelection selection = StoneShapes.FromSelection(doc, KnownSizes);
            List<StoneShape> stones = selection.Stones;
            if (stones.Count == 0)
            {
                SetStatus("motif.repeat.select");
                return null;
            }

            Shape? frame = null;
            double frameX = 0, frameY = 0, frameW = 0, frameH = 0;
            foreach (Shape tool in selection.Tools)
            {
                tool.GetBoundingBox(out double x, out double y, out double w, out double h, false);
                if (w * h > frameW * frameH)
                {
                    frame = tool;
                    frameX = x;
                    frameY = y;
                    frameW = w;
                    frameH = h;
                }
            }

            double minX = stones.Min(s => s.Stone.Center.X - s.Stone.DiameterMm / 2);
            double maxX = stones.Max(s => s.Stone.Center.X + s.Stone.DiameterMm / 2);
            double minY = stones.Min(s => s.Stone.Center.Y - s.Stone.DiameterMm / 2);
            double maxY = stones.Max(s => s.Stone.Center.Y + s.Stone.DiameterMm / 2);
            double gap = input.GapMm;
            var options = new RepeatOptions
            {
                StepXMm = input.StepXMm > 0 ? input.StepXMm : (frame != null ? frameW : maxX - minX + gap),
                StepYMm = input.StepYMm > 0 ? input.StepYMm : (frame != null ? frameH : maxY - minY + gap),
                Shift = input.Shift,
                Alternate = input.Alternate,
                Columns = input.Columns,
                Rows = input.Rows,
                GapMm = gap,
                Pivot = frame != null ? new Point2D(frameX + frameW / 2, frameY + frameH / 2) : (Point2D?)null,
            };

            return frame != null
                ? new RepeatTile(selection, frame, options, Math.Min(frameX, minX), Math.Max(frameY + frameH, maxY), Math.Max(frameX + frameW, maxX), Math.Min(frameY, minY))
                : new RepeatTile(selection, null, options, minX, maxY, maxX, minY);
        }

        // ---- Повторить по ткани ----------------------------------------------------------------------

        private void Repeat_Click(object sender, RoutedEventArgs e)
        {
            RepeatOptions input = RepeatInput();
            RunInDocument("undo.repeat", doc =>
            {
                if (!context.CanCreate)
                {
                    throw new InvalidOperationException(Loc["license.needed"]);
                }

                RepeatTile? tile = ReadRepeatTile(doc, input);
                if (tile == null)
                {
                    return;
                }

                RepeatOptions options = tile.Options;
                List<StoneShape> stones = tile.Stones;

                // Что копировать: выделенные фигуры верхнего уровня, кроме рамки и других не-страз.
                var tools = new HashSet<int>(tile.Selection.Tools.Select(t => t.StaticID));
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
                List<int> conflicts = Repeat.SeamConflicts(tile.Placed, options);
                var deleted = new HashSet<int>();
                foreach (int i in conflicts)
                {
                    deleted.Add(stones[i].Shape.StaticID);
                    stones[i].Shape.Delete();
                }

                tops.RemoveAll(t => deleted.Contains(t.StaticID));
                Point2D pivot = Repeat.PivotOf(tile.Placed, options);
                var all = new List<object>(tops.Cast<object>());
                List<Point2D> offsets = Repeat.Offsets(options);
                for (int k = 1; k < offsets.Count; k++)
                {
                    bool second = Repeat.IsAlternate(options, k);
                    foreach (Shape top in tops)
                    {
                        Shape copy = top.Duplicate(offsets[k].X, offsets[k].Y);

                        // Копия «живой» группы не должна пересобираться по исходной фигуре на месте оригинала.
                        LiveStones.Forget(copy);
                        if (second)
                        {
                            TurnCopy(copy, options.Alternate, pivot + offsets[k]);
                        }

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
                    "motif.repeat.done", options.Columns * options.Rows, perTile * options.Columns * options.Rows, conflicts.Count,
                    FormatLength(options.StepXMm), FormatLength(options.StepYMm));
            });
        }

        /// <summary>
        /// «Вторая» копия: поворот на 180° или отражение вокруг середины плитки. CorelDRAW поворачивает и
        /// отражает фигуру вокруг её собственной середины, поэтому потом она сдвигается туда, куда середину
        /// её габарита переносит поворот вокруг середины плитки (у поворота на 180° и отражения габарит
        /// переходит в габарит, так что это точно).
        /// </summary>
        private static void TurnCopy(Shape copy, RepeatAlternate alternate, Point2D pivot)
        {
            copy.GetBoundingBox(out double x, out double y, out double w, out double h, false);
            Point2D target = Repeat.Transform(alternate, new Point2D(x + w / 2, y + h / 2), pivot);
            switch (alternate)
            {
                case RepeatAlternate.Rotate180:
                    copy.Rotate(180);
                    break;
                case RepeatAlternate.MirrorX:
                    copy.Flip(cdrFlipAxes.cdrFlipHorizontal);
                    break;
                case RepeatAlternate.MirrorY:
                    copy.Flip(cdrFlipAxes.cdrFlipVertical);
                    break;
                default:
                    return;
            }

            copy.GetBoundingBox(out x, out y, out w, out h, false);
            copy.Move(target.X - (x + w / 2), target.Y - (y + h / 2));
        }

        // ---- Лист без шва ---------------------------------------------------------------------------

        /// <summary>
        /// Лист трафарета, который можно класть встык без шва: самый большой лист не больше заданного, у
        /// которого ширина и высота — целое число повторов узора. Лист заполняется стразами плитки и ставится
        /// справа от неё; сама плитка не меняется. Страза на краю листа достаётся только одному из соседних листов.
        /// </summary>
        private void Sheet_Click(object sender, RoutedEventArgs e)
        {
            RepeatOptions input = RepeatInput();
            double maxW = sheetWidthMm, maxH = sheetHeightMm;
            string fallbackSize = (SizeCombo.SelectedItem as SizeOption)?.Size.Name ?? string.Empty;
            RunInDocument("undo.sheet", doc =>
            {
                RepeatTile? tile = ReadRepeatTile(doc, input);
                if (tile == null)
                {
                    return;
                }

                RepeatOptions options = tile.Options;
                RepeatSheet sheet = Repeat.SeamlessSheet(options, maxW, maxH);
                if (!sheet.IsValid)
                {
                    SetStatus("motif.sheet.tooSmall", FormatLength(maxW), FormatLength(maxH), FormatLength(options.StepXMm), FormatLength(options.StepYMm));
                    return;
                }

                // Лист начинается от угла клетки плитки (её рамки) и ставится справа от плитки, верхом вровень с ней.
                Point2D pivot = Repeat.PivotOf(tile.Placed, options);
                var corner = new Point2D(pivot.X - options.StepXMm / 2, pivot.Y + options.StepYMm / 2);
                List<SheetStone> onSheet = Repeat.FillSheet(tile.Placed, options, sheet, corner, out int removed);
                if (onSheet.Count == 0)
                {
                    SetStatus("status.noStones");
                    return;
                }

                var move = new Point2D(tile.Right + SheetSpacingMm - corner.X, tile.Top - corner.Y);
                var sources = new List<MotifStone>(tile.Stones.Count);
                foreach (StoneShape s in tile.Stones)
                {
                    string rgb = TryUniformFill(s.Shape, out byte r, out byte g, out byte b) ? Hex(r, g, b) : "#C0C0C0";
                    string size = s.Size.Length > 0 ? s.Size : SizeNameFor(s.Stone.DiameterMm, fallbackSize);
                    sources.Add(new MotifStone(s.Stone.Center.X, s.Stone.Center.Y, s.Stone.DiameterMm, size, s.Color, rgb));
                }

                List<MotifPlacement> placed = onSheet.Select(s => new MotifPlacement(s.Center + move, sources[s.Source], 0)).ToList();
                string name = Loc.Format("motif.sheet.name", FormatLength(sheet.WidthMm), FormatLength(sheet.HeightMm));
                Shape group = CreateMotifStones(doc, placed, name);

                Point2D topLeft = corner + move;
                CoreCurve border = CoreCurve.FromPolyline(
                    new[]
                    {
                        topLeft,
                        new Point2D(topLeft.X + sheet.WidthMm, topLeft.Y),
                        new Point2D(topLeft.X + sheet.WidthMm, topLeft.Y - sheet.HeightMm),
                        new Point2D(topLeft.X, topLeft.Y - sheet.HeightMm),
                    },
                    isClosed: true);
                Shape outline = CreateCurveShape(doc, new[] { border }, null);
                outline.Name = name;
                object[] frame = { outline };
                ShapeRange frameRange = doc.CreateShapeRangeFromArray(ref frame);
                frameRange.ApplyNoFill();
                frameRange.SetOutlineProperties(Width: 0.3, Color: app!.CreateRGBColor(229, 57, 53));

                object[] result = { group, outline };
                doc.ClearSelection();
                doc.CreateShapeRangeFromArray(ref result).CreateSelection();
                SetStatus(
                    "motif.sheet.done", FormatLength(sheet.WidthMm), FormatLength(sheet.HeightMm), sheet.Across, sheet.Down,
                    onSheet.Count.ToString("N0", CultureInfo.CurrentCulture), removed);
            });
        }

        // ---- Камней на метр ткани --------------------------------------------------------------------

        /// <summary>
        /// Сколько камней каждого размера и цвета уйдёт на погонный метр ткани заданной ширины: камни плитки
        /// (без страз со стыков) умножаются на число плиток в метре. Таблица — сразу в буфере обмена.
        /// </summary>
        private void PerMetre_Click(object sender, RoutedEventArgs e)
        {
            RepeatOptions input = RepeatInput();
            double fabric = fabricWidthMm;
            StoneCount? perTile = null;
            double tiles = 0;
            RunInDocument(null, doc =>
            {
                RepeatTile? tile = ReadRepeatTile(doc, input);
                if (tile == null)
                {
                    return;
                }

                var drop = new HashSet<int>(Repeat.SeamConflicts(tile.Placed, tile.Options));
                perTile = StoneCount.Of(tile.Stones.Where((s, i) => !drop.Contains(i)).Select(s => (s.Size, s.Color)), KnownSizes);
                tiles = Repeat.TilesPerRunningMetre(tile.Options, fabric);
            });

            if (perTile == null || perTile.Total == 0)
            {
                return;
            }

            StoneCount metre = perTile.Times(tiles);
            bool copied = true;
            try
            {
                Clipboard.SetText(metre.ToTable(Loc["prod.count.size"], Loc["prod.count.color"], Loc["prod.count.pieces"], Loc["prod.count.total"]));
            }
            catch (Exception)
            {
                // Буфер обмена занят другой программой — таблица всё равно видна в окне.
                copied = false;
            }

            CultureInfo culture = CultureInfo.CurrentCulture;
            SetStatus("motif.metre.done", metre.Total.ToString("N0", culture), FormatLength(fabric));
            string head = Loc.Format("motif.metre.head", FormatLength(fabric), tiles.ToString("0.#", culture), perTile.Total.ToString("N0", culture));
            MessageBox.Show(head + "\n\n" + CountText(metre, copied), Loc["motif.metre.title"], MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ---- Обрезать по рамке -----------------------------------------------------------------------

        /// <summary>
        /// Убирает стразы, которые вылезают за рамку (замкнутую фигуру в выделении): целиком вне или только
        /// центром вне — как выбрано. Удобно отрезать ткань-раппорт по форме изделия.
        /// </summary>
        private void TrimFrame_Click(object sender, RoutedEventArgs e)
        {
            FrameTrimMode mode = SelectedTrimMode;
            RunInDocument("undo.trim", doc =>
            {
                StoneSelection selection = StoneShapes.FromSelection(doc, KnownSizes);
                List<CoreCurve> frame = ToolCurves(selection.Tools).Where(c => c.IsClosed).ToList();
                if (selection.Stones.Count == 0 || frame.Count == 0)
                {
                    SetStatus("motif.trim.select");
                    return;
                }

                List<PlacedStone> stones = selection.Stones.Select(s => new PlacedStone(s.Stone.Center, s.Stone.DiameterMm, false)).ToList();
                List<int> outside = FrameTrim.Outside(stones, frame, mode);
                if (outside.Count > 0)
                {
                    object[] doomed = outside.Select(i => (object)selection.Stones[i].Shape).ToArray();
                    doc.CreateShapeRangeFromArray(ref doomed).Delete();
                }

                SetStatus("motif.trim.done", outside.Count, stones.Count - outside.Count);
            });
        }
    }
}
