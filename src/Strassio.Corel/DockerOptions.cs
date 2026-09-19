#nullable enable
using System;
using System.Collections.Generic;
using System.Windows.Media;
using Strassio.Core.Placement;
using Strassio.Core.Stones;
using CoreCurve = Strassio.Core.Geometry.Curve;

namespace Strassio.Corel
{
    /// <summary>Метод расстановки в списке «Метод»: ключ текста, вкладка и сама функция из Strassio.Core.</summary>
    public sealed class MethodOption
    {
        internal MethodOption(
            string key, bool isFill, bool needsClosed, Func<IReadOnlyList<CoreCurve>, double, IReadOnlyList<PlacedStone>> scatter)
        {
            Key = key;
            IsFill = isFill;
            NeedsClosed = needsClosed;
            Scatter = scatter;
            Text = key;
        }

        public string Key { get; }

        public string Text { get; set; }

        internal bool IsFill { get; }

        internal bool NeedsClosed { get; }

        /// <summary>Контуры фигуры и диаметр камня, мм → стразы.</summary>
        internal Func<IReadOnlyList<CoreCurve>, double, IReadOnlyList<PlacedStone>> Scatter { get; }
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
