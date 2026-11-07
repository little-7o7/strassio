using System;
using System.Collections.Generic;
using System.Linq;
using Strassio.Core.Editing;
using Strassio.Core.Geometry;
using Strassio.Core.Placement;

namespace Strassio.Core.Methods
{
    /// <summary>Результат «весь дизайн за раз»: стразы каждой фигуры отдельно (у каждой фигуры своя группа и свой цвет).</summary>
    public sealed class DesignResult
    {
        public DesignResult(IReadOnlyList<MethodResult> perShape, int removed, int shifted, int skippedShapes)
        {
            PerShape = perShape;
            Removed = removed;
            Shifted = shifted;
            SkippedShapes = skippedShapes;
        }

        /// <summary>Стразы каждой фигуры — в том же порядке, что и фигуры на входе. Пусто — фигура пропущена.</summary>
        public IReadOnlyList<MethodResult> PerShape { get; }

        /// <summary>Сколько страз убрано: налезали на стразы фигуры повыше или на уже лежащие.</summary>
        public int Removed { get; }

        /// <summary>Сколько страз чуть отодвинуто от соседей.</summary>
        public int Shifted { get; }

        /// <summary>Фигуры, которым метод не подходит (методу нужна замкнутая, а линия открыта).</summary>
        public int SkippedShapes { get; }

        public int StoneCount => PerShape.Sum(r => r.Stones.Count);
    }

    /// <summary>
    /// «Весь дизайн за раз» и «учитывать готовые стразы» (docs/SPEC.md, разделы 5 и 6): один метод на
    /// несколько фигур. Фигуры идут сверху вниз — верхняя фигура выкладывается целиком, а нижняя
    /// обходит её: в заливке верхняя фигура (с запасом в зазор) вырезается из нижней, и ряды нижней
    /// огибают её край. Остальные встречи (линии, уже лежащие стразы) решаются <see cref="StoneAvoider"/>:
    /// новая страза чуть отодвигается или убирается, а то, что уже выложено, не трогается.
    /// </summary>
    public static class DesignRunner
    {
        /// <param name="shapes">Фигуры дизайна, самая верхняя первой; у каждой — все её контуры (с отверстиями).</param>
        /// <param name="existing">Стразы, которые уже лежат в документе и которые нельзя задевать; null или пусто — не учитывать.</param>
        public static DesignResult Run(
            MethodKind kind, IReadOnlyList<IReadOnlyList<Curve>> shapes, double stoneDiameterMm, MethodParameters p,
            IReadOnlyDictionary<string, double>? sizes = null, IReadOnlyList<PlacedStone>? existing = null)
        {
            MethodInfo info = MethodCatalog.Get(kind);
            var placed = new List<PlacedStone>(existing ?? Array.Empty<PlacedStone>());
            var uppers = new List<(IReadOnlyList<Curve> Grown, Point2D Min, Point2D Max)>();
            var perShape = new List<MethodResult>();
            int removed = 0, shifted = 0, skipped = 0;
            bool show = p.Intersections == MethodChoices.IntersectShow;
            double maxShift = p.Intersections == MethodChoices.IntersectShift ? stoneDiameterMm * 0.3 : 0;

            foreach (IReadOnlyList<Curve> shape in shapes)
            {
                List<Curve> own = shape.Where(c => c.Segments.Count > 0).ToList();
                if (own.Count == 0 || (info.NeedsClosed && !MethodRunner.OuterContour(own).IsClosed))
                {
                    skipped++;
                    perShape.Add(new MethodResult(Array.Empty<PlacedStone>(), Array.Empty<int>()));
                    continue;
                }

                var region = new ColorRegion(own, 0, 0, 0, 0);

                // Заливка обходит фигуры повыше: их контуры (раздутые на зазор) становятся отверстиями.
                // Чётно-нечётное правило даёт «эта фигура XOR верхняя»; лишнее снаружи этой фигуры
                // потом отсекается проверкой «центр внутри своей фигуры».
                List<Curve> contours = own;
                bool cut = false;
                if (info.IsFill && !region.IsEmpty)
                {
                    foreach ((IReadOnlyList<Curve> grown, Point2D min, Point2D max) in uppers)
                    {
                        if (min.X <= region.Max.X && max.X >= region.Min.X && min.Y <= region.Max.Y && max.Y >= region.Min.Y)
                        {
                            if (!cut)
                            {
                                contours = new List<Curve>(own);
                                cut = true;
                            }

                            contours.AddRange(grown);
                        }
                    }
                }

                MethodResult result = MethodRunner.Run(kind, contours, stoneDiameterMm, p, sizes);
                IReadOnlyList<PlacedStone> stones = result.Stones;
                IReadOnlyList<int> conflicts = result.ConflictIndices;
                if (cut)
                {
                    var keep = new List<int>();
                    for (int i = 0; i < stones.Count; i++)
                    {
                        if (region.Contains(stones[i].Center))
                        {
                            keep.Add(i);
                        }
                    }

                    (stones, conflicts) = Subset(stones, conflicts, keep);
                }

                if (show)
                {
                    // «Только показать»: ничего не убираем, налезающие на чужие стразы помечаем.
                    var marked = new HashSet<int>(conflicts);
                    marked.UnionWith(StoneAvoider.Conflicts(stones, placed, p.GapMm));
                    perShape.Add(new MethodResult(stones, marked.OrderBy(i => i).ToList()));
                }
                else
                {
                    Func<Point2D, bool>? allowed = info.IsFill && !region.IsEmpty ? region.Contains : (Func<Point2D, bool>?)null;
                    AvoidResult avoided = StoneAvoider.Avoid(stones, placed, p.GapMm, maxShift, allowed);
                    removed += avoided.Removed;
                    shifted += avoided.Shifted;
                    (IReadOnlyList<PlacedStone> _, IReadOnlyList<int> kept) = Subset(stones, conflicts, avoided.KeptIndices);
                    perShape.Add(new MethodResult(avoided.Stones, kept));
                }

                placed.AddRange(perShape[perShape.Count - 1].Stones);
                if (info.IsFill && !region.IsEmpty)
                {
                    uppers.Add((Grow(own, region, p.GapMm), new Point2D(region.Min.X - p.GapMm, region.Min.Y - p.GapMm),
                        new Point2D(region.Max.X + p.GapMm, region.Max.Y + p.GapMm)));
                }
            }

            return new DesignResult(perShape, removed, shifted, skipped);
        }

        /// <summary>Оставляет стразы с номерами <paramref name="keep"/> (по возрастанию); номера наложений пересчитываются.</summary>
        private static (IReadOnlyList<PlacedStone>, IReadOnlyList<int>) Subset(
            IReadOnlyList<PlacedStone> stones, IReadOnlyList<int> conflicts, IReadOnlyList<int> keep)
        {
            var newIndex = new Dictionary<int, int>();
            var list = new List<PlacedStone>(keep.Count);
            foreach (int i in keep)
            {
                newIndex[i] = list.Count;
                list.Add(stones[i]);
            }

            var mapped = new List<int>();
            foreach (int c in conflicts)
            {
                if (newIndex.TryGetValue(c, out int n))
                {
                    mapped.Add(n);
                }
            }

            return (list, mapped);
        }

        /// <summary>
        /// Фигура, раздутая наружу на <paramref name="distance"/>: внешний контур отодвигается наружу,
        /// отверстия сжимаются. Так ряды нижней фигуры встают от верхней не ближе зазора.
        /// </summary>
        public static IReadOnlyList<Curve> Grow(IReadOnlyList<Curve> contours, ColorRegion region, double distance)
        {
            var closed = contours.Where(c => c.IsClosed).ToList();
            if (distance <= 1e-6)
            {
                return closed;
            }

            var grown = new List<Curve>();
            foreach (Curve contour in closed)
            {
                FlattenedCurve flat = CurveFlattener.Flatten(contour, 0.05);
                if (flat.Points.Count < 3)
                {
                    continue;
                }

                // С какой стороны контура сама фигура: шагнём чуть влево по ходу и проверим.
                Point2D a = flat.Points[0].Position;
                Point2D b = flat.Points[1].Position;
                Point2D dir = (b - a).Normalized();
                Point2D left = new Point2D(-dir.Y, dir.X);
                Point2D middle = (a + b) * 0.5;
                bool shapeOnLeft = region.Contains(middle + left * 0.01);
                double signed = shapeOnLeft ? -distance : distance;

                List<Point2D> offset = CurveOffsetter.Offset(flat, signed, 0.05);
                if (offset.Count >= 3)
                {
                    grown.Add(Curve.FromPolyline(offset, isClosed: true));
                }
            }

            return grown;
        }
    }
}
