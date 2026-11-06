#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Corel.Interop.VGCore;
using Strassio.Core.Editing;
using Strassio.Core.Geometry;
using Strassio.Core.Methods;
using Strassio.Core.Placement;
using Strassio.Core.Stones;
using Color = System.Windows.Media.Color;
using CoreCurve = Strassio.Core.Geometry.Curve;

namespace Strassio.Corel
{
    /// <summary>
    /// «Весь дизайн за раз» и «учитывать готовые стразы»: «Создать» и «Предпросмотр» на нескольких
    /// выделенных фигурах сразу. Фигуры идут сверху вниз, нижние обходят верхние, новые стразы не
    /// налезают на уже лежащие (Strassio.Core.Methods.DesignRunner). С галочкой «Цвет — из фигуры»
    /// у каждой фигуры свой камень — ближайший к цвету её заливки. Каждая фигура — своя группа
    /// («живые» стразы помнят свою фигуру), всё вместе — одна отмена Ctrl+Z.
    /// </summary>
    public partial class Docker
    {
        /// <summary>Фигура дизайна: её контуры, StaticID (для «живых» страз) и камень, которым её выкладывать.</summary>
        private sealed class DesignPart
        {
            public DesignPart(List<CoreCurve> contours, int sourceId, StoneColor? ownColor)
            {
                Contours = contours;
                SourceId = sourceId;
                OwnColor = ownColor;
            }

            public List<CoreCurve> Contours { get; }

            public int SourceId { get; }

            /// <summary>Камень по цвету заливки фигуры; null — выбранный в докере.</summary>
            public StoneColor? OwnColor { get; }

            public StoneColor ColorOr(StoneColor chosen) => OwnColor ?? chosen;
        }

        /// <summary>Дизайн, прочитанный последним «Предпросмотром», — для живого пересчёта без CorelDRAW.</summary>
        private List<DesignPart>? previewDesign;
        private List<PlacedStone>? previewObstacles;

        private void AvoidExistingBox_Click(object sender, RoutedEventArgs e)
        {
            context.Settings.AvoidExisting = AvoidExistingBox.IsChecked == true;
            context.SaveSettingsQuietly();
            ResetPreviewSource();
        }

        private void ColorFromShapeBox_Click(object sender, RoutedEventArgs e)
        {
            context.Settings.ColorFromShape = ColorFromShapeBox.IsChecked == true;
            context.SaveSettingsQuietly();
            ResetPreviewSource();
        }

        /// <summary>Галочки меняют, что читается из документа, — прежнюю картинку живого предпросмотра убираем.</summary>
        private void ResetPreviewSource()
        {
            previewContours = null;
            previewGuides = null;
            previewDesign = null;
            previewObstacles = null;
            HidePreview();
        }

        /// <summary>
        /// Нужен ли путь «дизайн»: несколько фигур, учёт готовых страз или цвет из фигуры. Методам со
        /// второй линией (F7, F8) и обводке всего дизайна — всегда прежний путь: у них своя выборка.
        /// </summary>
        private bool UsesDesignPath(Document doc, MethodOption method)
        {
            if (method.Info.NeedsGuide || method.Info.UsesWholeSelection)
            {
                return false;
            }

            return context.Settings.AvoidExisting || context.Settings.ColorFromShape || doc.SelectionRange.Count > 1;
        }

        /// <summary>
        /// Выделенные фигуры дизайна сверху вниз (группы — насквозь; стразы и группы Strassio
        /// пропускаются). null — читать нечего, сообщение уже внизу докера. Единицы — мм.
        /// </summary>
        private List<DesignPart>? ReadDesign(Document doc, MethodOption method, StoneSize size, StoneColor color)
        {
            List<string> known = KnownSizes;
            ShapeRange selection = doc.SelectionRange;
            if (selection.Count == 0)
            {
                SetStatus("status.noSelection");
                return null;
            }

            // Одна фигура (или группа) без «цвета из фигуры» читается целиком, как раньше: у букв
            // в группе общая сетка заливки. Новое здесь только одно — обход готовых страз.
            if (selection.Count == 1 && !context.Settings.ColorFromShape)
            {
                Shape single = selection[1];
                global::Corel.Interop.VGCore.Curve? whole = GetCurveOf(single);
                if (whole == null || whole.SubPaths.Count == 0)
                {
                    SetStatus("status.cannotReadShape", single.Type);
                    return null;
                }

                List<CoreCurve> read = ReadAllSubPaths(whole);
                if (read.Count == 0)
                {
                    SetStatus("status.noContours");
                    return null;
                }

                if (method.Info.NeedsClosed && !MethodRunner.OuterContour(read).IsClosed)
                {
                    SetStatus("status.needClosed");
                    return null;
                }

                return new List<DesignPart> { new DesignPart(read, single.StaticID, null) };
            }

            var shapes = new List<Shape>();

            void Collect(Shape shape)
            {
                if (shape.Type == cdrShapeType.cdrGroupShape)
                {
                    if ((shape.Name ?? string.Empty).StartsWith("Strassio", StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    Shapes children = shape.Shapes;
                    for (int i = 1; i <= children.Count; i++)
                    {
                        Collect(children[i]);
                    }

                    return;
                }

                if (!StoneShapes.IsStone(shape, known))
                {
                    shapes.Add(shape);
                }
            }

            for (int i = 1; i <= selection.Count; i++)
            {
                Collect(selection[i]);
            }

            if (shapes.Count == 0)
            {
                SetStatus("status.noSelection");
                return null;
            }

            // Сверху вниз: верхняя фигура выкладывается целиком, нижние её обходят.
            Dictionary<int, int> order = StackOrder(doc.ActivePage, shapes);
            shapes = shapes.OrderByDescending(s => order.TryGetValue(s.StaticID, out int o) ? o : int.MinValue).ToList();

            var parts = new List<DesignPart>();
            foreach (Shape shape in shapes)
            {
                global::Corel.Interop.VGCore.Curve? curve = GetCurveOf(shape);
                List<CoreCurve> contours = curve == null || curve.SubPaths.Count == 0 ? new List<CoreCurve>() : ReadAllSubPaths(curve);
                if (contours.Count == 0)
                {
                    continue;
                }

                StoneColor? ownColor = null;
                if (context.Settings.ColorFromShape && TryUniformFill(shape, out byte r, out byte g, out byte b))
                {
                    ownColor = NearestStoneColor(size.Name, r, g, b);
                }

                parts.Add(new DesignPart(contours, shape.StaticID, ownColor));
            }

            if (parts.Count == 0)
            {
                SetStatus("status.noContours");
                return null;
            }

            if (method.Info.NeedsClosed && parts.All(p => !MethodRunner.OuterContour(p.Contours).IsClosed))
            {
                SetStatus("status.needClosed");
                return null;
            }

            return parts;
        }

        /// <summary>Уже лежащие стразы рядом с дизайном — препятствия; галочка выключена — пусто.</summary>
        private List<PlacedStone> ReadObstacles(Document doc, IReadOnlyList<DesignPart> parts, double diameterMm)
        {
            if (!context.Settings.AvoidExisting)
            {
                return new List<PlacedStone>();
            }

            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (CoreCurve contour in parts.SelectMany(part => part.Contours))
            {
                foreach (FlattenedPoint point in CurveFlattener.Flatten(contour, 0.1).Points)
                {
                    minX = Math.Min(minX, point.Position.X);
                    minY = Math.Min(minY, point.Position.Y);
                    maxX = Math.Max(maxX, point.Position.X);
                    maxY = Math.Max(maxY, point.Position.Y);
                }
            }

            // Запас: ряды «вокруг линии» и смещённые ряды выходят за саму линию.
            MethodParameters p = Params;
            double margin = 5 + (Math.Max(1, p.RowCount) + 1) * (diameterMm * 1.6 + p.RowGapMm) + p.OffsetMm;
            return StoneShapes.ObstaclesNear(doc.ActivePage, KnownSizes, minX - margin, minY - margin, maxX + margin, maxY + margin);
        }

        private DesignResult RunDesign(IReadOnlyList<DesignPart> parts, IReadOnlyList<PlacedStone> obstacles, MethodOption method, StoneSize size) =>
            DesignRunner.Run(
                method.Info.Kind,
                parts.Select(p => (IReadOnlyList<CoreCurve>)p.Contours).ToList(),
                size.DiameterMm,
                Params,
                SizeTable,
                obstacles);

        /// <summary>«Создать» для дизайна: каждая фигура — своя группа своим камнем, всё — одна отмена.</summary>
        private void CreateDesign(StoneSize size, StoneColor color, MethodOption method)
        {
            RunInDocument(null, doc =>
            {
                List<DesignPart>? parts = ReadDesign(doc, method, size, color);
                if (parts == null)
                {
                    return;
                }

                List<PlacedStone> obstacles = ReadObstacles(doc, parts, size.DiameterMm);
                DesignResult result = RunDesign(parts, obstacles, method, size);
                if (result.StoneCount == 0)
                {
                    SetStatus(result.Removed > 0 ? "status.allBlocked" : "status.noStones", result.Removed);
                    return;
                }

                int conflicts = 0;
                int groups = 0;
                doc.BeginCommandGroup(Loc.Format(method.Info.IsFill ? "undo.fill" : "undo.line", size.Name, color.Name));
                try
                {
                    for (int i = 0; i < parts.Count; i++)
                    {
                        MethodResult part = result.PerShape[i];
                        if (part.Stones.Count == 0)
                        {
                            continue;
                        }

                        StoneColor partColor = parts[i].ColorOr(color);
                        partColor.TryGetRgb(out byte red, out byte green, out byte blue);
                        string caption = Loc.Format(method.Info.IsFill ? "undo.fill" : "undo.line", size.Name, partColor.Name);
                        Shape group = CreateStoneGroup(doc, part.Stones, size, partColor.Name, red, green, blue, part.ConflictIndices, caption);
                        LiveStones.Save(group, new LiveRecipe
                        {
                            Method = method.Info.Kind.ToString().ToLowerInvariant(),
                            Size = size.Name,
                            Color = partColor.Name,
                            Sources = new List<int> { parts[i].SourceId },
                            Parameters = Params.Clone(),
                        });
                        conflicts += part.ConflictIndices.Count;
                        groups++;
                    }
                }
                finally
                {
                    doc.EndCommandGroup();
                }

                RememberRecent(method, size.Name, color.Name);
                SetStatus(DesignStatusKey(result, conflicts, preview: false), result.StoneCount, groups, result.Removed + conflicts, result.SkippedShapes);
            });
        }

        /// <summary>
        /// «Предпросмотр» для дизайна: фигуры и готовые стразы читаются один раз, дальше картинка
        /// считается без CorelDRAW. Документ не меняется, поэтому лицензия здесь не нужна — как и
        /// у обычного предпросмотра.
        /// </summary>
        private void PreviewDesign(Document doc, StoneSize size, StoneColor color, MethodOption method)
        {
            cdrUnit prevUnit = doc.Unit;
            try
            {
                doc.Unit = cdrUnit.cdrMillimeter;
                List<DesignPart>? parts = ReadDesign(doc, method, size, color);
                if (parts == null)
                {
                    HidePreview();
                    return;
                }

                previewContours = null;
                previewGuides = null;
                previewDesign = parts;
                previewObstacles = ReadObstacles(doc, parts, size.DiameterMm);
                ClearVariants();
            }
            catch (Exception ex)
            {
                SetStatus("status.error", ex.Message);
                return;
            }
            finally
            {
                doc.Unit = prevUnit;
            }

            ShowDesignPreview();
        }

        /// <summary>Пересчитывает и рисует дизайн из прочитанного «Предпросмотром» (живой предпросмотр зовёт тоже).</summary>
        private void ShowDesignPreview()
        {
            if (previewDesign == null || !(SizeCombo.SelectedItem is SizeOption size) ||
                !(ColorList.SelectedItem is ColorOption color) || !(MethodCombo.SelectedItem is MethodOption method))
            {
                return;
            }

            if (method.Info.NeedsGuide || method.Info.UsesWholeSelection)
            {
                HidePreview();
                return;
            }

            DesignResult result;
            try
            {
                result = RunDesign(previewDesign, previewObstacles ?? new List<PlacedStone>(), method, size.Size);
            }
            catch (Exception ex)
            {
                SetStatus("status.error", ex.Message);
                return;
            }

            if (result.StoneCount == 0)
            {
                HidePreview();
                SetStatus(result.Removed > 0 ? "status.allBlocked" : "status.noStones", result.Removed);
                return;
            }

            PreviewImage.Source = BuildDesignPreview(previewDesign, result, previewObstacles, color.Color);
            PreviewPanel.Visibility = Visibility.Visible;
            lastPreviewCount = result.StoneCount;
            int conflicts = result.PerShape.Sum(r => r.ConflictIndices.Count);
            SetStatus(DesignStatusKey(result, conflicts, preview: true), result.StoneCount, previewDesign.Count, result.Removed + conflicts, result.SkippedShapes);
        }

        private static string DesignStatusKey(DesignResult result, int conflicts, bool preview)
        {
            string prefix = preview ? "status.designPreview" : "status.design";
            if (conflicts > 0)
            {
                return prefix + "Conflicts";
            }

            return result.Removed > 0 ? prefix + "Removed" : prefix;
        }

        /// <summary>Картинка дизайна: контуры всех фигур, стразы каждой — своим цветом, готовые — бледно.</summary>
        private static DrawingImage BuildDesignPreview(
            IReadOnlyList<DesignPart> parts, DesignResult result, IReadOnlyList<PlacedStone>? obstacles, StoneColor chosen)
        {
            var group = new DrawingGroup();
            double maxDiameter = result.PerShape.SelectMany(r => r.Stones).Max(s => s.DiameterMm);

            var outlinePen = new Pen(new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)), maxDiameter / 12);
            outlinePen.Freeze();
            foreach (CoreCurve contour in parts.SelectMany(part => part.Contours))
            {
                group.Children.Add(new GeometryDrawing(null, outlinePen, Polyline(contour)));
            }

            if (obstacles != null && obstacles.Count > 0)
            {
                var ghost = new SolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x80));
                ghost.Freeze();
                group.Children.Add(new GeometryDrawing(ghost, null, Circles(obstacles, i => true)));
            }

            var stonePen = new Pen(new SolidColorBrush(Color.FromArgb(0x99, 0x33, 0x33, 0x33)), maxDiameter / 20);
            stonePen.Freeze();
            var conflictPen = new Pen(new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)), maxDiameter / 6);
            conflictPen.Freeze();

            for (int i = 0; i < parts.Count; i++)
            {
                MethodResult part = result.PerShape[i];
                if (part.Stones.Count == 0)
                {
                    continue;
                }

                parts[i].ColorOr(chosen).TryGetRgb(out byte r, out byte g, out byte b);
                var fill = new SolidColorBrush(Color.FromRgb(r, g, b));
                fill.Freeze();
                var conflicts = new HashSet<int>(part.ConflictIndices);
                group.Children.Add(new GeometryDrawing(fill, stonePen, Circles(part.Stones, k => !conflicts.Contains(k))));
                if (conflicts.Count > 0)
                {
                    group.Children.Add(new GeometryDrawing(fill, conflictPen, Circles(part.Stones, conflicts.Contains)));
                }
            }

            group.Freeze();
            var image = new DrawingImage(group);
            image.Freeze();
            return image;
        }
    }
}
