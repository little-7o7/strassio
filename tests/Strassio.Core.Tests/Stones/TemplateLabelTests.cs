using Strassio.Core.Geometry;
using Strassio.Core.Stones;

namespace Strassio.Core.Tests.Stones;

/// <summary>Подпись шаблона: габарит по краям камней, количество по размерам и цветам, место под дизайном.</summary>
public class TemplateLabelTests
{
    [Fact]
    public void Label_MeasuresByStoneEdges_AndCountsEverySize()
    {
        var stones = new List<LabelStone>
        {
            new(new Point2D(10, 10), 2.8, "ss10", "Кристалл"),
            new(new Point2D(40, 10), 2.8, "ss10", "Кристалл"),
            new(new Point2D(25, 30), 3.9, "ss16", "Золото"),
            new(new Point2D(25, 20), 2.8, "SS10", "Розовый"),
        };

        TemplateLabel label = TemplateLabel.Of(stones, new[] { "ss6", "ss10", "ss16" })!;

        Assert.Equal(10 - 1.4, label.MinX, 6);
        Assert.Equal(40 + 1.4, label.MaxX, 6);
        Assert.Equal(10 - 1.4, label.MinY, 6);
        Assert.Equal(30 + 1.95, label.MaxY, 6);
        Assert.Equal(32.8, label.Width, 6);
        Assert.Equal(23.35, label.Height, 6);
        Assert.Equal(4, label.Count.Total);
        Assert.Equal(new[] { "ss10", "ss16" }, label.Count.BySize.Select(s => s.Key));
        Assert.Equal(3, label.Count.BySize[0].Value);
        Assert.Equal(2.8, label.Diameters["ss10"]);
        Assert.Equal(3.9, label.Diameters["SS16"]);

        // Подпись — под дизайном по левому краю.
        Point2D anchor = label.Anchor(3);
        Assert.Equal(label.MinX, anchor.X, 6);
        Assert.Equal(label.MinY - 3, anchor.Y, 6);
    }

    [Fact]
    public void NoStones_NoLabel()
    {
        Assert.Null(TemplateLabel.Of(new List<LabelStone>(), new[] { "ss10" }));
    }
}
