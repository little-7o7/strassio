#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Corel.Interop.VGCore;
using Strassio.Core.Editing;
using Strassio.Core.Methods;
using Strassio.Core.Placement;
using Strassio.Core.Stones;
using CoreCurve = Strassio.Core.Geometry.Curve;

namespace Strassio.Corel
{
    /// <summary>
    /// Инструменты Этапа 4 (docs/SPEC.md, разделы 3.4, 7.2, 8): вернуть размер камням после
    /// масштабирования, заполнить дырки, обновить «живые» стразы, пипетка, смешение цветов, цвета
    /// камней в палитру документа. Расчёты — в Strassio.Core (HoleFinder, ColorMixer, MethodRunner).
    /// </summary>
    public partial class Docker
    {
        // ---- Правка: размер после масштабирования ---------------------------------------------

        /// <summary>
        /// «Масштабирование с сохранением размера камней» (раздел 8): дизайн растянули средствами
        /// CorelDRAW — круги стали не того диаметра. Каждому камню возвращается диаметр его размера из
        /// таблицы, центр остаётся на месте.
        /// </summary>
        private void RestoreSize_Click(object sender, RoutedEventArgs e)
        {
            Dictionary<string, double> table = SizeTable;
            RunInDocument("undo.restoreSize", doc =>
            {
                List<StoneShape> stones = StoneShapes.FromSelection(doc, KnownSizes).Stones;
                if (stones.Count == 0)
                {
                    SetStatus("edit.selectStones");
                    return;
                }

                int restored = 0;
                int unknown = 0;
                foreach (StoneShape stone in stones)
                {
                    if (!table.TryGetValue(stone.Size, out double d))
                    {
                        unknown++;
                        continue;
                    }

                    if (Math.Abs(stone.Stone.DiameterMm - d) > 0.001)
                    {
                        stone.Shape.SetSizeEx(stone.Stone.Center.X, stone.Stone.Center.Y, d, d);
                        restored++;
                    }
                }

                SetStatus("edit.sizeRestored", restored, unknown);
            });
        }

        // ---- Правка: дырки ---------------------------------------------------------------------

        private void FillHoles_Click(object sender, RoutedEventArgs e)
        {
            if (!(SizeCombo.SelectedItem is SizeOption size) || !(ColorList.SelectedItem is ColorOption color))
            {
                SetStatus("status.noStone");
                return;
            }

            string caption = Loc.Format("undo.holes", size.Size.Name, color.Name);
            RunInDocument(null, doc =>
            {
                StoneSelection selection = StoneShapes.FromSelection(doc, KnownSizes);
                List<StoneShape> stones = selection.Stones.Count > 0 ? selection.Stones : StoneShapes.FromPage(doc.ActivePage, KnownSizes);
                List<Strassio.Core.Geometry.Point2D> holes = HoleFinder.Find(stones.Select(s => s.Stone).ToList(), size.Size.DiameterMm, Params.GapMm);
                if (holes.Count == 0)
                {
                    SetStatus("edit.noHoles");
                    return;
                }

                color.Color.TryGetRgb(out byte r, out byte g, out byte b);
                doc.BeginCommandGroup(caption);
                try
                {
                    List<PlacedStone> placed = holes.Select(h => new PlacedStone(h, size.Size.DiameterMm, isCorner: false)).ToList();
                    CreateStoneGroup(doc, placed, size.Size, color.Name, r, g, b, Array.Empty<int>(), caption).CreateSelection();
                }
                finally
                {
                    doc.EndCommandGroup();
                }

                SetStatus("edit.holesFilled", holes.Count);
            });
        }

        // ---- Правка: живые стразы --------------------------------------------------------------

        /// <summary>
        /// Перестраивает выделенные группы Strassio по их рецепту: исходную линию или форму поправили —
        /// стразы встают по новой, тем же методом, камнем и параметрами. Всё — одной отменой.
        /// </summary>
        private void UpdateLive_Click(object sender, RoutedEventArgs e)
        {
            RunInDocument("undo.live", doc =>
            {
                var groups = new List<(Shape Group, LiveRecipe Recipe)>();
                ShapeRange selection = doc.SelectionRange;
                for (int i = 1; i <= selection.Count; i++)
                {
                    // Выделена сама группа Strassio — или одна страза внутри неё (Ctrl+щелчок).
                    Shape? group = selection[i];
                    LiveRecipe? recipe = LiveStones.Read(group);
                    if (recipe == null)
                    {
                        group = ParentOf(group);
                        recipe = group == null ? null : LiveStones.Read(group);
                    }

                    if (recipe != null && group != null && !groups.Any(g => g.Group.StaticID == group.StaticID))
                    {
                        groups.Add((group, recipe));
                    }
                }

                if (groups.Count == 0)
                {
                    SetStatus("live.none");
                    return;
                }

                int updated = 0;
                int missing = 0;
                foreach ((Shape group, LiveRecipe recipe) in groups)
                {
                    if (RebuildLive(doc, group, recipe))
                    {
                        updated++;
                    }
                    else
                    {
                        missing++;
                    }
                }

                SetStatus(missing == 0 ? "live.updated" : "live.partly", updated, missing);
            });
        }

        private static Shape? ParentOf(Shape shape)
        {
            try
            {
                return shape.ParentGroup;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private bool RebuildLive(Document doc, Shape oldGroup, LiveRecipe recipe)
        {
            if (!recipe.TryGetKind(out MethodKind kind))
            {
                return false;
            }

            var read = new List<List<CoreCurve>>();
            foreach (int id in recipe.Sources)
            {
                Shape? source = LiveStones.FindSource(doc, id);
                global::Corel.Interop.VGCore.Curve? curve = source == null ? null : GetCurveOf(source);
                if (curve == null || curve.SubPaths.Count == 0)
                {
                    return false;
                }

                read.Add(ReadAllSubPaths(curve));
            }

            MethodInfo info = MethodCatalog.Get(kind);
            List<CoreCurve> contours = info.NeedsGuide ? new List<CoreCurve> { MethodRunner.OuterContour(read[0]) } : read[0];
            if (info.Kind == MethodKind.F8)
            {
                contours = read[0];
            }

            List<CoreCurve>? guides = info.NeedsGuide && read.Count > 1 ? new List<CoreCurve> { MethodRunner.OuterContour(read[1]) } : null;

            // Камень: из таблицы по имени; нет в таблице — размер как у старых камней группы.
            StoneSize size = ActiveSet?.Sizes.FirstOrDefault(s => string.Equals(s.Name, recipe.Size, StringComparison.OrdinalIgnoreCase))
                ?? new StoneSize { Name = recipe.Size, DiameterMm = FirstStoneDiameter(oldGroup) };
            (byte r, byte g, byte b) = ColorOf(recipe, size, oldGroup);

            MethodResult result = MethodRunner.Run(kind, contours, size.DiameterMm, recipe.Parameters, SizeTable, guides);
            if (result.Stones.Count == 0)
            {
                return false;
            }

            Shape group = CreateStoneGroup(doc, result.Stones, size, recipe.Color, r, g, b, result.ConflictIndices, oldGroup.Name);
            LiveStones.Save(group, recipe);
            oldGroup.Delete();
            return true;
        }

        private static double FirstStoneDiameter(Shape group)
        {
            try
            {
                Shape first = group.Shapes[1];
                first.GetBoundingBox(out _, out _, out double w, out double h, false);
                return (w + h) / 2;
            }
            catch (Exception)
            {
                return 2.4;
            }
        }

        /// <summary>Цвет рецепта: из таблицы (у этого размера или у любого), иначе — заливка старых камней.</summary>
        private (byte R, byte G, byte B) ColorOf(LiveRecipe recipe, StoneSize size, Shape oldGroup)
        {
            StoneColor? color = size.Colors.FirstOrDefault(c => string.Equals(c.Name, recipe.Color, StringComparison.OrdinalIgnoreCase))
                ?? ActiveSet?.Sizes.SelectMany(s => s.Colors).FirstOrDefault(c => string.Equals(c.Name, recipe.Color, StringComparison.OrdinalIgnoreCase));
            if (color != null && color.TryGetRgb(out byte r, out byte g, out byte b))
            {
                return (r, g, b);
            }

            try
            {
                global::Corel.Interop.VGCore.Color fill = oldGroup.Shapes[1].Fill.UniformColor;
                return ((byte)fill.RGBRed, (byte)fill.RGBGreen, (byte)fill.RGBBlue);
            }
            catch (Exception)
            {
                return (128, 128, 128);
            }
        }

        // ---- Цвет: пипетка ---------------------------------------------------------------------

        /// <summary>Пипетка (раздел 3.4): взять размер и цвет с выделенной стразы и выбрать их вверху.</summary>
        private void Pipette_Click(object sender, RoutedEventArgs e)
        {
            RunInDocument(null, doc =>
            {
                StoneShape? stone = StoneShapes.FromSelection(doc, KnownSizes).Stones.FirstOrDefault();
                if (stone == null)
                {
                    SetStatus("edit.selectStones");
                    return;
                }

                SizeOption? size = (SizeCombo.ItemsSource as IEnumerable<SizeOption>)?
                    .FirstOrDefault(o => string.Equals(o.Size.Name, stone.Size, StringComparison.OrdinalIgnoreCase));
                if (size == null)
                {
                    SetStatus("color.pickUnknown", stone.Size, stone.Color);
                    return;
                }

                SizeCombo.SelectedItem = size;
                ColorOption? color = (ColorList.ItemsSource as IEnumerable<ColorOption>)?
                    .FirstOrDefault(o => string.Equals(o.Name, stone.Color, StringComparison.OrdinalIgnoreCase));
                if (color != null)
                {
                    ColorList.SelectedItem = color;
                }

                SetStatus(color != null ? "color.picked" : "color.pickUnknown", stone.Size, stone.Color);
            });
        }

        // ---- Цвет: смешение --------------------------------------------------------------------

        /// <summary>Списки цветов для смешения — цвета выбранного размера; доли по умолчанию 70 / 30 / 0.</summary>
        private void RebuildMixColors()
        {
            List<ColorOption> colors = (ColorList.ItemsSource as IEnumerable<ColorOption>)?.ToList() ?? new List<ColorOption>();
            ComboBox[] combos = { MixColor1, MixColor2, MixColor3 };
            for (int i = 0; i < combos.Length; i++)
            {
                string? previous = (combos[i].SelectedItem as ColorOption)?.Name;
                combos[i].ItemsSource = colors;
                combos[i].SelectedItem = colors.FirstOrDefault(c => c.Name == previous) ?? (i < colors.Count ? colors[i] : colors.FirstOrDefault());
            }

            var modes = new List<ChoiceOption>
            {
                new ChoiceOption("random", Loc["color.mix.random"]),
                new ChoiceOption("sequence", Loc["color.mix.sequence"]),
            };
            string? mode = (MixModeCombo.SelectedItem as ChoiceOption)?.Value;
            MixModeCombo.ItemsSource = modes;
            MixModeCombo.SelectedItem = modes.FirstOrDefault(m => m.Value == mode) ?? modes[0];
        }

        private void Mix_Click(object sender, RoutedEventArgs e)
        {
            var parts = new List<(ColorOption Color, int Value)>();
            ComboBox[] combos = { MixColor1, MixColor2, MixColor3 };
            TextBox[] values = { MixValue1, MixValue2, MixValue3 };
            for (int i = 0; i < combos.Length; i++)
            {
                if (!(combos[i].SelectedItem is ColorOption color))
                {
                    continue;
                }

                if (!int.TryParse(values[i].Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int value) || value < 0 || value > 1000)
                {
                    SetStatus("color.mix.badNumber");
                    values[i].Focus();
                    return;
                }

                if (value > 0)
                {
                    parts.Add((color, value));
                }
            }

            if (parts.Count < 2)
            {
                SetStatus("color.mix.needTwo");
                return;
            }

            bool sequence = (MixModeCombo.SelectedItem as ChoiceOption)?.Value == "sequence";
            string fallbackSize = (SizeCombo.SelectedItem as SizeOption)?.Size.Name ?? string.Empty;
            RunInDocument("undo.mix", doc =>
            {
                List<StoneShape> stones = StoneShapes.FromSelection(doc, KnownSizes).Stones;
                if (stones.Count == 0)
                {
                    SetStatus("edit.selectStones");
                    return;
                }

                int[] assignment = sequence
                    ? ColorMixer.Sequence(stones.Count, parts.Select(p => p.Value).ToList())
                    : ColorMixer.Random(stones.Count, parts.Select(p => (double)p.Value).ToList(), Environment.TickCount);

                for (int c = 0; c < parts.Count; c++)
                {
                    List<StoneShape> ofColor = stones.Where((s, i) => assignment[i] == c).ToList();
                    if (ofColor.Count == 0)
                    {
                        continue;
                    }

                    parts[c].Color.Color.TryGetRgb(out byte r, out byte g, out byte b);
                    object[] shapes = ofColor.Select(s => (object)s.Shape).ToArray();
                    doc.CreateShapeRangeFromArray(ref shapes).ApplyUniformFill(app!.CreateRGBColor(r, g, b));
                    foreach (StoneShape stone in ofColor)
                    {
                        StoneShapes.Rename(stone.Shape, stone.Size.Length > 0 ? stone.Size : fallbackSize, parts[c].Color.Name);
                    }
                }

                SetStatus("color.mixed", stones.Count, parts.Count);
            });
        }

        // ---- Цвет: палитра документа -----------------------------------------------------------

        /// <summary>Все цвета камней из таблицы — в палитру документа CorelDRAW (раздел 3.4), без повторов.</summary>
        private void Palette_Click(object sender, RoutedEventArgs e)
        {
            RunInDocument(null, doc =>
            {
                Palette palette = doc.Palette;
                int added = 0;
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (StoneColor stoneColor in ActiveSet?.Sizes.SelectMany(s => s.Colors) ?? Enumerable.Empty<StoneColor>())
                {
                    if (!seen.Add(stoneColor.Name) || !stoneColor.TryGetRgb(out byte r, out byte g, out byte b))
                    {
                        continue;
                    }

                    bool exists;
                    try
                    {
                        exists = palette.FindColor(stoneColor.Name) > 0;
                    }
                    catch (Exception)
                    {
                        exists = false;
                    }

                    if (exists)
                    {
                        continue;
                    }

                    global::Corel.Interop.VGCore.Color color = app!.CreateRGBColor(r, g, b);
                    color.SetName(stoneColor.Name);
                    palette.AddColor(color);
                    added++;
                }

                SetStatus("color.paletteAdded", added);
            });
        }
    }
}
