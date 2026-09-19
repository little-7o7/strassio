using System.IO;
using Strassio.Core.Stones;

namespace Strassio.Core.Tests.Stones;

public class StoneTableTests
{
    private static StoneTable SampleTable() => new StoneTable
    {
        Sets = new List<StoneSet>
        {
            new StoneSet
            {
                Name = "Основной",
                Sizes = new List<StoneSize>
                {
                    new StoneSize
                    {
                        Name = "ss6",
                        DiameterMm = 2.4,
                        Colors = new List<StoneColor>
                        {
                            new StoneColor { Name = "Красный", Rgb = "#E53935" },
                            new StoneColor { Name = "Зелёный", Rgb = "#43A047" },
                        },
                    },
                },
            },
        },
    };

    [Fact]
    public void ToJson_MatchesShapeFromSpec()
    {
        string json = SampleTable().ToJson();

        Assert.Contains("\"sets\"", json);
        Assert.Contains("\"name\":\"Основной\"", json);
        Assert.Contains("\"sizes\"", json);
        Assert.Contains("\"diameterMm\":2.4", json);
        Assert.Contains("\"colors\"", json);
        Assert.Contains("\"rgb\":\"#E53935\"", json);
    }

    [Fact]
    public void RoundTrip_JsonPreservesAllData()
    {
        StoneTable original = SampleTable();

        StoneTable restored = StoneTable.FromJson(original.ToJson());

        Assert.Single(restored.Sets);
        Assert.Equal("Основной", restored.Sets[0].Name);
        Assert.Single(restored.Sets[0].Sizes);
        StoneSize size = restored.Sets[0].Sizes[0];
        Assert.Equal("ss6", size.Name);
        Assert.Equal(2.4, size.DiameterMm, 6);
        Assert.Equal(2, size.Colors.Count);
        Assert.Equal("Красный", size.Colors[0].Name);
        Assert.Equal("#E53935", size.Colors[0].Rgb);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsThroughDisk()
    {
        string path = Path.Combine(Path.GetTempPath(), "strassio-stones-test-" + Path.GetRandomFileName() + ".json");
        try
        {
            SampleTable().Save(path);

            StoneTable restored = StoneTable.Load(path);

            Assert.Single(restored.Sets);
            Assert.Equal("ss6", restored.Sets[0].Sizes[0].Name);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Theory]
    [InlineData("#E53935", 229, 57, 53)]
    [InlineData("43a047", 67, 160, 71)]
    public void TryGetRgb_ParsesHex(string rgb, int r, int g, int b)
    {
        Assert.True(new StoneColor { Rgb = rgb }.TryGetRgb(out byte red, out byte green, out byte blue));
        Assert.Equal((r, g, b), (red, green, blue));
    }

    [Theory]
    [InlineData("")]
    [InlineData("#E539")]
    [InlineData("#GGGGGG")]
    public void TryGetRgb_RejectsBroken(string rgb)
    {
        Assert.False(new StoneColor { Rgb = rgb }.TryGetRgb(out _, out _, out _));
    }
}
