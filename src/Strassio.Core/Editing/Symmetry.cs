using System;
using System.Collections.Generic;
using Strassio.Core.Geometry;

namespace Strassio.Core.Editing
{
    /// <summary>С какой стороны рисунка ось симметрии.</summary>
    public enum MirrorSide
    {
        /// <summary>Ось — через центры самых правых страз, копия справа.</summary>
        Right,

        /// <summary>Ось — через центры самых левых страз, копия слева.</summary>
        Left,

        /// <summary>Ось — через центры самых нижних страз (в CorelDRAW Y растёт вверх), копия снизу.</summary>
        Down,

        /// <summary>Ось — через центры самых верхних страз, копия сверху.</summary>
        Up,
    }

    /// <summary>Новая страза-копия: с какой исходной стразы (её номер) и куда.</summary>
    public sealed class MirroredStone
    {
        public MirroredStone(int source, Point2D center)
        {
            Source = source;
            Center = center;
        }

        /// <summary>Номер исходной стразы: копия того же размера и цвета.</summary>
        public int Source { get; }

        public Point2D Center { get; }
    }

    public sealed class MirrorResult
    {
        public MirrorResult(IReadOnlyList<MirroredStone> added, IReadOnlyList<StoneMove> snapped, int skipped)
        {
            Added = added;
            Snapped = snapped;
            Skipped = skipped;
        }

        /// <summary>Новые стразы — зеркальные копии.</summary>
        public IReadOnlyList<MirroredStone> Added { get; }

        /// <summary>Стразы у самой оси, поставленные точно на ось (их копия легла бы на них же).</summary>
        public IReadOnlyList<StoneMove> Snapped { get; }

        /// <summary>Копии, которые не поставлены: налезли бы на стразы у оси.</summary>
        public int Skipped { get; }
    }

    /// <summary>
    /// Симметрия (docs/SPEC.md, раздел 8): выкладываете половину или четверть, плагин зеркалит. Камни на оси
    /// не двоятся: страза, чья копия легла бы на неё же, ставится точно на ось и не копируется. Копия, которая
    /// налезла бы на другую стразу у оси, не ставится.
    /// </summary>
    public static class Symmetry
    {
        /// <summary>Ось у края рисунка: точка на оси и направление оси.</summary>
        public static (Point2D Point, Point2D Direction) AxisAt(IReadOnlyList<DocStone> stones, MirrorSide side)
        {
            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            foreach (DocStone s in stones)
            {
                minX = Math.Min(minX, s.Center.X);
                maxX = Math.Max(maxX, s.Center.X);
                minY = Math.Min(minY, s.Center.Y);
                maxY = Math.Max(maxY, s.Center.Y);
            }

            switch (side)
            {
                case MirrorSide.Right:
                    return (new Point2D(maxX, 0), new Point2D(0, 1));
                case MirrorSide.Left:
                    return (new Point2D(minX, 0), new Point2D(0, 1));
                case MirrorSide.Down:
                    return (new Point2D(0, minY), new Point2D(1, 0));
                default:
                    return (new Point2D(0, maxY), new Point2D(1, 0));
            }
        }

        /// <summary>Отражение точки относительно прямой через <paramref name="point"/> с направлением <paramref name="direction"/>.</summary>
        public static Point2D Reflect(Point2D p, Point2D point, Point2D direction)
        {
            Point2D u = direction.Normalized();
            Point2D d = p - point;
            double along = d.X * u.X + d.Y * u.Y;
            Point2D foot = point + u * along;
            return foot * 2 - p;
        }

        /// <summary>Зеркалит стразы относительно одной оси.</summary>
        public static MirrorResult Mirror(IReadOnlyList<DocStone> stones, Point2D axisPoint, Point2D axisDirection, double minGapMm) =>
            MirrorMany(stones, new[] { (axisPoint, axisDirection) }, minGapMm);

        /// <summary>
        /// Четверть → целое: сначала отражение вправо, потом всё вместе — вниз (оси — у правого и нижнего края
        /// четверти).
        /// </summary>
        public static MirrorResult Quarter(IReadOnlyList<DocStone> stones, double minGapMm) =>
            MirrorMany(stones, new[] { AxisAt(stones, MirrorSide.Right), AxisAt(stones, MirrorSide.Down) }, minGapMm);

        /// <summary>Отражения по очереди; каждое следующее отражает и копии предыдущих.</summary>
        public static MirrorResult MirrorMany(
            IReadOnlyList<DocStone> stones, IReadOnlyList<(Point2D Point, Point2D Direction)> axes, double minGapMm)
        {
            // Все стразы, уже стоящие на листе: исходные (возможно, придвинутые к оси) и копии.
            var centers = new List<Point2D>();
            var diameters = new List<double>();
            var roots = new List<int>();
            var locked = new List<bool>();
            double maxD = 0;
            for (int i = 0; i < stones.Count; i++)
            {
                centers.Add(stones[i].Center);
                diameters.Add(stones[i].DiameterMm);
                roots.Add(i);
                locked.Add(stones[i].IsLocked);
                maxD = Math.Max(maxD, stones[i].DiameterMm);
            }

            int skipped = 0;
            foreach ((Point2D point, Point2D direction) in axes)
            {
                if (direction.Length < 1e-9)
                {
                    continue;
                }

                Point2D u = direction.Normalized();
                var normal = new Point2D(-u.Y, u.X);
                int count = centers.Count;

                // 1. Стразы у самой оси — точно на ось (если там не тесно и страза не закреплена).
                var mirrorThis = new bool[count];
                for (int i = 0; i < count; i++)
                {
                    double off = (centers[i] - point).X * normal.X + (centers[i] - point).Y * normal.Y;
                    double half = (diameters[i] + minGapMm) / 2;
                    if (Math.Abs(off) >= half - 1e-6)
                    {
                        mirrorThis[i] = true;
                        continue;
                    }

                    Point2D onAxis = centers[i] - normal * off;
                    if (!locked[i] && Math.Abs(off) > 1e-6 && Free(onAxis, diameters[i], centers, diameters, minGapMm, ignore: i))
                    {
                        centers[i] = onAxis;
                    }
                }

                // 2. Копии остальных; копия, которая налезла бы на уже стоящую стразу, не ставится.
                var grid = new StoneGrid(Math.Max(0.5, 2 * maxD + minGapMm));
                for (int i = 0; i < centers.Count; i++)
                {
                    grid.Add(centers[i], diameters[i] / 2);
                }

                for (int i = 0; i < count; i++)
                {
                    if (!mirrorThis[i])
                    {
                        continue;
                    }

                    Point2D copy = Reflect(centers[i], point, u);
                    if (grid.Overlaps(copy, diameters[i] / 2, minGapMm - 0.05))
                    {
                        skipped++;
                        continue;
                    }

                    grid.Add(copy, diameters[i] / 2);
                    centers.Add(copy);
                    diameters.Add(diameters[i]);
                    roots.Add(roots[i]);
                    locked.Add(false);
                }
            }

            var added = new List<MirroredStone>();
            for (int i = stones.Count; i < centers.Count; i++)
            {
                added.Add(new MirroredStone(roots[i], centers[i]));
            }

            var snapped = new List<StoneMove>();
            for (int i = 0; i < stones.Count; i++)
            {
                if (Point2D.Distance(centers[i], stones[i].Center) > 1e-6)
                {
                    snapped.Add(new StoneMove(i, centers[i]));
                }
            }

            return new MirrorResult(added, snapped, skipped);
        }

        private static bool Free(Point2D p, double d, List<Point2D> centers, List<double> diameters, double minGapMm, int ignore)
        {
            for (int j = 0; j < centers.Count; j++)
            {
                if (j != ignore && Point2D.Distance(p, centers[j]) - (d + diameters[j]) / 2 < minGapMm - 0.05)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
