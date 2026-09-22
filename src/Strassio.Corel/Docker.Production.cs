#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using Corel.Interop.VGCore;
using Strassio.Core.Editing;
using Strassio.Core.Settings;

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

        /// <summary>Каждый размер — на свой слой с названием размера («ss6», «ss10»…).</summary>
        private void LayersBySize_Click(object sender, RoutedEventArgs e)
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
                foreach (IGrouping<string, StoneShape> bySize in stones.GroupBy(s => s.Size.Length > 0 ? s.Size : "?", StringComparer.OrdinalIgnoreCase))
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

                SetStatus("prod.layersDone", stones.Count, stones.Select(s => s.Size).Distinct(StringComparer.OrdinalIgnoreCase).Count(), layers);
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
