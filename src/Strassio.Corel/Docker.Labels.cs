#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using Corel.Interop.VGCore;
using Strassio.Core.Geometry;
using Strassio.Core.Stones;

namespace Strassio.Corel
{
    /// <summary>
    /// Подписи размеров на шаблоне (вкладка «Правка», раздел «Производство»): под дизайном появляется текст —
    /// ширина × высота по краям камней и сколько камней каждого размера и цвета. «Подписать каждый слой» делает
    /// то же для каждого слоя отдельно (после «Разложить по слоям: размер и цвет» — на каждом трафарете своя подпись).
    /// Подпись — обычный текст CorelDRAW: его можно двигать, править или удалить.
    /// </summary>
    public partial class Docker
    {
        private const double LabelGapMm = 3;
        private const float LabelFontPt = 10f;

        private void LabelTemplate_Click(object sender, RoutedEventArgs e)
        {
            RunInDocument("undo.label", doc =>
            {
                StoneSelection selection = StoneShapes.FromSelection(doc, KnownSizes);
                List<StoneShape> stones = selection.Stones.Count > 0 ? selection.Stones : StoneShapes.FromPage(doc.ActivePage, KnownSizes);
                TemplateLabel? label = LabelOf(stones);
                if (label == null)
                {
                    SetStatus("edit.noStones");
                    return;
                }

                Shape text = CreateLabel(doc.ActiveLayer, label, null);
                doc.ClearSelection();
                text.CreateSelection();
                SetStatus("prod.label.done", Format(label.Width), Format(label.Height), label.Count.Total.ToString("N0", CultureInfo.CurrentCulture));
            });
        }

        private void LabelLayers_Click(object sender, RoutedEventArgs e)
        {
            RunInDocument("undo.label", doc =>
            {
                Page page = doc.ActivePage;
                List<string> known = KnownSizes;
                int made = 0;
                for (int i = 1; i <= page.Layers.Count; i++)
                {
                    Layer layer = page.Layers[i];
                    TemplateLabel? label = LabelOf(StoneShapes.FromLayer(layer, known));
                    if (label != null)
                    {
                        CreateLabel(layer, label, layer.Name);
                        made++;
                    }
                }

                if (made == 0)
                {
                    SetStatus("edit.noStones");
                }
                else
                {
                    SetStatus("prod.label.layersDone", made);
                }
            });
        }

        private TemplateLabel? LabelOf(IEnumerable<StoneShape> stones) =>
            TemplateLabel.Of(stones.Select(s => new LabelStone(s.Stone.Center, s.Stone.DiameterMm, s.Size, s.Color)).ToList(), KnownSizes);

        /// <summary>Текст подписи под дизайном, по его левому краю.</summary>
        private Shape CreateLabel(Layer layer, TemplateLabel label, string? title)
        {
            Shape shape = layer.CreateArtisticText(0, 0, LabelText(label, title), Size: LabelFontPt);
            shape.GetBoundingBox(out double x, out double y, out double w, out double h, false);
            Point2D anchor = label.Anchor(LabelGapMm);
            shape.Move(anchor.X - x, anchor.Y - (y + h));
            shape.Name = Loc["prod.label.name"];
            return shape;
        }

        /// <summary>Строки: (слой), размер дизайна, по строке на размер камня с цветами, всего.</summary>
        private string LabelText(TemplateLabel label, string? title)
        {
            CultureInfo culture = CultureInfo.CurrentCulture;
            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(title))
            {
                lines.Add(title!);
            }

            lines.Add(Loc.Format("prod.label.size", Format(label.Width), Format(label.Height)));
            foreach (KeyValuePair<string, int> size in label.Count.BySize)
            {
                string colors = string.Join(", ", label.Count.Rows
                    .Where(r => string.Equals(r.Size, size.Key, StringComparison.OrdinalIgnoreCase))
                    .Select(r => r.Color + " " + r.Count.ToString("N0", culture)));
                string diameter = label.Diameters.TryGetValue(size.Key, out double mm) ? Format(mm) : StoneCount.Unknown;
                lines.Add(Loc.Format("prod.label.row", size.Key, diameter, size.Value.ToString("N0", culture), colors));
            }

            lines.Add(Loc.Format("prod.count.totalLine", label.Count.Total.ToString("N0", culture)));
            return string.Join("\r", lines);
        }
    }
}
