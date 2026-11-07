using Strassio.Core.Cnc;
using Strassio.Core.Geometry;

namespace Strassio.Core.Tests.Cnc;

/// <summary>Свёрла по размерам камней, общий ноль, повторы, предупреждения.</summary>
public class DrillPlannerTests
{
    private static DrillHole Hole(double x, double y, double d, string size, string color = "Кристалл") =>
        new DrillHole(new Point2D(x, y), d, size, color);

    [Fact]
    public void ChooseDrill_RuleThenNearestThenHole()
    {
        var s = new DrillSettings { DrillsMm = new List<double> { 2.0, 2.5, 3.0 } };
        s.Rules.Add(new DrillRule { Size = "SS10", DrillMm = 3.2 });

        Assert.Equal(3.2, DrillPlanner.ChooseDrill("ss10", 2.9, s));
        Assert.Equal(2.5, DrillPlanner.ChooseDrill("ss6", 2.4, s));
        // Ровно посередине — большее: камень должен пролезть.
        Assert.Equal(3.0, DrillPlanner.ChooseDrill("ss8", 2.75, s));
        Assert.Equal(2.42, DrillPlanner.ChooseDrill("ss6", 2.4213, new DrillSettings()));
    }

    [Fact]
    public void GroupsByDrill_SmallFirst_ToolNumbersFromRack()
    {
        var s = new DrillSettings { DrillsMm = new List<double> { 3.0, 2.4 } };
        var holes = new[]
        {
            Hole(10, 10, 2.9, "ss10"), Hole(20, 10, 2.4, "ss6"), Hole(30, 10, 2.4, "ss6"), Hole(40, 10, 2.5, "ss8"),
        };

        DrillPlan plan = DrillPlanner.Plan(holes, s);

        Assert.Equal(2, plan.Jobs.Count);
        Assert.Equal(2.4, plan.Jobs[0].DrillMm);
        Assert.Equal(2, plan.Jobs[0].Tool);
        Assert.Equal(new[] { "ss6", "ss8" }, plan.Jobs[0].Sizes);
        Assert.Equal(3, plan.Jobs[0].Points.Count);
        Assert.Equal(3.0, plan.Jobs[1].DrillMm);
        Assert.Equal(1, plan.Jobs[1].Tool);
        Assert.Equal(4, plan.Holes);
    }

    [Fact]
    public void Origin_LowerLeftByHoleEdge_SharedByAllDrills()
    {
        var holes = new[] { Hole(10, 20, 2, "ss6"), Hole(50, 80, 4, "ss16") };
        DrillPlan plan = DrillPlanner.Plan(holes, new DrillSettings());

        Assert.Equal(new Point2D(9, 19), plan.Origin);
        Assert.Equal(0, plan.Min.X, 9);
        Assert.Equal(0, plan.Min.Y, 9);
        Assert.Equal(43, plan.WidthMm, 9);
        Assert.Equal(63, plan.HeightMm, 9);
        Assert.Equal(new Point2D(1, 1), plan.Jobs[0].Points[0]);
        Assert.Equal(new Point2D(41, 61), plan.Jobs[1].Points[0]);
    }

    [Fact]
    public void Origin_CenterAndDocument()
    {
        var holes = new[] { Hole(10, 10, 2, "ss6"), Hole(30, 50, 2, "ss6") };

        DrillPlan center = DrillPlanner.Plan(holes, new DrillSettings { Origin = DrillOrigin.DesignCenter });
        Assert.Equal(new Point2D(20, 30), center.Origin);
        Assert.Contains(new Point2D(-10, -20), center.Jobs[0].Points);

        DrillPlan doc = DrillPlanner.Plan(holes, new DrillSettings { Origin = DrillOrigin.Document });
        Assert.Contains(new Point2D(30, 50), doc.Jobs[0].Points);
    }

    [Theory]
    [InlineData(BoardCorner.LowerLeft, 110, 70)]
    [InlineData(BoardCorner.UpperLeft, 110, -330)]
    [InlineData(BoardCorner.LowerRight, -490, 70)]
    [InlineData(BoardCorner.UpperRight, -490, -330)]
    [InlineData(BoardCorner.Center, -190, -130)]
    public void Board_HomeCorner_LikeArtCamModel(BoardCorner home, double x, double y)
    {
        // Страница 600 × 400 с левым нижним углом в (−10, −20); камень в (100, 50) — на доске в (110, 70) от левого нижнего угла.
        var page = new DrillBoard(new Point2D(-10, -20), 600, 400);
        var s = new DrillSettings { Origin = DrillOrigin.Board, Home = home };
        DrillPlan plan = DrillPlanner.Plan(new[] { Hole(100, 50, 2.4, "ss6") }, s, page: page);

        Assert.Equal(x, plan.Jobs[0].Points[0].X, 6);
        Assert.Equal(y, plan.Jobs[0].Points[0].Y, 6);
        Assert.Equal(600, plan.Board!.Value.WidthMm);
        Assert.Equal(0, plan.OutsideBoard);
    }

    [Fact]
    public void Board_SizeFromSettings_AnchoredAtPageLowerLeft_AndOutsideIsCounted()
    {
        var page = new DrillBoard(new Point2D(0, 0), 600, 400);
        var s = new DrillSettings { Origin = DrillOrigin.Board, Home = BoardCorner.UpperLeft, BoardWidthMm = 300, BoardHeightMm = 200 };
        DrillPlan plan = DrillPlanner.Plan(new[] { Hole(100, 50, 2, "ss6"), Hole(299.5, 50, 2, "ss6"), Hole(50, 250, 2, "ss6") }, s, page: page);

        // Ноль — левый верхний угол доски 300 × 200, то есть (0, 200) на странице.
        Assert.Equal(new Point2D(0, 200), plan.Origin);
        Assert.Contains(new Point2D(100, -150), plan.Jobs[0].Points);
        Assert.Equal(2, plan.OutsideBoard);
        Assert.Equal(51, plan.OverhangMm, 6);
        Assert.Equal(new Point2D(0, -200), plan.Board!.Value.LowerLeft);
    }

    [Fact]
    public void Board_WithoutPageOrSize_IsTheDesignFrame()
    {
        DrillPlan plan = DrillPlanner.Plan(new[] { Hole(10, 20, 2, "ss6"), Hole(50, 80, 4, "ss16") }, new DrillSettings());
        Assert.Equal(DrillOrigin.Board, new DrillSettings().Origin);
        Assert.Equal(new Point2D(9, 19), plan.Origin);
        Assert.Equal(0, plan.OutsideBoard);
    }

    [Fact]
    public void SameHoleTwice_IsDrilledOnce()
    {
        var holes = new[] { Hole(10, 10, 2.4, "ss6"), Hole(10.01, 10, 2.4, "ss6"), Hole(20, 10, 2.4, "ss6") };
        DrillPlan plan = DrillPlanner.Plan(holes, new DrillSettings());
        Assert.Equal(2, plan.Holes);
        Assert.Equal(1, plan.Duplicates);
    }

    [Fact]
    public void Mismatch_WhenDrillFarFromHole()
    {
        var s = new DrillSettings { DrillsMm = new List<double> { 2.0, 4.0 } };
        DrillPlan plan = DrillPlanner.Plan(new[] { Hole(0, 0, 2.9, "ss10"), Hole(5, 0, 2.1, "ss5") }, s);
        DrillMismatch m = Assert.Single(plan.Mismatches);
        Assert.Equal("ss10", m.Size);
        Assert.Equal(2.0, m.DrillMm);
    }

    [Fact]
    public void SplitByColor_SeparateTemplates_SameOrigin()
    {
        var s = new DrillSettings { SplitByColor = true };
        var holes = new[] { Hole(10, 10, 2.4, "ss6", "Розовый"), Hole(30, 10, 2.4, "ss6", "Кристалл"), Hole(50, 10, 2.9, "ss10", "Розовый") };
        DrillPlan plan = DrillPlanner.Plan(holes, s);

        Assert.Equal(3, plan.Jobs.Count);
        Assert.Equal(new[] { "Кристалл", "Розовый" }, plan.Colors);
        Assert.Equal(new Point2D(8.8, 8.55), new Point2D(Math.Round(plan.Origin.X, 6), Math.Round(plan.Origin.Y, 6)));
    }

    [Fact]
    public void Estimate_GrowsWithHolesAndTravel()
    {
        var s = new DrillSettings();
        double a = DrillPlanner.Estimate(100, 1000, s);
        double b = DrillPlanner.Estimate(200, 1000, s);
        double c = DrillPlanner.Estimate(100, 5000, s);
        Assert.True(b > a && c > a);
        Assert.InRange(a, 30, 600);
    }

    [Fact]
    public void Empty_PlanIsEmpty()
    {
        Assert.True(DrillPlanner.Plan(Array.Empty<DrillHole>(), new DrillSettings()).IsEmpty);
    }
}

/// <summary>Ввод чисел в окне «Сверление»: запятая и точка, список свёрл через «;», пробелы или «, ».</summary>
public class DrillTextTests
{
    [Theory]
    [InlineData("2,4", 2.4)]
    [InlineData(" 2.4 ", 2.4)]
    [InlineData("3 мм", 3)]
    [InlineData("1.5mm", 1.5)]
    public void ParsesMm(string text, double expected)
    {
        Assert.True(DrillText.TryParseMm(text, out double v));
        Assert.Equal(expected, v, 9);
    }

    [Theory]
    [InlineData("2,4; 2,8; 3", new[] { 2.4, 2.8, 3 })]
    [InlineData("2.4, 2.8, 3", new[] { 2.4, 2.8, 3 })]
    [InlineData("2,4 2,8 3", new[] { 2.4, 2.8, 3 })]
    [InlineData("2.4;3;", new[] { 2.4, 3 })]
    [InlineData("", new double[0])]
    public void ParsesDrillList(string text, double[] expected)
    {
        Assert.Equal(expected, DrillText.ParseList(text));
    }

    [Fact]
    public void BadListIsNull()
    {
        Assert.Null(DrillText.ParseList("2,4; abc"));
        Assert.Null(DrillText.ParseList("2,4; -1"));
    }

    [Fact]
    public void FormatsInUserCulture()
    {
        Assert.Equal("2,4; 3; 4,25", DrillText.FormatList(new[] { 2.4, 3, 4.25 }, new System.Globalization.CultureInfo("ru-RU")));
    }
}
