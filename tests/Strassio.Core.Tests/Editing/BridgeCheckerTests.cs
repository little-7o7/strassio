using Strassio.Core.Editing;
using Strassio.Core.Geometry;

namespace Strassio.Core.Tests.Editing;

/// <summary>Проверка перемычек (docs/SPEC.md, раздел 11).</summary>
public class BridgeCheckerTests
{
    private static DocStone S(double x, double y, double d = 2.4) => new(new Point2D(x, y), d, 0);

    [Fact]
    public void FindsThinBridges_ThinnestFirst()
    {
        var stones = new[] { S(0, 0), S(2.6, 0), S(10, 0), S(12.5, 0), S(20, 0), S(23, 0) };
        List<ThinBridge> bridges = BridgeChecker.Find(stones, 0.3);

        Assert.Equal(2, bridges.Count);
        Assert.Equal(0.1, bridges[0].GapMm, 6);
        Assert.Equal(0.2, bridges[1].GapMm, 6);
        Assert.Equal((2, 3), (bridges[0].First, bridges[0].Second));
        Assert.Equal(11.25, bridges[0].Middle.X, 6);
    }

    [Fact]
    public void OverlappingHoles_HaveNegativeBridge()
    {
        List<ThinBridge> bridges = BridgeChecker.Find(new[] { S(0, 0), S(2, 0) }, 0.3);
        Assert.Equal(-0.4, Assert.Single(bridges).GapMm, 6);
    }

    [Fact]
    public void GoodLayout_HasNoProblems()
    {
        var stones = new List<DocStone>();
        for (int i = 0; i < 50; i++)
        {
            stones.Add(S(i * 2.8, 0));
        }

        Assert.Empty(BridgeChecker.Find(stones, 0.3));
    }

    [Fact]
    public void DifferentSizes_UseTheirOwnRadii()
    {
        List<ThinBridge> bridges = BridgeChecker.Find(new[] { S(0, 0, 3.9), S(3.3, 0, 2.4) }, 0.3);
        Assert.Equal(0.15, Assert.Single(bridges).GapMm, 6);
    }
}
