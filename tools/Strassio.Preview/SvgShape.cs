using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Strassio.Core.Geometry;

namespace Strassio.Preview
{
    /// <summary>
    /// Читает форму из SVG, сохранённого CorelDRAW («Файл → Экспорт → SVG»), чтобы проверять
    /// алгоритмы на настоящих работах автора, а не на выдуманных фигурах. Понимает то, что
    /// CorelDRAW кладёт в путь: M (начало), c (кривая Безье относительно) и z (замкнуть).
    /// Кривые сразу разбиваются в полилинию — дальше ядро всё равно работает с полилинией.
    /// Координаты переводятся в миллиметры по ширине из заголовка SVG.
    /// </summary>
    internal static class SvgShape
    {
        public static Curve Load(string svgPath)
        {
            string text = File.ReadAllText(svgPath);

            double widthMm = Millimetres(Attribute(text, "width"));
            (double vbWidth, double _) = ViewBox(text);
            double scale = vbWidth > 0 && widthMm > 0 ? widthMm / vbWidth : 1;

            List<Point2D> points = Flatten(PathData(text));
            if (points.Count < 3)
            {
                throw new InvalidOperationException("В SVG не нашлось замкнутого контура.");
            }

            var mm = new List<Point2D>(points.Count);
            foreach (Point2D p in points)
            {
                mm.Add(new Point2D(p.X * scale, p.Y * scale));
            }

            return Curve.FromPolyline(mm, isClosed: true);
        }

        private static string Attribute(string text, string name)
        {
            Match m = Regex.Match(text, name + "=\"([^\"]+)\"");
            return m.Success ? m.Groups[1].Value : string.Empty;
        }

        private static double Millimetres(string value)
        {
            Match m = Regex.Match(value, @"([0-9.]+)\s*(mm|cm|in|px)?");
            if (!m.Success)
            {
                return 0;
            }

            double n = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            switch (m.Groups[2].Value)
            {
                case "cm": return n * 10;
                case "in": return n * 25.4;
                case "px": return n * 25.4 / 96;
                default: return n; // мм или без единиц
            }
        }

        private static (double Width, double Height) ViewBox(string text)
        {
            string[] parts = Attribute(text, "viewBox").Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4)
            {
                return (0, 0);
            }

            return (double.Parse(parts[2], CultureInfo.InvariantCulture),
                double.Parse(parts[3], CultureInfo.InvariantCulture));
        }

        /// <summary>Самый длинный путь в файле — это и есть форма (остальное у CorelDRAW служебное).</summary>
        private static string PathData(string text)
        {
            string best = string.Empty;
            foreach (Match m in Regex.Matches(text, "d=\"([^\"]+)\""))
            {
                string value = m.Groups[1].Value;
                if (value.Length > best.Length && (value.StartsWith("M") || value.StartsWith("m")))
                {
                    best = value;
                }
            }

            return best;
        }

        /// <summary>Разбивает путь в полилинию: каждая кривая Безье — на 24 отрезка.</summary>
        private static List<Point2D> Flatten(string d)
        {
            var points = new List<Point2D>();
            var numbers = new List<double>();
            char command = ' ';
            var current = new Point2D(0, 0);
            var start = new Point2D(0, 0);

            foreach (Match token in Regex.Matches(d, @"[MmCcLlZz]|-?[0-9]*\.?[0-9]+(?:e-?[0-9]+)?"))
            {
                string t = token.Value;
                if (t.Length == 1 && char.IsLetter(t[0]))
                {
                    Apply(points, ref current, ref start, command, numbers);
                    numbers.Clear();
                    command = t[0];

                    if (command == 'z' || command == 'Z')
                    {
                        current = start;
                    }

                    continue;
                }

                numbers.Add(double.Parse(t, CultureInfo.InvariantCulture));
            }

            Apply(points, ref current, ref start, command, numbers);
            return points;
        }

        private static void Apply(
            List<Point2D> points, ref Point2D current, ref Point2D start, char command, List<double> numbers)
        {
            switch (command)
            {
                case 'M':
                case 'm':
                    for (int i = 0; i + 1 < numbers.Count; i += 2)
                    {
                        var p = new Point2D(numbers[i], numbers[i + 1]);
                        current = command == 'm' ? current + p : p;
                        if (i == 0)
                        {
                            start = current;
                        }

                        points.Add(current);
                    }

                    break;

                case 'L':
                case 'l':
                    for (int i = 0; i + 1 < numbers.Count; i += 2)
                    {
                        var p = new Point2D(numbers[i], numbers[i + 1]);
                        current = command == 'l' ? current + p : p;
                        points.Add(current);
                    }

                    break;

                case 'C':
                case 'c':
                    for (int i = 0; i + 5 < numbers.Count; i += 6)
                    {
                        Point2D c1 = Resolve(current, numbers[i], numbers[i + 1], command == 'c');
                        Point2D c2 = Resolve(current, numbers[i + 2], numbers[i + 3], command == 'c');
                        Point2D end = Resolve(current, numbers[i + 4], numbers[i + 5], command == 'c');

                        const int Steps = 24;
                        for (int k = 1; k <= Steps; k++)
                        {
                            points.Add(Bezier(current, c1, c2, end, k / (double)Steps));
                        }

                        current = end;
                    }

                    break;
            }
        }

        private static Point2D Resolve(Point2D from, double x, double y, bool relative) =>
            relative ? new Point2D(from.X + x, from.Y + y) : new Point2D(x, y);

        private static Point2D Bezier(Point2D p0, Point2D p1, Point2D p2, Point2D p3, double t)
        {
            double u = 1 - t;
            double a = u * u * u;
            double b = 3 * u * u * t;
            double c = 3 * u * t * t;
            double e = t * t * t;

            return new Point2D(
                a * p0.X + b * p1.X + c * p2.X + e * p3.X,
                a * p0.Y + b * p1.Y + c * p2.Y + e * p3.Y);
        }
    }
}
