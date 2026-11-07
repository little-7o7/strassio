using Strassio.Core.Geometry;
using Strassio.Core.Motifs;

namespace Strassio.Core.Tests.Motifs;

/// <summary>Мотивы: сохранение, по линии, по кругу, рамка, повтор вокруг центра (docs/SPEC.md, раздел 9).</summary>
public class MotifTests
{
    private const double Gap = 0.2;

    /// <summary>Цветочек: серединка и шесть лепестков вокруг, ширина 3·2,8 + 2·0,2 ≈ 8,8 мм.</summary>
    private static Motif Flower(string name = "flower", double d = 2.8)
    {
        var stones = new List<MotifStone> { new(0, 0, d, "ss12", "Жёлтый", "#FFD54F") };
        for (int i = 0; i < 6; i++)
        {
            double a = Math.PI / 3 * i;
            stones.Add(new MotifStone((d + Gap) * Math.Cos(a), (d + Gap) * Math.Sin(a), d, "ss12", "Розовый", "#F48FB1"));
        }

        return Motif.FromStones(name, stones);
    }

    /// <summary>«Стрелка вверх»: камень в середине и один над ним — видно, куда смотрит верх мотива.</summary>
    private static Motif Arrow() => Motif.FromStones("arrow", new[]
    {
        new MotifStone(10, 10, 2, "ss6", "A", "#000000"),
        new MotifStone(10, 13, 2, "ss6", "B", "#FF0000"),
    });

    private static Curve Line(double length) => Curve.FromPolyline(new List<Point2D> { new(0, 0), new(length, 0) });

    private static Curve Circle(double r)
    {
        var pts = new List<Point2D>();
        for (int i = 0; i < 180; i++)
        {
            double a = 2 * Math.PI * i / 180;
            pts.Add(new Point2D(r * Math.Cos(a), r * Math.Sin(a)));
        }

        return Curve.FromPolyline(pts, true);
    }

    private static void AssertNoOverlaps(MotifLayout layout, double gap)
    {
        var s = layout.Stones;
        for (int i = 0; i < s.Count; i++)
        {
            for (int j = i + 1; j < s.Count; j++)
            {
                double need = (s[i].Source.DiameterMm + s[j].Source.DiameterMm) / 2 + gap - 0.03;
                Assert.True(Point2D.Distance(s[i].Center, s[j].Center) >= need, $"камни {i} и {j} налезают");
            }
        }
    }

    [Fact]
    public void FromStones_CentersMotif_AndSurvivesJson()
    {
        Motif arrow = Arrow();
        Assert.Equal(0, arrow.Stones.Average(s => s.X), 6);
        Assert.Equal(-1.5, arrow.Stones[0].Y, 6);
        Assert.Equal(2, arrow.Width, 6);
        Assert.Equal(5, arrow.Height, 6);

        Motif back = Motif.FromJson(arrow.ToJson());
        Assert.Equal("arrow", back.Name);
        Assert.Equal(2, back.Stones.Count);
        Assert.Equal("#FF0000", back.Stones[1].Rgb);
        Assert.Equal("ss6", back.Stones[1].Size);
        Assert.Equal(1.5, back.Stones[1].Y, 6);
    }

    [Fact]
    public void Library_SavesListsAndDeletes()
    {
        string dir = Path.Combine(Path.GetTempPath(), "strassio-motifs-" + Guid.NewGuid().ToString("N"));
        try
        {
            var library = new MotifLibrary(dir);
            Assert.Empty(library.LoadAll());

            library.Save(Flower("Ромашка: 1/2"));
            library.Save(Arrow());
            File.WriteAllText(Path.Combine(dir, "broken.json"), "{not json");

            List<Motif> all = library.LoadAll();
            Assert.Equal(new[] { "arrow", "Ромашка: 1/2" }, all.Select(m => m.Name));
            Assert.True(library.Exists("Ромашка: 1/2"));

            library.Delete("arrow");
            Assert.Single(library.LoadAll());
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }

    [Fact]
    public void AlongLine_FitsEvenly_FromEndToEnd()
    {
        Motif flower = Flower();
        double w = flower.Width;
        MotifLayout layout = MotifPlacer.AlongLine(Line(100), new[] { flower }, new MotifLayoutOptions { SpacingMm = 2 });

        int expected = (int)Math.Floor((100 - w) / (w + 2)) + 1;
        Assert.Equal(expected, layout.Copies);
        Assert.Equal(0, layout.Dropped);
        var middles = layout.Stones.Where(s => s.Source.Color == "Жёлтый").Select(s => s.Center.X).OrderBy(x => x).ToList();
        Assert.Equal(w / 2, middles[0], 3);
        Assert.Equal(100 - w / 2, middles[^1], 3);
        AssertNoOverlaps(layout, Gap);
    }

    [Fact]
    public void AlongLine_ExactCount_AndAlternates()
    {
        Motif small = Flower("small", 2.0);
        Motif big = Flower("big", 3.0);
        MotifLayout layout = MotifPlacer.AlongLine(Line(120), new[] { small, big }, new MotifLayoutOptions { Count = 6 });

        Assert.Equal(6, layout.Copies);
        var copies = layout.Stones.GroupBy(s => s.Copy).OrderBy(g => g.Key).Select(g => g.First().Source.DiameterMm).ToList();
        Assert.Equal(new[] { 2.0, 3.0, 2.0, 3.0, 2.0, 3.0 }, copies);
    }

    [Fact]
    public void Circle_MakesRosette_TopOutward_OrInwardWhenFlipped()
    {
        const double r = 20;
        MotifLayout outward = MotifPlacer.AlongLine(Circle(r), new[] { Arrow() }, new MotifLayoutOptions { Count = 8 });
        Assert.Equal(8, outward.Copies);
        foreach (MotifPlacement p in outward.Stones.Where(s => s.Source.Color == "B"))
        {
            Assert.InRange(p.Center.Length, r + 1.4, r + 1.6);
        }

        MotifLayout inward = MotifPlacer.AlongLine(Circle(r), new[] { Arrow() }, new MotifLayoutOptions { Count = 8, Flip = true });
        foreach (MotifPlacement p in inward.Stones.Where(s => s.Source.Color == "B"))
        {
            Assert.InRange(p.Center.Length, r - 1.6, r - 1.4);
        }
    }

    [Fact]
    public void Frame_PutsCornerMotifInEveryCorner_AndFillsSides()
    {
        var rect = Curve.FromPolyline(new List<Point2D> { new(0, 0), new(80, 0), new(80, 50), new(0, 50) }, true);
        Motif corner = Flower("corner", 3.0);
        Motif side = Flower("side", 2.0);
        MotifLayout layout = MotifPlacer.Frame(rect, new[] { side }, corner, new MotifLayoutOptions { SpacingMm = 1.5 });

        var cornerMiddles = layout.Stones.Where(s => s.Source.DiameterMm == 3.0 && s.Source.Color == "Жёлтый").Select(s => s.Center).ToList();
        Assert.Equal(4, cornerMiddles.Count);
        foreach (Point2D c in new Point2D[] { new(0, 0), new(80, 0), new(80, 50), new(0, 50) })
        {
            Assert.Contains(cornerMiddles, m => Point2D.Distance(m, c) < 0.01);
        }

        // Угол занимает 9,4/2 + 1,5 = 6,2 мм с каждой стороны; цветок сторон — 6,4 мм + 1,5 промежуток:
        // на длинной стороне (80 − 12,4) влезает 8, на короткой (50 − 12,4) — 4.
        Assert.Equal(4 + 2 * 8 + 2 * 4, layout.Copies);
        Assert.Equal(0, layout.Dropped);
        AssertNoOverlaps(layout, Gap);
    }

    [Fact]
    public void Frame_WithoutCorners_IsLikeAlongLine()
    {
        MotifLayout frame = MotifPlacer.Frame(Circle(15), new[] { Flower() }, Flower("corner", 3.0), new MotifLayoutOptions { Count = 5 });
        Assert.Equal(5, frame.Copies);
        Assert.All(frame.Stones, s => Assert.Equal(2.8, s.Source.DiameterMm));
    }

    [Fact]
    public void Around_RepeatsStonesAroundCenter_SkippingThePivot()
    {
        var centers = new List<Point2D> { new(10, 0), new(0, 0) };
        var diameters = new List<double> { 2.0, 2.0 };
        List<RotatedCopy> copies = MotifPlacer.Around(centers, diameters, Point2D.Zero, 6, Gap);

        Assert.Equal(5, copies.Count);
        Assert.All(copies, c => Assert.Equal(0, c.Source));
        Assert.All(copies, c => Assert.InRange(c.Center.Length, 9.99, 10.01));
        Assert.Contains(copies, c => Point2D.Distance(c.Center, new Point2D(-10, 0)) < 1e-6);
    }

    [Fact]
    public void Fill_PlacesWholeMotifsInsideTheShape_WithoutOverlaps()
    {
        var options = new MotifLayoutOptions { SpacingMm = 2, StoneGapMm = Gap, Rotate = true };
        MotifLayout layout = MotifPlacer.Fill(new[] { Circle(40) }, new[] { Flower(), Arrow() }, options, variant: 4);

        Assert.True(layout.Copies >= 20, $"мотивов {layout.Copies}");
        Assert.Equal(0, layout.Dropped);
        AssertNoOverlaps(layout, Gap);
        Assert.All(layout.Stones, s => Assert.True(s.Center.Length <= 40 - s.Source.DiameterMm / 2 + 0.05, $"камень {s.Center} за краем"));

        // Мотивы чередуются: есть и цветочки, и стрелки.
        Assert.Contains(layout.Stones, s => s.Source.Color == "Розовый");
        Assert.Contains(layout.Stones, s => s.Source.Color == "B");
    }

    [Fact]
    public void Fill_SameVariant_SameLayout_AndCountLimits()
    {
        var options = new MotifLayoutOptions { SpacingMm = 1, StoneGapMm = Gap };
        MotifLayout a = MotifPlacer.Fill(new[] { Circle(30) }, new[] { Flower() }, options, variant: 2);
        MotifLayout b = MotifPlacer.Fill(new[] { Circle(30) }, new[] { Flower() }, options, variant: 2);
        Assert.Equal(a.Stones.Select(s => s.Center), b.Stones.Select(s => s.Center));

        options.Count = 5;
        Assert.Equal(5, MotifPlacer.Fill(new[] { Circle(30) }, new[] { Flower() }, options, variant: 2).Copies);
    }
}
