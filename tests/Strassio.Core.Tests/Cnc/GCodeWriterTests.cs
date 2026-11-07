using System.Globalization;
using Strassio.Core.Cnc;
using Strassio.Core.Geometry;
using Strassio.Core.Settings;

namespace Strassio.Core.Tests.Cnc;

/// <summary>
/// G-code для сверления: каждый файл «проигрывается» самопроверкой — все отверстия на месте и на
/// полной глубине, сверло не ходит по материалу, шпиндель включён, программа заканчивается M30.
/// </summary>
public class GCodeWriterTests
{
    private static List<DrillHole> Design()
    {
        var holes = new List<DrillHole>();
        var rnd = new Random(5);
        for (int i = 0; i < 300; i++)
        {
            holes.Add(new DrillHole(new Point2D(20 + rnd.NextDouble() * 100, 30 + rnd.NextDouble() * 60), 2.4, "ss6", "Кристалл"));
        }

        for (int i = 0; i < 120; i++)
        {
            holes.Add(new DrillHole(new Point2D(20 + rnd.NextDouble() * 100, 30 + rnd.NextDouble() * 60), 2.9, "ss10", "Розовый"));
        }

        for (int i = 0; i < 40; i++)
        {
            holes.Add(new DrillHole(new Point2D(20 + rnd.NextDouble() * 100, 30 + rnd.NextDouble() * 60), 4.0, "ss16", "Кристалл"));
        }

        return holes;
    }

    private static void AssertDrillsEverything(DrillPlan plan, IReadOnlyList<GCodeFile> files, DrillSettings s)
    {
        int total = 0;
        foreach (GCodeFile file in files)
        {
            GCodeRun run = GCodeCheck.Run(file.Text, file.MillLimitMm);
            Assert.True(run.Problems.Count == 0, file.Name + ": " + string.Join("; ", run.Problems));
            Assert.True(run.Ended);
            Assert.Equal(file.Holes, run.Holes.Count);
            Assert.Equal(-s.DepthMm, run.MinZ, 6);

            var expected = file.Jobs.SelectMany(j => j.Points.Select((p, k) => (p, j.Tool, Mill: j.MillRadii[k]))).ToList();
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.Equal(expected[i].p.X, run.Holes[i].At.X, 3);
                Assert.Equal(expected[i].p.Y, run.Holes[i].At.Y, 3);
                Assert.Equal(-s.DepthMm, run.Holes[i].BottomZ, 6);
                if (s.FileMode == DrillFileMode.OneFileToolChange)
                {
                    Assert.Equal(expected[i].Tool, run.Holes[i].Tool);
                }
                Assert.Equal(expected[i].Mill, run.Holes[i].MillMm, 3);
            }

            total += run.Holes.Count;
        }

        Assert.Equal(plan.Holes, total);
    }

    [Fact]
    public void FilePerDrill_SimpleMoves_DrillsAllHoles()
    {
        var s = new DrillSettings();
        DrillPlan plan = DrillPlanner.Plan(Design(), s, DrillRack.Of(2.4, 3.0, 4.0));
        IReadOnlyList<GCodeFile> files = GCodeWriter.Files(plan, s, "Роза");

        Assert.Equal(3, files.Count);
        Assert.Equal("Roza_1_D2.4_ss6.tap", files[0].Name);
        Assert.Equal("Roza_2_D3_ss10.tap", files[1].Name);
        Assert.Equal("Roza_3_D4_ss16.tap", files[2].Name);
        AssertDrillsEverything(plan, files, s);

        string text = files[0].Text;
        Assert.DoesNotContain("G81", text);
        Assert.DoesNotContain("M6", text);
        Assert.Contains("M3 S18000", text);
        Assert.EndsWith("M30\r\n", text);
        Assert.DoesNotContain("\n\n", text.Replace("\r\n", "\n").TrimEnd('\n'));
    }

    [Fact]
    public void NumbersUseDotEvenInRussianCulture()
    {
        CultureInfo old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
            var s = new DrillSettings();
            DrillPlan plan = DrillPlanner.Plan(new[] { new DrillHole(new Point2D(10.25, 7.5), 2.4, "ss6", "") }, s);
            string text = GCodeWriter.Program(plan, plan.Jobs, s);
            Assert.Contains("X1.200 Y1.200", text);
            Assert.Contains("G1 Z-1.500 F500", text);
            Assert.DoesNotContain(",", text.Split('\n').Where(l => !l.StartsWith("(")).Aggregate("", (a, b) => a + b));
        }
        finally
        {
            CultureInfo.CurrentCulture = old;
        }
    }

    [Fact]
    public void Peck_PlungesInSteps_AndReachesFullDepth()
    {
        var s = new DrillSettings { DepthMm = 3, PeckMm = 1 };
        DrillPlan plan = DrillPlanner.Plan(Design().Take(30), s);
        IReadOnlyList<GCodeFile> files = GCodeWriter.Files(plan, s, "peck");
        AssertDrillsEverything(plan, files, s);
        string text = files[0].Text;
        Assert.Contains("G1 Z-1.000", text);
        Assert.Contains("G1 Z-2.000", text);
        Assert.Contains("G1 Z-3.000", text);
        Assert.Contains("G0 Z-0.700", text);
    }

    [Fact]
    public void CannedCycle_UsesG81_AndG83WithPeck()
    {
        var s = new DrillSettings { CannedCycle = true };
        DrillPlan plan = DrillPlanner.Plan(Design(), s);
        IReadOnlyList<GCodeFile> files = GCodeWriter.Files(plan, s, "cycle");
        AssertDrillsEverything(plan, files, s);
        Assert.Contains("G99 G81 X", files[0].Text);
        Assert.Contains("R1.000 F500", files[0].Text);
        Assert.Contains("G80", files[0].Text);

        s.PeckMm = 0.5;
        string peck = GCodeWriter.Files(plan, s, "cycle")[0].Text;
        Assert.Contains("G83 X", peck);
        Assert.Contains("Q0.500", peck);
    }

    [Fact]
    public void OneFileToolChange_HasTM6PerDrill()
    {
        var s = new DrillSettings { FileMode = DrillFileMode.OneFileToolChange };
        DrillPlan plan = DrillPlanner.Plan(Design(), s, DrillRack.Of(4.0, 2.4, 3.0));
        IReadOnlyList<GCodeFile> files = GCodeWriter.Files(plan, s, "rose");

        GCodeFile file = Assert.Single(files);
        Assert.Equal("rose.tap", file.Name);
        AssertDrillsEverything(plan, files, s);
        Assert.Contains("T2 M6", file.Text);
        Assert.Contains("T3 M6", file.Text);
        Assert.Contains("T1 M6", file.Text);
        Assert.True(file.Text.IndexOf("T2 M6") < file.Text.IndexOf("T3 M6"));
        Assert.Equal(new[] { 2, 3, 1 }, GCodeCheck.Run(file.Text).Holes.Select(h => h.Tool).Distinct());
    }

    [Fact]
    public void SplitByColor_FileNamesInLatin()
    {
        var s = new DrillSettings { SplitByColor = true };
        DrillPlan plan = DrillPlanner.Plan(Design(), s, DrillRack.Of(2.4, 3.0, 4.0));
        IReadOnlyList<GCodeFile> files = GCodeWriter.Files(plan, s, "узор");
        Assert.Equal(
            new[] { "uzor_Kristall_1_D2.4_ss6.tap", "uzor_Kristall_2_D4_ss16.tap", "uzor_Rozovyy_3_D3_ss10.tap" },
            files.Select(f => f.Name));
        AssertDrillsEverything(plan, files, s);
        Assert.All(files, f => Assert.True(f.Text.All(ch => ch < 128), f.Name));
    }

    [Fact]
    public void LineNumbersPercentNoComments_NoSpindle()
    {
        var s = new DrillSettings { LineNumbers = true, Percent = true, Comments = false, SpindleRpm = 0 };
        DrillPlan plan = DrillPlanner.Plan(Design().Take(5), s);
        string text = GCodeWriter.Program(plan, plan.Jobs, s);
        string[] lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("%", lines[0]);
        Assert.Equal("%", lines[^1]);
        Assert.StartsWith("N10 G21", lines[1]);
        Assert.DoesNotContain("(", text);
        Assert.DoesNotContain("M3 ", text);
        Assert.DoesNotContain("M5", text);
    }

    [Fact]
    public void Board_UpperLeftHome_NegativeY_AndComment()
    {
        var s = new DrillSettings { Origin = DrillOrigin.Board, Home = BoardCorner.UpperLeft, BoardWidthMm = 600, BoardHeightMm = 400 };
        DrillPlan plan = DrillPlanner.Plan(Design(), s, page: new DrillBoard(Point2D.Zero, 600, 400));
        IReadOnlyList<GCodeFile> files = GCodeWriter.Files(plan, s, "board");
        AssertDrillsEverything(plan, files, s);
        Assert.Contains("(X0 Y0: upper left corner of board 600 x 400 mm. Z0: top of material)", files[0].Text);
        Assert.All(GCodeCheck.Run(files[0].Text).Holes, h => Assert.True(h.At.Y < 0 && h.At.X > 0));
    }

    [Fact]
    public void Check_FindsDangerousPrograms()
    {
        Assert.Contains(GCodeCheck.Run("G21\nG90\nG0 Z5\nM3 S1000\nG0 X1 Y1\nG1 Z-1 F100\nG0 X5 Y5\nG0 Z5\nM5\nM30").Problems, p => p.Contains("X/Y move"));
        Assert.Contains(GCodeCheck.Run("G21\nG0 Z5\nG0 X1 Y1\nG1 Z-1 F100\nG0 Z5\nM30").Problems, p => p.Contains("spindle off"));
        Assert.Contains(GCodeCheck.Run("G21\nG0 Z5\nM3\nG0 X1 Y1\nG0 Z-1\nG0 Z5\nM5\nM30").Problems, p => p.Contains("rapid move into material"));
        Assert.Contains(GCodeCheck.Run("G21\nG0 Z5\nM3\nM5").Problems, p => p.Contains("M30"));
    }

    [Fact]
    public void Ascii_TranslitAndSafeNames()
    {
        Assert.Equal("Rozovyy", Ascii.Translit("Розовый"));
        Assert.Equal("Moy_dizayn_1", Ascii.FileName("Мой дизайн (1)"));
        Assert.Equal("ss6 Zoloto", Ascii.Comment("ss6 (Золото)"));
    }

    [Fact]
    public void Settings_RoundTripAndProblems()
    {
        var config = new DrillConfig();
        DrillSettings s = config.Profile();
        s.DepthMm = 2;
        s.Origin = DrillOrigin.DesignCenter;
        s.FileMode = DrillFileMode.OneFileToolChange;
        s.PathStyle = DrillPathStyle.Snake;
        s.Marks = true;
        s.MarksOffsetMm = 12;
        s.RepeatX = 2;
        string dir = Path.Combine(Path.GetTempPath(), "strassio-drill-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(dir);
            store.SaveDrill(config);
            Assert.Contains("\"snake\"", File.ReadAllText(store.DrillPath));
            DrillSettings back = store.LoadDrill().Profile();
            Assert.Equal(2, back.DepthMm);
            Assert.Equal(DrillOrigin.DesignCenter, back.Origin);
            Assert.Equal(DrillFileMode.OneFileToolChange, back.FileMode);
            Assert.Equal(BoardCorner.LowerLeft, back.Home);
            Assert.Equal(DrillPathStyle.Snake, back.PathStyle);
            Assert.True(back.Marks);
            Assert.Equal(12, back.MarksOffsetMm);
            Assert.Equal(2, back.RepeatX);
            Assert.Equal(1, back.RepeatY);
            Assert.Equal(1.0, back.RetractMm);
            Assert.True(back.MillSmaller);
            Assert.Empty(back.Problems());
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }

        Assert.Contains("drill.problem.depth", new DrillSettings { DepthMm = 0 }.Problems());
        Assert.Contains("drill.problem.clearance", new DrillSettings { ClearanceMm = 0.5 }.Problems());
        Assert.Contains("drill.problem.board", new DrillSettings { BoardWidthMm = -1 }.Problems());
        Assert.Contains("drill.problem.repeat", new DrillSettings { RepeatX = 0 }.Problems());
        Assert.Contains("drill.problem.marks", new DrillSettings { MarksOffsetMm = -1 }.Problems());
        Assert.Contains("drill.problem.millFeed", new DrillSettings { MillFeed = 0 }.Problems());
    }

    [Fact]
    public void Config_ReadsOldSingleProfileFile()
    {
        // drill.json до профилей: один профиль со списком свёрл и своими свёрлами для размеров.
        const string old = "{\"depthMm\":2.5,\"drills\":[2.2,3],\"home\":\"upperRight\",\"origin\":\"board\",\"rules\":[{\"drillMm\":2.2,\"size\":\"ss6\"}]}";
        DrillConfig config = DrillConfig.FromJson(old);
        DrillSettings p = Assert.Single(config.Profiles);
        Assert.Equal(2.5, p.DepthMm);
        Assert.Equal(BoardCorner.UpperRight, p.Home);
        Assert.Equal(new[] { 2.2, 3 }, p.DrillsMm);
        Assert.Equal(DrillPathStyle.Fast, p.PathStyle);
        Assert.Equal(1, p.RepeatX);
        Assert.Equal(GCodeFormats.ArtCam, p.Format);
        Assert.False(string.IsNullOrWhiteSpace(p.Name));
    }

    [Fact]
    public void Config_Profiles_AddRenameRemove()
    {
        var config = new DrillConfig();
        DrillSettings first = config.Profile();
        first.DepthMm = 2;
        DrillSettings second = config.Add(first, "Цех");
        Assert.Equal(2, second.DepthMm);
        Assert.Equal("Цех", config.Active);
        Assert.Equal("Цех 2", config.Add(null, "Цех").Name);
        Assert.False(config.Rename(second, "Цех 2"));
        Assert.True(config.Rename(second, "Станок 2"));
        Assert.Same(second, config.Profile("станок 2"));
        Assert.True(config.Remove(second));
        Assert.True(config.Remove(config.Profiles[1]));
        Assert.False(config.Remove(config.Profiles[0]));

        DrillConfig back = DrillConfig.FromJson(config.ToJson());
        Assert.Single(back.Profiles);
        Assert.Equal(2, back.Profile().DepthMm);
    }

    [Fact]
    public void Formats_ApplyAndDetect()
    {
        var s = new DrillSettings { FileMode = DrillFileMode.OneFileToolChange };
        Assert.Equal(GCodeFormats.ArtCam, GCodeFormats.Detect(s));
        GCodeFormats.Apply(s, GCodeFormats.Grbl);
        Assert.Equal(".nc", s.Extension);
        Assert.Equal(DrillFileMode.FilePerDrill, s.FileMode);
        Assert.Equal(GCodeFormats.Grbl, GCodeFormats.Detect(s));
        s.LineNumbers = true;
        Assert.Equal(GCodeFormats.Custom, GCodeFormats.Detect(s));
        GCodeFormats.Apply(s, GCodeFormats.Plain);
        Assert.False(s.Comments);
        Assert.Equal(GCodeFormats.Plain, GCodeFormats.Detect(s));
    }
}
