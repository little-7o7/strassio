using Strassio.Core.Geometry;
using Strassio.Core.Motifs;

namespace Strassio.Core.Tests.Motifs;

/// <summary>Готовые мотивы и рамки: собираются из камней пользователя, без наложений, у каждого своё имя.</summary>
public class StarterMotifsTests
{
    private static readonly StarterSize[] Sizes =
    {
        new StarterSize("ss6", 2.0), new StarterSize("ss10", 2.8), new StarterSize("ss16", 3.9), new StarterSize("ss20", 4.7),
    };

    private static readonly StarterColor[] Colors =
    {
        new StarterColor("Кристалл", "#E8EEF5"), new StarterColor("Золото", "#D4A017"),
    };

    [Fact]
    public void Kit_TakesNeighbourSizes_AndAnotherColour()
    {
        StarterKit kit = StarterKit.From(Sizes, Sizes[1], Colors, Colors[0], 0.3);

        Assert.Equal("ss6", kit.Small.Name);
        Assert.Equal("ss10", kit.Main.Name);
        Assert.Equal("ss16", kit.Large.Name);
        Assert.Equal("Золото", kit.Accent.Name);

        // Один размер и один цвет — всё из него.
        StarterKit single = StarterKit.From(new[] { Sizes[0] }, Sizes[0], new[] { Colors[0] }, Colors[0], 0.3);
        Assert.Equal("ss6", single.Small.Name);
        Assert.Equal("ss6", single.Large.Name);
        Assert.Equal("Кристалл", single.Accent.Name);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void Build_MakesEveryMotif_WithoutOverlaps_FromTheKitOnly(int mainIndex)
    {
        StarterKit kit = StarterKit.From(Sizes, Sizes[mainIndex], Colors, Colors[0], 0.3);

        List<Motif> motifs = StarterMotifs.Build(kit, key => "[" + key + "]");

        Assert.Equal(StarterMotifs.Keys.Count, motifs.Count);
        Assert.Equal(motifs.Count, motifs.Select(m => m.Name).Distinct().Count());
        var allowedSizes = new[] { kit.Small, kit.Main, kit.Large }.GroupBy(s => s.Name).ToDictionary(g => g.Key, g => g.First().DiameterMm);
        foreach (Motif motif in motifs)
        {
            Assert.EndsWith($"({kit.Main.Name})", motif.Name);
            Assert.True(motif.Stones.Count >= 3, $"{motif.Name}: {motif.Stones.Count} камней");
            foreach (MotifStone s in motif.Stones)
            {
                Assert.True(allowedSizes.TryGetValue(s.Size, out double d) && Math.Abs(d - s.DiameterMm) < 1e-9, $"{motif.Name}: размер {s.Size}");
                Assert.Contains(s.Color, new[] { kit.Color.Name, kit.Accent.Name });
            }

            for (int i = 0; i < motif.Stones.Count; i++)
            {
                for (int j = i + 1; j < motif.Stones.Count; j++)
                {
                    MotifStone a = motif.Stones[i], b = motif.Stones[j];
                    double clear = Point2D.Distance(a.Offset, b.Offset) - (a.DiameterMm + b.DiameterMm) / 2;
                    Assert.True(clear >= 0.05, $"{motif.Name}: камни {i} и {j} почти касаются ({clear:0.000} мм)");
                }
            }
        }
    }

    [Fact]
    public void Motifs_SaveAndLoad_ThroughTheLibrary()
    {
        string folder = Path.Combine(Path.GetTempPath(), "strassio-starter-" + Guid.NewGuid().ToString("N"));
        try
        {
            var library = new MotifLibrary(folder);
            Assert.False(library.StarterInstalled(StarterMotifs.Version));

            StarterKit kit = StarterKit.From(Sizes, Sizes[1], Colors, Colors[0], 0.3);
            int added = library.InstallStarter(StarterMotifs.Build(kit, key => key), StarterMotifs.Version);

            Assert.Equal(StarterMotifs.Keys.Count, added);
            Assert.True(library.StarterInstalled(StarterMotifs.Version));
            Assert.Equal(added, library.LoadAll().Count);

            // Повторная установка не дублирует и не трогает то, что уже есть.
            Assert.Equal(0, library.InstallStarter(StarterMotifs.Build(kit, key => key), StarterMotifs.Version));
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
