using Strassio.Core.Cnc;
using Strassio.Core.Geometry;
using Strassio.Core.Stones;

namespace Strassio.Core.Tests.Cnc;

/// <summary>
/// Каталог страз и свёрл и то, что из него следует при сверлении: сверло у размера, свои номера,
/// подачи и обороты, дофрезеровка, метки совмещения, повтор на доске, перемычки, порядок обхода.
/// </summary>
public class DrillCatalogTests
{
    private static DrillHole Hole(double x, double y, double d, string size, string color = "Кристалл") =>
        new DrillHole(new Point2D(x, y), d, size, color);

    private static StoneTable Catalog()
    {
        var table = new StoneTable();
        table.Sets.Add(new StoneSet
        {
            Name = "Основной",
            Sizes =
            {
                new StoneSize { Name = "ss6", DiameterMm = 2.4, DrillMm = 2.2, Colors = { new StoneColor { Name = "Кристалл", Rgb = "#E8EEF5" } } },
                new StoneSize { Name = "ss10", DiameterMm = 2.9, Colors = { new StoneColor { Name = "Кристалл", Rgb = "#E8EEF5" } } },
            },
        });
        table.Drills.Add(new DrillTool { DiameterMm = 3.0, Tool = 5, Feed = 300, Rpm = 12000, Name = "фреза 3" });
        table.Drills.Add(new DrillTool { DiameterMm = 2.2 });
        return table;
    }

    private static List<DrillHole> Grid(int nx, int ny, double step, double d, string size, double x0 = 0, double y0 = 0, string color = "Кристалл")
    {
        var holes = new List<DrillHole>();
        for (int j = 0; j < ny; j++)
        {
            for (int i = 0; i < nx; i++)
            {
                holes.Add(Hole(x0 + i * step, y0 + j * step, d, size, color));
            }
        }

        return holes;
    }

    private static void AssertSafe(GCodeFile file)
    {
        GCodeRun run = GCodeCheck.Run(file.Text, file.MillLimitMm);
        Assert.True(run.Problems.Count == 0, file.Name + ": " + string.Join("; ", run.Problems));
        Assert.Equal(file.Holes, run.Holes.Count);
    }

    [Fact]
    public void Catalog_SizeDrill_ToolNumbers_FeedAndRpm()
    {
        DrillRack rack = DrillRack.Of(Catalog(), "основной");
        Assert.Equal(2.2, rack.DrillForSize("SS6"));
        Assert.Null(rack.DrillForSize("ss10"));
        Assert.Equal(5, rack.ToolNumber(3.0));
        Assert.Equal(2, rack.ToolNumber(2.2));

        var s = new DrillSettings();
        DrillPlan plan = DrillPlanner.Plan(new[] { Hole(0, 0, 2.4, "ss6"), Hole(5, 0, 2.9, "ss10") }, s, rack);
        Assert.Equal(new[] { 2.2, 3.0 }, plan.Jobs.Select(j => j.DrillMm));
        Assert.Equal(new[] { 2, 5 }, plan.Jobs.Select(j => j.Tool));
        Assert.Equal(new[] { 500.0, 300.0 }, plan.Jobs.Select(j => j.Feed));
        Assert.Equal(new[] { 18000, 12000 }, plan.Jobs.Select(j => j.Rpm));
        Assert.Equal("фреза 3", plan.Jobs[1].Name);

        IReadOnlyList<GCodeFile> files = GCodeWriter.Files(plan, s, "cat");
        Assert.Contains("M3 S12000", files[1].Text);
        Assert.Contains(" F300", files[1].Text);
        Assert.Contains("(Drill 3 mm freza 3, T5, F300 S12000", files[1].Text);

        // Шпиндель руками — своих оборотов в файле нет.
        DrillPlan manual = DrillPlanner.Plan(new[] { Hole(5, 0, 2.9, "ss10") }, new DrillSettings { SpindleRpm = 0 }, rack);
        Assert.Equal(0, manual.Jobs[0].Rpm);
        Assert.DoesNotContain("M3 ", GCodeWriter.Files(manual, new DrillSettings { SpindleRpm = 0 }, "m")[0].Text);
    }

    [Fact]
    public void OneFile_ChangesRpmPerDrill()
    {
        var s = new DrillSettings { FileMode = DrillFileMode.OneFileToolChange };
        DrillPlan plan = DrillPlanner.Plan(new[] { Hole(0, 0, 2.4, "ss6"), Hole(5, 0, 2.9, "ss10") }, s, DrillRack.Of(Catalog(), null));
        GCodeFile file = Assert.Single(GCodeWriter.Files(plan, s, "one"));
        AssertSafe(file);
        Assert.True(file.Text.IndexOf("T2 M6") < file.Text.IndexOf("M3 S18000"));
        Assert.True(file.Text.IndexOf("T5 M6") < file.Text.IndexOf("M3 S12000"));
    }

    [Fact]
    public void Milling_SmallerDrill_CirclesUpToHole()
    {
        var s = new DrillSettings();
        DrillPlan plan = DrillPlanner.Plan(Grid(5, 4, 3.0, 2.4, "ss6"), s, DrillRack.Of(Catalog(), null));
        DrillJob job = Assert.Single(plan.Jobs);
        Assert.Equal(2.2, job.DrillMm);
        Assert.Equal(20, job.Milled);
        Assert.All(job.MillRadii, r => Assert.Equal(0.1, r, 6));
        Assert.Empty(plan.Mismatches);

        GCodeFile file = Assert.Single(GCodeWriter.Files(plan, s, "mill"));
        Assert.Equal(0.1, file.MillLimitMm, 6);
        GCodeRun run = GCodeCheck.Run(file.Text, file.MillLimitMm);
        Assert.Empty(run.Problems);
        Assert.Equal(20, run.Holes.Count);
        Assert.All(run.Holes, h => Assert.Equal(0.1, h.MillMm, 3));
        Assert.Contains("20 milled to D2.4", file.Text);
        Assert.Contains("F300", file.Text);

        // Без разрешения на боковые ходы проверка такой файл не пропускает.
        Assert.NotEmpty(GCodeCheck.Run(file.Text).Problems);

        // Фрезеровка выключена — отверстия по сверлу, без кругов.
        DrillPlan plain = DrillPlanner.Plan(Grid(5, 4, 3.0, 2.4, "ss6"), new DrillSettings { MillSmaller = false, MaxMismatchMm = 0.1 }, DrillRack.Of(Catalog(), null));
        Assert.Equal(0, plain.Milled);
        Assert.Single(plain.Mismatches);
    }

    [Fact]
    public void Milling_BigHole_SeveralRings_WithCannedCycleAndPeck()
    {
        var s = new DrillSettings { CannedCycle = true };
        var holes = new List<DrillHole> { Hole(0, 0, 6.4, "ss30"), Hole(10, 0, 2.2, "ss5"), Hole(20, 0, 6.4, "ss30"), Hole(30, 0, 2.2, "ss5") };
        DrillPlan plan = DrillPlanner.Plan(holes, s, DrillRack.Of(2.2));
        DrillJob job = Assert.Single(plan.Jobs);
        Assert.Equal(2, job.Milled);
        Assert.Equal(3, DrillPlanner.MillRings(2.1, 2.2).Count);

        GCodeFile file = Assert.Single(GCodeWriter.Files(plan, s, "big"));
        AssertSafe(file);
        Assert.Contains("G81", file.Text);
        Assert.Contains("G80", file.Text);
        Assert.All(GCodeCheck.Run(file.Text, file.MillLimitMm).Holes.Where(h => h.MillMm > 0), h => Assert.Equal(2.1, h.MillMm, 3));

        s.CannedCycle = false;
        s.PeckMm = 0.5;
        AssertSafe(GCodeWriter.Files(DrillPlanner.Plan(holes, s, DrillRack.Of(2.2)), s, "peck")[0]);
    }

    [Fact]
    public void Marks_FourPerCopy_OutsideByOffset_InFirstFileOfEachTemplate()
    {
        var s = new DrillSettings { Origin = DrillOrigin.Document, Marks = true, MarksOffsetMm = 10, SplitByColor = true };
        var holes = Grid(3, 3, 5, 2.4, "ss6", 0, 0, "Розовый");
        holes.Add(Hole(5, 5, 2.9, "ss10", "Розовый"));
        holes.AddRange(Grid(2, 2, 10, 2.4, "ss6", 0, 0, "Кристалл"));
        DrillPlan plan = DrillPlanner.Plan(holes, s, DrillRack.Of(2.4, 2.9));

        // Рамка рисунка по краю отверстий: −1,2 … 11,2; метки на 10 мм снаружи.
        var expected = new[] { new Point2D(-11.2, -11.2), new Point2D(21.2, -11.2), new Point2D(21.2, 21.2), new Point2D(-11.2, 21.2) };
        Assert.Equal(8, plan.MarkHoles);
        foreach (string color in new[] { "Кристалл", "Розовый" })
        {
            List<DrillJob> jobs = plan.Jobs.Where(j => j.Color == color).ToList();
            Assert.Equal(4, jobs[0].Marks.Count);
            Assert.All(jobs.Skip(1), j => Assert.Empty(j.Marks));
            foreach (Point2D e in expected)
            {
                Assert.Contains(jobs[0].Marks, m => Point2D.Distance(m, e) < 1e-6);
            }
        }

        Assert.All(GCodeWriter.Files(plan, s, "marks"), AssertSafe);
    }

    [Fact]
    public void Repeat_CopiesRightAndDown_WithGapBetweenMarks()
    {
        var s = new DrillSettings { Origin = DrillOrigin.Document, RepeatX = 3, RepeatY = 2, RepeatGapMm = 5, Marks = true, MarksOffsetMm = 10 };
        List<DrillHole> design = Grid(4, 4, 3, 2.4, "ss6");
        DrillPlan plan = DrillPlanner.Plan(design, s);

        Assert.Equal(3, plan.CopiesX);
        Assert.Equal(2, plan.CopiesY);
        Assert.Equal(16 * 6 + 4 * 6, plan.Holes);
        // Рисунок 11,4 мм + метки по 10 мм и радиус метки с двух сторон + промежуток 5.
        Assert.Equal(11.4 + 2 * (10 + 1.2) + 5, plan.Pitch.X, 6);
        Assert.Contains(plan.Jobs[0].Points, p => Point2D.Distance(p, new Point2D(2 * plan.Pitch.X, -plan.Pitch.Y)) < 1e-6);
        Assert.Equal(11.4 + 2 * plan.Pitch.X, plan.WidthMm, 6);
        Assert.Equal(11.4 + plan.Pitch.Y, plan.HeightMm, 6);
        Assert.Equal(0, plan.Bridges.Count);
        AssertSafe(GCodeWriter.Files(plan, s, "rep")[0]);
    }

    [Fact]
    public void FitCopies_FillsTheBoardFromTheDesign()
    {
        // Рисунок 30 × 30 см в 2 см от левого верхнего угла листа 95 × 70 см, промежуток 1 см.
        var page = new DrillBoard(new Point2D(0, 0), 950, 700);
        var s = new DrillSettings { RepeatGapMm = 10 };
        var design = new List<DrillHole> { Hole(21.2, 678.8, 2.4, "ss6"), Hole(318.8, 381.2, 2.4, "ss6") };
        Assert.Equal((3, 2), DrillPlanner.FitCopies(design, s, null, page));

        // С метками (1 см снаружи) копия шире на 2 × 11,2 мм — по X влезает только две.
        s.Marks = true;
        Assert.Equal((2, 2), DrillPlanner.FitCopies(design, s, null, page));
    }

    [Fact]
    public void Bridges_ByRealHoles_PerTemplate()
    {
        var s = new DrillSettings { MinBridgeMm = 0.3 };
        // Камни через 2,6 мм: при отверстиях 2,4 перемычка 0,2 — тонко; сверлом 2,2 без фрезеровки — 0,4.
        List<DrillHole> holes = Grid(3, 1, 2.6, 2.4, "ss6");
        DrillPlan milled = DrillPlanner.Plan(holes, s, DrillRack.Of(2.2));
        Assert.Equal(2, milled.Bridges.Count);
        Assert.Equal(0.2, milled.Bridges.ThinnestMm, 6);
        Assert.Equal(new Point2D(1.3, 0) - milled.Origin, milled.Bridges.Spots[0] - Point2D.Zero);

        DrillPlan drilled = DrillPlanner.Plan(holes, new DrillSettings { MinBridgeMm = 0.3, MillSmaller = false }, DrillRack.Of(2.2));
        Assert.Equal(0, drilled.Bridges.Count);

        // Разные цвета — разные трафареты: перемычек между ними нет.
        var split = new DrillSettings { MinBridgeMm = 0.3, SplitByColor = true };
        DrillPlan colors = DrillPlanner.Plan(new[] { Hole(0, 0, 2.4, "ss6", "A"), Hole(2.6, 0, 2.4, "ss6", "B") }, split);
        Assert.Equal(0, colors.Bridges.Count);
    }

    [Theory]
    [InlineData(DrillPathStyle.Fast)]
    [InlineData(DrillPathStyle.Rows)]
    [InlineData(DrillPathStyle.Snake)]
    [InlineData(DrillPathStyle.Columns)]
    [InlineData(DrillPathStyle.Groups)]
    [InlineData(DrillPathStyle.Spread)]
    public void PathStyles_VisitEveryHoleOnce_AndFileIsSafe(DrillPathStyle style)
    {
        var holes = Grid(12, 9, 2.8, 2.4, "ss6");
        holes.AddRange(Grid(5, 5, 2.8, 2.4, "ss6", 80, 40));
        var s = new DrillSettings { PathStyle = style };
        DrillPlan plan = DrillPlanner.Plan(holes, s);
        DrillJob job = Assert.Single(plan.Jobs);
        Assert.Equal(holes.Count, job.Points.Count);
        Assert.Equal(holes.Count, job.Points.Distinct().Count());
        AssertSafe(GCodeWriter.Files(plan, s, "path")[0]);
    }

    [Fact]
    public void PathStyles_RowsSnakeColumnsGroupsSpread_LookRight()
    {
        List<Point2D> points = Grid(4, 3, 3, 2.4, "ss6").Select(h => h.Center).ToList();
        Point2D[] Run(DrillPathStyle style) => PathOrder.Arrange(points, Point2D.Zero, style, 2.4).Select(i => points[i]).ToArray();

        Point2D[] rows = Run(DrillPathStyle.Rows);
        Assert.Equal(new[] { 0.0, 3, 6, 9, 0, 3, 6, 9, 0, 3, 6, 9 }, rows.Select(p => p.X));
        Assert.Equal(new[] { 0.0, 0, 0, 0, 3, 3, 3, 3, 6, 6, 6, 6 }, rows.Select(p => p.Y));

        Point2D[] snake = Run(DrillPathStyle.Snake);
        Assert.Equal(new[] { 0.0, 3, 6, 9, 9, 6, 3, 0, 0, 3, 6, 9 }, snake.Select(p => p.X));

        Point2D[] columns = Run(DrillPathStyle.Columns);
        Assert.Equal(new[] { 0.0, 0, 0, 3, 3, 3 }, columns.Take(6).Select(p => p.X));
        Assert.Equal(new[] { 0.0, 3, 6, 0, 3, 6 }, columns.Take(6).Select(p => p.Y));

        // Две кучки: сначала целиком ближняя, потом дальняя.
        var two = Grid(3, 3, 3, 2.4, "ss6").Select(h => h.Center).Concat(Grid(3, 3, 3, 2.4, "ss6", 60, 0).Select(h => h.Center)).ToList();
        Point2D[] groups = PathOrder.Arrange(two, Point2D.Zero, DrillPathStyle.Groups, 2.4).Select(i => two[i]).ToArray();
        Assert.All(groups.Take(9), p => Assert.True(p.X < 10));
        Assert.All(groups.Skip(9), p => Assert.True(p.X > 50));

        // Вразброс: соседние по очереди отверстия не стоят рядом.
        List<Point2D> line = Enumerable.Range(0, 30).Select(i => new Point2D(i * 3.0, 0)).ToList();
        Point2D[] spread = PathOrder.Arrange(line, Point2D.Zero, DrillPathStyle.Spread, 2.4).Select(i => line[i]).ToArray();
        for (int i = 1; i < spread.Length; i++)
        {
            Assert.True(Point2D.Distance(spread[i - 1], spread[i]) > 3.5, $"{spread[i - 1]} → {spread[i]}");
        }
    }

    [Fact]
    public void StoneTable_OldFileWithoutDrills_AndRoundTrip()
    {
        StoneTable old = StoneTable.FromJson("{\"sets\":[{\"name\":\"A\",\"sizes\":[{\"name\":\"ss6\",\"diameterMm\":2.4,\"colors\":[{\"name\":\"K\",\"rgb\":\"#FFFFFF\"}]}]}]}");
        Assert.NotNull(old.Drills);
        Assert.Empty(old.Drills);
        Assert.Equal(0, old.Sets[0].Sizes[0].DrillMm);

        StoneTable back = StoneTable.FromJson(Catalog().ToJson());
        Assert.Equal(2, back.Drills.Count);
        Assert.Equal(5, back.Drills[0].Tool);
        Assert.Equal("фреза 3", back.Drills[0].Name);
        Assert.Equal(2.2, back.Sets[0].Sizes[0].DrillMm);
        Assert.Empty(StoneTableRules.Validate(back));

        StoneSet copy = StoneTableRules.AddSet(back, back.Sets[0], "Копия");
        Assert.Equal(2.2, copy.Sizes[0].DrillMm);
    }

    [Fact]
    public void Catalog_Validation()
    {
        var drills = new List<DrillTool>
        {
            new DrillTool { DiameterMm = 2.2 },
            new DrillTool { DiameterMm = 2.2 },
            new DrillTool { DiameterMm = 3, Tool = 1 },
            new DrillTool { DiameterMm = 0 },
            new DrillTool { DiameterMm = 4, Feed = -1, Rpm = 200000 },
        };
        List<string> keys = DrillCatalog.Validate(drills).Select(p => p.Key).ToList();
        Assert.Contains("catalog.error.duplicate", keys);
        Assert.Contains("catalog.error.toolDuplicate", keys);
        Assert.Contains("catalog.error.diameter", keys);
        Assert.Contains("catalog.error.feed", keys);
        Assert.Contains("catalog.error.rpm", keys);
        Assert.Empty(DrillCatalog.Validate(Catalog().Drills));
    }

    [Fact]
    public void Catalog_SizesOfDrill_AndLegacyImport()
    {
        StoneTable table = Catalog();
        StoneSet set = table.Sets[0];
        Assert.Equal(new[] { "ss6" }, DrillCatalog.SizesOf(table.Drills[1], set, table.Drills, true, 0.2));
        Assert.Equal(new[] { "ss10" }, DrillCatalog.SizesOf(table.Drills[0], set, table.Drills, true, 0.2));

        var empty = new StoneTable();
        empty.Sets.Add(new StoneSet { Name = "A", Sizes = { new StoneSize { Name = "ss10", DiameterMm = 2.9 } } });
        var legacy = new DrillSettings { DrillsMm = new List<double> { 2.4, 3 } };
        legacy.Rules.Add(new DrillRule { Size = "SS10", DrillMm = 3.2 });
        Assert.True(DrillCatalog.ImportLegacy(empty, legacy, null));
        Assert.Equal(new[] { 2.4, 3, 3.2 }, empty.Drills.Select(d => d.DiameterMm));
        Assert.Equal(3.2, empty.Sets[0].Sizes[0].DrillMm);
        Assert.Empty(legacy.DrillsMm);
        Assert.False(DrillCatalog.ImportLegacy(empty, new DrillSettings { DrillsMm = new List<double> { 5 } }, null));
    }
}
