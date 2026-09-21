using System.IO;
using System.Linq;
using Strassio.Core.Geometry;
using Strassio.Core.Methods;
using Strassio.Core.Placement;
using Strassio.Core.Settings;

namespace Strassio.Core.Tests.Methods;

/// <summary>Методы с параметрами из докера (docs/SPEC.md, разделы 2.2, 4 и 5).</summary>
public class MethodRunnerTests : IDisposable
{
    private const double D = 2.4;

    private readonly string dir = Path.Combine(Path.GetTempPath(), "strassio-method-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static Curve Square(double size, bool counterClockwise = true)
    {
        var pts = new List<Point2D> { new(0, 0), new(size, 0), new(size, size), new(0, size) };
        if (!counterClockwise)
        {
            pts.Reverse();
        }

        return Curve.FromPolyline(pts, true);
    }

    private static Curve Line(double length) =>
        Curve.FromPolyline(new List<Point2D> { new(0, 0), new(length, 0) }, false);

    private static MethodResult Run(MethodKind kind, Curve curve, MethodParameters p) =>
        MethodRunner.Run(kind, new[] { curve }, D, p);

    private static double MinPairGap(IReadOnlyList<PlacedStone> stones)
    {
        double min = double.MaxValue;
        for (int i = 0; i < stones.Count; i++)
        {
            for (int j = i + 1; j < stones.Count; j++)
            {
                double gap = Point2D.Distance(stones[i].Center, stones[j].Center) - (stones[i].DiameterMm + stones[j].DiameterMm) / 2;
                min = Math.Min(min, gap);
            }
        }

        return min;
    }

    [Fact]
    public void EveryMethod_RunsWithDefaults_OnSquare()
    {
        foreach (MethodInfo info in MethodCatalog.All)
        {
            MethodResult result = Run(info.Kind, Square(40), new MethodParameters());
            Assert.True(result.Stones.Count > 0, info.Key);
            Assert.True(MinPairGap(result.Stones) > -0.05, info.Key + ": стразы накладываются");
        }
    }

    [Fact]
    public void L1_BiggerGap_FewerStones()
    {
        int small = Run(MethodKind.L1, Line(100), new MethodParameters { GapMm = 0.2 }).Stones.Count;
        int big = Run(MethodKind.L1, Line(100), new MethodParameters { GapMm = 2 }).Stones.Count;
        Assert.True(big < small);
    }

    [Fact]
    public void L1_ExactCount_GivesThatMany()
    {
        var p = new MethodParameters { StepMode = MethodChoices.StepCount, ExactCount = 7 };
        Assert.Equal(7, Run(MethodKind.L1, Line(100), p).Stones.Count);
    }

    [Fact]
    public void L1_ExactStep_KeepsStep()
    {
        var p = new MethodParameters { StepMode = MethodChoices.StepExact, ExactStepMm = 5 };
        IReadOnlyList<PlacedStone> stones = Run(MethodKind.L1, Line(100), p).Stones;
        Assert.InRange(stones[1].Center.X - stones[0].Center.X, 4.99, 5.01);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void L2_RowCount_MakesThatManyRows(int rows)
    {
        var p = new MethodParameters { RowCount = rows, RowGapMm = 0.5 };
        MethodResult result = Run(MethodKind.L2, Line(100), p);
        int distinctRows = result.Stones.Select(s => Math.Round(s.Center.Y, 1)).Distinct().Count();
        Assert.Equal(rows, distinctRows);
        Assert.True(MinPairGap(result.Stones) > -0.05);
    }

    [Fact]
    public void L2_BothSides_IsSymmetricAroundLine()
    {
        var p = new MethodParameters { RowCount = 3, RowGapMm = 0.6 };
        double[] ys = Run(MethodKind.L2, Line(100), p).Stones
            .Select(s => Math.Round(s.Center.Y, 2)).Distinct().OrderBy(y => y).ToArray();
        Assert.Equal(3, ys.Length);
        Assert.InRange(ys[0], -(D + 0.6) - 0.02, -(D + 0.6) + 0.02);
        Assert.InRange(ys[1], -0.02, 0.02);
        Assert.InRange(ys[2], D + 0.6 - 0.02, D + 0.6 + 0.02);
    }

    [Fact]
    public void L2_Stagger_ShiftsEverySecondRow()
    {
        var plain = new MethodParameters { RowCount = 2, RowSide = MethodChoices.SideOutside };
        var staggered = new MethodParameters { RowCount = 2, RowSide = MethodChoices.SideOutside, Stagger = true };

        double FirstX(MethodResult r, bool onLine) => r.Stones
            .Where(s => (Math.Abs(s.Center.Y) < 0.01) == onLine)
            .Min(s => s.Center.X);

        MethodResult a = Run(MethodKind.L2, Line(100), plain);
        MethodResult b = Run(MethodKind.L2, Line(100), staggered);
        Assert.Equal(FirstX(a, true), FirstX(b, true), 3);
        Assert.NotEqual(FirstX(a, false), FirstX(b, false), 3);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void L2_Outside_RowsAreOutsideSquare_WhateverDirection(bool counterClockwise)
    {
        var p = new MethodParameters { RowCount = 3, RowSide = MethodChoices.SideOutside };
        IReadOnlyList<PlacedStone> stones = Run(MethodKind.L2, Square(40, counterClockwise), p).Stones;
        Assert.All(stones, s => Assert.False(
            s.Center.X > 0.5 && s.Center.X < 39.5 && s.Center.Y > 0.5 && s.Center.Y < 39.5,
            "страза внутри квадрата"));
        Assert.Contains(stones, s => s.Center.X < -2 * D || s.Center.Y < -2 * D);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void L3_Inside_RowIsInsideSquare_WhateverDirection(bool counterClockwise)
    {
        var p = new MethodParameters { OffsetMm = 5, OffsetSide = MethodChoices.SideInside };
        IReadOnlyList<PlacedStone> stones = Run(MethodKind.L3, Square(40, counterClockwise), p).Stones;
        Assert.NotEmpty(stones);
        Assert.All(stones, s =>
        {
            Assert.InRange(s.Center.X, 4.9, 35.1);
            Assert.InRange(s.Center.Y, 4.9, 35.1);
        });
    }

    [Fact]
    public void L3_Outside_RowIsOutsideSquare()
    {
        var p = new MethodParameters { OffsetMm = 5, OffsetSide = MethodChoices.SideOutside };
        IReadOnlyList<PlacedStone> stones = Run(MethodKind.L3, Square(40), p).Stones;
        Assert.NotEmpty(stones);
        Assert.All(stones, s => Assert.InRange(DistanceOutsideSquare(s.Center, 40), 4.9, 5.1));
    }

    [Fact]
    public void L2_ShowOnly_KeepsAllStonesAndReportsConflicts()
    {
        // Наложения рядов появляются у острого угла: узкая «стрелка».
        var arrow = Curve.FromPolyline(new List<Point2D> { new(0, 0), new(50, 3), new(0, 6) }, false);
        var show = new MethodParameters { RowCount = 3, Intersections = MethodChoices.IntersectShow, Corners = MethodChoices.CornersSharp };
        var remove = new MethodParameters { RowCount = 3, Intersections = MethodChoices.IntersectRemove, Corners = MethodChoices.CornersSharp };

        MethodResult shown = Run(MethodKind.L2, arrow, show);
        MethodResult removed = Run(MethodKind.L2, arrow, remove);

        Assert.NotEmpty(shown.ConflictIndices);
        Assert.True(shown.Stones.Count > removed.Stones.Count);
        Assert.Empty(removed.ConflictIndices);
    }

    [Fact]
    public void F1_Angle_ChangesLayout()
    {
        MethodResult straight = Run(MethodKind.F1, Square(30), new MethodParameters { AngleDeg = 0 });
        MethodResult turned = Run(MethodKind.F1, Square(30), new MethodParameters { AngleDeg = 45 });
        Assert.NotEqual(
            straight.Stones.Min(s => s.Center.X + s.Center.Y),
            turned.Stones.Min(s => s.Center.X + s.Center.Y),
            3);
    }

    [Fact]
    public void Fill_EdgeMargin_KeepsStonesAwayFromEdge()
    {
        IReadOnlyList<PlacedStone> stones = Run(MethodKind.F2, Square(30), new MethodParameters { EdgeMarginMm = 3 }).Stones;
        Assert.NotEmpty(stones);
        Assert.All(stones, s =>
        {
            Assert.InRange(s.Center.X, 3 + D / 2 - 0.01, 30 - 3 - D / 2 + 0.01);
            Assert.InRange(s.Center.Y, 3 + D / 2 - 0.01, 30 - 3 - D / 2 + 0.01);
        });
    }

    [Fact]
    public void F5_EdgeOnly_HasFewerStonesThanF4()
    {
        int edge = Run(MethodKind.F5, Square(40), new MethodParameters { Rings = 2 }).Stones.Count;
        int combined = Run(MethodKind.F4, Square(40), new MethodParameters { Rings = 2 }).Stones.Count;
        int oneRing = Run(MethodKind.F5, Square(40), new MethodParameters { Rings = 1 }).Stones.Count;
        Assert.True(edge < combined);
        Assert.True(oneRing < edge);
    }

    [Fact]
    public void Fields_RejectOutOfRange_AndFractionalCounts()
    {
        var p = new MethodParameters();
        Assert.False(MethodCatalog.Gap.TrySetNumber(p, -1));
        Assert.False(MethodCatalog.RowCount.TrySetNumber(p, 2.5));
        Assert.False(MethodCatalog.RowCount.TrySetNumber(p, 0));
        Assert.True(MethodCatalog.RowCount.TrySetNumber(p, 5));
        Assert.Equal(5, p.RowCount);
        Assert.False(MethodCatalog.Step.TrySetChoice(p, "nonsense"));
        Assert.Equal(MethodChoices.StepFit, p.StepMode);
    }

    [Fact]
    public void ExactStepField_VisibleOnlyInExactStepMode()
    {
        var p = new MethodParameters();
        Assert.False(MethodCatalog.ExactStep.IsVisible(p));
        p.StepMode = MethodChoices.StepExact;
        Assert.True(MethodCatalog.ExactStep.IsVisible(p));
        Assert.False(MethodCatalog.ExactCount.IsVisible(p));
    }

    [Fact]
    public void EveryMethodAndField_HasTextsInBothLanguages()
    {
        string langDir = Path.Combine(FindRepoRoot(), "lang");
        foreach (string file in new[] { "ru.json", "en.json" })
        {
            string json = File.ReadAllText(Path.Combine(langDir, file));
            foreach (MethodInfo info in MethodCatalog.All)
            {
                Assert.Contains("\"" + info.Key + "\"", json);
                Assert.Contains("\"" + info.Key + ".tooltip\"", json);
                foreach (MethodField field in info.Fields)
                {
                    Assert.Contains("\"" + field.LabelKey + "\"", json);
                    Assert.Contains("\"" + field.TooltipKey + "\"", json);
                    foreach (string choice in field.Choices)
                    {
                        Assert.Contains("\"" + field.ChoiceKey(choice) + "\"", json);
                    }
                }
            }
        }
    }

    [Fact]
    public void Parameters_SavedAndLoaded_WithSettings()
    {
        var store = new SettingsStore(dir);
        var s = new PluginSettings { LastMethod = "l2" };
        s.Method.GapMm = 0.5;
        s.Method.RowCount = 5;
        s.Method.RowSide = MethodChoices.SideInside;
        s.Method.Stagger = true;
        s.Method.StepMode = MethodChoices.StepCount;
        store.SaveSettings(s);

        PluginSettings loaded = store.LoadSettings();
        Assert.Equal("l2", loaded.LastMethod);
        Assert.Equal(0.5, loaded.Method.GapMm);
        Assert.Equal(5, loaded.Method.RowCount);
        Assert.Equal(MethodChoices.SideInside, loaded.Method.RowSide);
        Assert.True(loaded.Method.Stagger);
        Assert.Equal(MethodChoices.StepCount, loaded.Method.StepMode);
        Assert.Equal(new MethodParameters().OffsetMm, loaded.Method.OffsetMm);
    }

    [Fact]
    public void OldSettingsFile_WithPartOfMethod_GetsDefaultsForTheRest()
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "settings.json"), "{\"language\":\"en\",\"method\":{\"gapMm\":0.4}}");

        PluginSettings loaded = new SettingsStore(dir).LoadSettings();
        Assert.Equal(0.4, loaded.Method.GapMm);
        Assert.Equal(3, loaded.Method.RowCount);
        Assert.Equal(MethodChoices.StepFit, loaded.Method.StepMode);
    }

    [Fact]
    public void OldSettingsFile_WithoutMethod_GetsDefaults()
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "settings.json"), "{\"language\":\"en\"}");

        PluginSettings loaded = new SettingsStore(dir).LoadSettings();
        Assert.NotNull(loaded.Method);
        Assert.Equal(0.2, loaded.Method.GapMm);
    }

    /// <summary>Расстояние от точки снаружи квадрата [0, size]² до него.</summary>
    private static double DistanceOutsideSquare(Point2D p, double size)
    {
        double dx = Math.Max(0, Math.Max(-p.X, p.X - size));
        double dy = Math.Max(0, Math.Max(-p.Y, p.Y - size));
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static string FindRepoRoot()
    {
        string? current = AppContext.BaseDirectory;
        while (current != null && !File.Exists(Path.Combine(current, "Strassio.sln")))
        {
            current = Path.GetDirectoryName(current);
        }

        return current ?? throw new InvalidOperationException("Не найден Strassio.sln");
    }
}
