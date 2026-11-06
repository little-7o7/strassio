using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Strassio.Core.Editing;
using Strassio.Core.Geometry;
using Strassio.Core.Methods;
using Strassio.Core.Placement;

namespace Strassio.Preview
{
    /// <summary>
    /// «Выровнять просветы»: ромашка, в которой часть камней убрали и сдвинули рукой (как после удаления
    /// наложений). Неровные места — розовым. Картинки до и после — в out/preview/gaps/.
    /// </summary>
    internal static class GapLab
    {
        private const string Plain = "#E8EEF5";
        private const string Uneven = "#F06292";

        public static void Render(string repoRoot)
        {
            string dir = Path.Combine(repoRoot, "out", "preview", "gaps");
            Directory.CreateDirectory(dir);

            MethodResult made = MethodRunner.Run(MethodKind.F2, new[] { Flower(12, 13, 11) }, 2.4, new MethodParameters { GapMm = 0.2 });
            List<DocStone> stones = HandEdited(made.Stones.Select(s => new DocStone(s.Center, s.DiameterMm, 0)).ToList(), new Random(5));

            IReadOnlyList<int> before = GapEvener.FindUneven(stones, 0.2, out double worstBefore);
            Save(Path.Combine(dir, "flower-before.svg"), stones.Select(s => s.Center).ToList(), stones, before);

            var watch = Stopwatch.StartNew();
            GapEvenResult result = GapEvener.Even(stones, maxShiftMm: 0.5, minGapMm: 0.15);
            long ms = watch.ElapsedMilliseconds;
            List<Point2D> after = stones.Select(s => s.Center).ToList();
            foreach (StoneMove move in result.Edit.Moved)
            {
                after[move.Index] = move.NewCenter;
            }

            Save(Path.Combine(dir, "flower-after.svg"), after, stones, result.UnevenAfter);
            Console.WriteLine(
                $"ромашка: {stones.Count} страз, неровных {result.UnevenBefore.Count} → {result.UnevenAfter.Count}, " +
                $"худшая разница {worstBefore:0.00} → {result.WorstAfterMm:0.00} мм, сдвинуто {result.Edit.Moved.Count}, {ms} мс → {dir}");
        }

        /// <summary>Убираем каждый 25-й камень и чуть сдвигаем треть остальных — не ближе 0,1 мм к соседям.</summary>
        private static List<DocStone> HandEdited(List<DocStone> stones, Random random)
        {
            var kept = stones.Where((s, i) => i % 25 != 7).ToList();
            var centers = kept.Select(s => s.Center).ToList();
            for (int i = 0; i < kept.Count; i++)
            {
                if (random.NextDouble() > 0.33)
                {
                    continue;
                }

                double angle = random.NextDouble() * 2 * Math.PI;
                Point2D moved = centers[i] + new Point2D(Math.Cos(angle), Math.Sin(angle)) * (0.15 + random.NextDouble() * 0.25);
                bool free = true;
                for (int j = 0; j < kept.Count && free; j++)
                {
                    free = j == i || Point2D.Distance(moved, centers[j]) - kept[i].Radius - kept[j].Radius >= 0.1;
                }

                if (free)
                {
                    centers[i] = moved;
                }
            }

            return kept.Select((s, i) => new DocStone(centers[i], s.DiameterMm, 0, s.IsLocked)).ToList();
        }

        private static void Save(string path, IReadOnlyList<Point2D> centers, IReadOnlyList<DocStone> stones, IReadOnlyList<int> uneven)
        {
            var marked = new HashSet<int>(uneven);
            var placed = new List<PlacedStone>();
            var fills = new List<string>();
            for (int i = 0; i < stones.Count; i++)
            {
                placed.Add(new PlacedStone(centers[i], stones[i].DiameterMm, false));
                fills.Add(marked.Contains(i) ? Uneven : Plain);
            }

            File.WriteAllText(path, SvgWriter.RenderColored(placed, fills));
        }

        private static Curve Flower(int petals, double inner, double petal)
        {
            var pts = new List<Point2D>();
            const int n = 720;
            for (int i = 0; i < n; i++)
            {
                double t = 2 * Math.PI * i / n;
                double r = inner + petal * Math.Pow(Math.Abs(Math.Cos(petals / 2.0 * t)), 0.6);
                pts.Add(new Point2D(r * Math.Cos(t), r * Math.Sin(t)));
            }

            return Curve.FromPolyline(pts, isClosed: true);
        }
    }
}
