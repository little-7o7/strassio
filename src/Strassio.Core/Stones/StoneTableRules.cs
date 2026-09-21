using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Strassio.Core.Stones
{
    /// <summary>Ошибка в таблице камней: ключ текста в файле языка и значения для него.</summary>
    public sealed class StoneProblem
    {
        public StoneProblem(string key, params object[] args)
        {
            Key = key;
            Args = args;
        }

        public string Key { get; }

        public object[] Args { get; }
    }

    /// <summary>
    /// Правила редактора таблицы камней (docs/SPEC.md, раздел 3.1: добавить, изменить, удалить,
    /// изменить порядок — прямо в плагине). Окно редактора работает с копией таблицы
    /// (<see cref="Clone"/>) и сохраняет её, только если <see cref="Validate"/> не нашёл ошибок.
    /// </summary>
    public static class StoneTableRules
    {
        /// <summary>Самая маленькая и самая большая страза, которые имеют смысл, мм.</summary>
        public const double MinDiameterMm = 0.5;

        public const double MaxDiameterMm = 30;

        /// <summary>Полная независимая копия — правки в окне не трогают рабочую таблицу до «ОК».</summary>
        public static StoneTable Clone(StoneTable table) => StoneTable.FromJson(table.ToJson());

        /// <summary>
        /// Все ошибки набора сразу, по порядку сверху вниз. Пусто — можно сохранять. Имена сравниваются
        /// без учёта регистра и пробелов по краям: «ss6» и « SS6 » — это один размер.
        /// </summary>
        public static IReadOnlyList<StoneProblem> Validate(StoneSet set)
        {
            var problems = new List<StoneProblem>();
            if (set.Sizes.Count == 0)
            {
                problems.Add(new StoneProblem("stones.error.noSizes"));
                return problems;
            }

            var sizeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (StoneSize size in set.Sizes)
            {
                string sizeName = (size.Name ?? string.Empty).Trim();
                if (sizeName.Length == 0)
                {
                    problems.Add(new StoneProblem("stones.error.sizeName"));
                }
                else if (!sizeNames.Add(sizeName))
                {
                    problems.Add(new StoneProblem("stones.error.sizeDuplicate", sizeName));
                }

                if (double.IsNaN(size.DiameterMm) || size.DiameterMm < MinDiameterMm || size.DiameterMm > MaxDiameterMm)
                {
                    problems.Add(new StoneProblem("stones.error.diameter", sizeName));
                }

                if (size.Colors.Count == 0)
                {
                    problems.Add(new StoneProblem("stones.error.noColors", sizeName));
                }

                var colorNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (StoneColor color in size.Colors)
                {
                    string colorName = (color.Name ?? string.Empty).Trim();
                    if (colorName.Length == 0)
                    {
                        problems.Add(new StoneProblem("stones.error.colorName", sizeName));
                    }
                    else if (!colorNames.Add(colorName))
                    {
                        problems.Add(new StoneProblem("stones.error.colorDuplicate", sizeName, colorName));
                    }

                    if (!color.TryGetRgb(out _, out _, out _))
                    {
                        problems.Add(new StoneProblem("stones.error.rgb", sizeName, colorName));
                    }
                }
            }

            return problems;
        }

        /// <summary>
        /// Убирает пробелы по краям названий. Вызывать перед сохранением, после <see cref="Validate"/>.
        /// </summary>
        public static void Tidy(StoneSet set)
        {
            set.Name = (set.Name ?? string.Empty).Trim();
            foreach (StoneSize size in set.Sizes)
            {
                size.Name = (size.Name ?? string.Empty).Trim();
                foreach (StoneColor color in size.Colors)
                {
                    color.Name = (color.Name ?? string.Empty).Trim();
                    if (TryNormalizeRgb(color.Rgb, out string rgb))
                    {
                        color.Rgb = rgb;
                    }
                }
            }
        }

        /// <summary>
        /// Понимает цвет в видах "#E53935", "e53935", "#E53" (короткая запись) и "229, 57, 53"
        /// (три числа 0–255) и приводит к "#E53935". false — это не цвет.
        /// </summary>
        public static bool TryNormalizeRgb(string? text, out string rgb)
        {
            rgb = string.Empty;
            string t = (text ?? string.Empty).Trim();
            if (t.Length == 0)
            {
                return false;
            }

            string[] parts = t.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 3)
            {
                var channels = new int[3];
                for (int i = 0; i < 3; i++)
                {
                    if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out channels[i]) ||
                        channels[i] < 0 || channels[i] > 255)
                    {
                        return false;
                    }
                }

                rgb = ToHex((byte)channels[0], (byte)channels[1], (byte)channels[2]);
                return true;
            }

            string hex = t.TrimStart('#');
            if (hex.Length == 3)
            {
                hex = new string(new[] { hex[0], hex[0], hex[1], hex[1], hex[2], hex[2] });
            }

            if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value))
            {
                return false;
            }

            rgb = ToHex((byte)(value >> 16), (byte)(value >> 8), (byte)value);
            return true;
        }

        public static string ToHex(byte red, byte green, byte blue) =>
            "#" + red.ToString("X2", CultureInfo.InvariantCulture) + green.ToString("X2", CultureInfo.InvariantCulture) +
            blue.ToString("X2", CultureInfo.InvariantCulture);

        /// <summary>
        /// Новый размер после <paramref name="after"/> (или в конец): чуть крупнее соседа и с теми же
        /// цветами — обычно у поставщика цвета одинаковые для всех размеров, вводить их заново незачем.
        /// </summary>
        public static StoneSize AddSize(StoneSet set, StoneSize? after, string baseName)
        {
            StoneSize? template = after ?? set.Sizes.LastOrDefault();
            var size = new StoneSize
            {
                Name = UniqueName(set.Sizes.Select(s => s.Name), baseName),
                DiameterMm = template == null ? 2.4 : Math.Min(MaxDiameterMm, Math.Round(template.DiameterMm + 0.1, 2)),
                Colors = template == null
                    ? new List<StoneColor>()
                    : template.Colors.Select(c => new StoneColor { Name = c.Name, Rgb = c.Rgb }).ToList(),
            };

            int index = after == null ? set.Sizes.Count : set.Sizes.IndexOf(after) + 1;
            set.Sizes.Insert(index, size);
            return size;
        }

        /// <summary>Новый цвет в конец списка размера — с уникальным названием, серый.</summary>
        public static StoneColor AddColor(StoneSize size, string baseName)
        {
            var color = new StoneColor { Name = UniqueName(size.Colors.Select(c => c.Name), baseName), Rgb = "#9E9E9E" };
            size.Colors.Add(color);
            return color;
        }

        /// <summary>Копирует цвета размера во все остальные размеры набора (по названию: есть — обновить RGB, нет — добавить).</summary>
        public static void CopyColorsToAllSizes(StoneSet set, StoneSize source)
        {
            foreach (StoneSize size in set.Sizes)
            {
                if (ReferenceEquals(size, source))
                {
                    continue;
                }

                foreach (StoneColor color in source.Colors)
                {
                    StoneColor? existing = size.Colors.FirstOrDefault(c =>
                        string.Equals((c.Name ?? string.Empty).Trim(), (color.Name ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                    {
                        existing.Rgb = color.Rgb;
                    }
                    else
                    {
                        size.Colors.Add(new StoneColor { Name = color.Name, Rgb = color.Rgb });
                    }
                }
            }
        }

        /// <summary>Сдвигает элемент на <paramref name="delta"/> позиций (−1 — вверх). Возвращает новый номер.</summary>
        public static int Move<T>(IList<T> list, int index, int delta)
        {
            if (index < 0 || index >= list.Count)
            {
                return index;
            }

            int target = Math.Max(0, Math.Min(list.Count - 1, index + delta));
            if (target == index)
            {
                return index;
            }

            T item = list[index];
            list.RemoveAt(index);
            list.Insert(target, item);
            return target;
        }

        /// <summary>Размеры по возрастанию диаметра; равные остаются в прежнем порядке.</summary>
        public static void SortByDiameter(StoneSet set)
        {
            List<StoneSize> sorted = set.Sizes.OrderBy(s => s.DiameterMm).ToList();
            set.Sizes.Clear();
            set.Sizes.AddRange(sorted);
        }

        /// <summary>"Новый", если свободно, иначе "Новый 2", "Новый 3"…</summary>
        public static string UniqueName(IEnumerable<string> existing, string baseName)
        {
            var names = new HashSet<string>(existing.Select(n => (n ?? string.Empty).Trim()), StringComparer.OrdinalIgnoreCase);
            if (!names.Contains(baseName))
            {
                return baseName;
            }

            for (int i = 2; ; i++)
            {
                string candidate = baseName + " " + i.ToString(CultureInfo.InvariantCulture);
                if (!names.Contains(candidate))
                {
                    return candidate;
                }
            }
        }
    }
}
