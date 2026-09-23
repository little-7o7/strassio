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

                // Сначала пробуем построить кольцо смещением самого контура — тогда у квадрата
                // и любой фигуры с углами ряды остаются с углами и идут параллельно друг другу.
                // Не вышло (фигура тонкая, контур вывернулся) — работает прежний способ по линии
                // равного расстояния, он не ломается никогда.
                List<List<Point2D>> loops = OffsetRing(flats, level, field, options) ?? IsoContour.Trace(field, level);
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

                // Доводка середины: ряды с двух сторон приходят туда под углом и оставляют завиток.
                // Заменяем эту тесноту одним ровным рядом по самой середине — «прожилкой» листа.
                if (options.MidribAlongSkeleton && ringsPlaced > 0)
                {
                    LayMidrib(result, flats, field, options, stoneStep, scatterOptions);
                }
            }

            // Запас на наложение — по зазору пользователя: при зазоре 0 касающиеся стразы — это норма,
            // а не наложение (раньше здесь стояли жёсткие 0,1 мм, и при зазоре 0 «Кант» терял почти все
            // стразы — скриншоты автора).
            List<PlacedStone> fixedStones = IntersectionFixer.RemoveOverlaps(result, Math.Min(0.1, options.GapMm / 2), 0.02);

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
        /// <summary>
        /// Кольцо ряда как смещённая копия контура с острым углом (митром).
        ///
        /// Линия равного расстояния (IsoContour) скругляет углы тем сильнее, чем дальше ряд от
        /// края: у квадрата второй и третий ряд уже с круглыми углами, ряды перестают быть
        /// параллельными и в углах разъезжаются — автор видел это как «пропускает стразы».
        /// Смещение контура углы сохраняет, и ряды идут ровно друг за другом.
        ///
        /// Проверяем результат по тому же полю расстояний: каждая точка смещённой линии должна
        /// лежать на нужной глубине. Если смещение вывернулось наизнанку (тонкая фигура, острый
        /// шип), проверка не сойдётся — возвращаем null, и кольцо строится прежним способом.
        /// Работает только для одиночного замкнутого контура: у формы с отверстиями кольца могут
        /// пересечься, там линия равного расстояния надёжнее.
        /// </summary>
        private static List<List<Point2D>>? OffsetRing(
            IReadOnlyList<FlattenedCurve> flats, double level, SignedDistanceField field, ContourFillOptions options)
        {
            if (flats.Count != 1 || !flats[0].IsClosed || level <= 0)
            {
                return null;
            }

            FlattenedCurve flat = flats[0];
            double sign = InwardSign(flat, field, options.FlattenToleranceMm);
            if (sign == 0)
            {
                return null;
            }

            List<Point2D> loop = CurveOffsetter.Offset(flat, sign * level, options.FlattenToleranceMm, roundOuterCorners: false);

            // У квадрата смещённое кольцо — это всего пять точек (четыре угла и замыкание),
            // поэтому нижняя граница здесь именно такая маленькая.
            if (loop.Count < 4)
            {
                return null;
            }

            if (SelfIntersects(loop))
            {
                return null; // контур вывернулся — кольцо строит линия равного расстояния
            }

            // Тонкие шипы (кончик звезды) смещение делает ещё тоньше, и ряд там рвётся: камни
            // не помещаются и вычищаются как наложения. На таких фигурах линия равного расстояния
            // скругляет кончик и укладывает больше камней — оставляем её.
            if (SharpestAngleDeg(loop) < 45)
            {
                return null;
            }

            // Допуск: клетка поля плюс десятая доля камня — ряд должен идти именно на своей глубине.
            double tolerance = field.CellSizeMm + options.StoneDiameterMm * 0.1;
            foreach (Point2D p in loop)
            {
                if (Math.Abs(field.ValueAt(p) - level) > tolerance)
                {
                    return null;
                }
            }

            return new List<List<Point2D>> { loop };
        }

        /// <summary>
        /// Пересекает ли ломаная сама себя. Смещение внутрь на тонком месте фигуры выворачивает
        /// контур — такое кольцо брать нельзя. Проверка простым перебором пар отрезков; на очень
        /// подробных контурах (гладкие кривые) она не запускается — там смещение и линия равного
        /// расстояния всё равно совпадают, поэтому и проверять нечего.
        /// </summary>
        private static bool SelfIntersects(List<Point2D> loop)
        {
            int n = loop.Count;
            if (n > 400)
            {
                return false;
            }

            for (int i = 0; i + 1 < n; i++)
            {
                for (int j = i + 2; j + 1 < n; j++)
                {
                    if (i == 0 && j + 2 == n)
                    {
                        continue; // первый и последний отрезки сходятся в точке замыкания
                    }

                    if (SegmentsCross(loop[i], loop[i + 1], loop[j], loop[j + 1]))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Самый острый угол ломаной в градусах (180 — идеально гладкая линия).</summary>
        private static double SharpestAngleDeg(List<Point2D> loop)
        {
            int n = loop.Count;
            if (n < 4)
            {
                return 180;
            }

            // Последняя точка повторяет первую — вершины считаем по 0…n-2.
            int count = n - 1;
            double sharpest = 180;

            for (int i = 0; i < count; i++)
            {
                Point2D prev = loop[(i - 1 + count) % count];
                Point2D cur = loop[i];
                Point2D next = loop[(i + 1) % count];

                Point2D u1 = (prev - cur).Normalized();
                Point2D u2 = (next - cur).Normalized();
                if (u1 == Point2D.Zero || u2 == Point2D.Zero)
                {
                    continue;
                }

                double cos = u1.X * u2.X + u1.Y * u2.Y;
                cos = cos < -1 ? -1 : cos > 1 ? 1 : cos;
                sharpest = Math.Min(sharpest, Math.Acos(cos) * 180 / Math.PI);
            }

            return sharpest;
        }

        private static bool SegmentsCross(Point2D a1, Point2D a2, Point2D b1, Point2D b2)
        {
            double Side(Point2D p, Point2D q, Point2D r) => (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);

            double d1 = Side(a1, a2, b1);
            double d2 = Side(a1, a2, b2);
            double d3 = Side(b1, b2, a1);
            double d4 = Side(b1, b2, a2);

            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }

        /// <summary>В какую сторону смещать, чтобы уйти внутрь фигуры: +1 или −1 (0 — не понятно).</summary>
        private static double InwardSign(FlattenedCurve flat, SignedDistanceField field, double toleranceMm)
        {
            const double Probe = 0.2;

            double Depth(double sign)
            {
                List<Point2D> probe = CurveOffsetter.Offset(flat, sign * Probe, toleranceMm, roundOuterCorners: false);
                if (probe.Count == 0)
                {
                    return double.NegativeInfinity;
                }

                double sum = 0;
                foreach (Point2D p in probe)
                {
                    sum += field.ValueAt(p);
                }

                return sum / probe.Count;
            }

            double plus = Depth(1);
            double minus = Depth(-1);
            if (Math.Abs(plus - minus) < 1e-9)
            {
                return 0;
            }

            return plus > minus ? 1 : -1;
        }

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
        /// <summary>
        /// «Прожилка»: один ровный ряд по самой середине формы вместо тесноты, которая получается
        /// там, где ряды с двух сторон сходятся под углом (сравнение автора с ручной работой —
        /// у него ряды сходятся к одной чистой линии, у нас оставался белый завиток).
        ///
        /// Делается осторожно: берётся только главная линия середины (самый длинный путь скелета),
        /// рядом с ней убираются старые стразы, и на её место кладётся ряд обычной подгонкой шага.
        /// Ветки скелета (лучи звезды, отростки букв) не трогаются совсем. Если середина короткая —
        /// например у круга, где она вырождается в точку, — ничего не меняем.
        /// </summary>
        private static void LayMidrib(
            List<PlacedStone> stones, IReadOnlyList<FlattenedCurve> flats, SignedDistanceField field,
            ContourFillOptions options, double stoneStep, LineScatterOptions scatterOptions)
        {
            // Середину считаем на отдельной крупной сетке: клетка в половину камня. Точнее не нужно —
            // линия всё равно сглаживается, — а считается это примерно в шестнадцать раз быстрее,
            // чем на рабочей сетке заливки (иначе на десяти тысячах страз ждать больше десяти секунд).
            double coarseCell = Math.Max(0.2, options.StoneDiameterMm / 2);
            SignedDistanceField coarse = SignedDistanceField.Build(flats, coarseCell);

            Skeleton skeleton = Skeleton.Build(coarse);
            List<Point2D> path = skeleton.LongestPath();
            if (path.Count < 3)
            {
                return;
            }

            double pathLength = 0;
            for (int i = 1; i < path.Count; i++)
            {
                pathLength += Point2D.Distance(path[i - 1], path[i]);
            }

            // Слишком короткая середина — менять нечего.
            if (pathLength < 3 * stoneStep)
            {
                return;
            }

            double radius = options.StoneDiameterMm / 2;

            // Убираем стразы вплотную к середине. Быстрая отсечка по карте расстояний до скелета,
            // потом точная проверка по самой линии — чтобы не задеть стразы у других веток.
            // Расчищаем полосу почти в целый шаг: иначе новому ряду не хватает места и его же
            // стразы вычищаются потом как наложения (проверено на листе автора — оставалась одна).
            double clearance = stoneStep;
            stones.RemoveAll(s =>
            {
                int ix = (int)Math.Round((s.Center.X - coarse.Origin.X) / coarse.CellSizeMm);
                int iy = (int)Math.Round((s.Center.Y - coarse.Origin.Y) / coarse.CellSizeMm);
                if (ix < 0 || iy < 0 || ix >= coarse.Width || iy >= coarse.Height)
                {
                    return false;
                }

                return skeleton.DistanceAt(ix, iy) < clearance * 1.6 && DistanceToPath(s.Center, path) < clearance;
            });

            // И кладём ряд по самой середине.
            Curve line;
            try
            {
                line = Curve.FromPolyline(path, isClosed: false);
            }
            catch (ArgumentException)
            {
                return;
            }

            foreach (PlacedStone stone in LineScatterer.Scatter(line, scatterOptions))
            {
                if (field.ValueAt(stone.Center) >= radius)
                {
                    stones.Add(new PlacedStone(stone.Center, options.StoneDiameterMm, false, rowId: -2));
                }
            }
        }

        /// <summary>Расстояние от точки до ломаной.</summary>
        private static double DistanceToPath(Point2D point, List<Point2D> path)
        {
            double best = double.MaxValue;

            for (int i = 1; i < path.Count; i++)
            {
                Point2D a = path[i - 1];
                Point2D b = path[i];
                Point2D ab = b - a;
                double len2 = ab.Dot(ab);
                double t = len2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, (point - a).Dot(ab) / len2));
                best = Math.Min(best, Point2D.Distance(a + ab * t, point));
            }

            return best;
        }

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
