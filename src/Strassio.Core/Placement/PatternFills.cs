using System;
using System.Collections.Generic;
using System.Linq;
using Strassio.Core.Geometry;

namespace Strassio.Core.Placement
{
    /// <summary>
    /// Узорные заливки для тканей (docs/SPEC.md, раздел 5): F15 «Лучи» — линии страз от середины к краю,
    /// как у подсолнуха или веера, F16 «Штрихи» — прерывистые параллельные полоски (икат, «дождь»),
    /// F17 «Волны» — параллельные волнистые линии, и штрихи вокруг мотива (вкладка «Мотивы»).
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
            var packer = new StonePacker(d, Math.Max(0, gap));
            StreakLines(
                region.DeepestPoint, region.Min, region.Max, d, gap, lineGap, angleDeg, maxStreak, maxBreak,
                fadeAt, p => region.Fits(p, d / 2, marginMm), random, packer);
            return packer.Stones;
        }

        /// <summary>
        /// Штрихи вокруг мотива (пункт 1 «тканевых» идей): прерывистые полоски вокруг выделенного рисунка —
        /// у самого мотива штрихи длинные и частые, дальше короче и реже, а на расстоянии
        /// <paramref name="widthMm"/> от края мотива сходят на нет. Мотив — замкнутые фигуры
        /// <paramref name="shapes"/> и/или стразы <paramref name="design"/>; на них и на
        /// <paramref name="obstacles"/> (стразы рядом на странице) штрихи не налезают.
        /// </summary>
        public static List<PlacedStone> StreaksAround(
            IReadOnlyList<Curve> shapes, IReadOnlyList<PlacedStone> design, IReadOnlyList<PlacedStone> obstacles,
            double d, double gap, double lineGap, double angleDeg, int maxStreak, int maxBreak, double widthMm, int variant)
        {
            var packer = new StonePacker(Math.Max(d, obstacles.Concat(design).Select(s => s.DiameterMm).DefaultIfEmpty(d).Max()), Math.Max(0, gap));
            if (widthMm <= 0 || (shapes.Count == 0 && design.Count == 0))
            {
                return packer.Stones;
            }

            foreach (PlacedStone s in design.Concat(obstacles))
            {
                packer.Add(s);
            }

            var distance = new DesignDistance(shapes, design, Math.Max(0.1, d / 4), widthMm + d);
            double clearance = d / 2 + Math.Max(0, gap);
            double span = Math.Max(1e-6, widthMm);
            var random = new Random(unchecked(variant * 7907 + 31));
            int before = packer.Stones.Count;
            StreakLines(
                (distance.Min + distance.Max) / 2, distance.Min - new Point2D(widthMm + d, widthMm + d), distance.Max + new Point2D(widthMm + d, widthMm + d),
                d, gap, lineGap, angleDeg, maxStreak, maxBreak,
                p => Math.Min(1, Math.Max(0, ((distance.At(p) ?? 0) - clearance) / span)),
                p =>
                {
                    double? at = distance.At(p);
                    return at != null && at.Value >= clearance - 1e-9 && at.Value <= clearance + span;
                },
                random, packer);
            return packer.Stones.Skip(before).ToList();
        }

        /// <summary>
        /// F17 «Волны»: параллельные волнистые линии страз через всю форму. Высота волны — от середины до
        /// гребня, длина — от гребня до гребня. Соседние волны сдвинуты так, чтобы и на крутом склоне между
        /// ними оставался просвет «между линиями»; стразы всех волн стоят друг под другом.
        /// </summary>
        public static List<PlacedStone> Waves(
            IReadOnlyList<Curve> contours, double d, double gap, double lineGap, double heightMm, double lengthMm, double angleDeg,
            double marginMm)
        {
            var region = new ShapeRegion(contours, d);
            double step = d + Math.Max(0, gap);
            double amplitude = Math.Max(0, heightMm);
            double wavelength = Math.Max(step, lengthMm);
            double k = 2 * Math.PI / wavelength;

            // Перпендикуляр между соседними волнами на самом крутом месте — сдвиг × cos(наклона).
            double steepest = Math.Atan(amplitude * k);
            double spacing = (d + Math.Max(0, lineGap)) / Math.Cos(steepest);

            double a = angleDeg * Math.PI / 180;
            var u = new Point2D(Math.Cos(a), Math.Sin(a));
            var v = new Point2D(-u.Y, u.X);
            Point2D c = region.DeepestPoint;
            Point2D[] corners = { region.Min, region.Max, new Point2D(region.Min.X, region.Max.Y), new Point2D(region.Max.X, region.Min.Y) };
            double uMin = corners.Min(p => Dot(p - c, u)) - step, uMax = corners.Max(p => Dot(p - c, u)) + step;
            double vMin = corners.Min(p => Dot(p - c, v)) - amplitude, vMax = corners.Max(p => Dot(p - c, v)) + amplitude;

            // Места страз на одной волне (вдоль u): от одной точки отсчёта, ровно через шаг по прямой.
            var along = new List<double>();
            double ds = Math.Max(0.01, Math.Min(step, wavelength) / 200);
            double last = uMin;
            along.Add(last);
            for (double s = uMin + ds; s <= uMax; s += ds)
            {
                double du = s - last, dv = amplitude * (Math.Sin(k * s) - Math.Sin(k * last));
                if (du * du + dv * dv >= step * step)
                {
                    // Точнее: между s − ds и s — делением пополам.
                    double lo = s - ds, hi = s;
                    for (int i = 0; i < 30; i++)
                    {
                        double mid = (lo + hi) / 2;
                        double mu = mid - last, mv = amplitude * (Math.Sin(k * mid) - Math.Sin(k * last));
                        if (mu * mu + mv * mv >= step * step)
                        {
                            hi = mid;
                        }
                        else
                        {
                            lo = mid;
                        }
                    }

                    last = hi;
                    along.Add(last);
                    s = last;
                }
            }

            var packer = new StonePacker(d, Math.Max(0, gap));
            int lineMin = (int)Math.Floor(vMin / spacing), lineMax = (int)Math.Ceiling(vMax / spacing);
            for (int line = lineMin; line <= lineMax; line++)
            {
                foreach (double s in along)
                {
                    Point2D p = c + u * s + v * (line * spacing + amplitude * Math.Sin(k * s));
                    if (region.Fits(p, d / 2, marginMm))
                    {
                        packer.TryAdd(p, d, line);
                    }
                }
            }

            return packer.Stones;
        }

        /// <summary>
        /// Параллельные линии штрихов через прямоугольник min–max вокруг c. fadeAt — 0 там, где штрихи густые,
        /// 1 — где реже всего; fits — можно ли поставить стразу в точке.
        /// </summary>
        private static void StreakLines(
            Point2D c, Point2D min, Point2D max, double d, double gap, double lineGap, double angleDeg, int maxStreak, int maxBreak,
            Func<Point2D, double>? fadeAt, Func<Point2D, bool> fits, Random random, StonePacker packer)
        {
            double step = d + Math.Max(0, gap);
            double spacing = d + Math.Max(0, lineGap);
            double a = angleDeg * Math.PI / 180;
            var u = new Point2D(Math.Cos(a), Math.Sin(a));
            var v = new Point2D(-u.Y, u.X);
            Point2D[] corners = { min, max, new Point2D(min.X, max.Y), new Point2D(max.X, min.Y) };
            double uMin = corners.Min(p => Dot(p - c, u)), uMax = corners.Max(p => Dot(p - c, u));
            double vMin = corners.Min(p => Dot(p - c, v)), vMax = corners.Max(p => Dot(p - c, v));
            maxStreak = Math.Max(1, maxStreak);
            maxBreak = Math.Max(1, maxBreak);

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

                    if (inStreak && fits(p))
                    {
                        packer.TryAdd(p, d, line);
                    }

                    left--;
                }
            }
        }

        /// <summary>Сколько лучей поместится на круге радиуса r, если соседние стразы по хорде не ближе lateral.</summary>
        private static int RaysThatFit(double r, double lateral) =>
            lateral >= 2 * r ? 1 : (int)Math.Floor(Math.PI / Math.Asin(lateral / (2 * r)));

        private static double Dot(Point2D a, Point2D b) => a.X * b.X + a.Y * b.Y;
    }
}
