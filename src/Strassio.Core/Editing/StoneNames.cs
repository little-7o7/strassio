using System;
using System.Collections.Generic;
using System.Linq;
using Strassio.Core.Stones;

namespace Strassio.Core.Editing
{
    /// <summary>
    /// Имя стразы в документе — «размер цвет», например «ss6 Красный» (docs/SPEC.md, раздел 3.3).
    /// Новые стразы дополнительно помечаются невидимыми метками CorelDRAW (размер и цвет отдельно),
    /// но стразы, созданные раньше или переименованные вручную, узнаются только по имени — поэтому
    /// имя тоже умеем разбирать.
    /// </summary>
    public static class StoneNames
    {
        public static string Compose(string size, string color) => size + " " + color;

        /// <summary>
        /// Разбирает имя на размер и цвет. Сначала ищет размеры из таблицы (самый длинный подходящий —
        /// так «ss6 big» не спутается с «ss6»), потом — просто первое слово. false — имя пустое.
        /// </summary>
        public static bool TryParse(string? name, IEnumerable<string> knownSizes, out string size, out string color)
        {
            size = string.Empty;
            color = string.Empty;
            string text = (name ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                return false;
            }

            foreach (string known in knownSizes.Where(k => !string.IsNullOrWhiteSpace(k)).OrderByDescending(k => k.Length))
            {
                string k = known.Trim();
                if (text.Equals(k, StringComparison.OrdinalIgnoreCase))
                {
                    size = k;
                    return true;
                }

                if (text.StartsWith(k + " ", StringComparison.OrdinalIgnoreCase))
                {
                    size = k;
                    color = text.Substring(k.Length + 1).Trim();
                    return true;
                }
            }

            int space = text.IndexOf(' ');
            size = space < 0 ? text : text.Substring(0, space);
            color = space < 0 ? string.Empty : text.Substring(space + 1).Trim();
            return true;
        }

        /// <summary>Все названия размеров из таблицы камней.</summary>
        public static IEnumerable<string> SizesOf(StoneTable table) =>
            table.Sets.SelectMany(s => s.Sizes).Select(s => s.Name);

        /// <summary>Подходит ли страза под выбор «размер», «цвет» или «размер и цвет» (пустое — любой).</summary>
        public static bool Matches(string size, string color, string? wantSize, string? wantColor) =>
            (string.IsNullOrEmpty(wantSize) || string.Equals(size, wantSize, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrEmpty(wantColor) || string.Equals(color, wantColor, StringComparison.OrdinalIgnoreCase));
    }
}
