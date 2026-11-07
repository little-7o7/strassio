#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Corel.Interop.VGCore;
using Strassio.Core.Editing;
using Strassio.Core.Geometry;
using CoreCurve = Strassio.Core.Geometry.Curve;

namespace Strassio.Corel
{
    /// <summary>
    /// Симметрия (docs/SPEC.md, раздел 8; пункт 8 прокачки): выкладываете половину или четверть, плагин
    /// зеркалит выделенные стразы. Копии — дубликаты исходных страз (тот же размер, цвет, имя, группа).
    /// Камни на оси не двоятся.
    /// </summary>
    public partial class Docker
    {
        private void RebuildMirrorSides()
        {
            var sides = new List<ChoiceOption>
            {
                new ChoiceOption("right", Loc["edit.mirror.right"]),
                new ChoiceOption("left", Loc["edit.mirror.left"]),
                new ChoiceOption("down", Loc["edit.mirror.down"]),
                new ChoiceOption("up", Loc["edit.mirror.up"]),
                new ChoiceOption("quarter", Loc["edit.mirror.quarter"]),
                new ChoiceOption("line", Loc["edit.mirror.line"]),
            };
            string? side = (MirrorSideCombo.SelectedItem as ChoiceOption)?.Value;
            MirrorSideCombo.ItemsSource = sides;
            MirrorSideCombo.SelectedItem = sides.FirstOrDefault(s => s.Value == side) ?? sides[0];
        }

        private void Mirror_Click(object sender, RoutedEventArgs e)
        {
            string side = (MirrorSideCombo.SelectedItem as ChoiceOption)?.Value ?? "right";
            double minGap = Params.GapMm;
            RunInDocument("undo.mirror", doc =>
            {
                StoneSelection selection = StoneShapes.FromSelection(doc, KnownSizes);
                List<StoneShape> stones = selection.Stones;
                if (stones.Count == 0)
                {
                    SetStatus("edit.selectStones");
                    return;
                }

                List<DocStone> docStones = stones.Select(s => s.Stone).ToList();
                MirrorResult result;
                switch (side)
                {
                    case "line":
                        (Point2D A, Point2D B)? axis = StraightLine(selection.Tools);
                        if (axis == null)
                        {
                            SetStatus("edit.mirror.needLine");
                            return;
                        }

                        result = Symmetry.Mirror(docStones, axis.Value.A, axis.Value.B - axis.Value.A, minGap);
                        break;

                    case "quarter":
                        result = Symmetry.Quarter(docStones, minGap);
                        break;

                    default:
                        MirrorSide mirrorSide = side == "left" ? MirrorSide.Left : side == "down" ? MirrorSide.Down : side == "up" ? MirrorSide.Up : MirrorSide.Right;
                        (Point2D point, Point2D direction) = Symmetry.AxisAt(docStones, mirrorSide);
                        result = Symmetry.Mirror(docStones, point, direction, minGap);
                        break;
                }

                // Сначала стразы у оси — на ось, потом копии: дубликат исходной стразы, сдвинутый на место копии.
                Point2D[] now = docStones.Select(s => s.Center).ToArray();
                foreach (StoneMove move in result.Snapped)
                {
                    stones[move.Index].Shape.Move(move.NewCenter.X - now[move.Index].X, move.NewCenter.Y - now[move.Index].Y);
                    now[move.Index] = move.NewCenter;
                }

                var all = new List<object>(stones.Select(s => (object)s.Shape));
                foreach (MirroredStone copy in result.Added)
                {
                    Point2D from = now[copy.Source];
                    all.Add(stones[copy.Source].Shape.Duplicate(copy.Center.X - from.X, copy.Center.Y - from.Y));
                }

                object[] shapes = all.ToArray();
                doc.ClearSelection();
                doc.CreateShapeRangeFromArray(ref shapes).CreateSelection();
                SetStatus("edit.mirrored", result.Added.Count, result.Snapped.Count, result.Skipped);
            });
        }

        /// <summary>Первая выделенная прямая (линия, у которой все точки почти на отрезке между концами).</summary>
        private static (Point2D A, Point2D B)? StraightLine(IEnumerable<Shape> tools)
        {
            foreach (CoreCurve curve in ToolCurves(tools))
            {
                IReadOnlyList<FlattenedPoint> points = CurveFlattener.Flatten(curve).Points;
                if (points.Count < 2)
                {
                    continue;
                }

                Point2D a = points[0].Position;
                Point2D b = points[points.Count - 1].Position;
                double length = Point2D.Distance(a, b);
                if (length < 1)
                {
                    continue;
                }

                Point2D u = (b - a) / length;
                bool straight = points.All(p => Math.Abs((p.Position - a).X * u.Y - (p.Position - a).Y * u.X) < 0.3);
                if (straight)
                {
                    return (a, b);
                }
            }

            return null;
        }
    }
}
