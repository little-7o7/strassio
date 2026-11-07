using System;
using System.Collections.Generic;
using System.Linq;
using Strassio.Core.Geometry;
using Strassio.Core.Placement;

namespace Strassio.Core.Motifs
{
    /// <summary>Как сдвинуты соседние копии раппорта.</summary>
    public enum RepeatShift
    {
        /// <summary>Прямой повтор: копии стоят ровной сеткой.</summary>
        None,

        /// <summary>Полшага вниз: каждый второй столбец опущен на половину шага по высоте.</summary>
        HalfDrop,

        /// <summary>Кирпич: каждый второй ряд сдвинут вбок на половину шага по ширине.</summary>
        Brick,
    }

    /// <summary>Чем отличается каждая вторая копия (в шахматном порядке) — раппорт из двух мотивов.</summary>
    public enum RepeatAlternate
    {
        /// <summary>Все копии одинаковые.</summary>
        None,

        /// <summary>Каждая вторая копия повёрнута на 180° вокруг середины плитки.</summary>
        Rotate180,

        /// <summary>Каждая вторая копия отражена слева направо.</summary>
        MirrorX,

        /// <summary>Каждая вторая копия отражена сверху вниз.</summary>
        MirrorY,
    }

    /// <summary>Лист трафарета, который можно класть встык без шва: сколько раппортов поперёк и вниз.</summary>
    public readonly struct RepeatSheet
    {
        public RepeatSheet(double widthMm, double heightMm, int across, int down)
        {
            WidthMm = widthMm;
            HeightMm = heightMm;
            Across = across;
            Down = down;
        }

        public double WidthMm { get; }

        public double HeightMm { get; }

        /// <summary>Сколько периодов узора по ширине листа (у полшага и шахматки период — два шага).</summary>
        public int Across { get; }

        public int Down { get; }

        /// <summary>Лист получился (шаг меньше наибольшего листа).</summary>
        public bool IsValid => Across > 0 && Down > 0;
    }

    /// <summary>Настройки раппорта (docs/SPEC.md, раздел 9, «Раппорт»).</summary>
    public sealed class RepeatOptions
    {
        /// <summary>Шаг по ширине: на сколько мм правее стоит следующая копия.</summary>
        public double StepXMm { get; set; }

        /// <summary>Шаг по высоте: на сколько мм ниже стоит следующий ряд.</summary>
        public double StepYMm { get; set; }

        public RepeatShift Shift { get; set; }

        public int Columns { get; set; } = 3;

        public int Rows { get; set; } = 3;

        /// <summary>Наименьший просвет между стразами соседних копий на стыке, мм.</summary>
        public double GapMm { get; set; } = 0.2;

        /// <summary>Каждая вторая копия (в шахматном порядке) повёрнута или отражена.</summary>
        public RepeatAlternate Alternate { get; set; }

        /// <summary>Середина плитки — вокруг неё поворот и отражение. Нет — середина страз плитки.</summary>
        public Point2D? Pivot { get; set; }
    }

    /// <summary>
    /// Раппорт — повтор одного рисунка (плитки) по всей ткани. Плитка повторяется сеткой: прямо, со сдвигом
    /// столбцов на полшага вниз или рядов на полшага вбок. Стразы у края плитки часто заходят на соседнюю
    /// копию — на стыке они налезли бы друг на друга. <see cref="SeamConflicts"/> находит такие стразы с учётом
    /// ВСЕХ соседей, и если убрать их из самой плитки, то стыки чистые во всей ткани сразу.
    /// </summary>
    public static class Repeat
    {
        /// <summary>
        /// Сдвиги всех копий относительно исходной плитки (первая — (0, 0), сама плитка). Ряды идут вниз
        /// (минус по Y, как в CorelDRAW), столбцы — вправо.
        /// </summary>
        public static List<Point2D> Offsets(RepeatOptions options)
        {
            var result = new List<Point2D>();
            for (int row = 0; row < Math.Max(1, options.Rows); row++)
            {
                for (int column = 0; column < Math.Max(1, options.Columns); column++)
                {
                    result.Add(Offset(options, column, row));
                }
            }

            return result;
        }

        /// <summary>Копия с номером <paramref name="index"/> в <see cref="Offsets"/> — «вторая» (повёрнутая или отражённая).</summary>
        public static bool IsAlternate(RepeatOptions options, int index)
        {
            int columns = Math.Max(1, options.Columns);
            return options.Alternate != RepeatAlternate.None && ((index % columns + index / columns) & 1) == 1;
        }

        /// <summary>Где окажется точка плитки во «второй» копии (до сдвига копии).</summary>
        public static Point2D Transform(RepeatAlternate alternate, Point2D p, Point2D pivot)
        {
            switch (alternate)
            {
                case RepeatAlternate.Rotate180:
                    return new Point2D(2 * pivot.X - p.X, 2 * pivot.Y - p.Y);
                case RepeatAlternate.MirrorX:
                    return new Point2D(2 * pivot.X - p.X, p.Y);
                case RepeatAlternate.MirrorY:
                    return new Point2D(p.X, 2 * pivot.Y - p.Y);
                default:
                    return p;
            }
        }

        /// <summary>Середина плитки для поворота и отражения: заданная или середина страз.</summary>
        public static Point2D PivotOf(IReadOnlyList<PlacedStone> tile, RepeatOptions options) =>
            options.Pivot ?? (tile.Count == 0 ? new Point2D(0, 0) : Center(tile));

        /// <summary>
        /// Самый большой лист трафарета не больше maxWidth × maxHeight, который кладётся встык без шва: его
        /// ширина и высота — целое число периодов узора. Период по ширине — шаг, а у полшага и у раппорта
        /// с «второй» копией — два шага (иначе соседний лист начнётся не с того места). По высоте так же.
        /// </summary>
        public static RepeatSheet SeamlessSheet(RepeatOptions options, double maxWidthMm, double maxHeightMm)
        {
            if (options.StepXMm <= 0 || options.StepYMm <= 0)
            {
                return default;
            }

            bool alternate = options.Alternate != RepeatAlternate.None;
            double px = options.StepXMm * (alternate || options.Shift == RepeatShift.HalfDrop ? 2 : 1);
            double py = options.StepYMm * (alternate || options.Shift == RepeatShift.Brick ? 2 : 1);
            int across = (int)Math.Floor(maxWidthMm / px + 1e-9);
            int down = (int)Math.Floor(maxHeightMm / py + 1e-9);
            return across > 0 && down > 0 ? new RepeatSheet(across * px, down * py, across, down) : default;
        }

        /// <summary>
        /// Сколько плиток уходит на погонный метр ткани шириной <paramref name="fabricWidthMm"/>: площадь
        /// метра делится на площадь одной плитки (шаг × шаг — у всех видов повтора одинаково).
        /// </summary>
        public static double TilesPerRunningMetre(RepeatOptions options, double fabricWidthMm) =>
            options.StepXMm <= 0 || options.StepYMm <= 0 ? 0 : Math.Max(0, fabricWidthMm) * 1000 / (options.StepXMm * options.StepYMm);

        /// <summary>
        /// Номера страз плитки, которые на стыке налезают на стразы соседних копий. Из каждой такой пары
        /// убирается одна — та, что дальше от середины плитки (она и «вылезла» за край). Плитку без этих
        /// страз можно повторять сколько угодно: ни одного наложения ни на одном стыке.
        /// </summary>
        public static List<int> SeamConflicts(IReadOnlyList<PlacedStone> tile, RepeatOptions options)
        {
            var removed = new HashSet<int>();
            if (tile.Count == 0 || options.StepXMm <= 0 || options.StepYMm <= 0)
            {
                return removed.ToList();
            }

            if (options.Alternate != RepeatAlternate.None)
            {
                return SeamConflictsAlternating(tile, options);
            }

            double gap = Math.Max(0, options.GapMm);
            double largest = tile.Max(s => s.DiameterMm);
            double cell = Math.Max(0.1, largest + gap);
            var grid = new Dictionary<(int, int), List<int>>();
            for (int i = 0; i < tile.Count; i++)
            {
                (int, int) key = Key(tile[i].Center, cell);
                if (!grid.TryGetValue(key, out List<int>? list))
                {
                    grid[key] = list = new List<int>();
                }

                list.Add(i);
            }

            Point2D center = Center(tile);
            var pairs = new List<(int A, int B, double Overlap)>();
            foreach (Point2D shift in NeighbourShifts(tile, options))
            {
                // Стразы соседней копии — это стразы плитки, сдвинутые на shift. Ищем, какие из них подходят
                // к стразам самой плитки ближе, чем можно.
                for (int j = 0; j < tile.Count; j++)
                {
                    Point2D p = tile[j].Center + shift;
                    (int cx, int cy) = Key(p, cell);
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            if (!grid.TryGetValue((cx + dx, cy + dy), out List<int>? list))
                            {
                                continue;
                            }

                            foreach (int i in list)
                            {
                                double need = (tile[i].DiameterMm + tile[j].DiameterMm) / 2 + gap - StoneTolerance;
                                double distance = Point2D.Distance(tile[i].Center, p);
                                if (distance < need)
                                {
                                    pairs.Add((i, j, need - distance));
                                }
                            }
                        }
                    }
                }
            }

            // Сначала самые сильные наложения; из пары уходит страза дальше от середины плитки.
            foreach ((int a, int b, double _) in pairs.OrderByDescending(p => p.Overlap))
            {
                if (removed.Contains(a) || removed.Contains(b))
                {
                    continue;
                }

                if (a == b)
                {
                    removed.Add(a);
                    continue;
                }

                double da = Point2D.Distance(tile[a].Center, center);
                double db = Point2D.Distance(tile[b].Center, center);
                removed.Add(da >= db ? a : b);
            }

            return removed.OrderBy(i => i).ToList();
        }

        /// <summary>
        /// Вся ткань: плитка без страз со стыков (<see cref="SeamConflicts"/>), повторённая по
        /// <see cref="Offsets"/>. <paramref name="removed"/> — сколько страз пришлось убрать из плитки.
        /// </summary>
        public static List<PlacedStone> Tile(IReadOnlyList<PlacedStone> tile, RepeatOptions options, out int removed)
        {
            var drop = new HashSet<int>(SeamConflicts(tile, options));
            removed = drop.Count;
            var clean = tile.Where((s, i) => !drop.Contains(i)).ToList();
            var result = new List<PlacedStone>(clean.Count * Math.Max(1, options.Columns) * Math.Max(1, options.Rows));
            Point2D pivot = PivotOf(tile, options);
            List<Point2D> offsets = Offsets(options);
            for (int k = 0; k < offsets.Count; k++)
            {
                Point2D offset = offsets[k];
                bool second = IsAlternate(options, k);
                result.AddRange(clean.Select(s => new PlacedStone(
                    (second ? Transform(options.Alternate, s.Center, pivot) : s.Center) + offset, s.DiameterMm, s.IsCorner, s.RowId)));
            }

            return result;
        }

        /// <summary>
        /// Стыки раппорта с «второй» копией: соседние копии бывают двух видов, поэтому проверяем по-честному
        /// две опорные копии (обычную и «вторую») против всех копий вокруг. Страза, которая хоть где-то
        /// налезает, убирается из плитки — значит и из обеих копий сразу.
        /// </summary>
        private static List<int> SeamConflictsAlternating(IReadOnlyList<PlacedStone> tile, RepeatOptions options)
        {
            double gap = Math.Max(0, options.GapMm);
            Point2D pivot = PivotOf(tile, options);
            Point2D[][] variants =
            {
                tile.Select(s => s.Center).ToArray(),
                tile.Select(s => Transform(options.Alternate, s.Center, pivot)).ToArray(),
            };

            double largest = tile.Max(s => s.DiameterMm);
            double spanX = 0, spanY = 0;
            foreach (Point2D[] v in variants)
            {
                spanX = Math.Max(spanX, v.Max(p => p.X) - v.Min(p => p.X));
                spanY = Math.Max(spanY, v.Max(p => p.Y) - v.Min(p => p.Y));
            }

            double reachX = spanX + largest + gap, reachY = spanY + largest + gap;
            int n = (int)Math.Ceiling(Math.Max(reachX / options.StepXMm, reachY / options.StepYMm)) + 2;

            Point2D center = Center(tile);
            double cell = Math.Max(0.1, largest + gap);
            var grids = new Dictionary<(int, int), List<int>>[2];
            for (int t = 0; t < 2; t++)
            {
                grids[t] = new Dictionary<(int, int), List<int>>();
                for (int i = 0; i < tile.Count; i++)
                {
                    (int, int) key = Key(variants[t][i], cell);
                    if (!grids[t].TryGetValue(key, out List<int>? list))
                    {
                        grids[t][key] = list = new List<int>();
                    }

                    list.Add(i);
                }
            }

            var pairs = new List<(int A, int B, double Overlap, double Da, double Db)>();
            foreach ((int c0, int r0) in new[] { (0, 0), (1, 0) })
            {
                int t0 = (c0 + r0) & 1;
                Point2D o0 = Offset(options, c0, r0);
                for (int c = c0 - n; c <= c0 + n; c++)
                {
                    for (int r = r0 - n; r <= r0 + n; r++)
                    {
                        if (c == c0 && r == r0)
                        {
                            continue;
                        }

                        Point2D shift = Offset(options, c, r) - o0;
                        if (Math.Abs(shift.X) > reachX || Math.Abs(shift.Y) > reachY)
                        {
                            continue;
                        }

                        int t = (c + r) & 1;
                        for (int j = 0; j < tile.Count; j++)
                        {
                            Point2D b = variants[t][j] + shift;
                            (int cx, int cy) = Key(b, cell);
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                for (int dy = -1; dy <= 1; dy++)
                                {
                                    if (!grids[t0].TryGetValue((cx + dx, cy + dy), out List<int>? list))
                                    {
                                        continue;
                                    }

                                    foreach (int i in list)
                                    {
                                        double need = (tile[i].DiameterMm + tile[j].DiameterMm) / 2 + gap - StoneTolerance;
                                        double distance = Point2D.Distance(variants[t0][i], b);
                                        if (distance < need)
                                        {
                                            pairs.Add((i, j, need - distance,
                                                Point2D.Distance(tile[i].Center, center), Point2D.Distance(tile[j].Center, center)));
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            var removed = new HashSet<int>();
            foreach ((int a, int b, double _, double da, double db) in pairs.OrderByDescending(p => p.Overlap))
            {
                if (removed.Contains(a) || removed.Contains(b))
                {
                    continue;
                }

                removed.Add(a == b || da >= db ? a : b);
            }

            return removed.OrderBy(i => i).ToList();
        }

        /// <summary>Погрешность, на которую стразы могут сойтись ближе (как у остальных раскладок).</summary>
        private const double StoneTolerance = 0.01;

        private static Point2D Offset(RepeatOptions o, int column, int row)
        {
            double x = column * o.StepXMm;
            double y = -row * o.StepYMm;
            if (o.Shift == RepeatShift.HalfDrop && (column & 1) != 0)
            {
                y -= o.StepYMm / 2;
            }
            else if (o.Shift == RepeatShift.Brick && (row & 1) != 0)
            {
                x += o.StepXMm / 2;
            }

            return new Point2D(x, y);
        }

        /// <summary>
        /// Сдвиги до всех копий бесконечного повтора, которые могут коснуться плитки. Для полшага и кирпича
        /// сетка копий не прямоугольная, поэтому берутся её собственные векторы.
        /// </summary>
        private static IEnumerable<Point2D> NeighbourShifts(IReadOnlyList<PlacedStone> tile, RepeatOptions o)
        {
            Point2D a, b;
            switch (o.Shift)
            {
                case RepeatShift.HalfDrop:
                    a = new Point2D(o.StepXMm, -o.StepYMm / 2);
                    b = new Point2D(0, o.StepYMm);
                    break;
                case RepeatShift.Brick:
                    a = new Point2D(o.StepXMm, 0);
                    b = new Point2D(o.StepXMm / 2, -o.StepYMm);
                    break;
                default:
                    a = new Point2D(o.StepXMm, 0);
                    b = new Point2D(0, o.StepYMm);
                    break;
            }

            // Копия может коснуться плитки, только если сдвиг не больше размаха плитки плюс камень и просвет.
            double minX = tile.Min(s => s.Center.X), maxX = tile.Max(s => s.Center.X);
            double minY = tile.Min(s => s.Center.Y), maxY = tile.Max(s => s.Center.Y);
            double reachX = maxX - minX + tile.Max(s => s.DiameterMm) + o.GapMm;
            double reachY = maxY - minY + tile.Max(s => s.DiameterMm) + o.GapMm;
            int n = (int)Math.Ceiling(Math.Max(reachX / o.StepXMm, reachY / (o.StepYMm / 2))) + 1;
            for (int i = -2 * n; i <= 2 * n; i++)
            {
                for (int j = -2 * n; j <= 2 * n; j++)
                {
                    if (i == 0 && j == 0)
                    {
                        continue;
                    }

                    Point2D s = a * i + b * j;
                    if (Math.Abs(s.X) <= reachX && Math.Abs(s.Y) <= reachY)
                    {
                        yield return s;
                    }
                }
            }
        }

        private static Point2D Center(IReadOnlyList<PlacedStone> tile)
        {
            double minX = tile.Min(s => s.Center.X), maxX = tile.Max(s => s.Center.X);
            double minY = tile.Min(s => s.Center.Y), maxY = tile.Max(s => s.Center.Y);
            return new Point2D((minX + maxX) / 2, (minY + maxY) / 2);
        }

        private static (int, int) Key(Point2D p, double cell) => ((int)Math.Floor(p.X / cell), (int)Math.Floor(p.Y / cell));
    }
}
