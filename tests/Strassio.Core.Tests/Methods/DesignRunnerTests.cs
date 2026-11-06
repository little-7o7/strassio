using Strassio.Core.Editing;
using Strassio.Core.Geometry;
using Strassio.Core.Methods;
using Strassio.Core.Placement;

namespace Strassio.Core.Tests.Methods;

/// <summary>«Весь дизайн за раз» и «учитывать готовые стразы»: фигуры не налезают друг на друга и на уже лежащие.</summary>
public class DesignRunnerTests
{
    private const double D = 2.4;

    private static Curve Square(double x, double y, double size) => Curve.FromPolyline(
        new[] { new Point2D(x, y), new Point2D(x + size, y), new Point2D(x + size, y + size), new Point2D(x, y + size) },
        isClosed: true);

    private static Curve Circle(double cx, double cy, double r) => Curve.FromPolyline(
        Enumerable.Range(0, 96).Select(i => new Point2D(cx + r * Math.Cos(i * Math.PI / 48), cy + r * Math.Sin(i * Math.PI / 48))).ToList(),
        isClosed: true);

    /// <summary>Наименьший зазор край в край между любыми двумя стразами (перебором — страз немного).</summary>
    private static double MinGap(IReadOnlyList<PlacedStone> stones)
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

    /// <summary>
    /// Наименьший зазор между стразами РАЗНЫХ фигур. Внутри одной фигуры зазор — дело самого метода
    /// (подгонка по длине может чуть сжать его), здесь проверяется только встреча фигур.
    /// </summary>
    private static double MinCrossGap(DesignResult result)
    {
        double min = double.MaxValue;
        for (int a = 0; a < result.PerShape.Count; a++)
        {
            for (int b = a + 1; b < result.PerShape.Count; b++)
            {
                foreach (PlacedStone x in result.PerShape[a].Stones)
                {
                    foreach (PlacedStone y in result.PerShape[b].Stones)
                    {
                        min = Math.Min(min, Point2D.Distance(x.Center, y.Center) - (x.DiameterMm + y.DiameterMm) / 2);
                    }
                }
            }
        }

        return min;
    }

    // ---- StoneAvoider ----------------------------------------------------------------------

    [Fact]
    public void Avoid_RemovesOnlyStonesThatHitExisting()
    {
        var existing = new[] { new PlacedStone(new Point2D(0, 0), D, false) };
        var fresh = new[]
        {
            new PlacedStone(new Point2D(1, 0), D, false),   // прямо на лежащей
            new PlacedStone(new Point2D(10, 0), D, false),  // далеко
            new PlacedStone(new Point2D(2.5, 0), D, false), // касается с зазором 0,1
        };

        AvoidResult result = StoneAvoider.Avoid(fresh, existing, minGapMm: 0.1);

        Assert.Equal(1, result.Removed);
        Assert.Equal(new[] { 1, 2 }, result.KeptIndices);
        Assert.Equal(new[] { 0 }, StoneAvoider.Conflicts(fresh, existing, 0.1));
    }

    [Fact]
    public void Avoid_ShiftsSlightlyOverlappingStone_InsteadOfRemoving()
    {
        var existing = new[] { new PlacedStone(new Point2D(0, 0), D, false) };
        var fresh = new[] { new PlacedStone(new Point2D(2.2, 0), D, false) }; // налезает на 0,3 мм (с зазором 0,1)

        AvoidResult result = StoneAvoider.Avoid(fresh, existing, minGapMm: 0.1, maxShiftMm: 0.7);

        Assert.Equal(0, result.Removed);
        Assert.Equal(1, result.Shifted);
        PlacedStone moved = result.Stones[0];
        Assert.True(Point2D.Distance(moved.Center, existing[0].Center) >= D + 0.1 - 1e-6);
        Assert.True(Point2D.Distance(moved.Center, fresh[0].Center) <= 0.7);
    }

    [Fact]
    public void Avoid_NoShiftOutsideAllowedArea()
    {
        var existing = new[] { new PlacedStone(new Point2D(0, 0), D, false) };
        var fresh = new[] { new PlacedStone(new Point2D(2.2, 0), D, false) };

        // Отодвинуть можно только вправо, а правее x = 2,3 — уже чужая территория.
        AvoidResult result = StoneAvoider.Avoid(fresh, existing, 0.1, 0.7, p => p.X <= 2.3);

        Assert.Equal(1, result.Removed);
        Assert.Empty(result.Stones);
    }

    [Fact]
    public void Avoid_ShiftedStoneDoesNotHitOtherNewStones()
    {
        var existing = new[] { new PlacedStone(new Point2D(0, 0), D, false) };

        // Справа вплотную стоит другая новая страза — отодвинуться некуда, страза убирается.
        var fresh = new[]
        {
            new PlacedStone(new Point2D(2.2, 0), D, false),
            new PlacedStone(new Point2D(4.7, 0), D, false),
        };

        AvoidResult result = StoneAvoider.Avoid(fresh, existing, 0.1, 0.7);

        Assert.True(MinGap(existing.Concat(result.Stones).ToList()) >= 0.1 - 1e-6);
    }

    // ---- DesignRunner ----------------------------------------------------------------------

    [Theory]
    [InlineData(MethodKind.F2)]
    [InlineData(MethodKind.F3)]
    [InlineData(MethodKind.F1)]
    public void TopShape_CutsOutOfBottomShape_NoOverlaps(MethodKind kind)
    {
        Curve centre = Circle(20, 20, 7); // сверху
        Curve petals = Square(0, 0, 40);  // снизу, под кругом
        var p = new MethodParameters { GapMm = 0.2 };

        DesignResult result = DesignRunner.Run(kind, new[] { new[] { centre }, new[] { petals } }, D, p);

        Assert.NotEmpty(result.PerShape[0].Stones);
        Assert.NotEmpty(result.PerShape[1].Stones);

        // Камни нижней фигуры не заходят под верхнюю.
        var top = new ColorRegion(new[] { centre }, 0, 0, 0, 0);
        Assert.DoesNotContain(result.PerShape[1].Stones, s => top.Contains(s.Center));

        Assert.True(MinCrossGap(result) >= 0.2 - 1e-6, $"зазор {MinCrossGap(result):0.###}");
    }

    [Fact]
    public void BottomShape_RowsWrapAroundTopShape_NotRaggedHole()
    {
        Curve centre = Circle(20, 20, 7);
        Curve petals = Square(0, 0, 40);
        var p = new MethodParameters { GapMm = 0.2 };

        DesignResult result = DesignRunner.Run(MethodKind.F2, new[] { new[] { centre }, new[] { petals } }, D, p);

        // Вокруг верхнего круга нижняя заливка подходит вплотную — дыра не шире одного камня.
        IReadOnlyList<PlacedStone> bottom = result.PerShape[1].Stones;
        double nearest = bottom.Min(s => Point2D.Distance(s.Center, new Point2D(20, 20)) - 7);
        Assert.True(nearest < D + 0.2 + D / 2, $"ближайший камень в {nearest:0.##} мм от края круга");
    }

    [Fact]
    public void ExistingStones_AreNeverHit()
    {
        Curve shape = Square(0, 0, 30);
        var kantParams = new MethodParameters { GapMm = 0.2, Rings = 2 };
        IReadOnlyList<PlacedStone> kant = MethodRunner.Run(MethodKind.F5, new[] { shape }, D, kantParams).Stones;

        var p = new MethodParameters { GapMm = 0.2 };
        DesignResult result = DesignRunner.Run(MethodKind.F2, new[] { new[] { shape } }, D, p, existing: kant);

        Assert.NotEmpty(result.PerShape[0].Stones);
        Assert.True(result.Removed > 0);
        Assert.Empty(StoneAvoider.Conflicts(result.PerShape[0].Stones, kant, 0.2 - 1e-6));
    }

    [Fact]
    public void Show_MarksInsteadOfRemoving()
    {
        Curve shape = Square(0, 0, 30);
        IReadOnlyList<PlacedStone> kant = MethodRunner.Run(MethodKind.F5, new[] { shape }, D, new MethodParameters { GapMm = 0.2 }).Stones;
        var p = new MethodParameters { GapMm = 0.2, Intersections = MethodChoices.IntersectShow };

        DesignResult shown = DesignRunner.Run(MethodKind.F2, new[] { new[] { shape } }, D, p, existing: kant);

        Assert.Equal(0, shown.Removed);
        Assert.NotEmpty(shown.PerShape[0].ConflictIndices);
    }

    [Fact]
    public void LineMethod_TwoCrossingLines_NoOverlapAtCrossing()
    {
        Curve a = Curve.FromPolyline(new[] { new Point2D(0, 0), new Point2D(40, 40) });
        Curve b = Curve.FromPolyline(new[] { new Point2D(0, 40), new Point2D(40, 0) });
        var p = new MethodParameters { GapMm = 0.2 };

        DesignResult result = DesignRunner.Run(MethodKind.L1, new[] { new[] { a }, new[] { b } }, D, p);

        Assert.True(result.PerShape[0].Stones.Count > 10);
        Assert.True(result.PerShape[1].Stones.Count > 10);
        Assert.True(MinCrossGap(result) >= 0.2 - 1e-6, $"зазор {MinCrossGap(result):0.###}");
    }

    [Fact]
    public void FillMethod_OpenLine_IsSkipped()
    {
        Curve open = Curve.FromPolyline(new[] { new Point2D(0, 0), new Point2D(40, 0), new Point2D(40, 20) });
        DesignResult result = DesignRunner.Run(MethodKind.F2, new[] { new[] { open }, new[] { Square(0, 30, 20) } }, D, new MethodParameters());

        Assert.Equal(1, result.SkippedShapes);
        Assert.Empty(result.PerShape[0].Stones);
        Assert.NotEmpty(result.PerShape[1].Stones);
    }

    [Fact]
    public void Grow_PushesOuterOut_AndHoleIn()
    {
        Curve outer = Square(0, 0, 20);
        Curve hole = Square(5, 5, 10);
        var region = new ColorRegion(new[] { outer, hole }, 0, 0, 0, 0);

        IReadOnlyList<Curve> grown = DesignRunner.Grow(new[] { outer, hole }, region, 1);
        var bigger = new ColorRegion(grown, 0, 0, 0, 0);

        Assert.True(bigger.Contains(new Point2D(-0.5, 10)));  // снаружи стало «внутри»
        Assert.True(bigger.Contains(new Point2D(5.5, 10)));   // край дырки тоже
        Assert.False(bigger.Contains(new Point2D(10, 10)));   // середина дырки — пусто
        Assert.False(bigger.Contains(new Point2D(-1.5, 10)));
    }
}

