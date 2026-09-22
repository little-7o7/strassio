using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Strassio.Licensing
{
    /// <summary>
    /// Код компьютера (docs/SPEC.md, раздел 13.1): SHA-256 от четырёх аппаратных признаков — UUID
    /// платы/BIOS, серийный номер платы, идентификатор процессора, серийный номер системного диска.
    /// Признаки, меняющиеся при переустановке Windows, не берутся. Хранится по отдельности (4 коротких
    /// хэша), чтобы сравнивать нечётко: смена ОДНОГО признака лицензию не ломает. Пользователю
    /// показывается как XXXX-XXXX-XXXX-XXXX.
    /// </summary>
    public sealed class HardwareCode
    {
        /// <summary>Сколько признаков.</summary>
        public const int ComponentCount = 4;

        /// <summary>Признак не удалось прочитать — при сравнении он ни с чем не спорит.</summary>
        public const string Unknown = "-";

        private HardwareCode(IReadOnlyList<string> hashes)
        {
            Hashes = hashes;
        }

        /// <summary>По 16 шестнадцатеричных знаков на признак (или <see cref="Unknown"/>).</summary>
        public IReadOnlyList<string> Hashes { get; }

        /// <summary>Для людей: XXXX-XXXX-XXXX-XXXX (первые 16 знаков SHA-256 от всех признаков).</summary>
        public string Display
        {
            get
            {
                string hex = Sha256Hex(string.Join("|", Hashes)).Substring(0, 16).ToUpperInvariant();
                return string.Join("-", Enumerable.Range(0, 4).Select(i => hex.Substring(i * 4, 4)));
            }
        }

        /// <summary>
        /// Из «сырых» признаков: пробелы по краям и регистр не важны; пустое или заглушка
        /// производителя («To be filled by O.E.M.», одни нули) — «неизвестно».
        /// </summary>
        public static HardwareCode FromComponents(IReadOnlyList<string?> raw)
        {
            if (raw.Count != ComponentCount)
            {
                throw new ArgumentException("Нужно ровно " + ComponentCount + " признака.", nameof(raw));
            }

            return new HardwareCode(raw.Select((value, i) => IsJunk(value) ? Unknown : Sha256Hex(i + ":" + value!.Trim().ToUpperInvariant()).Substring(0, 16)).ToList());
        }

        /// <summary>Строка для файла лицензии и сервера: хэши через точку.</summary>
        public string ToStorage() => string.Join(".", Hashes);

        public static bool TryParse(string? text, out HardwareCode? code)
        {
            code = null;
            string[] parts = (text ?? string.Empty).Split('.');
            if (parts.Length != ComponentCount || parts.Any(p => p != Unknown && (p.Length != 16 || !p.All(Uri.IsHexDigit))))
            {
                return false;
            }

            code = new HardwareCode(parts.Select(p => p.ToLowerInvariant()).ToList());
            return true;
        }

        /// <summary>
        /// Тот же компьютер? Сравниваются только признаки, известные в обоих кодах; различаться может
        /// не больше <paramref name="allowedDifferences"/> (по умолчанию 1 — поменяли диск или плату).
        /// Совпасть должны хотя бы два признака.
        /// </summary>
        public bool Matches(HardwareCode other, int allowedDifferences = 1)
        {
            int same = 0, different = 0;
            for (int i = 0; i < ComponentCount; i++)
            {
                if (Hashes[i] == Unknown || other.Hashes[i] == Unknown)
                {
                    continue;
                }

                if (Hashes[i] == other.Hashes[i])
                {
                    same++;
                }
                else
                {
                    different++;
                }
            }

            return different <= allowedDifferences && same >= 2;
        }

        private static bool IsJunk(string? value)
        {
            string v = (value ?? string.Empty).Trim();
            if (v.Length == 0)
            {
                return true;
            }

            string upper = v.ToUpperInvariant();
            if (upper.Contains("O.E.M") || upper.Contains("DEFAULT STRING") || upper.Contains("NOT APPLICABLE") ||
                upper == "NONE" || upper == "SYSTEM SERIAL NUMBER")
            {
                return true;
            }

            return v.All(c => c == '0' || c == '-' || c == 'F' || c == 'f' || c == ' ');
        }

        internal static string Sha256Hex(string text)
        {
            using SHA256 sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
            return string.Concat(hash.Select(b => b.ToString("x2")));
        }
    }

    /// <summary>Откуда брать признаки компьютера (в аддоне — WMI; в тестах — подставные значения).</summary>
    public interface IHardwareSource
    {
        /// <summary>Четыре признака: UUID платы/BIOS, серийный номер платы, процессор, системный диск.</summary>
        IReadOnlyList<string?> ReadComponents();
    }
}
