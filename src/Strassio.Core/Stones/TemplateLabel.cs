using System;
using System.Collections.Generic;
using System.Linq;
using Strassio.Core.Geometry;

namespace Strassio.Core.Stones
{
    /// <summary>Одна страза для подписи шаблона: центр, диаметр, размер и цвет.</summary>
    public readonly struct LabelStone
    {
        public LabelStone(Point2D center, double diameterMm, string size, string color)
        {
            Center = center;
            DiameterMm = diameterMm;
            Size = size;
            Color = color;
        }

        public Point2D Center { get; }

        public double DiameterMm { get; }

        public string Size { get; }

        public string Color { get; }
    }

    /// <summary>
    /// Подпись шаблона (пункт TRW-3 прокачки): габарит дизайна по краям камней, сколько камней каждого
    /// размера и цвета и диаметр каждого размера. Текст подписи собирает плагин из файлов языков; здесь
    /// только числа и место под дизайном.
    /// </summary>
    public sealed class TemplateLabel
    {
        private TemplateLabel(double minX, double minY, double maxX, double maxY, StoneCount count, IReadOnlyDictionary<string, double> diameters)
        {
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
            Count = count;
            Diameters = diameters;
        }

        public double MinX { get; }

        public double MinY { get; }

        public double MaxX { get; }

        public double MaxY { get; }

        /// <summary>Ширина дизайна по краям камней, мм.</summary>
        public double Width => MaxX - MinX;

        /// <summary>Высота дизайна по краям камней, мм.</summary>
        public double Height => MaxY - MinY;

        public StoneCount Count { get; }

        /// <summary>Диаметр каждого размера (самый частый, если у одного имени встречаются разные), мм.</summary>
        public IReadOnlyDictionary<string, double> Diameters { get; }

        /// <summary>Подпись для камней; null — камней нет.</summary>
        public static TemplateLabel? Of(IReadOnlyList<LabelStone> stones, IReadOnlyList<string> sizeOrder)
        {
            if (stones == null || stones.Count == 0)
            {
                return null;
            }

            double minX = stones.Min(s => s.Center.X - s.DiameterMm / 2);
            double minY = stones.Min(s => s.Center.Y - s.DiameterMm / 2);
            double maxX = stones.Max(s => s.Center.X + s.DiameterMm / 2);
            double maxY = stones.Max(s => s.Center.Y + s.DiameterMm / 2);
            StoneCount count = StoneCount.Of(stones.Select(s => (s.Size, s.Color)), sizeOrder);
            var diameters = stones
                .GroupBy(s => string.IsNullOrWhiteSpace(s.Size) ? StoneCount.Unknown : s.Size.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.GroupBy(s => Math.Round(s.DiameterMm, 3)).OrderByDescending(d => d.Count()).First().Key,
                    StringComparer.OrdinalIgnoreCase);
            return new TemplateLabel(minX, minY, maxX, maxY, count, diameters);
        }

        /// <summary>Верхний левый угол подписи: под дизайном, с отступом <paramref name="gapMm"/>, по левому краю.</summary>
        public Point2D Anchor(double gapMm) => new Point2D(MinX, MinY - gapMm);
    }
}
