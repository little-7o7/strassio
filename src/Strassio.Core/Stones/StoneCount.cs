using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Strassio.Core.Stones
{
    /// <summary>Сколько камней одного размера и цвета.</summary>
    public sealed class StoneCountRow
    {
        public StoneCountRow(string size, string color, int count)
        {
            Size = size;
            Color = color;
            Count = count;
        }

        public string Size { get; }

        public string Color { get; }

        public int Count { get; }
    }

    /// <summary>
    /// Подсчёт камней по размерам и цветам — для заказа камней (решение автора 2026-11-06: только
    /// количество, без цен). Размеры идут в порядке таблицы камней, цвета внутри размера — по убыванию
    /// количества.
    /// </summary>
    public sealed class StoneCount
    {
        /// <summary>Подпись для камней без размера или цвета (круг с непонятной меткой).</summary>
        public const string Unknown = "?";

        private StoneCount(List<StoneCountRow> rows)
        {
            Rows = rows;
            Total = rows.Sum(r => r.Count);
        }

        public IReadOnlyList<StoneCountRow> Rows { get; }

        public int Total { get; }

        /// <summary>Размеры по порядку и сколько камней каждого (все цвета вместе).</summary>
        public IReadOnlyList<KeyValuePair<string, int>> BySize =>
            Rows.GroupBy(r => r.Size, StringComparer.OrdinalIgnoreCase)
                .Select(g => new KeyValuePair<string, int>(g.Key, g.Sum(r => r.Count)))
                .ToList();

        public int ColorCount => Rows.Select(r => r.Color).Distinct(StringComparer.OrdinalIgnoreCase).Count();

        /// <param name="stones">Размер и цвет каждого камня.</param>
        /// <param name="sizeOrder">Размеры в порядке таблицы камней; размеры не из таблицы — в конце, по алфавиту.</param>
        public static StoneCount Of(IEnumerable<(string Size, string Color)> stones, IReadOnlyList<string> sizeOrder)
        {
            var order = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < sizeOrder.Count; i++)
            {
                if (!order.ContainsKey(sizeOrder[i]))
                {
                    order[sizeOrder[i]] = i;
                }
            }

            var counts = new Dictionary<(string, string), int>();
            var sizeNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var colorNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach ((string size, string color) in stones)
            {
                // Регистр не важен («SS6» и «ss6» — один размер); показываем написание первого встреченного.
                string s = Name(size, sizeNames);
                string c = Name(color, colorNames);
                var key = (s, c);
                counts.TryGetValue(key, out int n);
                counts[key] = n + 1;
            }

            List<StoneCountRow> rows = counts
                .Select(kv => new StoneCountRow(kv.Key.Item1, kv.Key.Item2, kv.Value))
                .OrderBy(r => order.TryGetValue(r.Size, out int i) ? i : int.MaxValue)
                .ThenBy(r => r.Size, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(r => r.Count)
                .ThenBy(r => r.Color, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return new StoneCount(rows);
        }

        /// <summary>
        /// Тот же подсчёт, умноженный на <paramref name="factor"/> (например, на число плиток в метре ткани).
        /// Дробное число камней округляется вверх — камней лучше заказать с запасом, чем не хватит.
        /// </summary>
        public StoneCount Times(double factor)
        {
            double k = Math.Max(0, factor);
            return new StoneCount(Rows.Select(r => new StoneCountRow(r.Size, r.Color, (int)Math.Ceiling(r.Count * k - 1e-9))).ToList());
        }

        /// <summary>
        /// Таблица для Excel: столбцы через табуляцию, строки размер · цвет · штук, внизу итог.
        /// Вставляется в Excel или Google Таблицы как есть.
        /// </summary>
        public string ToTable(string sizeHeader, string colorHeader, string countHeader, string totalLabel)
        {
            var text = new StringBuilder();
            text.Append(sizeHeader).Append('\t').Append(colorHeader).Append('\t').Append(countHeader).Append("\r\n");
            foreach (StoneCountRow row in Rows)
            {
                text.Append(row.Size).Append('\t').Append(row.Color).Append('\t')
                    .Append(row.Count.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            }

            text.Append(totalLabel).Append("\t\t").Append(Total.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            return text.ToString();
        }

        private static string Name(string? text, Dictionary<string, string> seen)
        {
            string trimmed = (text ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                return Unknown;
            }

            if (!seen.TryGetValue(trimmed, out string? first))
            {
                seen[trimmed] = trimmed;
                first = trimmed;
            }

            return first;
        }
    }
}
