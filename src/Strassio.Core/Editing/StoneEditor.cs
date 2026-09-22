using System;
using System.Collections.Generic;
using System.Linq;
using Strassio.Core.Geometry;

namespace Strassio.Core.Editing
{
    /// <summary>Режимы удаления (docs/SPEC.md, раздел 7.1, утверждены автором).</summary>
    public enum DeleteMode
    {
        /// <summary>1. Оставить верхние, удалить нижние: мешающие снизу убираются.</summary>
        KeepTop,

        /// <summary>2. Удалить верхние, оставить нижние.</summary>
        KeepBottom,

        /// <summary>3. Удалить по линии: всё, что линия пересекает, — «разрезать» заливку.</summary>
        AlongLine,

        /// <summary>4а. Удалить внутри выбранной формы.</summary>
        InsideShape,

        /// <summary>4б. Удалить снаружи выбранной формы.</summary>
        OutsideShape,

        /// <summary>5. Сдвинуть вместо удаления, если камень встаёт без наложения; иначе удалить.</summary>
        Shift,
    }

    /// <summary>
    /// Правка страз, уже лежащих в документе (docs/SPEC.md, разделы 7.1 и 7.2). Считает только, что
    /// удалить и что куда сдвинуть — сам документ меняет аддон. Закреплённые стразы
    /// (<see cref="DocStone.IsLocked"/>) не удаляются и не двигаются никогда; при наложениях они
    /// главнее любых незакреплённых.
    /// </summary>
    public static class StoneEditor
    {
        /// <summary>Стразы считаются налезающими, если зазор между ними меньше этого, мм.</summary>
        public const double DefaultMinGapMm = 0.05;

        /// <summary>Дальше этого (в долях диаметра) режим «сдвинуть» стразу не двигает — тогда удаляет.</summary>
        public const double DefaultMaxShiftFraction = 0.5;

        /// <summary>
        /// Режимы 1 и 2: из каждой пары налезающих страз остаётся верхняя (или нижняя). Идём от
        /// главных к остальным и оставляем стразу, только если она ни на кого из оставленных не налезает.
        /// </summary>
        public static EditResult ResolveOverlaps(IReadOnlyList<DocStone> stones, bool keepTop, double minGapMm = DefaultMinGapMm)
        {
            var deleted = new List<int>();
            StoneGrid grid = NewGrid(stones, minGapMm, 0);
            foreach (int i in PriorityOrder(stones, keepTop))
            {
                DocStone s = stones[i];
                if (!s.IsLocked && grid.Overlaps(s.Center, s.Radius, minGapMm))
                {
                    deleted.Add(i);
                    continue;
                }

                grid.Add(s.Center, s.Radius);
            }

            deleted.Sort();
            return new EditResult(deleted, Array.Empty<StoneMove>());
        }

        /// <summary>
        /// Режим 5: как «оставить верхние», но мешающую стразу сначала пробуем сдвинуть — в сторону от
        /// тех, на кого она налезает, не дальше <paramref name="maxShiftFraction"/> диаметра и не налезая
        /// на других. Не вышло — удаляем.
        /// </summary>
        public static EditResult ShiftApart(
            IReadOnlyList<DocStone> stones, double minGapMm = DefaultMinGapMm, double maxShiftFraction = DefaultMaxShiftFraction)
        {
            var deleted = new List<int>();
            var moved = new List<StoneMove>();
            double maxDiameter = stones.Count == 0 ? 0 : stones.Max(s => s.DiameterMm);
            StoneGrid grid = NewGrid(stones, minGapMm, maxDiameter * maxShiftFraction);

            foreach (int i in PriorityOrder(stones, keepTop: true))
            {
                DocStone s = stones[i];
                if (s.IsLocked || !grid.Overlaps(s.Center, s.Radius, minGapMm))
                {
                    grid.Add(s.Center, s.Radius);
                    continue;
                }

                Point2D? place = FindFreePlace(grid, s, minGapMm, s.DiameterMm * maxShiftFraction);
                if (place.HasValue)
                {
                    moved.Add(new StoneMove(i, place.Value));
                    grid.Add(place.Value, s.Radius);
                }
                else
                {
                    deleted.Add(i);
                }
            }

            deleted.Sort();
            return new EditResult(deleted, moved.OrderBy(m => m.Index).ToList());
        }

        /// <summary>
        /// Режим 3: удалить все стразы, которые пересекает хотя бы одна из линий-«ножей». Страза
        /// задета, если линия проходит ближе, чем её радиус плюс <paramref name="marginMm"/>.
        /// </summary>
        public static EditResult DeleteAlongLines(IReadOnlyList<DocStone> stones, IReadOnlyList<Curve> cutters, double marginMm = 0)
        {
            List<FlattenedCurve> flats = cutters.Select(c => CurveFlattener.Flatten(c)).ToList();
            var deleted = new List<int>();
            for (int i = 0; i < stones.Count; i++)
            {
                DocStone s = stones[i];
                if (s.IsLocked)
                {
                    continue;
                }

                double reach = s.Radius + marginMm;
                if (flats.Any(f => DistanceToPolyline(f, s.Center) < reach))
                {
                    deleted.Add(i);
                }
            }

            return new EditResult(deleted, Array.Empty<StoneMove>());
        }

        /// <summary>
        /// Режим 4: удалить стразы внутри (или снаружи) формы. Страза «внутри», если внутри её центр;
        /// отверстия формы (буква «О») считаются снаружи. <paramref name="touchingToo"/> — удалить и те,
        /// что лежат на границе (центр с другой стороны, но круг задевает контур).
        /// </summary>
        public static EditResult DeleteByShape(
            IReadOnlyList<DocStone> stones, IReadOnlyList<Curve> shape, bool inside, bool touchingToo = false)
        {
            List<FlattenedCurve> flats = shape.Select(c => CurveFlattener.Flatten(c)).Where(f => f.IsClosed).ToList();
            var deleted = new List<int>();
            if (flats.Count == 0)
            {
                return new EditResult(deleted, Array.Empty<StoneMove>());
            }

            for (int i = 0; i < stones.Count; i++)
            {
                DocStone s = stones[i];
                if (s.IsLocked)
                {
                    continue;
                }

                bool centerInside = IsInside(flats, s.Center);
                bool hit = centerInside == inside;
                if (!hit && touchingToo)
                {
                    hit = flats.Any(f => DistanceToPolyline(f, s.Center) < s.Radius);
                }

                if (hit)
                {
                    deleted.Add(i);
                }
            }

            return new EditResult(deleted, Array.Empty<StoneMove>());
        }

        /// <summary>
        /// Удаление дублей (раздел 7.2): стразы, лежащие друг на друге после копирования, — центры
        /// ближе <paramref name="toleranceMm"/> и почти одинаковый размер. Из каждой кучки остаётся
        /// одна — закреплённая, а если таких нет, самая верхняя.
        /// </summary>
        public static EditResult FindDuplicates(IReadOnlyList<DocStone> stones, double toleranceMm = 0.05)
        {
            var deleted = new List<int>();
            double maxDiameter = stones.Count == 0 ? 0 : stones.Max(s => s.DiameterMm);
            var grid = new StoneGrid(Math.Max(toleranceMm * 4, maxDiameter));
            var kept = new List<int>();

            foreach (int i in PriorityOrder(stones, keepTop: true))
            {
                DocStone s = stones[i];
                bool duplicate = false;
                foreach (int g in grid.Near(s.Center))
                {
                    DocStone other = stones[kept[g]];
                    if (Point2D.Distance(other.Center, s.Center) <= toleranceMm &&
                        Math.Abs(other.DiameterMm - s.DiameterMm) <= toleranceMm * 2)
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (duplicate && !s.IsLocked)
                {
                    deleted.Add(i);
                    continue;
                }

                grid.Add(s.Center, s.Radius);
                kept.Add(i);
            }

            deleted.Sort();
            return new EditResult(deleted, Array.Empty<StoneMove>());
        }

        /// <summary>Расстояние от точки до ломаной (для замкнутой — с отрезком «последняя → первая»).</summary>
        public static double DistanceToPolyline(FlattenedCurve curve, Point2D p)
        {
            IReadOnlyList<FlattenedPoint> pts = curve.Points;
            if (pts.Count == 0)
            {
                return double.MaxValue;
            }

            if (pts.Count == 1)
            {
                return Point2D.Distance(pts[0].Position, p);
            }

            double best = double.MaxValue;
            int segments = curve.IsClosed ? pts.Count : pts.Count - 1;
            for (int i = 0; i < segments; i++)
            {
                Point2D a = pts[i].Position;
                Point2D b = pts[(i + 1) % pts.Count].Position;
                best = Math.Min(best, DistanceToSegment(a, b, p));
            }

            return best;
        }

        private static double DistanceToSegment(Point2D a, Point2D b, Point2D p)
        {
            Point2D ab = b - a;
            double len2 = ab.Dot(ab);
            if (len2 < 1e-18)
            {
                return Point2D.Distance(a, p);
            }

            double t = Math.Max(0, Math.Min(1, (p - a).Dot(ab) / len2));
            return Point2D.Distance(a + ab * t, p);
        }

        /// <summary>Чётно-нечётное правило по всем контурам: внутри отверстия — снаружи формы.</summary>
        private static bool IsInside(List<FlattenedCurve> contours, Point2D p)
        {
            int count = 0;
            foreach (FlattenedCurve c in contours)
            {
                if (PointInPolygon.IsInside(c, p))
                {
                    count++;
                }
            }

            return count % 2 == 1;
        }

        /// <summary>
        /// Порядок обработки: закреплённые первыми (они главнее всех), потом верхние (или нижние);
        /// при равной высоте — по номеру в списке.
        /// </summary>
        private static IEnumerable<int> PriorityOrder(IReadOnlyList<DocStone> stones, bool keepTop) =>
            Enumerable.Range(0, stones.Count)
                .OrderByDescending(i => stones[i].IsLocked)
                .ThenBy(i => keepTop ? -stones[i].Order : stones[i].Order)
                .ThenBy(i => i);

        private static StoneGrid NewGrid(IReadOnlyList<DocStone> stones, double minGapMm, double extraMm)
        {
            double maxDiameter = stones.Count == 0 ? 1 : stones.Max(s => s.DiameterMm);
            return new StoneGrid(maxDiameter + minGapMm + extraMm);
        }

        /// <summary>
        /// Ближайшее свободное место для стразы: сначала в сторону «от соседей» (сумма направлений от
        /// тех, на кого она налезает), потом — по кругу из 24 направлений; расстояние растёт шагами
        /// по 5% диаметра. null — свободного места в пределах <paramref name="maxShiftMm"/> нет.
        /// </summary>
        private static Point2D? FindFreePlace(StoneGrid grid, DocStone s, double minGapMm, double maxShiftMm)
        {
            Point2D push = Point2D.Zero;
            foreach (int g in grid.Near(s.Center))
            {
                (Point2D c, double r) = grid[g];
                Point2D away = s.Center - c;
                double dist = away.Length;
                if (dist < r + s.Radius + minGapMm && dist > 1e-9)
                {
                    push += away / dist * (r + s.Radius + minGapMm - dist);
                }
            }

            var directions = new List<Point2D>();
            if (push.Length > 1e-9)
            {
                directions.Add(push.Normalized());
            }

            const int ring = 24;
            for (int k = 0; k < ring; k++)
            {
                double a = 2 * Math.PI * k / ring;
                directions.Add(new Point2D(Math.Cos(a), Math.Sin(a)));
            }

            double step = Math.Max(0.01, s.DiameterMm * 0.05);
            for (double d = step; d <= maxShiftMm + 1e-9; d += step)
            {
                foreach (Point2D dir in directions)
                {
                    Point2D candidate = s.Center + dir * d;
                    if (!grid.Overlaps(candidate, s.Radius, minGapMm))
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }
    }
}
