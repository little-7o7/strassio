using System;
using System.Collections.Generic;
using System.Linq;
using Strassio.Core.Geometry;

namespace Strassio.Core.Placement
{
    /// <summary>
    /// Узорные заливки для тканей (docs/SPEC.md, раздел 5): F15 «Лучи» — линии страз от середины к краю,
    /// как у подсолнуха или веера, и F16 «Штрихи» — прерывистые параллельные полоски (икат, «дождь»).
    /// </summary>
    public static class PatternFills
    {
        /// <summary>
        /// F15 «Лучи». От самой глубокой точки формы расходятся лучи; страза за стразой с шагом
        /// «камень + зазор». Соседние лучи стоят не ближе «камень + между лучами», поэтому у середины лучей
        /// мало, а дальше, где места больше, между каждыми двумя лучами вырастает новый — как у подсолнуха.
        /// <paramref name="holeMm"/> — радиус пустой середины (0 — в середине одна страза).
        /// </summary>
        public static List<PlacedStone> Rays(
            IReadOnlyList<Curve> contours, double d, double gap, double rayGap, double holeMm, double marginMm)
        {
            var region = new ShapeRegion(contours, d);
            Point2D c = region.DeepestPoint;
            double step = d + Math.Max(0, gap);
            double lateral = d + Math.Max(Math.Max(0, gap), rayGap);
            double reach = new[] { region.Min, region.Max, new Point2D(region.Min.X, region.Max.Y), new Point2D(region.Max.X, region.Min.Y) }
                .Max(p => Point2D.Distance(p, c)) + step;
            var packer = new StonePacker(d, Math.Max(0, gap));

            double start;
            if (holeMm < step / 2)
            {
                if (region.Fits(c, d / 2, marginMm))
                {
                    packer.TryAdd(c, d, 0);
                }

                start = step;
            }
            else
            {
                start = holeMm + d / 2;
            }

            // Сколько лучей помещается на первом круге: соседние стразы по хорде — не ближе lateral.
            int count = Math.Max(3, RaysThatFit(start, lateral));
            var rays = new List<(double Angle, double From)>();
            for (int k = 0; k < count; k++)
            {
                rays.Add((Math.PI / 2 + 2 * Math.PI * k / count, start));
            }

            // Каждое удвоение — новый луч посередине между соседними, с того радиуса, где ему хватает места.
            for (int level = 1; level < 16; level++)
            {
                int total = count << level;
                double from = lateral / (2 * Math.Sin(Math.PI / total));
                if (from > reach)
                {
                    break;
                }

                for (int k = 1; k < total; k += 2)
                {
                    rays.Add((Math.PI / 2 + 2 * Math.PI * k / total, from));
                }
            }

            for (int i = 0; i < rays.Count; i++)
            {
                (double angle, double from) = rays[i];
                double cos = Math.Cos(angle), sin = Math.Sin(angle);
                for (double r = from; r <= reach; r += step)
                {
                    var p = new Point2D(c.X + r * cos, c.Y + r * sin);
                    if (region.Fits(p, d / 2, marginMm))
                    {
                        packer.TryAdd(p, d, i + 1);
                    }
                }
            }

            return packer.Stones;
        }

        /// <summary>
        /// F16 «Штрихи». Параллельные линии под углом <paramref name="angleDeg"/> (90° — сверху вниз) через
        /// «камень + между линиями». На каждой линии — штрихи случайной длины, до <paramref name="maxStreak"/>
        /// страз, и разрывы до <paramref name="maxBreak"/> пустых мест. Если задано, куда редеть
        /// (<paramref name="fade"/>), штрихи там короче, а разрывы длиннее. Тот же вариант — та же раскладка.
        /// </summary>
        public static List<PlacedStone> Streaks(
            IReadOnlyList<Curve> contours, double d, double gap, double lineGap, double angleDeg, int maxStreak, int maxBreak,
            FadeDirection? fade, int variant, double marginMm)
        {
            var region = new ShapeRegion(contours, d);
            var random = new Random(unchecked(variant * 7907 + 31));
            Func<Point2D, double>? fadeAt = fade.HasValue ? Scatter.FadeMap(region, fade.Value, d / 2, marginMm) : null;
            double step = d + Math.Max(0, gap);
            double spacing = d + Math.Max(0, lineGap);
            double a = angleDeg * Math.PI / 180;
            var u = new Point2D(Math.Cos(a), Math.Sin(a));
            var v = new Point2D(-u.Y, u.X);
            Point2D c = region.DeepestPoint;
            Point2D[] corners = { region.Min, region.Max, new Point2D(region.Min.X, region.Max.Y), new Point2D(region.Max.X, region.Min.Y) };
            double uMin = corners.Min(p => Dot(p - c, u)), uMax = corners.Max(p => Dot(p - c, u));
            double vMin = corners.Min(p => Dot(p - c, v)), vMax = corners.Max(p => Dot(p - c, v));
            maxStreak = Math.Max(1, maxStreak);
            maxBreak = Math.Max(1, maxBreak);
            var packer = new StonePacker(d, Math.Max(0, gap));

            int lineMin = (int)Math.Floor(vMin / spacing), lineMax = (int)Math.Ceiling(vMax / spacing);
            for (int line = lineMin; line <= lineMax; line++)
            {
                // Соседние линии сдвинуты на полшага: при тесных линиях стразы ложатся «в ёлочку», не налезая.
                double shift = (line & 1) == 0 ? 0 : step / 2;
                int iMin = (int)Math.Floor((uMin - shift) / step) - 1, iMax = (int)Math.Ceiling((uMax - shift) / step) + 1;

                // Линия начинается с разрыва случайной длины, чтобы штрихи соседних линий не начинались вровень.
                int left = random.Next(0, maxStreak + maxBreak);
                bool inStreak = false;
                for (int i = iMin; i <= iMax; i++)
                {
                    Point2D p = c + u * (i * step + shift) + v * (line * spacing);
                    double t = fadeAt == null ? 0 : Scatter.Ease(fadeAt(p));
                    if (left <= 0)
                    {
                        inStreak = !inStreak;
                        // Штрих — от трети самого длинного до самого длинного: иначе много одиночных точек
                        // и полоски не читаются. Разрыв — от одного места до самого длинного.
                        left = inStreak
                            ? Math.Max(1, (int)Math.Round(random.Next((maxStreak + 2) / 3, maxStreak + 1) * (1 - 0.8 * t)))
                            : (int)Math.Round(random.Next(1, maxBreak + 1) * (1 + 4 * t));
                    }

                    if (inStreak && region.Fits(p, d / 2, marginMm))
                    {
                        packer.TryAdd(p, d, line);
                    }

                    left--;
                }
            }

            return packer.Stones;
        }

        /// <summary>Сколько лучей поместится на круге радиуса r, если соседние стразы по хорде не ближе lateral.</summary>
        private static int RaysThatFit(double r, double lateral) =>
            lateral >= 2 * r ? 1 : (int)Math.Floor(Math.PI / Math.Asin(lateral / (2 * r)));

        private static double Dot(Point2D a, Point2D b) => a.X * b.X + a.Y * b.Y;
    }
}
