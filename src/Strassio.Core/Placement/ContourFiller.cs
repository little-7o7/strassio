using System;
using System.Collections.Generic;
using System.Linq;
using Strassio.Core.Geometry;

namespace Strassio.Core.Placement
{
    /// <summary>
    /// Методы F3 «контурная», F4 «комбинированная» и F5 «кант» (docs/SPEC.md, раздел 5): ряды от
    /// края внутрь, повторяя форму. F3 — рядов сколько поместится, до самой середины;
    /// F5 «кант» — только MaxRings рядов, внутри пусто (FillCenter=false); F4 — MaxRings рядов
    /// по краю плюс сетка/соты внутри (FillCenter=true).
    ///
    /// Каждый ряд — линия равного расстояния до края (<see cref="IsoContour"/> по карте
    /// <see cref="SignedDistanceField"/>): первый ряд на расстоянии «радиус + отступ», каждый
    /// следующий на шаг ряда дальше внутрь. Раньше ряды строились смещением контура, и на звезде
    /// или другой фигуре с тонкими местами контур выворачивался наизнанку — ряды путались и
    /// оставляли дыры. Линия равного расстояния так сломаться не может и сама распадается на
    /// несколько колец там, где распадается сама форма.
    /// </summary>
    public static class ContourFiller
    {
        public static List<PlacedStone> Fill(Curve boundary, ContourFillOptions options) =>
            Fill(new[] { boundary }, options);

        /// <summary>
        /// Форма может состоять из нескольких контуров — внешняя граница и отверстия
        /// (буквы «О», «А», кольца). Ряды тогда идут и снаружи, и вокруг дырки.
        /// </summary>
        public static List<PlacedStone> Fill(IReadOnlyList<Curve> boundaries, ContourFillOptions options)
        {
            if (boundaries == null || boundaries.Count == 0)
            {
                return new List<PlacedStone>();
            }

            List<FlattenedCurve> flats = boundaries
                .Select(b => CurveFlattener.Flatten(b, options.FlattenToleranceMm))
                .ToList();

            double radius = options.StoneDiameterMm / 2;
            double stoneStep = options.StoneDiameterMm + options.GapMm;
            double rowSpacing = stoneStep;
            double firstLevel = radius + options.MarginFromEdgeMm;

            SignedDistanceField field = BuildField(flats, options);

            var scatterOptions = new LineScatterOptions
            {
                StoneDiameterMm = options.StoneDiameterMm,
                GapMm = options.GapMm,
                Mode = StepMode.FitEven,
                CornerAngleThresholdDeg = options.CornerAngleThresholdDeg,
                FlattenToleranceMm = options.FlattenToleranceMm,
            };

            var result = new List<PlacedStone>();
            int ringsPlaced = 0;
            double lastLevel = 0;

            // Потолок на всякий случай: дальше половины меньшей стороны формы рядов быть не может.
            int safetyMaxRings = SafetyMaxRings(flats, rowSpacing);

            for (int ring = 0; ring < safetyMaxRings; ring++)
            {
                if (options.MaxRings.HasValue && ring >= options.MaxRings.Value)
                {
                    break;
                }

                double level = firstLevel + ring * rowSpacing;
                List<List<Point2D>> loops = IsoContour.Trace(field, level);
                if (loops.Count == 0)
                {
                    break;
                }

                bool placedAnything = false;
                foreach (List<Point2D> loop in loops)
                {
                    placedAnything |= PlaceRing(result, loop, ring, stoneStep, options, scatterOptions);
                }

                if (!placedAnything)
                {
                    break;
                }

                ringsPlaced = ring + 1;
                lastLevel = level;
            }

            if (options.FillCenter)
            {
                AddCenterFill(result, boundaries, field, options, ringsPlaced > 0 ? lastLevel + rowSpacing : 0);
            }

            List<PlacedStone> fixedStones = IntersectionFixer.RemoveOverlaps(result);

            if (options.FillCenter)
            {
                FillGaps(fixedStones, field, options);
            }

            return fixedStones;
        }

        private static SignedDistanceField BuildField(
            IReadOnlyList<FlattenedCurve> flats, ContourFillOptions options)
        {
            // Шаг сетки мельче стразы примерно в 8 раз: ряд получается гладким, а считается быстро.
            // Ниже 0,05 мм опускаться незачем — это уже точнее, чем CorelDRAW показывает координаты.
            double cell = Math.Max(0.05, options.StoneDiameterMm / 8.0);
            return SignedDistanceField.Build(flats, cell);
        }

        private static int SafetyMaxRings(IReadOnlyList<FlattenedCurve> flats, double rowSpacing)
        {
            // Берём самый большой контур: это внешняя граница. Отверстия (буква «О») меньше неё
            // и ограничивать число рядов всей формы не должны.
            double largestSide = 0;
            foreach (FlattenedCurve flat in flats)
            {
                (double width, double height) = CurveMetrics.BoundingSize(flat);
                largestSide = Math.Max(largestSide, Math.Max(width, height));
            }

            if (largestSide <= 0 || rowSpacing <= 0)
            {
                return 4;
            }

            return Math.Max(4, (int)(largestSide / (2 * rowSpacing)) + 4);
        }

        /// <summary>
        /// Расставляет стразы по одному кольцу. Если кольцо уже настолько мало, что двум стразам
        /// на нём не разойтись, ставим одну стразу в его середине — так центр формы аккуратно
        /// добивается сам собой, без дырки (F3, раздел 5 ТЗ).
        /// </summary>
        private static bool PlaceRing(
            List<PlacedStone> result, List<Point2D> loop, int ring, double stoneStep,
            ContourFillOptions options, LineScatterOptions scatterOptions)
        {
            double perimeter = Perimeter(loop);

            if (perimeter < 2 * stoneStep)
            {
                result.Add(new PlacedStone(Centroid(loop), options.StoneDiameterMm, false, ring));
                return true;
            }

            Curve ringCurve;
            try
            {
                ringCurve = Curve.FromPolyline(loop, isClosed: true);
            }
            catch (ArgumentException)
            {
                return false; // выродившееся кольцо — пропускаем
            }

            foreach (PlacedStone stone in LineScatterer.Scatter(ringCurve, scatterOptions))
            {
                result.Add(new PlacedStone(stone.Center, stone.DiameterMm, stone.IsCorner, ring));
            }

            return true;
        }

        /// <summary>
        /// Добивка середины сеткой (F4 «комбинированная», а при MaxRings=0 — обычная заливка).
        /// Берём обычную сетку по всей форме и оставляем только то, что лежит достаточно глубоко,
        /// чтобы не налезть на последний ряд канта.
        /// </summary>
        private static void AddCenterFill(
            List<PlacedStone> result, IReadOnlyList<Curve> boundaries, SignedDistanceField field,
            ContourFillOptions options, double minDistanceFromEdgeMm)
        {
            var gridOptions = new GridFillOptions
            {
                StoneDiameterMm = options.StoneDiameterMm,
                GapMm = options.GapMm,
                Pattern = options.CenterPattern,
                MarginFromEdgeMm = options.MarginFromEdgeMm,
                FlattenToleranceMm = options.FlattenToleranceMm,
            };

            foreach (PlacedStone stone in GridFiller.Fill(boundaries, gridOptions))
            {
                if (field.ValueAt(stone.Center) >= minDistanceFromEdgeMm - 1e-9)
                {
                    result.Add(stone);
                }
            }
        }

        /// <summary>
        /// Последний проход: там, где ряды сошлись в середине формы (у звезды — в центре, где
        /// встречаются лучи), может остаться пустое место, в которое страза ещё помещается.
        /// Обходим узлы карты расстояний, ищем такие места и ставим туда стразы — сначала в самые
        /// глубокие, чтобы новая страза вставала посередине дырки, а не у её края.
        /// </summary>
        private static void FillGaps(List<PlacedStone> stones, SignedDistanceField field, ContourFillOptions options)
        {
            double radius = options.StoneDiameterMm / 2;
            double minDepth = radius + options.MarginFromEdgeMm;
            double step = options.StoneDiameterMm + options.GapMm;

            // Небольшой допуск: ряды построены по сетке, и соседние стразы стоят с точностью
            // до сотых миллиметра. Без допуска дырка «ровно на одну стразу» оставалась бы пустой.
            double required = step - 0.02;

            var grid = new StoneGrid(step);
            foreach (PlacedStone stone in stones)
            {
                grid.Add(stone.Center);
            }

            // Сначала одним проходом по стразам отмечаем узлы карты, которые уже заняты (рядом
            // есть страза). Проверять соседей для каждого из сотен тысяч узлов по отдельности
            // заметно дольше, а на 10 000 страз это уже ощутимо.
            bool[] covered = MarkCovered(stones, field, required);

            var candidates = new List<(Point2D Point, double Depth)>();
            for (int iy = 0; iy < field.Height; iy++)
            {
                for (int ix = 0; ix < field.Width; ix++)
                {
                    if (covered[iy * field.Width + ix])
                    {
                        continue;
                    }

                    double depth = field.ValueAt(ix, iy);
                    if (depth >= minDepth)
                    {
                        candidates.Add((field.PositionOf(ix, iy), depth));
                    }
                }
            }

            foreach ((Point2D point, double _) in candidates.OrderByDescending(c => c.Depth))
            {
                if (grid.HasStoneCloserThan(point, required))
                {
                    continue; // место уже заняла страза, поставленная чуть раньше в этом же проходе
                }

                stones.Add(new PlacedStone(point, options.StoneDiameterMm, false, rowId: -1));
                grid.Add(point);
            }
        }

        private static bool[] MarkCovered(List<PlacedStone> stones, SignedDistanceField field, double distance)
        {
            var covered = new bool[field.Width * field.Height];
            double cell = field.CellSizeMm;
            double distanceSquared = distance * distance;

            foreach (PlacedStone stone in stones)
            {
                double gx = (stone.Center.X - field.Origin.X) / cell;
                double gy = (stone.Center.Y - field.Origin.Y) / cell;
                int x0 = Math.Max(0, (int)Math.Floor(gx - distance / cell));
                int x1 = Math.Min(field.Width - 1, (int)Math.Ceiling(gx + distance / cell));
                int y0 = Math.Max(0, (int)Math.Floor(gy - distance / cell));
                int y1 = Math.Min(field.Height - 1, (int)Math.Ceiling(gy + distance / cell));

                for (int iy = y0; iy <= y1; iy++)
                {
                    double dy = field.Origin.Y + iy * cell - stone.Center.Y;
                    for (int ix = x0; ix <= x1; ix++)
                    {
                        double dx = field.Origin.X + ix * cell - stone.Center.X;
                        if (dx * dx + dy * dy < distanceSquared)
                        {
                            covered[iy * field.Width + ix] = true;
                        }
                    }
                }
            }

            return covered;
        }

        /// <summary>Стразы, разложенные по клеткам размером с шаг, — чтобы быстро искать соседей.</summary>
        private sealed class StoneGrid
        {
            private readonly double cellSize;
            private readonly Dictionary<long, List<Point2D>> cells = new Dictionary<long, List<Point2D>>();

            public StoneGrid(double cellSize)
            {
                this.cellSize = cellSize;
            }

            public void Add(Point2D point)
            {
                long key = Key((int)Math.Floor(point.X / cellSize), (int)Math.Floor(point.Y / cellSize));
                if (!cells.TryGetValue(key, out List<Point2D> list))
                {
                    list = new List<Point2D>();
                    cells[key] = list;
                }

                list.Add(point);
            }

            public bool HasStoneCloserThan(Point2D point, double distance)
            {
                int cx = (int)Math.Floor(point.X / cellSize);
                int cy = (int)Math.Floor(point.Y / cellSize);
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (!cells.TryGetValue(Key(cx + dx, cy + dy), out List<Point2D> list))
                        {
                            continue;
                        }

                        foreach (Point2D other in list)
                        {
                            if (Point2D.Distance(point, other) < distance)
                            {
                                return true;
                            }
                        }
                    }
                }

                return false;
            }

            private static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;
        }

        private static double Perimeter(List<Point2D> loop)
        {
            double sum = 0;
            for (int i = 0; i < loop.Count - 1; i++)
            {
                sum += Point2D.Distance(loop[i], loop[i + 1]);
            }

            return sum;
        }

        private static Point2D Centroid(List<Point2D> loop)
        {
            // Последняя точка дублирует первую — её не считаем.
            int n = loop.Count - 1;
            if (n <= 0)
            {
                return loop[0];
            }

            double sx = 0, sy = 0;
            for (int i = 0; i < n; i++)
            {
                sx += loop[i].X;
                sy += loop[i].Y;
            }

            return new Point2D(sx / n, sy / n);
        }
    }
}
