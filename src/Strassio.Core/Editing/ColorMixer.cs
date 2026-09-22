using System;
using System.Collections.Generic;
using System.Linq;

namespace Strassio.Core.Editing
{
    /// <summary>
    /// Смешение цветов (docs/SPEC.md, раздел 3.4): какой цвет получит каждая страза.
    /// «Случайно» — доли в процентах соблюдаются точно (70% из 100 страз — ровно 70), а стразы
    /// выбираются вперемешку; «по очереди» — шаблон вдоль линии, например 2 и 1 → К-К-Б-К-К-Б.
    /// </summary>
    public static class ColorMixer
    {
        /// <summary>
        /// Случайное смешение: номер цвета для каждой из <paramref name="count"/> страз по долям
        /// <paramref name="weights"/> (например 70 и 30). <paramref name="seed"/> — тот же номер даёт ту же раскладку.
        /// </summary>
        public static int[] Random(int count, IReadOnlyList<double> weights, int seed)
        {
            int[] quota = Quotas(count, weights);
            var result = new List<int>(count);
            for (int c = 0; c < quota.Length; c++)
            {
                result.AddRange(Enumerable.Repeat(c, quota[c]));
            }

            // Перемешивание Фишера–Йетса.
            var rnd = new Random(seed);
            for (int i = result.Count - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                (result[i], result[j]) = (result[j], result[i]);
            }

            return result.ToArray();
        }

        /// <summary>По очереди: цвет c повторяется <paramref name="runs"/>[c] раз подряд, и так по кругу.</summary>
        public static int[] Sequence(int count, IReadOnlyList<int> runs)
        {
            var pattern = new List<int>();
            for (int c = 0; c < runs.Count; c++)
            {
                pattern.AddRange(Enumerable.Repeat(c, Math.Max(0, runs[c])));
            }

            if (pattern.Count == 0)
            {
                pattern.Add(0);
            }

            return Enumerable.Range(0, count).Select(i => pattern[i % pattern.Count]).ToArray();
        }

        /// <summary>Сколько страз каждого цвета: доли, округлённые так, чтобы в сумме вышло ровно <paramref name="count"/>.</summary>
        public static int[] Quotas(int count, IReadOnlyList<double> weights)
        {
            double total = weights.Where(w => w > 0).Sum();
            var quota = new int[weights.Count];
            if (total <= 0 || count <= 0)
            {
                if (weights.Count > 0 && count > 0)
                {
                    quota[0] = count;
                }

                return quota;
            }

            var remainders = new List<(int Index, double Fraction)>();
            int assigned = 0;
            for (int c = 0; c < weights.Count; c++)
            {
                double exact = count * Math.Max(0, weights[c]) / total;
                quota[c] = (int)Math.Floor(exact);
                assigned += quota[c];
                remainders.Add((c, exact - quota[c]));
            }

            // Остаток раздаём тем, у кого дробная часть больше (метод наибольших остатков).
            foreach ((int index, _) in remainders.OrderByDescending(r => r.Fraction).ThenBy(r => r.Index).Take(count - assigned))
            {
                quota[index]++;
            }

            return quota;
        }
    }
}
