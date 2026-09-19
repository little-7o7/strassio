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
    }
}
