using System;
using System.Globalization;

namespace Strassio.Core.Settings
{
    /// <summary>Единицы, в которых пользователь видит и вводит размеры.</summary>
    public enum LengthUnit
    {
        Millimeter,
        Inch,
    }

    /// <summary>
    /// Перевод мм ↔ единицы интерфейса. Внутри плагина всё считается в миллиметрах (CLAUDE.md,
    /// правило 6); дюймы появляются только здесь — при показе числа и при разборе введённого.
    /// </summary>
    public static class LengthUnits
    {
        public const double MillimetersPerInch = 25.4;

        public static double FromMillimeters(double mm, LengthUnit unit) =>
            unit == LengthUnit.Inch ? mm / MillimetersPerInch : mm;

        public static double ToMillimeters(double value, LengthUnit unit) =>
            unit == LengthUnit.Inch ? value * MillimetersPerInch : value;

        /// <summary>Число для показа: не больше <paramref name="decimals"/> знаков, без лишних нулей в конце.</summary>
        public static string Format(double mm, LengthUnit unit, int decimals, CultureInfo culture)
        {
            double value = Math.Round(FromMillimeters(mm, unit), Math.Max(0, Math.Min(decimals, 10)));
            return value.ToString("0.##########", culture);
        }

        /// <summary>Разбирает введённое число; и запятая, и точка годятся как разделитель. Результат — в мм.</summary>
        public static bool TryParse(string? text, LengthUnit unit, out double mm)
        {
            mm = 0;
            if (text == null)
            {
                return false;
            }

            string normalized = text.Trim().Replace(',', '.');
            if (normalized.Length == 0 ||
                !double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
                double.IsNaN(value) || double.IsInfinity(value))
            {
                return false;
            }

            mm = ToMillimeters(value, unit);
            return true;
        }
    }
}
