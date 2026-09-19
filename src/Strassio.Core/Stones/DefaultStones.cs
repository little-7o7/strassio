using System;
using System.Collections.Generic;

namespace Strassio.Core.Stones
{
    /// <summary>
    /// Стартовая таблица камней для первого запуска — пример из docs/SPEC.md, раздел 3.1–3.2.
    /// Пользователь потом правит её сам (размеры у поставщиков разные). Названия набора и цветов —
    /// через файлы языков (ключи "stones.default.*").
    /// </summary>
    public static class DefaultStones
    {
        public static StoneTable Create(Func<string, string> text)
        {
            List<StoneColor> Colors() => new List<StoneColor>
            {
                new StoneColor { Name = text("stones.default.crystal"), Rgb = "#E8EEF5" },
                new StoneColor { Name = text("stones.default.red"), Rgb = "#E53935" },
                new StoneColor { Name = text("stones.default.green"), Rgb = "#43A047" },
                new StoneColor { Name = text("stones.default.gold"), Rgb = "#D4A017" },
            };

            return new StoneTable
            {
                Sets = new List<StoneSet>
                {
                    new StoneSet
                    {
                        Name = text("stones.default.set"),
                        Sizes = new List<StoneSize>
                        {
                            new StoneSize { Name = "ss5", DiameterMm = 2.1, Colors = Colors() },
                            new StoneSize { Name = "ss6", DiameterMm = 2.4, Colors = Colors() },
                            new StoneSize { Name = "ss8", DiameterMm = 2.5, Colors = Colors() },
                        },
                    },
                },
            };
        }

        /// <summary>
        /// Чинит таблицу, которую первая версия Этапа 2 записала без файлов языков: вместо названий
        /// там остались ключи вида "[stones.default.red]". Меняет только такие названия (свои
        /// названия пользователя не трогает). true — что-то исправлено, таблицу надо сохранить.
        /// </summary>
        public static bool RepairUntranslatedNames(StoneTable table, Func<string, string> text)
        {
            bool changed = false;

            string Fix(string name)
            {
                if (name != null && name.StartsWith("[stones.default.", StringComparison.Ordinal) && name.EndsWith("]", StringComparison.Ordinal))
                {
                    string key = name.Substring(1, name.Length - 2);
                    string translated = text(key);
                    if (!translated.StartsWith("[", StringComparison.Ordinal))
                    {
                        changed = true;
                        return translated;
                    }
                }

                return name!;
            }

            foreach (StoneSet set in table.Sets)
            {
                set.Name = Fix(set.Name);
                foreach (StoneSize size in set.Sizes)
                {
                    foreach (StoneColor color in size.Colors)
                    {
                        color.Name = Fix(color.Name);
                    }
                }
            }

            return changed;
        }
    }
}
