using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Strassio.Core.Geometry;
using Strassio.Core.Placement;

namespace Strassio.Preview
{
    /// <summary>
    /// Замечание автора «пропускает стразы»: метод «Кант» (F5 — ряды по краю фигуры), квадрат,
    /// 3 ряда, зазор 0. Рисуем то же самое и считаем числами, где в ряду пропуски: для каждой
    /// стразы ищем ближайшую соседку, и если до неё больше полутора шагов — это дыра.
    /// </summary>
    internal static class KantLab
    {
        private const double Diameter = 2.4;
        private const double Scale = 9;
        private const double PadMm = 4;

        public static void Render(string repoRoot)
        {
            string outDir = Path.Combine(repoRoot, "out", "preview");
            Directory.CreateDirectory(outDir);

            var cases = new (string Title, Curve Shape, double Gap, bool WholeShape)[]
            {
                ("Квадрат 40 мм, зазор 0", Square(40), 0, false),
                ("Звезда, зазор 0", Star(), 0, false),
                ("Ваш вектор — как сейчас", Author(), 0, true),
                ("Ваш вектор — внутренние подвинуты", Author(), 0, true),
            };

            var sb = new StringBuilder();
            double cellW = 56 * Scale;
            double cellH = 56 * Scale + 40;
            sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{N(cellW * cases.Length)}\" height=\"{N(cellH + 26)}\" font-family=\"Segoe UI, Arial, sans-serif\">");
            sb.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"white\"/>");

            Console.WriteLine("Кант (3 ряда по краю):");
            for (int i = 0; i < cases.Length; i++)
            {
                (string title, Curve shape, double gap, bool whole) = cases[i];
                List<PlacedStone> stones = Run(shape, gap, whole);
                if (title.Contains("подвинуты"))
                {
                    stones = Relax(stones, shape, gap);
                }

                (int holes, double worst) = Holes(stones, Diameter + gap);
                Console.WriteLine($"    оценка: {Score(stones, gap)}");

                // Фигуру двигаем в начало клетки — рисуем её там, где она поместится.
                double minX = double.MaxValue, minY = double.MaxValue;
                foreach (PlacedStone s0 in stones)
                {
                    minX = Math.Min(minX, s0.Center.X - s0.DiameterMm / 2);
                    minY = Math.Min(minY, s0.Center.Y - s0.DiameterMm / 2);
                }

                if (stones.Count == 0)
                {
                    minX = minY = 0;
                }

                double left = i * cellW;
                sb.AppendLine($"<text x=\"{N(left + cellW / 2)}\" y=\"20\" text-anchor=\"middle\" font-size=\"15\" font-weight=\"600\" fill=\"#222\">{title}</text>");
                sb.AppendLine($"<g transform=\"translate({N(left + PadMm * Scale - minX * Scale)},{N(26 + PadMm * Scale - minY * Scale)})\">");

                foreach (PlacedStone s in stones)
                {
                    sb.AppendLine($"<circle cx=\"{N(s.Center.X * Scale)}\" cy=\"{N(s.Center.Y * Scale)}\" r=\"{N(s.DiameterMm / 2 * Scale)}\" fill=\"#43A047\" fill-opacity=\"0.75\" stroke=\"#333\" stroke-width=\"0.7\"/>");
                }

                sb.AppendLine("</g>");
                string note = $"{stones.Count} страз, пропусков {holes}, самый одинокий камень — сосед в {worst:0.00} мм";
                sb.AppendLine($"<text x=\"{N(left + cellW / 2)}\" y=\"{N(cellH + 16)}\" text-anchor=\"middle\" font-size=\"12\" fill=\"#444\">{note}</text>");
                Console.WriteLine($"  {title,-26} — {note}");
            }

            DiagnoseRings("лепесток", Petal());

            RenderAuthorShape(outDir);
            RenderLeaf(outDir);

            sb.AppendLine("</svg>");
            string outPath = Path.Combine(outDir, "kant.svg");
            File.WriteAllText(outPath, sb.ToString());
            Console.WriteLine("SVG: " + outPath);
        }

        /// <summary>
        /// Почему кольцо строится тем или иным способом: куда уходит смещение контура и насколько
        /// точно его точки ложатся на нужную глубину (по тому же полю расстояний, что и в ядре).
        /// </summary>
        private static void Diagnose(string name, Curve shape)
        {
            FlattenedCurve flat = CurveFlattener.Flatten(shape, 0.02);
            SignedDistanceField field = SignedDistanceField.Build(new[] { flat }, Diameter / 8.0);

            double MeanDepth(double sign)
            {
                List<Point2D> probe = CurveOffsetter.Offset(flat, sign * 0.2, 0.02, roundOuterCorners: false);
                if (probe.Count == 0)
                {
                    return double.NaN;
                }

                double sum = 0;
                foreach (Point2D p in probe)
                {
                    sum += field.ValueAt(p);
                }

                return sum / probe.Count;
            }

            double plus = MeanDepth(1);
            double minus = MeanDepth(-1);
            double inward = plus > minus ? 1 : -1;
            double level = Diameter / 2;

            List<Point2D> loop = CurveOffsetter.Offset(flat, inward * level, 0.02, roundOuterCorners: false);
            double worst = 0;
            foreach (Point2D p in loop)
            {
                worst = Math.Max(worst, Math.Abs(field.ValueAt(p) - level));
            }

            Console.WriteLine($"  {name}: проба внутрь {plus:0.000}, наружу {minus:0.000}; точек смещения {loop.Count}, " +
                $"худшее отклонение глубины {worst:0.000} мм (допуск {field.CellSizeMm + Diameter * 0.1:0.000})");
        }

        /// <summary>Сколько колец даёт линия равного расстояния и что с ними происходит дальше.</summary>
        private static void DiagnoseRings(string name, Curve shape)
        {
            FlattenedCurve flat = CurveFlattener.Flatten(shape, 0.02);
            SignedDistanceField field = SignedDistanceField.Build(new[] { flat }, Diameter / 8.0);
            Console.WriteLine($"  {name}: сетка {field.Width}×{field.Height}, клетка {field.CellSizeMm:0.###} мм");

            for (int ring = 0; ring < 5; ring++)
            {
                double level = Diameter / 2 + ring * Diameter;
                List<List<Point2D>> loops = IsoContour.Trace(field, level);
                var sizes = new List<string>();
                foreach (List<Point2D> loop in loops)
                {
                    string ok;
                    try
                    {
                        Curve.FromPolyline(loop, isClosed: true);
                        ok = "кривая ок";
                    }
                    catch (ArgumentException e)
                    {
                        ok = "НЕ СТРОИТСЯ: " + e.Message;
                    }

                    double len = 0;
                    for (int i = 1; i < loop.Count; i++)
                    {
                        len += Point2D.Distance(loop[i - 1], loop[i]);
                    }

                    sizes.Add($"{loop.Count} точек, длина {len:0.0} мм, {ok}");
                }

                Console.WriteLine($"    глубина {level:0.0} мм: колец {loops.Count}" +
                    (sizes.Count > 0 ? " — " + string.Join("; ", sizes) : string.Empty));
            }
        }

        /// <summary>whole = true — контурная заливка целиком (метод «Контурная»), иначе кант в 3 ряда.</summary>
        private static List<PlacedStone> Run(Curve shape, double gap, bool whole) =>
            ContourFiller.Fill(new[] { shape }, new ContourFillOptions
            {
                StoneDiameterMm = Diameter,
                GapMm = gap,
                MaxRings = whole ? (int?)null : 3,
                FillCenter = whole,
            });

        /// <summary>
        /// Лист автора из 1.svg. Для сравнения: его ручная работа на этой же форме — 174 стразы,
        /// наша прежняя заливка давала 171, но с белым завитком посередине.
        /// </summary>
        private static void RenderLeaf(string outDir)
        {
            string file = Path.Combine(RepoRoot(), "1.svg");
            if (!File.Exists(file))
            {
                return;
            }

            Curve shape = SvgShape.Load(file);
            bool midrib = Environment.GetEnvironmentVariable("STRASSIO_MIDRIB") == "1";
            bool lengthwise = Environment.GetEnvironmentVariable("STRASSIO_LENGTHWISE") == "1";
            List<PlacedStone> stones = lengthwise
                ? AdvancedFillers.Lengthwise(new[] { shape }, Diameter, 0, 0)!
                : ContourFiller.Fill(
                    new[] { shape },
                    new ContourFillOptions { StoneDiameterMm = Diameter, GapMm = 0, MidribAlongSkeleton = midrib });

            const double S = 12;
            const double Pad = 4;
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (PlacedStone st in stones)
            {
                minX = Math.Min(minX, st.Center.X - st.DiameterMm / 2);
                minY = Math.Min(minY, st.Center.Y - st.DiameterMm / 2);
                maxX = Math.Max(maxX, st.Center.X + st.DiameterMm / 2);
                maxY = Math.Max(maxY, st.Center.Y + st.DiameterMm / 2);
            }

            double w = (maxX - minX + 2 * Pad) * S;
            double h = (maxY - minY + 2 * Pad) * S + 30;

            var sb = new StringBuilder();
            sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{N(w)}\" height=\"{N(h)}\" font-family=\"Segoe UI, Arial, sans-serif\">");
            sb.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"white\"/>");
            sb.AppendLine($"<text x=\"{N(w / 2)}\" y=\"20\" text-anchor=\"middle\" font-size=\"15\" font-weight=\"600\" fill=\"#222\">Лист: {stones.Count} страз (у автора 174)</text>");
            sb.AppendLine($"<g transform=\"translate({N(Pad * S - minX * S)},{N(28 + Pad * S - minY * S)})\">");

            foreach (PlacedStone st in stones)
            {
                // Прожилка помечена номером ряда -2 — рисуем её отдельным цветом.
                string fill = st.RowId == -2 ? "#2F74E0" : "#43A047";
                sb.AppendLine($"<circle cx=\"{N(st.Center.X * S)}\" cy=\"{N(st.Center.Y * S)}\" r=\"{N(st.DiameterMm / 2 * S)}\" fill=\"{fill}\" fill-opacity=\"0.85\" stroke=\"#333\" stroke-width=\"0.6\"/>");
            }

            sb.AppendLine("</g>");
            sb.AppendLine("</svg>");

            string path = Path.Combine(outDir, lengthwise ? "leaf-lengthwise.svg" : midrib ? "leaf-midrib.svg" : "leaf.svg");
            File.WriteAllText(path, sb.ToString());
            Console.WriteLine($"Лист автора: {stones.Count} страз — {path}");
        }

        private static string RepoRoot() => Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(typeof(KantLab).Assembly.Location) ?? ".", "..", "..", "..", "..", ".."));

        /// <summary>Ваша форма целиком, крупно: слева как сейчас, справа после правки внутренних рядов.</summary>
        private static void RenderAuthorShape(string outDir)
        {
            Curve shape = Author();
            List<PlacedStone> now = Run(shape, 0, true);
            List<PlacedStone> moved = Relax(now, shape, 0);

            const double S = 8;             // пикселей на миллиметр
            const double Pad = 5;           // поля, мм
            double cellW = (39.8 + 2 * Pad) * S;
            double cellH = (98.9 + 2 * Pad) * S + 30;

            var sb = new StringBuilder();
            sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{N(cellW * 2)}\" height=\"{N(cellH)}\" font-family=\"Segoe UI, Arial, sans-serif\">");
            sb.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"white\"/>");

            var variants = new (string Title, List<PlacedStone> Stones)[]
            {
                ("Как сейчас", now),
                ("Внутренние подвинуты", moved),
            };

            for (int v = 0; v < variants.Length; v++)
            {
                (string title, List<PlacedStone> stones) = variants[v];

                double minX = double.MaxValue, minY = double.MaxValue;
                foreach (PlacedStone st in stones)
                {
                    minX = Math.Min(minX, st.Center.X - st.DiameterMm / 2);
                    minY = Math.Min(minY, st.Center.Y - st.DiameterMm / 2);
                }

                double left = v * cellW;
                sb.AppendLine($"<text x=\"{N(left + cellW / 2)}\" y=\"20\" text-anchor=\"middle\" font-size=\"15\" font-weight=\"600\" fill=\"#222\">{title} — {stones.Count} страз</text>");
                sb.AppendLine($"<g transform=\"translate({N(left + Pad * S - minX * S)},{N(28 + Pad * S - minY * S)})\">");

                foreach (PlacedStone st in stones)
                {
                    // Крайний ряд — синим: видно, что его не двигали.
                    string fill = st.RowId == 0 ? "#2F74E0" : st.RowId < 0 ? "#F57C00" : "#43A047";
                    sb.AppendLine($"<circle cx=\"{N(st.Center.X * S)}\" cy=\"{N(st.Center.Y * S)}\" r=\"{N(st.DiameterMm / 2 * S)}\" fill=\"{fill}\" fill-opacity=\"0.8\" stroke=\"#333\" stroke-width=\"0.6\"/>");
                }

                sb.AppendLine("</g>");
            }

            sb.AppendLine("</svg>");
            string path = Path.Combine(outDir, "kant-author.svg");
            File.WriteAllText(path, sb.ToString());
            Console.WriteLine("Ваша форма крупно: " + path);
        }

        /// <summary>Настоящая работа автора: vector.svg из корня репозитория.</summary>
        private static Curve Author() => SvgShape.Load(Path.Combine(RepoRoot(), "vector.svg"));

        /// <summary>
        /// Длинный сужающийся лепесток — как на сравнении автора (зелёный вручную, красный Strassio).
        /// Две дуги от широкого основания к острому кончику.
        /// </summary>
        private static Curve Petal()
        {
            var pts = new List<Point2D>();
            const int Steps = 90;

            // Внешняя сторона: дуга большого радиуса.
            for (int i = 0; i <= Steps; i++)
            {
                double t = i / (double)Steps;
                double a = Math.PI * (0.62 - 0.30 * t);
                double r = 52;
                pts.Add(new Point2D(60 + r * Math.Cos(a), 58 - r * Math.Sin(a)));
            }

            // Внутренняя сторона обратно: радиус меньше, к концу сходится с внешней — получается остриё.
            for (int i = Steps; i >= 0; i--)
            {
                double t = i / (double)Steps;
                double a = Math.PI * (0.62 - 0.30 * t);
                double r = 52 - 16 * (1 - t * t); // широкое основание, сужение к самому кончику
                pts.Add(new Point2D(60 + r * Math.Cos(a), 58 - r * Math.Sin(a)));
            }

            return Curve.FromPolyline(pts, isClosed: true);
        }

        /// <summary>
        /// По указанию автора: «наружные линии красивые, не надо трогать; надо поправить
        /// внутренние, по немножку двигать, чтобы закрыть щели».
        ///
        /// Поэтому крайний ряд (RowId = 0) закреплён намертво, а внутренние камни могут сдвинуться
        /// не больше чем на MaxShift. Двигаются они туда, где закрывают щель: если соседей с одной
        /// стороны нет, а с другой тесно — камень отходит в пустую сторону. Наложений не допускаем.
        /// </summary>
        private static List<PlacedStone> Relax(List<PlacedStone> stones, Curve shape, double gap)
        {
            FlattenedCurve flat = CurveFlattener.Flatten(shape, 0.02);
            SignedDistanceField field = SignedDistanceField.Build(new[] { flat }, Diameter / 10.0);
            double step = Diameter + gap;
            double radius = Diameter / 2;

            var points = new List<Point2D>();
            foreach (PlacedStone s0 in stones)
            {
                points.Add(s0.Center);
            }

            // Крайний ряд не трогаем совсем — он и так красивый.
            var frozen = new bool[points.Count];
            var origin = new Point2D[points.Count];
            for (int i = 0; i < stones.Count; i++)
            {
                frozen[i] = stones[i].RowId == 0;
                origin[i] = stones[i].Center;
            }

            const double MaxShift = 0.6; // «по немножку», мм

            for (int pass = 0; pass < 40; pass++)
            {
                var shift = new Point2D[points.Count];

                for (int i = 0; i < points.Count; i++)
                {
                    for (int j = i + 1; j < points.Count; j++)
                    {
                        Point2D d = points[i] - points[j];
                        double dist = Math.Sqrt(d.X * d.X + d.Y * d.Y);
                        if (dist < 1e-9 || dist > step * 1.4)
                        {
                            continue;
                        }

                        // Тесно — расталкиваем; просторно — слегка стягиваем, щель закрывается.
                        double weight = dist < step ? 0.5 : 0.10;
                        Point2D force = d * ((step - dist) / dist * weight);
                        shift[i] = shift[i] + force;
                        shift[j] = shift[j] - force;
                    }
                }

                for (int i = 0; i < points.Count; i++)
                {
                    if (frozen[i])
                    {
                        continue;
                    }

                    Point2D moved = points[i] + shift[i];

                    // Дальше MaxShift от исходного места камень не уходит.
                    Point2D delta = moved - origin[i];
                    double len = Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
                    if (len > MaxShift)
                    {
                        moved = origin[i] + delta * (MaxShift / len);
                    }

                    points[i] = Keep(field, moved, radius, points[i]);
                }
            }

            // Жёсткое правило: камни не могут стоять ближе шага. Несколько проходов только
            // на расталкивание — после них наложений не остаётся.
            for (int pass = 0; pass < 40; pass++)
            {
                bool moved = false;
                for (int i = 0; i < points.Count; i++)
                {
                    for (int j = i + 1; j < points.Count; j++)
                    {
                        Point2D d = points[i] - points[j];
                        double dist = Math.Sqrt(d.X * d.X + d.Y * d.Y);
                        if (dist >= step - 1e-4 || dist < 1e-9)
                        {
                            continue;
                        }

                        Point2D push = d * ((step - dist) / dist * 0.5);
                        if (!frozen[i])
                        {
                            points[i] = Keep(field, points[i] + push, radius, points[i]);
                        }

                        if (!frozen[j])
                        {
                            points[j] = Keep(field, points[j] - push, radius, points[j]);
                        }

                        moved = true;
                    }
                }

                if (!moved)
                {
                    break;
                }
            }

            var result = new List<PlacedStone>();
            for (int i = 0; i < points.Count; i++)
            {
                result.Add(new PlacedStone(points[i], Diameter, false, stones[i].RowId));
            }

            AddIntoHoles(result, field, step, radius);
            return result;
        }

        /// <summary>Держит камень внутри фигуры: если новое место не годится, остаётся прежнее.</summary>
        private static Point2D Keep(SignedDistanceField field, Point2D candidate, double radius, Point2D fallback)
        {
            double depth = field.ValueAt(candidate);
            if (depth >= radius - 0.02)
            {
                return candidate;
            }

            Point2D inward = Gradient(field, candidate);
            Point2D pulled = candidate + inward * (radius - depth);
            return field.ValueAt(pulled) >= radius - 0.02 ? pulled : fallback;
        }

        /// <summary>Куда «вглубь» фигуры: направление роста расстояния до края.</summary>
        private static Point2D Gradient(SignedDistanceField field, Point2D p)
        {
            double h = field.CellSizeMm;
            double dx = field.ValueAt(new Point2D(p.X + h, p.Y)) - field.ValueAt(new Point2D(p.X - h, p.Y));
            double dy = field.ValueAt(new Point2D(p.X, p.Y + h)) - field.ValueAt(new Point2D(p.X, p.Y - h));
            var g = new Point2D(dx, dy);
            double len = Math.Sqrt(g.X * g.X + g.Y * g.Y);
            return len < 1e-9 ? new Point2D(0, 0) : g * (1 / len);
        }

        /// <summary>После расталкивания в пустоты добавляем камни — там, где помещается целый.</summary>
        private static void AddIntoHoles(List<PlacedStone> stones, SignedDistanceField field, double step, double radius)
        {
            for (int iy = 0; iy < field.Height; iy++)
            {
                for (int ix = 0; ix < field.Width; ix++)
                {
                    if (field.ValueAt(ix, iy) < radius)
                    {
                        continue;
                    }

                    Point2D p = field.PositionOf(ix, iy);
                    bool free = true;
                    for (int k = 0; k < stones.Count; k++)
                    {
                        if (Point2D.Distance(stones[k].Center, p) < step - 0.02)
                        {
                            free = false;
                            break;
                        }
                    }

                    if (free)
                    {
                        stones.Add(new PlacedStone(p, Diameter, false, rowId: -1));
                    }
                }
            }
        }

        /// <summary>Оценка раскладки числами: средний шаг между соседями и его разброс.</summary>
        private static string Score(IReadOnlyList<PlacedStone> stones, double gap)
        {
            double step = Diameter + gap;
            var nearest = new List<double>();
            for (int i = 0; i < stones.Count; i++)
            {
                double best = double.MaxValue;
                for (int j = 0; j < stones.Count; j++)
                {
                    if (i != j)
                    {
                        best = Math.Min(best, Point2D.Distance(stones[i].Center, stones[j].Center));
                    }
                }

                if (best != double.MaxValue)
                {
                    nearest.Add(best);
                }
            }

            if (nearest.Count == 0)
            {
                return "нет камней";
            }

            double mean = 0;
            foreach (double v in nearest)
            {
                mean += v;
            }

            mean /= nearest.Count;

            double variance = 0;
            foreach (double v in nearest)
            {
                variance += (v - mean) * (v - mean);
            }

            double spread = Math.Sqrt(variance / nearest.Count);
            return $"средний шаг {mean:0.00} мм (нужен {step:0.00}), разброс {spread:0.00} мм";
        }

        /// <summary>Пропуск — страза, у которой ближайшая соседка дальше полутора шагов ряда.</summary>
        private static (int Holes, double Worst) Holes(IReadOnlyList<PlacedStone> stones, double step)
        {
            int holes = 0;
            double worst = 0;

            for (int i = 0; i < stones.Count; i++)
            {
                double nearest = double.MaxValue;
                for (int j = 0; j < stones.Count; j++)
                {
                    if (i != j)
                    {
                        nearest = Math.Min(nearest, Point2D.Distance(stones[i].Center, stones[j].Center));
                    }
                }

                if (nearest == double.MaxValue)
                {
                    continue;
                }

                worst = Math.Max(worst, nearest);
                if (nearest > step * 1.5)
                {
                    holes++;
                }
            }

            return (holes, worst);
        }

        private static Curve Square(double side) => Curve.FromPolyline(
            new[] { new Point2D(0, 0), new Point2D(side, 0), new Point2D(side, side), new Point2D(0, side) },
            isClosed: true);

        private static Curve Star()
        {
            var pts = new List<Point2D>();
            for (int i = 0; i < 10; i++)
            {
                double a = Math.PI / 2 + i * Math.PI / 5;
                double r = i % 2 == 0 ? 24 : 10;
                pts.Add(new Point2D(24 + r * Math.Cos(a), 24 - r * Math.Sin(a)));
            }

            return Curve.FromPolyline(pts, isClosed: true);
        }

        private static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
