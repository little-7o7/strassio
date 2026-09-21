#nullable enable
using System.Windows.Media;
using Strassio.Core.Methods;
using Strassio.Core.Stones;

namespace Strassio.Corel
{
    /// <summary>Метод в списке «Метод»: описание из Strassio.Core (вкладка, поля) и переведённое название.</summary>
    public sealed class MethodOption
    {
        internal MethodOption(MethodInfo info)
        {
            Info = info;
            Text = info.Key;
        }

        internal MethodInfo Info { get; }

        public string Key => Info.Key;

        public string Text { get; set; }
    }

    /// <summary>Вариант в выпадающем списке поля параметров: слово для settings.json и переведённый текст.</summary>
    public sealed class ChoiceOption
    {
        internal ChoiceOption(string value, string text)
        {
            Value = value;
            Text = text;
        }

        public string Value { get; }

        public string Text { get; }
    }

    /// <summary>Строка списка размеров: «ss6 — 2,4 мм».</summary>
    public sealed class SizeOption
    {
        internal SizeOption(StoneSize size, string text)
        {
            Size = size;
            Text = text;
        }

        public StoneSize Size { get; }

        public string Text { get; }
    }

    /// <summary>Образец цвета в сетке: название (подсказка) и кисть для кружка.</summary>
    public sealed class ColorOption
    {
        internal ColorOption(StoneColor color)
        {
            Color = color;
            Name = color.Name;
            var brush = color.TryGetRgb(out byte r, out byte g, out byte b)
                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b))
                : new SolidColorBrush(Colors.Transparent);
            brush.Freeze();
            Brush = brush;
        }

        public StoneColor Color { get; }

        public string Name { get; }

        public Brush Brush { get; }
    }
}
