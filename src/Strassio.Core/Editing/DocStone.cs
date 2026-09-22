using System;
using System.Collections.Generic;
using Strassio.Core.Geometry;

namespace Strassio.Core.Editing
{
    /// <summary>
    /// Страза, уже лежащая в документе (docs/SPEC.md, раздел 7): где она, какого размера, закреплена
    /// ли и насколько «сверху». Порядок <see cref="Order"/> — как в CorelDRAW: больше — выше
    /// (нарисована поверх остальных).
    /// </summary>
    public readonly struct DocStone
    {
        public DocStone(Point2D center, double diameterMm, int order, bool isLocked = false)
        {
            Center = center;
            DiameterMm = diameterMm;
            Order = order;
            IsLocked = isLocked;
        }

        public Point2D Center { get; }

        public double DiameterMm { get; }

        /// <summary>Порядок по высоте: больше — выше.</summary>
        public int Order { get; }

        /// <summary>Закреплённую стразу правка не трогает — ни удаляет, ни двигает (раздел 7.2).</summary>
        public bool IsLocked { get; }

        public double Radius => DiameterMm / 2;
    }

    /// <summary>Что сделать с документом после правки: какие стразы удалить и какие куда подвинуть.</summary>
    public sealed class EditResult
    {
        public EditResult(IReadOnlyList<int> deleted, IReadOnlyList<StoneMove> moved)
        {
            Deleted = deleted;
            Moved = moved;
        }

        public static EditResult Nothing { get; } = new EditResult(Array.Empty<int>(), Array.Empty<StoneMove>());

        /// <summary>Номера страз (в исходном списке) — удалить.</summary>
        public IReadOnlyList<int> Deleted { get; }

        /// <summary>Стразы, которые надо сдвинуть на новое место.</summary>
        public IReadOnlyList<StoneMove> Moved { get; }

        public bool IsEmpty => Deleted.Count == 0 && Moved.Count == 0;
    }

    /// <summary>Страза номер <see cref="Index"/> переезжает в <see cref="NewCenter"/>.</summary>
    public readonly struct StoneMove
    {
        public StoneMove(int index, Point2D newCenter)
        {
            Index = index;
            NewCenter = newCenter;
        }

        public int Index { get; }

        public Point2D NewCenter { get; }
    }

    /// <summary>
    /// Быстрый поиск соседей: стразы разложены по клеткам сетки, и сравниваются только с теми,
    /// что в соседних клетках, — а не каждая с каждой. 50 000 страз проверяются за доли секунды.
    /// </summary>
    internal sealed class StoneGrid
    {
        private readonly double cell;
        private readonly Dictionary<(int, int), List<int>> cells = new Dictionary<(int, int), List<int>>();
        private readonly List<(Point2D Center, double Radius)> items = new List<(Point2D, double)>();

        public StoneGrid(double cellSize)
        {
            cell = Math.Max(0.05, cellSize);
        }

        /// <summary>Добавляет стразу; возвращает её номер в сетке.</summary>
        public int Add(Point2D center, double radius)
        {
            items.Add((center, radius));
            (int, int) key = Key(center);
            if (!cells.TryGetValue(key, out List<int>? list))
            {
                list = new List<int>();
                cells[key] = list;
            }

            list.Add(items.Count - 1);
            return items.Count - 1;
        }

        /// <summary>Налезает ли круг на что-то уже добавленное (зазор меньше <paramref name="minGapMm"/>).</summary>
        public bool Overlaps(Point2D center, double radius, double minGapMm, int ignore = -1)
        {
            foreach (int i in Near(center))
            {
                if (i == ignore)
                {
                    continue;
                }

                (Point2D c, double r) = items[i];
                double limit = r + radius + minGapMm;
                if (Point2D.Distance(c, center) < limit - 1e-9)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Добавленные круги в соседних клетках (кандидаты в соседи).</summary>
        public IEnumerable<int> Near(Point2D center)
        {
            (int cx, int cy) = Key(center);
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (cells.TryGetValue((cx + dx, cy + dy), out List<int>? list))
                    {
                        foreach (int i in list)
                        {
                            yield return i;
                        }
                    }
                }
            }
        }

        public (Point2D Center, double Radius) this[int index] => items[index];

        private (int, int) Key(Point2D p) => ((int)Math.Floor(p.X / cell), (int)Math.Floor(p.Y / cell));
    }
}
