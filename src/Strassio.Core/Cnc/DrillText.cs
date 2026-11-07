using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Strassio.Core.Cnc
{
    /// <summary>
    /// Числа в окне «Сверление»: всегда миллиметры (станок работает в мм), запятая и точка — обе
    /// годятся. Список свёрл — через «;» или пробелы: «2,4; 2,8; 3» или «2.4 2.8 3».
    /// </summary>
    public static class DrillText
    {
        public static bool TryParseMm(string? text, out double value)
        {
            string t = (text ?? string.Empty).Trim().Replace(',', '.');
            if (t.EndsWith("mm", StringComparison.OrdinalIgnoreCase) || t.EndsWith("мм", StringComparison.OrdinalIgnoreCase))
            {
                t = t.Substring(0, t.Length - 2).Trim();
            }

            return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        /// <summary>Список диаметров; null — какое-то число не читается.</summary>
        public static List<double>? ParseList(string? text)
        {
            string t = (text ?? string.Empty).Trim();
            if (t.Length == 0)
            {
                return new List<double>();
            }

            // «2,4; 3» — точка с запятой разделяет. Иначе «2.4, 3» — запятая с пробелом разделяет,
            // а «2,4 3» — пробелы (запятая без пробела — десятичная).
            string[] parts = t.Contains(";")
                ? t.Split(';')
                : t.Contains(", ") ? t.Split(new[] { ", " }, StringSplitOptions.None) : t.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var result = new List<double>();
            foreach (string part in parts)
            {
                string p = part.Trim();
                if (p.Length == 0)
                {
                    continue;
                }

                if (!TryParseMm(p, out double d) || d <= 0)
                {
                    return null;
                }

                result.Add(d);
            }

            return result;
        }

        /// <summary>«2,4; 3; 4» — для поля ввода (десятичный знак — как в Windows у пользователя).</summary>
        public static string FormatList(IEnumerable<double> values, CultureInfo culture) =>
            string.Join("; ", values.Select(v => Format(v, culture)));

        public static string Format(double value, CultureInfo culture) => Math.Round(value, 3).ToString("0.###", culture);
    }
}
