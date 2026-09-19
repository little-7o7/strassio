using System.Globalization;
using System.Runtime.Serialization;

namespace Strassio.Core.Stones
{
    /// <summary>Цвет камня — название и RGB (docs/SPEC.md, раздел 3.2).</summary>
    [DataContract]
    public sealed class StoneColor
    {
        [DataMember(Name = "name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>RGB в виде "#RRGGBB".</summary>
        [DataMember(Name = "rgb")]
        public string Rgb { get; set; } = "#000000";

        /// <summary>Разбирает <see cref="Rgb"/> ("#RRGGBB" или "RRGGBB"). false — строка испорчена.</summary>
        public bool TryGetRgb(out byte red, out byte green, out byte blue)
        {
            red = green = blue = 0;
            string text = (Rgb ?? string.Empty).Trim().TrimStart('#');
            if (text.Length != 6 ||
                !int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value))
            {
                return false;
            }

            red = (byte)(value >> 16);
            green = (byte)(value >> 8);
            blue = (byte)value;
            return true;
        }
    }
}
