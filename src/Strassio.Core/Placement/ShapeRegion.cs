using System;
using System.Collections.Generic;
using System.Linq;
using Strassio.Core.Geometry;

namespace Strassio.Core.Placement
{
    /// <summary>
    /// Форма для заливки как «карта расстояний до края» (SignedDistanceField): в любой точке сразу
    /// известно, внутри ли она и насколько далеко от края. Отверстия (буква «О») — снаружи.
    /// Нужна заливкам Этапа 4 (F6, F8–F12): камень помещается, если его центр глубже радиуса плюс отступ.
    /// </summary>
    public sealed class ShapeRegion
    {
        public ShapeRegion(IReadOnlyList<Curve> contours, double smallestStoneMm, double flattenToleranceMm = 0.02)
        {
            Contours = contours.Select(c => CurveFlattener.Flatten(c, flattenToleranceMm)).Where(f => f.IsClosed).ToList();
            if (Contours.Count == 0)
            {
                throw new ArgumentException("Нужна замкнутая форма.", nameof(contours));
            }

            // Шаг карты — примерно восьмая часть самого мелкого камня (как у контурной заливки).
            Field = SignedDistanceField.Build(Contours, Math.Max(0.05, smallestStoneMm / 8.0));

            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (FlattenedCurve f in Contours)
            {
                foreach (FlattenedPoint p in f.Points)
                {
                    minX = Math.Min(minX, p.Position.X);
                    minY = Math.Min(minY, p.Position.Y);
                    maxX = Math.Max(maxX, p.Position.X);
                    maxY = Math.Max(maxY, p.Position.Y);
                }
            }

            Min = new Point2D(minX, minY);
            Max = new Point2D(maxX, maxY);

            double best = double.MinValue;
            Point2D deepest = (Min + Max) / 2;
            for (int iy = 0; iy < Field.Height; iy++)
            {
                for (int ix = 0; ix < Field.Width; ix++)
                {
                    double v = Field.ValueAt(ix, iy);
                    if (v > best)
                    {
                        best = v;
                        deepest = Field.PositionOf(ix, iy);
                    }
                }
            }

            MaxDepth = Math.Max(0, best);
            DeepestPoint = deepest;
        }

        public IReadOnlyList<FlattenedCurve> Contours { get; }

        public SignedDistanceField Field { get; }

        /// <summary>Левый нижний угол габаритов формы.</summary>
        public Point2D Min { get; }

        /// <summary>Правый верхний угол габаритов формы.</summary>
        public Point2D Max { get; }

        /// <summary>Самое большое расстояние от края внутри формы («толщина» формы пополам).</summary>
        public double MaxDepth { get; }

        /// <summary>Самая «глубокая» точка — дальше всего от края; центр для заливки «от центра».</summary>
        public Point2D DeepestPoint { get; }

        /// <summary>Расстояние от точки до края формы: внутри — плюс, снаружи — минус.</summary>
        public double Depth(Point2D p) => Field.ValueAt(p);

        /// <summary>Помещается ли камень радиуса <paramref name="radius"/> с отступом от края <paramref name="margin"/>.</summary>
        public bool Fits(Point2D center, double radius, double margin) => Field.ValueAt(center) >= radius + margin - 1e-9;
    }

    /// <summary>
    /// Жадная укладка без наложений: камень добавляется, только если он ни на кого не налезает.
    /// Соседи ищутся по клеткам сетки — быстро и на десятках тысяч камней.
    /// </summary>
    internal sealed class StonePacker
    {
        /// <summary>Сколько стразам можно «налезть» из-за погрешности расчёта, мм.</summary>
        public const double Tolerance = 0.02;

        private readonly double gap;
        private readonly double cell;
        private readonly Dictionary<(int, int), List<int>> cells = new Dictionary<(int, int), List<int>>();

        public StonePacker(double maxDiameterMm, double gapMm)
        {
            gap = gapMm;
            cell = Math.Max(0.05, maxDiameterMm + gapMm);
        }

        public List<PlacedStone> Stones { get; } = new List<PlacedStone>();

        public bool Fits(Point2D center, double diameter)
        {
            (int cx, int cy) = Key(center);
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (!cells.TryGetValue((cx + dx, cy + dy), out List<int>? list))
                    {
                        continue;
                    }

                    foreach (int i in list)
                    {
                        // Допуск 0,02 мм: ряды строятся по карте расстояний с точностью до сотых — без
                        // допуска стразы, стоящие ровно вплотную, отбрасывались бы через одну.
                        PlacedStone s = Stones[i];
                        if (Point2D.Distance(s.Center, center) < (s.DiameterMm + diameter) / 2 + gap - Tolerance)
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        public bool TryAdd(Point2D center, double diameter, int rowId = -1)
        {
            if (!Fits(center, diameter))
            {
                return false;
            }

            Add(new PlacedStone(center, diameter, isCorner: false, rowId));
            return true;
        }

        public void Add(PlacedStone stone)
        {
            Stones.Add(stone);
            (int, int) key = Key(stone.Center);
            if (!cells.TryGetValue(key, out List<int>? list))
            {
                list = new List<int>();
                cells[key] = list;
            }

            list.Add(Stones.Count - 1);
        }

        private (int, int) Key(Point2D p) => ((int)Math.Floor(p.X / cell), (int)Math.Floor(p.Y / cell));
    }
}
