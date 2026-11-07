using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Strassio.Core.Motifs;

namespace Strassio.Preview
{
    /// <summary>
    /// Готовые мотивы и рамки в комплекте: весь набор на одном листе — под ss10 и под ss16, чтобы видно было,
    /// что мотивы подстраиваются под камни. Картинки — в out/preview/starter/.
    /// </summary>
    internal static class StarterLab
    {
        private static readonly StarterSize[] Sizes =
        {
            new StarterSize("ss6", 2.0), new StarterSize("ss10", 2.8), new StarterSize("ss16", 3.9), new StarterSize("ss20", 4.7),
        };

        private static readonly StarterColor[] Colors =
        {
            new StarterColor("Розовый", "#E91E63"), new StarterColor("Золото", "#FFC107"),
        };

        public static void Render(string repoRoot)
        {
            string dir = Path.Combine(repoRoot, "out", "preview", "starter");
            Directory.CreateDirectory(dir);
            foreach (StarterSize main in new[] { Sizes[1], Sizes[2] })
            {
                StarterKit kit = StarterKit.From(Sizes, main, Colors, Colors[0], 0.3);
                List<Motif> motifs = StarterMotifs.Build(kit, key => key);
                string file = Path.Combine(dir, $"starter-{main.Name}.svg");
                File.WriteAllText(file, Sheet(motifs));
                Console.WriteLine($"{Path.GetFileName(file)}: {motifs.Count} мотивов, " +
                    string.Join(", ", motifs.Select(m => $"{m.Name.Split(' ')[0]} {m.Stones.Count} ({m.Width:0}×{m.Height:0} мм)")));
            }

            Console.WriteLine($"SVG сохранены: {dir}");
        }

        /// <summary>Мотивы сеткой по 6 в ряд, каждый в своей клетке со своим масштабом.</summary>
        private static string Sheet(IReadOnlyList<Motif> motifs)
        {
            const int perRow = 6;
            const double cell = 180, label = 18;
            var ci = CultureInfo.InvariantCulture;
            int rows = (motifs.Count + perRow - 1) / perRow;
            var sb = new StringBuilder();
            sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{perRow * cell}\" height=\"{rows * (cell + label)}\" font-family=\"sans-serif\" font-size=\"13\">");
            sb.Append("<rect width=\"100%\" height=\"100%\" fill=\"white\"/>");
            for (int i = 0; i < motifs.Count; i++)
            {
                Motif m = motifs[i];
                double x0 = i % perRow * cell, y0 = i / perRow * (cell + label);
                double k = (cell - 20) / Math.Max(m.Width, m.Height);
                sb.Append($"<text x=\"{(x0 + cell / 2).ToString(ci)}\" y=\"{(y0 + cell + 12).ToString(ci)}\" text-anchor=\"middle\" fill=\"#555\">{m.Name} · {m.Stones.Count}</text>");
                foreach (MotifStone s in m.Stones)
                {
                    double cx = x0 + cell / 2 + s.X * k, cy = y0 + cell / 2 - s.Y * k;
                    sb.Append($"<circle cx=\"{cx.ToString("0.#", ci)}\" cy=\"{cy.ToString("0.#", ci)}\" r=\"{(s.DiameterMm / 2 * k).ToString("0.##", ci)}\" fill=\"{s.Rgb}\" stroke=\"#37474F\" stroke-width=\"0.6\"/>");
                }
            }

            sb.Append("</svg>");
            return sb.ToString();
        }
    }
}
