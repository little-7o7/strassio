#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using Corel.Interop.VGCore;
using Strassio.Core.Editing;
using Strassio.Core.Geometry;
using Strassio.Core.Stones;
using CoreCurve = Strassio.Core.Geometry.Curve;

namespace Strassio.Corel
{
    /// <summary>
    /// «Перекрасить по рисунку» (вкладка «Цвет»): каждая страза берёт цвет фигуры рисунка, в которую
    /// попал её центр, — камень ближайшего цвета из таблицы. Кто под какой фигурой и какой камень
    /// ближе по цвету, считает Strassio.Core.Editing.ArtworkColors; здесь — чтение фигур и перекраска.
    /// </summary>
    public partial class Docker
    {
        /// <summary>Цвет фигуры отличается от камня больше этого (ΔE) — пишем, что цвета в таблице нет.</summary>
        private const double ExactColorDelta = 6;

        private void ArtworkRecolor_Click(object sender, RoutedEventArgs e)
        {
            string fallbackSize = (SizeCombo.SelectedItem as SizeOption)?.Size.Name ?? string.Empty;
            RunInDocument("undo.artwork", doc =>
            {
                List<string> known = KnownSizes;
                var stones = new List<StoneShape>();
                var shapes = new List<Shape>();
                CollectArtwork(doc.SelectionRange, known, stones, shapes);
                if (stones.Count == 0)
                {
                    stones = StoneShapes.FromPage(doc.ActivePage, known);
                }

                Dictionary<int, int> order = StackOrder(doc.ActivePage, shapes);
                var regions = new List<ColorRegion>();
                int noFill = 0;
                foreach (Shape shape in shapes)
                {
                    ColorRegion? region = ReadColorRegion(shape, order);
                    if (region == null)
                    {
                        noFill++;
                    }
                    else
                    {
                        regions.Add(region);
                    }
                }

                if (regions.Count == 0)
                {
                    SetStatus("color.artwork.needShapes");
                    return;
                }

                if (stones.Count == 0)
                {
                    SetStatus("edit.noStones");
                    return;
                }

                int[] regionOf = ArtworkColors.RegionOf(stones.Select(s => s.Stone.Center).ToList(), regions);

                // Камень для каждой стразы: ближайший цвет среди цветов её размера.
                var byColor = new Dictionary<string, (StoneColor Color, List<StoneShape> Stones)>(StringComparer.OrdinalIgnoreCase);
                var replaced = new SortedSet<string>(StringComparer.Ordinal);
                int outside = 0;
                for (int i = 0; i < stones.Count; i++)
                {
                    if (regionOf[i] < 0)
                    {
                        outside++;
                        continue;
                    }

                    ColorRegion region = regions[regionOf[i]];
                    string sizeName = stones[i].Size.Length > 0 ? stones[i].Size : fallbackSize;
                    StoneColor? color = NearestStoneColor(sizeName, region.Red, region.Green, region.Blue);
                    if (color == null)
                    {
                        outside++;
                        continue;
                    }

                    string key = color.Name + "\n" + color.Rgb;
                    if (!byColor.TryGetValue(key, out var bucket))
                    {
                        bucket = (color, new List<StoneShape>());
                        byColor[key] = bucket;
                    }

                    bucket.Stones.Add(stones[i]);
                    if (color.TryGetRgb(out byte r, out byte g, out byte b) &&
                        ArtworkColors.Difference(region.Red, region.Green, region.Blue, r, g, b) > ExactColorDelta)
                    {
                        replaced.Add(Hex(region.Red, region.Green, region.Blue) + " → " + color.Name);
                    }
                }

                int recolored = 0;
                foreach ((StoneColor color, List<StoneShape> group) in byColor.Values)
                {
                    color.TryGetRgb(out byte r, out byte g, out byte b);
                    object[] range = group.Select(s => (object)s.Shape).ToArray();
                    doc.CreateShapeRangeFromArray(ref range).ApplyUniformFill(app!.CreateRGBColor(r, g, b));
                    foreach (StoneShape stone in group)
                    {
                        StoneShapes.Rename(stone.Shape, stone.Size.Length > 0 ? stone.Size : fallbackSize, color.Name);
                    }

                    recolored += group.Count;
                }

                // Дополнения к итогу собираются из своих текстов языка; при смене языка они останутся
                // на прежнем до следующего действия — это просто строка итога.
                string extra = string.Empty;
                if (replaced.Count > 0)
                {
                    extra += " " + Loc.Format("color.artwork.replaced", string.Join(", ", replaced.Take(4)) + (replaced.Count > 4 ? "…" : string.Empty));
                }

                if (noFill > 0)
                {
                    extra += " " + Loc.Format("color.artwork.noFill", noFill);
                }

                SetStatus("color.artwork.done", recolored, byColor.Count, outside, extra);
            });
        }

        /// <summary>
        /// Стразы и цветные фигуры из выделения. Группы просматриваются насквозь: в группе Strassio —
        /// стразы, в группе рисунка — фигуры.
        /// </summary>
        private static void CollectArtwork(ShapeRange selection, List<string> knownSizes, List<StoneShape> stones, List<Shape> shapes)
        {
            void Collect(Shape shape)
            {
                if (shape.Type == cdrShapeType.cdrGroupShape)
                {
                    Shapes children = shape.Shapes;
                    for (int i = 1; i <= children.Count; i++)
                    {
                        Collect(children[i]);
                    }

                    return;
                }

                StoneShape? stone = StoneShapes.Read(shape, knownSizes);
                if (stone != null)
                {
                    stones.Add(stone);
                    return;
                }

                shapes.Add(shape);
            }

            for (int i = 1; i <= selection.Count; i++)
            {
                Collect(selection[i]);
            }
        }

        /// <summary>
        /// Порядок фигур по высоте (больше — выше), по StaticID. В CorelDRAW Shapes[1] — самая верхняя,
        /// поэтому обходим страницу сверху вниз и останавливаемся, когда нашли все нужные. Группы,
        /// созданные Strassio, пропускаем целиком — фигур рисунка в них нет, а камней там тысячи.
        /// </summary>
        private static Dictionary<int, int> StackOrder(Page page, IReadOnlyCollection<Shape> wanted)
        {
            var need = new HashSet<int>(wanted.Select(s => s.StaticID));
            var order = new Dictionary<int, int>();
            int position = 0;

            bool Walk(Shapes shapes)
            {
                int count = shapes.Count;
                for (int i = 1; i <= count; i++)
                {
                    Shape shape = shapes[i];
                    int id = shape.StaticID;
                    if (need.Remove(id))
                    {
                        order[id] = -position;
                    }

                    position++;
                    if (need.Count == 0)
                    {
                        return true;
                    }

                    if (shape.Type == cdrShapeType.cdrGroupShape &&
                        !(shape.Name ?? string.Empty).StartsWith("Strassio", StringComparison.OrdinalIgnoreCase) &&
                        Walk(shape.Shapes))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (need.Count > 0)
            {
                Walk(page.Shapes);
            }

            return order;
        }

        /// <summary>Цветная область фигуры: её контуры и цвет сплошной заливки. null — заливки нет или она не сплошная.</summary>
        private static ColorRegion? ReadColorRegion(Shape shape, Dictionary<int, int> order)
        {
            if (!TryUniformFill(shape, out byte r, out byte g, out byte b))
            {
                return null;
            }

            global::Corel.Interop.VGCore.Curve? curve = GetCurveOf(shape);
            if (curve == null || curve.SubPaths.Count == 0)
            {
                return null;
            }

            List<CoreCurve> contours = ReadAllSubPaths(curve);
            var region = new ColorRegion(contours, r, g, b, order.TryGetValue(shape.StaticID, out int o) ? o : int.MinValue / 2);
            return region.IsEmpty ? null : region;
        }

        /// <summary>Цвет сплошной заливки фигуры в RGB. false — заливки нет или она не сплошная (градиент, узор).</summary>
        private static bool TryUniformFill(Shape shape, out byte red, out byte green, out byte blue)
        {
            red = green = blue = 0;
            try
            {
                Fill fill = shape.Fill;
                if (fill.Type != cdrFillType.cdrUniformFill)
                {
                    return false;
                }

                global::Corel.Interop.VGCore.Color color = fill.UniformColor;
                red = (byte)color.RGBRed;
                green = (byte)color.RGBGreen;
                blue = (byte)color.RGBBlue;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Ближайший цвет среди цветов этого размера; размера нет в таблице — среди всех цветов набора.</summary>
        private StoneColor? NearestStoneColor(string sizeName, byte red, byte green, byte blue)
        {
            List<StoneColor> palette = ActiveSet?.Sizes
                .FirstOrDefault(s => string.Equals(s.Name, sizeName, StringComparison.OrdinalIgnoreCase))?.Colors
                ?? new List<StoneColor>();
            if (palette.Count == 0)
            {
                palette = (ActiveSet?.Sizes ?? new List<StoneSize>())
                    .SelectMany(s => s.Colors)
                    .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();
            }

            int index = ArtworkColors.Nearest(red, green, blue, palette);
            return index < 0 ? null : palette[index];
        }

        private static string Hex(byte r, byte g, byte b) =>
            "#" + r.ToString("X2", CultureInfo.InvariantCulture) + g.ToString("X2", CultureInfo.InvariantCulture) + b.ToString("X2", CultureInfo.InvariantCulture);
    }
}
