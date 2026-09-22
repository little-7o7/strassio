#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using Corel.Interop.VGCore;
using Strassio.Core.Editing;
using Strassio.Core.Geometry;
using Strassio.Core.Settings;
using Strassio.Core.Vector;
using CoreCurve = Strassio.Core.Geometry.Curve;

namespace Strassio.Corel
{
    /// <summary>
    /// Вкладка «Векторы» (docs/SPEC.md, раздел 10): подготовка линий и фигур — смещение, параллельные
    /// линии, разрезать на части, соединить концы, замкнуть, разрывы, направление, сглаживание,
    /// центральная линия, проверка. Считает Strassio.Core.Vector; здесь — чтение и создание кривых.
    /// </summary>
    public partial class Docker
    {
        /// <summary>Выделенная фигура (не страза) и её контуры.</summary>
        private sealed class SourceShape
        {
            public SourceShape(Shape shape, List<CoreCurve> contours)
            {
                Shape = shape;
                Contours = contours;
            }

            public Shape Shape { get; }

            public List<CoreCurve> Contours { get; }
        }

        private List<SourceShape> SelectedCurves(Document doc)
        {
            var result = new List<SourceShape>();
            List<string> known = KnownSizes;
            ShapeRange selection = doc.SelectionRange;
            for (int i = 1; i <= selection.Count; i++)
            {
                Shape shape = selection[i];
                if (shape.Type == cdrShapeType.cdrGroupShape || StoneShapes.IsStone(shape, known))
                {
                    continue;
                }

                global::Corel.Interop.VGCore.Curve? curve = GetCurveOf(shape);
                if (curve != null && curve.SubPaths.Count > 0)
                {
                    List<CoreCurve> contours = ReadAllSubPaths(curve);
                    if (contours.Count > 0)
                    {
                        result.Add(new SourceShape(shape, contours));
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Поля длин вкладки «Векторы»: значение хранится в мм (запоминается при вводе — в тех единицах,
        /// в которых набрано) и показывается в текущих единицах; при смене мм/дюймов пересчитывается.
        /// </summary>
        private Dictionary<System.Windows.Controls.TextBox, double>? vectorLengths;

        private void RefreshVectorBoxes()
        {
            if (vectorLengths == null)
            {
                vectorLengths = new Dictionary<System.Windows.Controls.TextBox, double>
                {
                    [OffsetBox] = 2, [ParallelStepBox] = 2.6, [JoinToleranceBox] = 0.5, [SmoothBox] = 0.1,
                };
                foreach (System.Windows.Controls.TextBox box in vectorLengths.Keys.ToList())
                {
                    box.TextChanged += (s, e) =>
                    {
                        if (LengthUnits.TryParse(box.Text, context.Settings.Units, out double mm) && mm >= 0)
                        {
                            vectorLengths[box] = mm;
                        }
                    };
                }
            }

            foreach (KeyValuePair<System.Windows.Controls.TextBox, double> pair in vectorLengths.ToList())
            {
                pair.Key.Text = LengthUnits.Format(pair.Value, context.Settings.Units, Math.Max(2, context.Settings.Decimals), CultureInfo.CurrentCulture);
            }
        }

        /// <summary>Длина из поля, мм; не число — последнее верное значение или <paramref name="fallbackMm"/>.</summary>
        private double LengthFrom(System.Windows.Controls.TextBox box, double fallbackMm) =>
            vectorLengths != null && vectorLengths.TryGetValue(box, out double mm) ? mm : fallbackMm;

        private static int CountFrom(System.Windows.Controls.TextBox box, int fallback, int max) =>
            int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int n) && n >= 1 && n <= max ? n : fallback;

        /// <summary>Кривые Strassio.Core → одна фигура-кривая CorelDRAW (узлы и контрольные точки как есть).</summary>
        private Shape CreateCurveShape(Document doc, IEnumerable<CoreCurve> curves, Shape? styleFrom)
        {
            global::Corel.Interop.VGCore.Curve curve = app!.CreateCurve(doc);
            foreach (CoreCurve c in curves)
            {
                IReadOnlyList<CurveSegment> segs = c.Segments;
                SubPath sp = curve.CreateSubPath(segs[0].Start.X, segs[0].Start.Y);
                for (int i = 0; i < segs.Count; i++)
                {
                    CurveSegment s = segs[i];
                    bool closingLine = c.IsClosed && i == segs.Count - 1 && s.IsLine && Point2D.Distance(s.End, segs[0].Start) < 1e-6;
                    if (closingLine)
                    {
                        break; // замыкающий отрезок CorelDRAW добавит сам
                    }

                    if (s.IsLine)
                    {
                        sp.AppendLineSegment(s.End.X, s.End.Y);
                    }
                    else
                    {
                        sp.AppendCurveSegment2(s.End.X, s.End.Y, s.Control1!.Value.X, s.Control1.Value.Y, s.Control2!.Value.X, s.Control2.Value.Y);
                    }
                }

                if (c.IsClosed)
                {
                    sp.Closed = true;
                }
            }

            Shape shape = doc.ActiveLayer.CreateCurve(curve);
            if (styleFrom != null)
            {
                try
                {
                    shape.CopyPropertiesFrom(styleFrom, cdrCopyProperties.cdrCopyOutlinePen | cdrCopyProperties.cdrCopyOutlineColor);
                }
                catch (Exception)
                {
                    // У исходной фигуры может не быть обводки — тогда стиль по умолчанию.
                }
            }

            return shape;
        }

        /// <summary>Пометки (разрывы, начало линии, ошибки) — маленькие красные кружки одной группой.</summary>
        private Shape? CreateMarkers(Document doc, IReadOnlyList<Point2D> points, string nameKey)
        {
            if (points.Count == 0)
            {
                return null;
            }

            var created = new object[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                Shape marker = doc.ActiveLayer.CreateEllipse2(points[i].X, points[i].Y, 0.6, 0.6);
                marker.Name = Loc[nameKey];
                created[i] = marker;
            }

            ShapeRange range = doc.CreateShapeRangeFromArray(ref created);
            range.ApplyNoFill();
            range.SetOutlineProperties(Width: 0.25, Color: app!.CreateRGBColor(229, 57, 53));
            Shape group = points.Count > 1 ? range.Group() : (Shape)created[0];
            group.Name = Loc[nameKey];
            return group;
        }

        private void RunOnCurves(string undoKey, Func<Document, List<SourceShape>, bool> action)
        {
            RunInDocument(undoKey, doc =>
            {
                List<SourceShape> sources = SelectedCurves(doc);
                if (sources.Count == 0)
                {
                    SetStatus("vec.selectCurves");
                    return;
                }

                action(doc, sources);
            });
        }

        // ---- Смещение и параллельные ----------------------------------------------------------

        private void OffsetOut_Click(object sender, RoutedEventArgs e) => DoOffset(+1);

        private void OffsetIn_Click(object sender, RoutedEventArgs e) => DoOffset(-1);

        private void DoOffset(int sign)
        {
            double distance = LengthFrom(OffsetBox, 2) * sign;
            bool round = SharpCornersBox.IsChecked != true;
            RunOnCurves("undo.offset", (doc, sources) =>
            {
                int made = 0;
                foreach (SourceShape src in sources)
                {
                    var result = new List<CoreCurve>();
                    foreach (CoreCurve c in src.Contours)
                    {
                        try
                        {
                            result.Add(VectorTools.Offset(c, distance, round));
                        }
                        catch (ArgumentException)
                        {
                            // Внутрь не помещается — пропускаем этот контур.
                        }
                    }

                    if (result.Count > 0)
                    {
                        CreateCurveShape(doc, result, src.Shape);
                        made++;
                    }
                }

                SetStatus(made > 0 ? "vec.done" : "vec.offsetNone", made);
                return true;
            });
        }

        private void Parallel_Click(object sender, RoutedEventArgs e)
        {
            int count = CountFrom(ParallelCountBox, 3, 100);
            double step = LengthFrom(ParallelStepBox, 2.6);
            bool both = ParallelBothBox.IsChecked == true;
            bool round = SharpCornersBox.IsChecked != true;
            RunOnCurves("undo.parallel", (doc, sources) =>
            {
                int made = 0;
                foreach (SourceShape src in sources)
                {
                    foreach (CoreCurve c in src.Contours)
                    {
                        foreach (CoreCurve line in VectorTools.Parallel(c, count, step, both, round))
                        {
                            CreateCurveShape(doc, new[] { line }, src.Shape);
                            made++;
                        }
                    }
                }

                SetStatus("vec.done", made);
                return true;
            });
        }

        // ---- Разрезать, соединить, замкнуть, разрывы ---------------------------------------------

        private void Split_Click(object sender, RoutedEventArgs e)
        {
            int parts = CountFrom(SplitBox, 2, 1000);
            RunOnCurves("undo.split", (doc, sources) =>
            {
                int made = 0;
                foreach (SourceShape src in sources)
                {
                    foreach (CoreCurve c in src.Contours)
                    {
                        foreach (CoreCurve piece in VectorTools.SplitEqual(c, parts))
                        {
                            CreateCurveShape(doc, new[] { piece }, src.Shape);
                            made++;
                        }
                    }

                    src.Shape.Delete();
                }

                SetStatus("vec.splitDone", made);
                return true;
            });
        }

        private void Join_Click(object sender, RoutedEventArgs e)
        {
            double tolerance = LengthFrom(JoinToleranceBox, 0.5);
            RunOnCurves("undo.join", (doc, sources) =>
            {
                List<CoreCurve> all = sources.SelectMany(s => s.Contours).ToList();
                List<CoreCurve> joined = VectorTools.JoinEnds(all, tolerance);
                foreach (CoreCurve c in joined)
                {
                    CreateCurveShape(doc, new[] { c }, sources[0].Shape);
                }

                foreach (SourceShape src in sources)
                {
                    src.Shape.Delete();
                }

                SetStatus("vec.joined", all.Count, joined.Count, joined.Count(c => c.IsClosed));
                return true;
            });
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            double tolerance = LengthFrom(JoinToleranceBox, 0.5);
            RunOnCurves("undo.close", (doc, sources) =>
            {
                int closed = 0;
                foreach (SourceShape src in sources)
                {
                    List<CoreCurve> result = src.Contours.Select(c => VectorTools.Close(c, tolerance)).ToList();
                    int newly = result.Count(c => c.IsClosed) - src.Contours.Count(c => c.IsClosed);
                    if (newly > 0)
                    {
                        CreateCurveShape(doc, result, src.Shape);
                        src.Shape.Delete();
                        closed += newly;
                    }
                }

                SetStatus("vec.closed", closed);
                return true;
            });
        }

        private void Gaps_Click(object sender, RoutedEventArgs e)
        {
            double max = Math.Max(LengthFrom(JoinToleranceBox, 0.5) * 4, 1);
            RunOnCurves("undo.marks", (doc, sources) =>
            {
                List<Point2D> gaps = VectorTools.FindGaps(sources.SelectMany(s => s.Contours).ToList(), 0.001, max);
                CreateMarkers(doc, gaps, "vec.markGap");
                SetStatus(gaps.Count > 0 ? "vec.gapsFound" : "vec.gapsNone", gaps.Count);
                return true;
            });
        }

        // ---- Направление -----------------------------------------------------------------------

        private void Reverse_Click(object sender, RoutedEventArgs e)
        {
            RunOnCurves("undo.reverse", (doc, sources) =>
            {
                int done = 0;
                foreach (SourceShape src in sources.Where(s => s.Shape.Type == cdrShapeType.cdrCurveShape))
                {
                    src.Shape.Curve.ReverseDirection();
                    done++;
                }

                SetStatus(done > 0 ? "vec.reversed" : "vec.reverseOnlyCurves", done);
                return true;
            });
        }

        /// <summary>Начало линии: кружок в первом узле и второй, поменьше, чуть дальше по ходу — видно направление.</summary>
        private void ShowStart_Click(object sender, RoutedEventArgs e)
        {
            RunOnCurves("undo.marks", (doc, sources) =>
            {
                var points = new List<Point2D>();
                foreach (CoreCurve c in sources.SelectMany(s => s.Contours))
                {
                    FlattenedCurve flat = CurveFlattener.Flatten(c);
                    points.Add(flat.PointAtDistance(0));
                    points.Add(flat.PointAtDistance(Math.Min(flat.TotalLength, 1.5)));
                    points.Add(flat.PointAtDistance(Math.Min(flat.TotalLength, 2.5)));
                }

                CreateMarkers(doc, points, "vec.markStart");
                SetStatus("vec.startShown", points.Count / 3);
                return true;
            });
        }

        // ---- Сглаживание и центральная линия ---------------------------------------------------

        private void Smooth_Click(object sender, RoutedEventArgs e)
        {
            double tolerance = LengthFrom(SmoothBox, 0.1);
            RunOnCurves("undo.smooth", (doc, sources) =>
            {
                int before = 0, after = 0;
                foreach (SourceShape src in sources)
                {
                    List<CoreCurve> smooth = src.Contours.Select(c => VectorTools.SimplifyAndSmooth(c, tolerance, smooth: true)).ToList();
                    before += src.Contours.Sum(c => c.Segments.Count);
                    after += smooth.Sum(c => c.Segments.Count);
                    Shape created = CreateCurveShape(doc, smooth, src.Shape);
                    try
                    {
                        created.CopyPropertiesFrom(src.Shape, cdrCopyProperties.cdrCopyFill);
                    }
                    catch (Exception)
                    {
                        // Без заливки — не страшно.
                    }

                    src.Shape.Delete();
                }

                SetStatus("vec.smoothed", before, after);
                return true;
            });
        }

        private void Centerline_Click(object sender, RoutedEventArgs e)
        {
            RunOnCurves("undo.centerline", (doc, sources) =>
            {
                int made = 0;
                foreach (SourceShape src in sources)
                {
                    List<CoreCurve> lines = Centerline.Build(src.Contours);
                    if (lines.Count > 0)
                    {
                        CreateCurveShape(doc, lines, null).Name = Loc["vec.centerlineName"];
                        made += lines.Count;
                    }
                }

                SetStatus(made > 0 ? "vec.centerlineDone" : "vec.centerlineNone", made);
                return true;
            });
        }

        // ---- Проверка --------------------------------------------------------------------------

        /// <summary>
        /// Проверка линий (раздел 10): самопересечения и микросегменты — пометками; крошечные мусорные
        /// объекты и дубли — выделяются, чтобы их можно было сразу удалить.
        /// </summary>
        private void Check_Click(object sender, RoutedEventArgs e)
        {
            RunInDocument("undo.marks", doc =>
            {
                List<SourceShape> sources = SelectedCurves(doc);
                if (sources.Count == 0)
                {
                    SetStatus("vec.selectCurves");
                    return;
                }

                var marks = new List<Point2D>();
                int crossings = 0, micro = 0;
                foreach (CoreCurve c in sources.SelectMany(s => s.Contours))
                {
                    List<Point2D> x = VectorTools.SelfIntersections(c);
                    List<Point2D> m = VectorTools.MicroSegments(c, 0.05);
                    crossings += x.Count;
                    micro += m.Count;
                    marks.AddRange(x);
                    marks.AddRange(m);
                }

                var bad = new List<Shape>();
                int tiny = 0, duplicates = 0;
                foreach (SourceShape src in sources)
                {
                    src.Shape.GetBoundingBox(out _, out _, out double w, out double h, false);
                    if (w < 0.3 && h < 0.3)
                    {
                        bad.Add(src.Shape);
                        tiny++;
                    }
                }

                for (int i = 0; i < sources.Count && sources.Count <= 3000; i++)
                {
                    for (int j = 0; j < i; j++)
                    {
                        if (sources[i].Contours.Count == sources[j].Contours.Count &&
                            sources[i].Contours.Zip(sources[j].Contours, (a, b) => VectorTools.AreSame(a, b)).All(same => same))
                        {
                            bad.Add(sources[i].Shape);
                            duplicates++;
                            break;
                        }
                    }
                }

                doc.ClearSelection();
                CreateMarkers(doc, marks, "vec.markProblem");
                if (bad.Count > 0)
                {
                    object[] shapes = bad.Distinct().Select(s => (object)s).ToArray();
                    doc.CreateShapeRangeFromArray(ref shapes).CreateSelection();
                }

                SetStatus(crossings + micro + tiny + duplicates == 0 ? "vec.checkOk" : "vec.checkFound", crossings, micro, tiny, duplicates);
            });
        }
    }
}
