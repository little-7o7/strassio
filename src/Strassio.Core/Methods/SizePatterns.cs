using System;
using System.Collections.Generic;
using System.Linq;

namespace Strassio.Core.Methods
{
    /// <summary>
    /// Шаблон размеров для L6 «чередование размеров» (docs/SPEC.md, раздел 4): названия из таблицы
    /// камней через запятую или точку с запятой, например «ss6, ss6, ss10». Пробелы внутри названия
    /// допустимы («ss6 big»), поэтому разделитель — только запятая и точка с запятой.
    /// </summary>
    public static class SizePatterns
    {
        public static string[] Split(string? pattern) =>
            (pattern ?? string.Empty)
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .ToArray();

        /// <summary>Диаметры по шаблону; неизвестные названия пропускаются.</summary>
        public static List<double> Parse(string? pattern, IReadOnlyDictionary<string, double>? sizes)
        {
            var result = new List<double>();
            if (sizes == null)
            {
                return result;
            }

            foreach (string name in Split(pattern))
            {
                foreach (KeyValuePair<string, double> size in sizes)
                {
                    if (string.Equals(size.Key.Trim(), name, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(size.Value);
                        break;
                    }
                }
            }

            return result;
        }

        /// <summary>Названия из шаблона, которых нет в таблице (пусто — шаблон годится).</summary>
        public static IReadOnlyList<string> Unknown(string? pattern, IEnumerable<string> knownSizes)
        {
            var known = new HashSet<string>(knownSizes.Select(k => k.Trim()), StringComparer.OrdinalIgnoreCase);
            return Split(pattern).Where(n => !known.Contains(n)).ToList();
        }
    }
}
