using System;
using System.Collections.Generic;
using System.Linq;
using Strassio.Core.Geometry;
using Strassio.Core.Placement;

namespace Strassio.Core.Motifs
{
    /// <summary>Как раскладывать мотивы по линии или рамкой.</summary>
    public sealed class MotifLayoutOptions
    {
        /// <summary>Сколько мотивов поставить; 0 — сколько влезет с промежутком <see cref="SpacingMm"/>.</summary>
        public int Count { get; set; }

        /// <summary>Промежуток между соседними мотивами (от края до края), мм.</summary>
        public double SpacingMm { get; set; } = 1;

        /// <summary>Поворачивать мотив по направлению линии (иначе все стоят ровно, как сохранены).</summary>
        public bool Rotate { get; set; } = true;

        /// <summary>Перевернуть: «верх» мотива смотрит внутрь (для замкнутой линии) или вправо от направления.</summary>
        public bool Flip { get; set; }

        /// <summary>Минимальный зазор между стразами разных мотивов, мм: налезающие камни не ставятся.</summary>
        public double StoneGapMm { get; set; } = 0.2;

        /// <summary>Для рамки: поворот линии больше этого угла — угол рамки, градусы.</summary>
        public double CornerDeg { get; set; } = 40;
    }

    /// <summary>Страза разложенного мотива: где она встала и из какой стразы мотива.</summary>
    public readonly struct MotifPlacement
    {
        public MotifPlacement(Point2D center, MotifStone source, int copy)
        {
            Center = center;
            Source = source;
            Copy = copy;
        }

        public Point2D Center { get; }

        public MotifStone Source { get; }

        /// <summary>Номер копии мотива (0, 1, 2…).</summary>
        public int Copy { get; }
    }

    /// <summary>Итог раскладки: стразы, сколько мотивов встало и сколько камней пропущено из-за наложений.</summary>
    public sealed class MotifLayout
    {
        public List<MotifPlacement> Stones { get; } = new List<MotifPlacement>();

        public int Copies { get; internal set; }

        public int Dropped { get; internal set; }
    }

    /// <summary>Копия выделенной стразы при повторе по кругу: номер исходной стразы и новое место.</summary>
    public readonly struct RotatedCopy
    {
        public RotatedCopy(int source, Point2D center)
        {
            Source = source;
            Center = center;
        }

        public int Source { get; }

        public Point2D Center { get; }
    }

    /// <summary>
    /// Раскладка мотивов (docs/SPEC.md, раздел 9): по линии (с поворотом и чередованием), по кругу
    /// (круг — тоже линия: мотив «верхом» наружу даёт розетку), рамкой с отдельными углами, и повтор
    /// выделенных страз вокруг центра (мандала).
    /// </summary>
    public static class MotifPlacer
    {
        /// <summary>
        /// Мотивы вдоль линии. Несколько мотивов чередуются по очереди. Замкнутая линия обходится по
        /// часовой стрелке — «верх» мотива (+Y) смотрит наружу; разомкнутая — «верх» слева по ходу линии.
        /// </summary>
        public static MotifLayout AlongLine(Curve path, IReadOnlyList<Motif> motifs, MotifLayoutOptions options)
        {
            var layout = new MotifLayout();
            List<Motif> list = motifs.Where(m => m.Stones.Count > 0).ToList();
            if (list.Count == 0)
            {
                return layout;
            }

            FlattenedCurve flat = Oriented(path);
            double length = flat.TotalLength;
            StonePacker packer = Packer(list, options);
            if (length < 1e-6)
            {
                Stamp(list[0], flat.Points[0].Position, 0, options, packer, layout);
                return layout;
            }

            double average = list.Average(m => m.Width);
            double pitch = average + Math.Max(0, options.SpacingMm);
            if (flat.IsClosed)
            {
                int n = options.Count > 0 ? options.Count : Math.Max(1, (int)Math.Floor(length / Math.Max(0.1, pitch) + 1e-9));
                for (int i = 0; i < n; i++)
                {
                    PlaceOnLine(flat, list[i % list.Count], length * i / n, options, packer, layout);
                }

                return layout;
            }

            // Разомкнутая: первый и последний мотив — у концов линии, остальные поровну между ними.
            int count = options.Count > 0
                ? options.Count
                : Math.Max(1, (int)Math.Floor((length - average) / Math.Max(0.1, pitch) + 1e-9) + 1);
            double first = Math.Min(list[0].Width / 2, length / 2);
            double last = Math.Max(first, length - list[(count - 1) % list.Count].Width / 2);
            for (int i = 0; i < count; i++)
            {
                double s = count == 1 ? length / 2 : first + (last - first) * i / (count - 1);
                PlaceOnLine(flat, list[i % list.Count], s, options, packer, layout);
            }

            return layout;
        }

        /// <summary>
        /// Рамка: в каждом остром углу контура — мотив углов <paramref name="corner"/> (нет — тот же мотив,
        /// что по сторонам), повёрнутый по биссектрисе; стороны между углами заполняются мотивами
        /// <paramref name="sides"/> поровну. Нет углов (круг, овал) — как <see cref="AlongLine"/>.
        /// </summary>
        public static MotifLayout Frame(Curve contour, IReadOnlyList<Motif> sides, Motif? corner, MotifLayoutOptions options)
        {
            List<Motif> list = sides.Where(m => m.Stones.Count > 0).ToList();
            Motif? cornerMotif = corner != null && corner.Stones.Count > 0 ? corner : list.FirstOrDefault();
            if (list.Count == 0 && cornerMotif != null)
            {
                list.Add(cornerMotif);
            }

            if (list.Count == 0 || !contour.IsClosed)
            {
                return AlongLine(contour, list, options);
            }

            FlattenedCurve flat = Oriented(contour);
            double length = flat.TotalLength;
            List<double> corners = CornerDetector.FindSharpCornerDistances(flat, options.CornerDeg)
                .Select(c => Wrap(c, length)).Distinct().OrderBy(c => c).ToList();
            if (corners.Count == 0)
            {
                return AlongLine(contour, list, options);
            }

            var layout = new MotifLayout();
            var all = new List<Motif>(list) { cornerMotif! };
            StonePacker packer = Packer(all, options);
            double spacing = Math.Max(0, options.SpacingMm);

            // Сначала углы — они важнее, стороны подстраиваются под них.
            foreach (double c in corners)
            {
                Point2D at = At(flat, c);
                Point2D before = (at - At(flat, c - 0.5)).Normalized();
                Point2D after = (At(flat, c + 0.5) - at).Normalized();
                Point2D outward = (Left(before) + Left(after)).Normalized();
                if (outward.Length < 1e-9)
                {
                    outward = Left(after);
                }

                // Мотив поворачивается так, чтобы его «верх» (+Y) смотрел по биссектрисе наружу
                // (с «Перевернуть» мотив отражается, и верх смотрит внутрь).
                double angle = options.Rotate ? Math.Atan2(-outward.X, outward.Y) : 0;
                Stamp(cornerMotif!, at, angle, options, packer, layout);
            }

            double clearance = Math.Max(cornerMotif!.Width, cornerMotif.Height) / 2 + spacing;
            double average = list.Average(m => m.Width);
            int index = 0;
            for (int k = 0; k < corners.Count; k++)
            {
                double from = corners[k];
                double to = k + 1 < corners.Count ? corners[k + 1] : corners[0] + length;
                double start = from + clearance;
                double free = to - clearance - start;
                int n = (int)Math.Floor((free + spacing) / Math.Max(0.1, average + spacing) + 1e-9);
                for (int i = 0; i < n; i++)
                {
                    PlaceOnLine(flat, list[index++ % list.Count], start + free * (i + 0.5) / n, options, packer, layout);
                }
            }

            return layout;
        }

        /// <summary>
        /// Повтор по кругу (мандала): выделенные стразы повторяются <paramref name="count"/> раз вокруг
        /// <paramref name="pivot"/> (исходные — первая копия). Копия, налезающая на уже стоящий камень
        /// (например, у самого центра), не ставится.
        /// </summary>
        public static List<RotatedCopy> Around(
            IReadOnlyList<Point2D> centers, IReadOnlyList<double> diameters, Point2D pivot, int count, double gapMm)
        {
            var copies = new List<RotatedCopy>();
            if (centers.Count == 0 || count < 2)
            {
                return copies;
            }

            var packer = new StonePacker(diameters.Max(), Math.Max(0, gapMm));
            for (int i = 0; i < centers.Count; i++)
            {
                packer.Add(new PlacedStone(centers[i], diameters[i], false));
            }

            for (int k = 1; k < count; k++)
            {
                double angle = 2 * Math.PI * k / count;
                for (int i = 0; i < centers.Count; i++)
                {
                    Point2D p = pivot + Rotate(centers[i] - pivot, angle);
                    if (packer.TryAdd(p, diameters[i]))
                    {
                        copies.Add(new RotatedCopy(i, p));
                    }
                }
            }

            return copies;
        }

        private static void PlaceOnLine(FlattenedCurve flat, Motif motif, double s, MotifLayoutOptions options, StonePacker packer, MotifLayout layout)
        {
            Point2D at = At(flat, s);
            double angle = 0;
            if (options.Rotate)
            {
                // Направление — по хорде на ширину мотива: на извилистой линии мотив не «дрожит».
                double half = Math.Max(0.5, motif.Width / 2);
                Point2D dir = At(flat, s + half) - At(flat, s - half);
                if (dir.Length < 1e-9)
                {
                    dir = flat.TangentAtDistance(Wrap(s, flat.TotalLength));
                }

                angle = Math.Atan2(dir.Y, dir.X);
            }

            Stamp(motif, at, angle, options, packer, layout);
        }

        private static void Stamp(Motif motif, Point2D at, double angle, MotifLayoutOptions options, StonePacker packer, MotifLayout layout)
        {
            int copy = layout.Copies++;
            foreach (MotifStone stone in motif.Stones)
            {
                Point2D offset = options.Flip ? new Point2D(stone.X, -stone.Y) : stone.Offset;
                Point2D p = at + Rotate(offset, angle);
                if (packer.TryAdd(p, stone.DiameterMm))
                {
                    layout.Stones.Add(new MotifPlacement(p, stone, copy));
                }
                else
                {
                    layout.Dropped++;
                }
            }
        }

        private static StonePacker Packer(IEnumerable<Motif> motifs, MotifLayoutOptions options) =>
            new StonePacker(Math.Max(0.1, motifs.Max(m => m.LargestStoneMm)), Math.Max(0, options.StoneGapMm));

        /// <summary>Замкнутая линия — по часовой стрелке (тогда слева по ходу — снаружи).</summary>
        private static FlattenedCurve Oriented(Curve curve)
        {
            FlattenedCurve flat = CurveFlattener.Flatten(curve);
            return flat.IsClosed && CurveMetrics.SignedArea(flat) > 0 ? flat.Reverse() : flat;
        }

        private static Point2D At(FlattenedCurve flat, double s)
        {
            if (flat.IsClosed)
            {
                return flat.PointAtDistance(Wrap(s, flat.TotalLength));
            }

            return flat.PointAtDistance(s);
        }

        private static double Wrap(double s, double length)
        {
            if (length <= 0)
            {
                return 0;
            }

            double w = s % length;
            return w < 0 ? w + length : w;
        }

        private static Point2D Left(Point2D dir) => new Point2D(-dir.Y, dir.X);

        private static Point2D Rotate(Point2D v, double angle)
        {
            double cos = Math.Cos(angle), sin = Math.Sin(angle);
            return new Point2D(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
        }
    }
}
