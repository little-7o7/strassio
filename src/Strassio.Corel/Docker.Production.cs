#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using Corel.Interop.VGCore;
using Strassio.Core.Editing;
using Strassio.Core.Settings;
using Strassio.Core.Stones;

namespace Strassio.Corel
{
    /// <summary>
    /// Подготовка к производству (docs/SPEC.md, раздел 11): проверка перемычек, раскладка по слоям по
    /// размерам (удобно для ArtCAM), «чистые» круги — без заливки, с тонкой обводкой, без групп Strassio.
    /// </summary>
    public partial class Docker
    {
        /// <summary>Сколько материала между отверстиями считается достаточным, мм (поле в докере; по умолчанию 0,3 мм).</summary>
        private double bridgeMm = 0.3;

        private double BridgeMinMm
        {
            get
            {
                if (LengthUnits.TryParse(BridgeBox.Text, context.Settings.Units, out double mm) && mm >= 0 && mm < 10)
                {
                    bridgeMm = mm;
                }

                return bridgeMm;
            }
        }

        /// <summary>Число запоминается при вводе — в тех единицах, в которых его набрали.</summary>
        private void BridgeBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => _ = BridgeMinMm;

        /// <summary>Поле перемычки — в текущих единицах (при смене мм/дюймов пересчитывается).</summary>
        private void RefreshBridgeBox() =>
            BridgeBox.Text = LengthUnits.Format(bridgeMm, context.Settings.Units, Math.Max(2, context.Settings.Decimals), CultureInfo.CurrentCulture);

        /// <summary>Проверка перемычек: выделяет стразы, между которыми тоньше заданного.</summary>
        private void CheckBridges_Click(object sender, RoutedEventArgs e)
        {
            double min = BridgeMinMm;
            RunInDocument(null, doc =>
            {
                StoneSelection selection = StoneShapes.FromSelection(doc, KnownSizes);
                List<StoneShape> stones = selection.Stones.Count > 0 ? selection.Stones : StoneShapes.FromPage(doc.ActivePage, KnownSizes);
                List<ThinBridge> bridges = BridgeChecker.Find(stones.Select(s => s.Stone).ToList(), min);

                doc.ClearSelection();
                if (bridges.Count == 0)
                {
                    SetStatus("prod.bridgesOk", Format(min));
                    return;
                }

                object[] bad = bridges.SelectMany(b => new[] { b.First, b.Second }).Distinct()
                    .Select(i => (object)stones[i].Shape).ToArray();
                doc.CreateShapeRangeFromArray(ref bad).CreateSelection();
                SetStatus("prod.bridgesBad", bridges.Count, Format(Math.Max(0, bridges[0].GapMm)), bad.Length);
            });
        }

        /// <summary>
        /// Сколько камней каждого размера и цвета — для заказа (только количество, без цен). Таблица
        /// сразу в буфере обмена: Ctrl+V в Excel или Google Таблицах.
        /// </summary>
        private void CountStones_Click(object sender, RoutedEventArgs e)
        {
            StoneCount? count = null;
            RunInDocument(null, doc =>
            {
                StoneSelection selection = StoneShapes.FromSelection(doc, KnownSizes);
                List<StoneShape> stones = selection.Stones.Count > 0 ? selection.Stones : StoneShapes.FromPage(doc.ActivePage, KnownSizes);
                if (stones.Count == 0)
                {
                    SetStatus("edit.noStones");
                    return;
                }

                count = StoneCount.Of(stones.Select(s => (s.Size, s.Color)), KnownSizes);
            });

            if (count == null)
            {
                return;
            }

            bool copied = true;
            try
            {
                Clipboard.SetText(count.ToTable(Loc["prod.count.size"], Loc["prod.count.color"], Loc["prod.count.pieces"], Loc["prod.count.total"]));
            }
            catch (Exception)
            {
                // Буфер обмена занят другой программой — таблица всё равно видна в окне.
                copied = false;
            }

            SetStatus(copied ? "prod.countDone" : "prod.countDoneNoCopy", count.Total, count.BySize.Count, count.ColorCount);
            MessageBox.Show(CountText(count, copied), Loc["prod.count.title"], MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>Для окна: размер, под ним цвета с числом камней, итог по размеру; внизу — всего.</summary>
        private string CountText(StoneCount count, bool copied)
        {
            var text = new System.Text.StringBuilder();
            CultureInfo culture = CultureInfo.CurrentCulture;
            foreach (KeyValuePair<string, int> size in count.BySize)
            {
                text.Append(size.Key).Append(": ").Append(size.Value.ToString("N0", culture)).Append('\n');
                foreach (StoneCountRow row in count.Rows.Where(r => string.Equals(r.Size, size.Key, StringComparison.OrdinalIgnoreCase)))
                {
                    text.Append("    ").Append(row.Color).Append(" — ").Append(row.Count.ToString("N0", culture)).Append('\n');
                }
            }

            text.Append('\n').Append(Loc.Format("prod.count.totalLine", count.Total.ToString("N0", culture)));
            if (copied)
            {
                text.Append("\n\n").Append(Loc["prod.count.copied"]);
            }

            return text.ToString();
        }

        /// <summary>Каждый размер — на свой слой с названием размера («ss6», «ss10»…).</summary>
        private void LayersBySize_Click(object sender, RoutedEventArgs e) => SplitToLayers(byColor: false);

        /// <summary>
        /// Каждый размер каждого цвета — на свой слой («ss6 Розовый»): у многоцветного дизайна на каждый цвет
        /// свой трафарет, камни насыпаются по одному цвету за раз.
        /// </summary>
        private void LayersBySizeColor_Click(object sender, RoutedEventArgs e) => SplitToLayers(byColor: true);

        private void SplitToLayers(bool byColor)
        {
            RunInDocument("undo.layers", doc =>
            {
                StoneSelection selection = StoneShapes.FromSelection(doc, KnownSizes);
                List<StoneShape> stones = selection.Stones.Count > 0 ? selection.Stones : StoneShapes.FromPage(doc.ActivePage, KnownSizes);
                if (stones.Count == 0)
                {
                    SetStatus("edit.noStones");
                    return;
                }

                Page page = doc.ActivePage;
                int layers = 0;
                string KeyOf(StoneShape s)
                {
                    string size = s.Size.Length > 0 ? s.Size : "?";
                    return byColor && s.Color.Length > 0 ? StoneNames.Compose(size, s.Color) : size;
                }

                foreach (IGrouping<string, StoneShape> bySize in stones.GroupBy(KeyOf, StringComparer.OrdinalIgnoreCase))
                {
                    string name = Loc.Format("prod.layerName", bySize.Key);
                    Layer? layer = FindLayer(page, name);
                    if (layer == null)
                    {
                        layer = page.CreateLayer(name);
                        layers++;
                    }

                    object[] shapes = bySize.Select(s => (object)s.Shape).ToArray();
                    doc.CreateShapeRangeFromArray(ref shapes).MoveToLayer(layer);
                }

                if (byColor)
                {
                    SetStatus("prod.layersColorDone", stones.Count, stones.Select(KeyOf).Distinct(StringComparer.OrdinalIgnoreCase).Count(), layers);
                }
                else
                {
                    SetStatus("prod.layersDone", stones.Count, stones.Select(s => s.Size).Distinct(StringComparer.OrdinalIgnoreCase).Count(), layers);
                }
            });
        }

        private static Layer? FindLayer(Page page, string name)
        {
            try
            {
                return page.Layers.Find(name);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>«Чистые» круги (раздел 11, [P1]): без заливки, с волосяной обводкой, группы Strassio разгруппированы.</summary>
        private void CleanCircles_Click(object sender, RoutedEventArgs e)
        {
            RunInDocument("undo.clean", doc =>
            {
                StoneSelection selection = StoneShapes.FromSelection(doc, KnownSizes);
                List<StoneShape> stones = selection.Stones.Count > 0 ? selection.Stones : StoneShapes.FromPage(doc.ActivePage, KnownSizes);
                if (stones.Count == 0)
                {
                    SetStatus("edit.noStones");
                    return;
                }

                // Сначала разгруппировать группы Strassio (только их — свои группы пользователя не трогаем).
                var groups = new Dictionary<int, Shape>();
                foreach (StoneShape stone in stones)
                {
                    Shape? parent = ParentOf(stone.Shape);
                    if (parent != null && (LiveStones.Read(parent) != null || (parent.Name ?? string.Empty).StartsWith("Strassio", StringComparison.Ordinal)))
                    {
                        groups[parent.StaticID] = parent;
                    }
                }

                foreach (Shape group in groups.Values)
                {
                    group.Ungroup();
                }

                object[] shapes = stones.Select(s => (object)s.Shape).ToArray();
                ShapeRange range = doc.CreateShapeRangeFromArray(ref shapes);
                range.ApplyNoFill();
                range.SetOutlineProperties(Width: HairlineMm, Color: app!.CreateRGBColor(0, 0, 0));
                SetStatus("prod.cleanDone", stones.Count, groups.Count);
            });
        }

        private string Format(double mm) =>
            LengthUnits.Format(mm, context.Settings.Units, context.Settings.Decimals, CultureInfo.CurrentCulture) + " " +
            Loc[context.Settings.Units == LengthUnit.Inch ? "unit.in" : "unit.mm"];
    }
}
