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
            foreach (Point2D offset in Offsets(options))
            {
                result.AddRange(clean.Select(s => new PlacedStone(s.Center + offset, s.DiameterMm, s.IsCorner, s.RowId)));
            }

            return result;
        }

        /// <summary>Погрешность, на которую стразы могут сойтись ближе (как у остальных раскладок).</summary>
        private const double StoneTolerance = 0.01;

        private static Point2D Offset(RepeatOptions o, int column, int row)
        {
            double x = column * o.StepXMm;
            double y = -row * o.StepYMm;
            if (o.Shift == RepeatShift.HalfDrop && (column & 1) == 1)
            {
                y -= o.StepYMm / 2;
            }
            else if (o.Shift == RepeatShift.Brick && (row & 1) == 1)
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
