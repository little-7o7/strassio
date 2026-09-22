using System;
using System.Collections.Generic;
using System.Linq;
using Strassio.Core.Geometry;
using Strassio.Core.Placement;

namespace Strassio.Core.Methods
{
    /// <summary>Результат метода: стразы и (для «только показать») номера тех, что накладываются.</summary>
    public sealed class MethodResult
    {
        public MethodResult(IReadOnlyList<PlacedStone> stones, IReadOnlyList<int> conflictIndices)
        {
            Stones = stones;
            ConflictIndices = conflictIndices;
        }

        public IReadOnlyList<PlacedStone> Stones { get; }

        /// <summary>Стразы с наложением — при действии «только показать» (раздел 6.4); иначе пусто.</summary>
        public IReadOnlyList<int> ConflictIndices { get; }
    }

    /// <summary>
    /// Запускает метод расстановки с параметрами из докера: переводит <see cref="MethodParameters"/>
    /// в опции алгоритмов Placement и вызывает нужный. Одна точка входа и для CorelDRAW, и для
    /// Preview, и для тестов.
    /// </summary>
    public static class MethodRunner
    {
        /// <param name="contours">Все контуры фигуры (внешний и отверстия), мм.</param>
        /// <param name="stoneDiameterMm">Диаметр выбранного камня.</param>
        /// <param name="sizes">
        /// Таблица размеров «название → диаметр, мм» — для методов с несколькими размерами (L2 с другими
        /// крайними рядами, L5, L6, L8). Нет таблицы или размера в ней — берётся основной камень.
        /// </param>
        /// <param name="guides">Вторая линия для F7 (вторая кривая перехода) и F8 (направляющая).</param>
        public static MethodResult Run(
            MethodKind kind, IReadOnlyList<Curve> contours, double stoneDiameterMm, MethodParameters p,
            IReadOnlyDictionary<string, double>? sizes = null, IReadOnlyList<Curve>? guides = null)
        {
            if (contours == null || contours.Count == 0)
            {
                throw new ArgumentException("Нужен хотя бы один контур.", nameof(contours));
            }

            if (p == null)
            {
                throw new ArgumentNullException(nameof(p));
            }

            double d = stoneDiameterMm;
            switch (kind)
            {
                case MethodKind.L1:
                    return Plain(LineScatterer.Scatter(OuterContour(contours), LineOptions(d, p)));

                case MethodKind.L2:
                    return AroundLine(OuterContour(contours), d, p, sizes);

                case MethodKind.L3:
                    return OffsetLine(OuterContour(contours), d, p);

                case MethodKind.L4:
                    return Calligraphy(OuterContour(contours), d, p);

                case MethodKind.L5:
                    return SizeTransition(OuterContour(contours), d, p, sizes);

                case MethodKind.L6:
                    return SizeAlternation(OuterContour(contours), d, p, sizes);

                case MethodKind.L7:
                    return Dashes(OuterContour(contours), d, p);

                case MethodKind.L8:
                    return Accents(OuterContour(contours), d, p, sizes);

                case MethodKind.Outline:
                    return Plain(DesignOutline(contours, d, p));

                case MethodKind.F1:
                case MethodKind.F2:
                    var grid = new GridFillOptions
                    {
                        StoneDiameterMm = d,
                        GapMm = p.GapMm,
                        Pattern = kind == MethodKind.F1 ? GridPattern.Square : GridPattern.Honeycomb,
                        AngleDeg = p.AngleDeg,
                        MarginFromEdgeMm = p.EdgeMarginMm,
                    };
                    return Plain(p.AutoGrid == MethodChoices.AutoOff
                        ? GridFiller.Fill(contours, grid)
                        : AdvancedFillers.AutoGrid(contours, grid, tryAngles: p.AutoGrid == MethodChoices.AutoShiftAngle));

                case MethodKind.F6:
                    return Plain(AdvancedFillers.Centerline(contours, d, p.GapMm, p.EdgeMarginMm));

                case MethodKind.F7:
                    if (guides == null || guides.Count == 0)
                    {
                        throw new ArgumentException("Для перехода нужны две кривые.", nameof(guides));
                    }

                    return Plain(AdvancedFillers.Blend(OuterContour(contours), guides[0], d, p.GapMm, p.RowGapMm));

                case MethodKind.F8:
                    if (guides == null || guides.Count == 0)
                    {
                        throw new ArgumentException("Нужна направляющая линия.", nameof(guides));
                    }

                    return Plain(AdvancedFillers.AlongGuide(contours, guides[0], d, p.GapMm, p.RowGapMm, p.EdgeMarginMm));

                case MethodKind.F9:
                    return Plain(AdvancedFillers.FromCenter(contours, d, p.GapMm, p.EdgeMarginMm, spiral: p.CenterMode == MethodChoices.CenterSpiral));

                case MethodKind.F10:
                    List<double> mix = SizePatterns.Parse(p.MixSizes, sizes);
                    if (mix.Count == 0)
                    {
                        mix.Add(d);
                    }

                    return Plain(AdvancedFillers.Random(contours, mix, p.GapMm, p.EdgeMarginMm, p.Variant));

                case MethodKind.F11:
                    return Plain(AdvancedFillers.Gradient(
                        contours, SizeRange(Diameter(p.FromSize, d, sizes), Diameter(p.ToSize, d, sizes), sizes),
                        p.GapMm, p.EdgeMarginMm, horizontal: p.GradientDirection == MethodChoices.GradientHorizontal));

                case MethodKind.F12:
                    double small = Diameter(p.FillSize, d * 0.6, sizes);
                    return Plain(AdvancedFillers.FillGaps(contours, d, Math.Min(small, d), p.GapMm, p.EdgeMarginMm));

                case MethodKind.F3:
                case MethodKind.F4:
                case MethodKind.F5:
                    return Plain(ContourFiller.Fill(contours, new ContourFillOptions
                    {
                        StoneDiameterMm = d,
                        GapMm = p.GapMm,
                        MarginFromEdgeMm = p.EdgeMarginMm,
                        MaxRings = kind == MethodKind.F3 ? (int?)null : p.Rings,
                        FillCenter = kind != MethodKind.F5,
                        CenterPattern = p.CenterPattern == MethodChoices.PatternSquare ? GridPattern.Square : GridPattern.Honeycomb,
                    }));

                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        /// <summary>
        /// Внешний контур фигуры — самый большой по габаритам. Методы по линии (L1-L3) работают
        /// именно по нему, отверстия им не нужны.
        /// </summary>
        public static Curve OuterContour(IReadOnlyList<Curve> contours)
        {
            Curve best = contours[0];
            double bestSize = -1;

            foreach (Curve contour in contours)
            {
                (double width, double height) = CurveMetrics.BoundingSize(CurveFlattener.Flatten(contour));
                double size = width * height;
                if (size > bestSize)
                {
                    bestSize = size;
                    best = contour;
                }
            }

            return best;
        }

        /// <summary>
        /// Знак смещения «наружу». У замкнутого контура «внутрь» — когда знак смещения совпадает со
        /// знаком площади (см. ContourFiller), значит наружу — противоположный. У незамкнутой линии
        /// «наружу» — просто одна из сторон (положительное смещение), «внутрь» — другая.
        /// </summary>
        internal static double OutwardSign(Curve curve)
        {
            FlattenedCurve flat = CurveFlattener.Flatten(curve);
            if (!flat.IsClosed)
            {
                return 1;
            }

            double area = CurveMetrics.SignedArea(flat);
            return area > 0 ? -1 : 1;
        }

        private static MethodResult Plain(IReadOnlyList<PlacedStone> stones) =>
            new MethodResult(stones, Array.Empty<int>());

        private static LineScatterOptions LineOptions(double d, MethodParameters p)
        {
            var options = new LineScatterOptions
            {
                StoneDiameterMm = d,
                GapMm = p.GapMm,
                StartOffsetMm = p.StartOffsetMm,
                EndMarginMm = p.EndMarginMm,
                Reverse = p.Reverse,
                CornerAngleThresholdDeg = p.CornerAngleDeg,
            };

            switch (p.StepMode)
            {
                case MethodChoices.StepExact:
                    options.Mode = StepMode.ExactStep;
                    options.ExactStepMm = p.ExactStepMm;
                    break;
                case MethodChoices.StepCount:
                    options.Mode = StepMode.ExactCount;
                    options.ExactCount = p.ExactCount;
                    break;
                default:
                    options.Mode = StepMode.FitEven;
                    break;
            }

            return options;
        }

        /// <summary>Опции ряда для L2/L3: зазор и порог угла из параметров, шаг — подгонка.</summary>
        private static LineScatterOptions RowOptions(double d, MethodParameters p, double startOffsetMm) =>
            new LineScatterOptions
            {
                StoneDiameterMm = d,
                GapMm = p.GapMm,
                Mode = StepMode.FitEven,
                StartOffsetMm = startOffsetMm,
                CornerAngleThresholdDeg = p.CornerAngleDeg,
            };

        private static CornerStyle Corners(MethodParameters p) =>
            p.Corners == MethodChoices.CornersSharp ? CornerStyle.Sharp : CornerStyle.Round;

        /// <summary>
        /// L2 «вокруг линии». Ряды идут через «зазор между рядами»; в обе стороны — симметрично
        /// относительно линии (при нечётном числе рядов средний лежит на самой линии). Ряд ближе
        /// к линии идёт в списке раньше — при наложениях он главнее (раздел 6.3).
        /// </summary>
        private static MethodResult AroundLine(Curve curve, double d, MethodParameters p, IReadOnlyDictionary<string, double>? sizes)
        {
            double outward = OutwardSign(curve);
            int count = Math.Max(1, p.RowCount);
            bool bothSides = p.RowSide != MethodChoices.SideOutside && p.RowSide != MethodChoices.SideInside;

            // Крайние ряды — своим размером (раздел 4: «центр ss10, края ss6»); остальные — основным.
            double edge = Diameter(p.EdgeSize, d, sizes);
            var diameters = new double[count];
            for (int i = 0; i < count; i++)
            {
                bool isEdge = count > 1 && (i == count - 1 || (bothSides && i == 0));
                diameters[i] = isEdge ? edge : d;
            }

            // Центры рядов поперёк линии: соседние — через половины их диаметров и зазор между рядами.
            var across = new double[count];
            for (int i = 1; i < count; i++)
            {
                across[i] = across[i - 1] + (diameters[i - 1] + diameters[i]) / 2 + p.RowGapMm;
            }

            var rows = new List<(double Offset, int Position)>();
            for (int i = 0; i < count; i++)
            {
                double offset;
                switch (p.RowSide)
                {
                    case MethodChoices.SideOutside:
                        offset = outward * across[i];
                        break;
                    case MethodChoices.SideInside:
                        offset = -outward * across[i];
                        break;
                    default:
                        offset = across[i] - across[count - 1] / 2;
                        break;
                }

                rows.Add((offset, i));
            }

            RowSpec[] specs = rows
                .OrderBy(r => Math.Abs(r.Offset))
                .Select(r => new RowSpec
                {
                    OffsetMm = r.Offset,
                    CornerStyle = Corners(p),
                    ScatterOptions = RowOptions(
                        diameters[r.Position], p,
                        p.Stagger && r.Position % 2 == 1 ? (diameters[r.Position] + p.GapMm) / 2 : 0),
                })
                .ToArray();

            IReadOnlyList<PlacedStone> stones = RingScatterer.Scatter(curve, specs);
            if (count == 1)
            {
                return Plain(stones);
            }

            IntersectionAction action =
                p.Intersections == MethodChoices.IntersectShift ? IntersectionAction.Shift :
                p.Intersections == MethodChoices.IntersectShow ? IntersectionAction.ShowOnly :
                IntersectionAction.Remove;

            IntersectionFixResult fixedResult = IntersectionFixer.Fix(stones, new IntersectionFixOptions
            {
                Action = action,

                // Наложение — когда стразы ближе заданных зазоров. Половина меньшего зазора — запас на
                // неточность смещённых кривых (при зазоре 0,2 мм это прежние 0,1 мм).
                MinGapMm = Math.Min(p.GapMm, p.RowGapMm) / 2,
            });

            // Номера налезающих страз нужны только для «только показать»: при «удалить»/«сдвинуть» их
            // уже нет или они перестали налезать.
            return new MethodResult(
                fixedResult.Stones,
                action == IntersectionAction.ShowOnly ? fixedResult.ConflictIndices : Array.Empty<int>());
        }

        /// <summary>
        /// L4 «каллиграфия» (раздел 4): число рядов меняется вдоль линии — 1 на концах и все ряды в
        /// середине (или от начала к концу). Строим все ряды, как в L2 «в обе стороны», и в каждом
        /// месте оставляем только столько рядов, сколько там «помещается» по профилю ширины.
        /// </summary>
        private static MethodResult Calligraphy(Curve curve, double d, MethodParameters p)
        {
            int count = Math.Max(1, p.RowCount);
            double rowStep = d + p.RowGapMm;
            var specs = new List<RowSpec>();
            var halfIndex = new List<double>();
            for (int i = 0; i < count; i++)
            {
                double j = i - (count - 1) / 2.0;
                specs.Add(new RowSpec { OffsetMm = j * rowStep, CornerStyle = Corners(p), ScatterOptions = RowOptions(d, p, 0) });
                halfIndex.Add(Math.Abs(j));
            }

            // Ряд у линии — первым: при наложениях он главнее.
            int[] order = Enumerable.Range(0, count).OrderBy(i => halfIndex[i]).ToArray();
            IReadOnlyList<PlacedStone> all = RingScatterer.Scatter(curve, order.Select(i => specs[i]).ToList());

            FlattenedCurve flat = CurveFlattener.Flatten(curve);
            var kept = new List<PlacedStone>();
            foreach (PlacedStone stone in all)
            {
                double j = halfIndex[order[stone.RowId]];
                double t = VariableLineScatterer.ProjectFraction(flat, stone.Center);
                // Ширина в рядах, округлённая: у самого края профиля уже виден последний ряд.
                double rowsHere = Math.Round(1 + (count - 1) * WidthProfile(p.WidthProfile, t));
                if (2 * j + 1 <= rowsHere + 1e-9)
                {
                    kept.Add(stone);
                }
            }

            return Plain(IntersectionFixer.Fix(kept, new IntersectionFixOptions { MinGapMm = Math.Min(p.GapMm, p.RowGapMm) / 2 }).Stones);
        }

        /// <summary>Доля полной ширины линии в точке t (0…1) для L4.</summary>
        private static double WidthProfile(string profile, double t)
        {
            switch (profile)
            {
                case MethodChoices.ProfileGrow:
                    return t;
                case MethodChoices.ProfileShrink:
                    return 1 - t;
                default:
                    return Math.Sin(Math.PI * t);
            }
        }

        /// <summary>
        /// L5 «переход размера» (раздел 4): камни меняют размер вдоль линии — от «с размера» до «до
        /// размера», через все размеры таблицы между ними, каждому — равная доля длины.
        /// </summary>
        private static MethodResult SizeTransition(Curve curve, double d, MethodParameters p, IReadOnlyDictionary<string, double>? sizes)
        {
            List<double> steps = SizeRange(Diameter(p.FromSize, d, sizes), Diameter(p.ToSize, d, sizes), sizes);
            int k = steps.Count;
            IReadOnlyList<PlacedStone> stones = VariableLineScatterer.Scatter(
                curve, (i, t) => steps[Math.Min(k - 1, (int)Math.Floor(t * k))], p.GapMm);
            return Plain(FixSingleRow(stones, p));
        }

        /// <summary>L6 «чередование размеров» по шаблону, например «ss6, ss6, ss10».</summary>
        private static MethodResult SizeAlternation(Curve curve, double d, MethodParameters p, IReadOnlyDictionary<string, double>? sizes)
        {
            List<double> pattern = SizePatterns.Parse(p.SizePattern, sizes);
            if (pattern.Count == 0)
            {
                pattern.Add(d);
            }

            IReadOnlyList<PlacedStone> stones = VariableLineScatterer.Scatter(curve, (i, t) => pattern[i % pattern.Count], p.GapMm);
            return Plain(FixSingleRow(stones, p));
        }

        /// <summary>L7 «пунктир»: ряд, как «по линии», но из каждых (группа + пропуск) мест заняты только первые «группа».</summary>
        private static MethodResult Dashes(Curve curve, double d, MethodParameters p)
        {
            IReadOnlyList<PlacedStone> row = LineScatterer.Scatter(curve, LineOptions(d, p));
            int dash = Math.Max(1, p.DashCount);
            int period = dash + Math.Max(0, p.SkipCount);
            return Plain(row.Where((stone, i) => i % period < dash).ToList());
        }

        /// <summary>
        /// L8 «акценты»: крупный камень на концах линии и/или в углах. Ряд строится как «по линии»,
        /// акценты заменяют камни на своих местах, а соседи, на которых акцент налез, убираются
        /// (оставшиеся раздвигаются, дырки нет).
        /// </summary>
        private static MethodResult Accents(Curve curve, double d, MethodParameters p, IReadOnlyDictionary<string, double>? sizes)
        {
            IReadOnlyList<PlacedStone> row = LineScatterer.Scatter(curve, LineOptions(d, p));
            if (row.Count == 0)
            {
                return Plain(row);
            }

            double accent = Diameter(p.AccentSize, d * 1.5, sizes);
            bool ends = p.AccentWhere != MethodChoices.AccentCorners && !CurveFlattener.Flatten(curve).IsClosed;
            bool corners = p.AccentWhere != MethodChoices.AccentEnds;

            var stones = new List<PlacedStone>(row.Count);
            for (int i = 0; i < row.Count; i++)
            {
                bool isAccent = (ends && (i == 0 || i == row.Count - 1)) || (corners && row[i].IsCorner);

                // Акцент помечаем как «угловой» — у таких приоритет при исправлении наложений (раздел 6.3).
                stones.Add(isAccent
                    ? new PlacedStone(row[i].Center, accent, isCorner: true, row[i].RowId)
                    : new PlacedStone(row[i].Center, row[i].DiameterMm, isCorner: false, row[i].RowId));
            }

            return Plain(FixSingleRow(stones, p));
        }

        /// <summary>
        /// Обводка всего дизайна (раздел 8, [NEW]): ряд страз снаружи вокруг ВСЕХ выделенных фигур сразу,
        /// на отступе «край дизайна — край камня». Фигуры объединяются: там, где они касаются или
        /// перекрываются, обводка идёт вокруг общего силуэта; отверстия внутри дизайна не обводятся.
        /// </summary>
        private static List<PlacedStone> DesignOutline(IReadOnlyList<Curve> contours, double d, MethodParameters p)
        {
            List<FlattenedCurve> flats = contours.Select(c => CurveFlattener.Flatten(c)).Where(f => f.IsClosed).ToList();
            var stones = new List<PlacedStone>();
            if (flats.Count == 0)
            {
                return stones;
            }

            double distance = p.EdgeMarginMm + d / 2;
            SignedDistanceField field = SignedDistanceField.Build(
                flats, Math.Max(0.05, d / 8), paddingMm: distance + 2 * d, union: true);

            // Петли вокруг пустот между фигурами лежат внутри внешней петли — их не обводим.
            List<List<Point2D>> loops = IsoContour.Trace(field, -distance).Where(l => l.Count >= 3).ToList();
            List<FlattenedCurve> loopFlats = loops
                .Select(l => new FlattenedCurve(l.Select(pt => new FlattenedPoint(pt, true)).ToList(), isClosed: true))
                .ToList();

            int row = 0;
            for (int i = 0; i < loops.Count; i++)
            {
                bool inner = false;
                for (int j = 0; j < loops.Count && !inner; j++)
                {
                    inner = j != i && PointInPolygon.IsInside(loopFlats[j], loops[i][0]);
                }

                if (inner)
                {
                    continue;
                }

                foreach (PlacedStone s in LineScatterer.Scatter(Curve.FromPolyline(loops[i], isClosed: true), RowOptions(d, p, 0)))
                {
                    stones.Add(new PlacedStone(s.Center, s.DiameterMm, s.IsCorner, row));
                }

                row++;
            }

            return FixSingleRow(stones, p);
        }

        /// <summary>Убирает наложения в одном ряду (у острых углов, у акцентов); соседи раздвигаются.</summary>
        private static List<PlacedStone> FixSingleRow(IReadOnlyList<PlacedStone> stones, MethodParameters p)
        {
            double minGap = p.GapMm / 2;
            IReadOnlyList<PlacedStone> fixedRow = IntersectionFixer.Fix(stones, new IntersectionFixOptions { MinGapMm = minGap }).Stones;

            // Раздвигание соседей в острых углах иногда задевает камень другого «плеча» угла, особенно
            // когда камни разного размера (звезда в Preview "methods"). Последняя проверка — без раздвигания.
            return IntersectionFixer.RemoveOverlaps(fixedRow, minGap);
        }

        /// <summary>Диаметр размера по названию; нет названия или таблицы — <paramref name="fallback"/>.</summary>
        private static double Diameter(string? name, double fallback, IReadOnlyDictionary<string, double>? sizes)
        {
            if (sizes == null || string.IsNullOrWhiteSpace(name))
            {
                return fallback;
            }

            foreach (KeyValuePair<string, double> size in sizes)
            {
                if (string.Equals(size.Key.Trim(), name!.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return size.Value;
                }
            }

            return fallback;
        }

        /// <summary>От одного диаметра к другому через все размеры таблицы между ними (в любую сторону).</summary>
        private static List<double> SizeRange(double from, double to, IReadOnlyDictionary<string, double>? sizes)
        {
            double lo = Math.Min(from, to);
            double hi = Math.Max(from, to);
            var list = new List<double> { lo, hi };
            if (sizes != null)
            {
                list.AddRange(sizes.Values.Where(v => v > lo + 1e-6 && v < hi - 1e-6));
            }

            list = list.Distinct().OrderBy(v => v).ToList();
            if (from > to)
            {
                list.Reverse();
            }

            return list;
        }

        /// <summary>L3 «по смещённой линии»: один ряд на заданном расстоянии наружу или внутрь, без наложений.</summary>
        private static MethodResult OffsetLine(Curve curve, double d, MethodParameters p)
        {
            double sign = OutwardSign(curve) * (p.OffsetSide == MethodChoices.SideInside ? -1 : 1);
            var row = new RowSpec
            {
                OffsetMm = sign * p.OffsetMm,
                CornerStyle = Corners(p),
                ScatterOptions = RowOptions(d, p, 0),
            };
            IReadOnlyList<PlacedStone> stones = RingScatterer.Scatter(curve, new[] { row });

            // У острых углов смещённая линия круто изгибается, и соседние стразы ряда могут налезть
            // друг на друга (звезда, сердце — см. Preview "methods"). Лишние убираем, соседей раздвигаем.
            return Plain(IntersectionFixer.Fix(stones, new IntersectionFixOptions { MinGapMm = p.GapMm / 2 }).Stones);
        }
    }
}
