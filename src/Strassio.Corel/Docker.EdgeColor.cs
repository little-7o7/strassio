#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using Strassio.Core.Editing;

namespace Strassio.Corel
{
    /// <summary>
    /// «Цвет крайних рядов» (пункт 6 прокачки): у выделенных страз крайние ряды — у внешнего края и у больших
    /// отверстий — перекрашиваются в выбранный цвет. Золотой кант и кристалл внутри — одной кнопкой, у любой
    /// раскладки: кант, комбинированная, «вокруг линии», соты, камни руками.
    /// </summary>
    public partial class Docker
    {
        private void EdgeRecolor_Click(object sender, RoutedEventArgs e)
        {
            if (!(ColorList.SelectedItem is ColorOption color))
            {
                SetStatus("status.noStone");
                return;
            }

            if (!int.TryParse(EdgeRowsBox.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int rows) || rows < 1 || rows > 10)
            {
                SetStatus("color.edge.badRows");
                EdgeRowsBox.Focus();
                return;
            }

            string fallbackSize = (SizeCombo.SelectedItem as SizeOption)?.Size.Name ?? string.Empty;
            RunInDocument("undo.edgeColor", doc =>
            {
                List<StoneShape> stones = StoneShapes.FromSelection(doc, KnownSizes).Stones;
                if (stones.Count == 0)
                {
                    SetStatus("edit.selectStones");
                    return;
                }

                int[] row = EdgeRows.Find(
                    stones.Select(s => s.Stone.Center).ToList(), stones.Select(s => s.Stone.DiameterMm).ToList(), rows);
                List<StoneShape> edge = stones.Where((s, i) => row[i] > 0).ToList();
                if (edge.Count == 0)
                {
                    SetStatus("color.edge.none");
                    return;
                }

                color.Color.TryGetRgb(out byte r, out byte g, out byte b);
                object[] shapes = edge.Select(s => (object)s.Shape).ToArray();
                doc.CreateShapeRangeFromArray(ref shapes).ApplyUniformFill(app!.CreateRGBColor(r, g, b));
                foreach (StoneShape stone in edge)
                {
                    StoneShapes.Rename(stone.Shape, stone.Size.Length > 0 ? stone.Size : fallbackSize, color.Name);
                }

                SetStatus("color.edgeDone", edge.Count, color.Name, row.Where(x => x > 0).Max());
            });
        }
    }
}
