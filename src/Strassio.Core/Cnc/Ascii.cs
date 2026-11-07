using System.Collections.Generic;
using System.Text;

namespace Strassio.Core.Cnc
{
    /// <summary>
    /// Латиница для имён файлов и пояснений в G-code: пульты станков (DSP, NcStudio) часто показывают
    /// кириллицу «квадратиками», а скобки внутри пояснения ломают строку. «Розовый» → «Rozovyy».
    /// </summary>
    public static class Ascii
    {
        private static readonly Dictionary<char, string> Cyrillic = new Dictionary<char, string>
        {
            ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['е'] = "e", ['ё'] = "e",
            ['ж'] = "zh", ['з'] = "z", ['и'] = "i", ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m",
            ['н'] = "n", ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t", ['у'] = "u",
            ['ф'] = "f", ['х'] = "kh", ['ц'] = "ts", ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "shch", ['ъ'] = "",
            ['ы'] = "y", ['ь'] = "", ['э'] = "e", ['ю'] = "yu", ['я'] = "ya",
            ['ў'] = "o", ['қ'] = "q", ['ғ'] = "g", ['ҳ'] = "h", ['і'] = "i", ['ї'] = "yi", ['є'] = "ye", ['ґ'] = "g",
        };

        /// <summary>Только латиница, цифры и «безопасные» знаки; кириллица — транслитом.</summary>
        public static string Translit(string text)
        {
            var sb = new StringBuilder();
            foreach (char ch in text ?? string.Empty)
            {
                char lower = char.ToLowerInvariant(ch);
                if (Cyrillic.TryGetValue(lower, out string? latin))
                {
                    if (latin.Length > 0 && ch != lower)
                    {
                        sb.Append(char.ToUpperInvariant(latin[0])).Append(latin, 1, latin.Length - 1);
                    }
                    else
                    {
                        sb.Append(latin);
                    }
                }
                else if (ch < 128)
                {
                    sb.Append(ch);
                }
            }

            return sb.ToString();
        }

        /// <summary>Часть имени файла: латиница, цифры, «.», «-», «_»; пробелы — «_».</summary>
        public static string FileName(string text)
        {
            var sb = new StringBuilder();
            foreach (char ch in Translit(text))
            {
                if (char.IsLetterOrDigit(ch) || ch == '.' || ch == '-' || ch == '_')
                {
                    sb.Append(ch);
                }
                else if (ch == ' ' && sb.Length > 0 && sb[sb.Length - 1] != '_')
                {
                    sb.Append('_');
                }
            }

            return sb.ToString().Trim('_', '.', '-');
        }

        /// <summary>Текст для пояснения в скобках: без скобок и управляющих знаков.</summary>
        public static string Comment(string text)
        {
            var sb = new StringBuilder();
            foreach (char ch in Translit(text))
            {
                if (ch >= 32 && ch != '(' && ch != ')' && ch != ';' && ch != '%')
                {
                    sb.Append(ch);
                }
            }

            return sb.ToString().Trim();
        }
    }
}
