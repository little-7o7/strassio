using System.Diagnostics;
using Strassio.Core.Geometry;
using Strassio.Core.Methods;
using Strassio.Core.Placement;

namespace Strassio.Core.Tests.Methods;

/// <summary>Заливки Этапа 4: F6–F12 и автоподбор сетки (docs/SPEC.md, раздел 5).</summary>
public class FillMethodsTests
{
    private const double D = 2.4;

    private static readonly Dictionary<string, double> Sizes = new()
    {
        ["ss3"] = 1.4, ["ss5"] = 2.1, ["ss6"] = 2.4, ["ss10"] = 2.9, ["ss16"] = 3.9,
    };

    private static Curve Rect(double x0, double y0, double w, double h) => Curve.FromPolyline(
        new List<Point2D> { new(x0, y0), new(x0 + w, y0), new(x0 + w, y0 + h), new(x0, y0 + h) }, true);

    private static Curve Circle(double r, double cx = 0, double cy = 0)
    {
        var pts = new List<Point2D>();
        for (int i = 0; i < 96; i++)
        {
            double a = 2 * Math.PI * i / 96;
            pts.Add(new Point2D(cx + r * Math.Cos(a), cy + r * Math.Sin(a)));
        }

        return Curve.FromPolyline(pts, true);
    }

    private static MethodResult Run(MethodKind kind, IReadOnlyList<Curve> shape, MethodParameters p, Curve? guide = null) =>
        MethodRunner.Run(kind, shape, D, p, Sizes, guide == null ? null : new[] { guide });

    private static void AssertClean(IReadOnlyList<PlacedStone> stones, IReadOnlyList<Curve> shape, double margin = 0)
    {
        Assert.NotEmpty(stones);
        List<FlattenedCurve> flats = shape.Select(c => CurveFlattener.Flatten(c)).ToList();
        foreach (PlacedStone s in stones)
        {
            int inside = flats.Count(f => PointInPolygon.IsInside(f, s.Center));
            Assert.True(inside % 2 == 1, $"страза {s.Center} вне формы");
            double edge = flats.Min(f => PointInPolygon.DistanceToBoundary(f, s.Center));
            Assert.True(edge >= s.DiameterMm / 2 + margin - 0.05, $"страза {s.Center} задевает край ({edge:0.00})");
        }

        bool[] overlaps = IntersectionFixer.FindIndicesToRemove(stones, 0.05, 0.01);
        Assert.DoesNotContain(true, overlaps);
    }

    [Theory]
    [InlineData(MethodKind.F6)]
    [InlineData(MethodKind.F9)]
    [InlineData(MethodKind.F10)]
    [InlineData(MethodKind.F11)]
    [InlineData(MethodKind.F12)]
    public void Fill_StaysInside_NoOverlaps_HolesEmpty(MethodKind kind)
    {
        Curve[] ring = { Circle(20), Circle(6) };
        var p = new MethodParameters { FromSize = "ss16", ToSize = "ss5", MixSizes = "ss6, ss10", FillSize = "ss5", EdgeMarginMm = 0.3 };
        IReadOnlyList<PlacedStone> stones = Run(kind, ring, p).Stones;
        AssertClean(stones, ring, 0.3);
        Assert.DoesNotContain(stones, s => Point2D.Distance(s.Center, Point2D.Zero) < 6);
    }

    [Fact]
    public void F6_RowsAreParallelToCenterline_OfLongStrip()
    {
        // Полоса 60×9: ряды вдоль длинной стороны, симметрично середине (y = 4,5).
        Curve[] strip = { Rect(0, 0, 60, 9) };
        IReadOnlyList<PlacedStone> stones = Run(MethodKind.F6, strip, new MethodParameters()).Stones;
        List<double> rows = stones.Where(s => s.Center.X > 10 && s.Center.X < 50).Select(s => Math.Round(s.Center.Y, 1)).Distinct().OrderBy(y => y).ToList();
        Assert.True(rows.Count >= 2);
        Assert.Equal(4.5, (rows.First() + rows.Last()) / 2, 1);
    }

    [Fact]
    public void F7_BlendsBetweenTwoLines()
    {
        var a = Curve.FromPolyline(new List<Point2D> { new(0, 0), new(40, 0) }, false);
        var b = Curve.FromPolyline(new List<Point2D> { new(40, 15), new(0, 15) }, false); // нарисована в другую сторону
        IReadOnlyList<PlacedStone> stones = MethodRunner.Run(MethodKind.F7, new[] { a }, D, new MethodParameters { RowGapMm = 0.6 }, Sizes, new[] { b }).Stones;

        List<double> rows = stones.Select(s => Math.Round(s.Center.Y, 1)).Distinct().OrderBy(y => y).ToList();
        Assert.Equal(0, rows.First(), 1);
        Assert.Equal(15, rows.Last(), 1);
        Assert.True(rows.Count >= 4);
        Assert.DoesNotContain(true, IntersectionFixer.FindIndicesToRemove(stones, 0.05, 0.01));
    }

    [Fact]
    public void F7_Gap0_RowsTouch_NoRowIsDropped()
    {
        // Замечание автора: «зазор между рядами слишком большой» при зазоре 0. Между линиями 16,3 мм:
        // раньше 16,3 ÷ 2,4 округлялось до 7 промежутков, ряды вставали теснее камня, каждый второй
        // ряд выкидывался целиком — оставалось 4 ряда с дырами по 2,26 мм.
        var a = Curve.FromPolyline(new List<Point2D> { new(0, 0), new(40, 0) }, false);
        var b = Curve.FromPolyline(new List<Point2D> { new(0, 16.3), new(40, 16.3) }, false);
        IReadOnlyList<PlacedStone> stones = MethodRunner.Run(MethodKind.F7, new[] { a }, D, new MethodParameters { GapMm = 0, RowGapMm = 0 }, Sizes, new[] { b }).Stones;

        List<double> rows = stones.Select(s => Math.Round(s.Center.Y, 1)).Distinct().OrderBy(y => y).ToList();
        Assert.Equal(7, rows.Count);
        for (int i = 1; i < rows.Count; i++)
        {
            Assert.InRange(rows[i] - rows[i - 1], D - 0.01, D + 0.4);
        }
    }

    [Fact]
    public void F7_ShiftedLines_RowSpacingMeasuredAcross()
    {
        // Вторая линия сдвинута вбок: «ступенька» между парными точками длиннее, чем расстояние
        // поперёк. Раньше ряды считались по ступеньке и налезали друг на друга.
        var a = Curve.FromPolyline(new List<Point2D> { new(0, 0), new(40, 0) }, false);
        var b = Curve.FromPolyline(new List<Point2D> { new(10, 15), new(50, 15) }, false);
        IReadOnlyList<PlacedStone> stones = MethodRunner.Run(MethodKind.F7, new[] { a }, D, new MethodParameters { GapMm = 0, RowGapMm = 0 }, Sizes, new[] { b }).Stones;

        Assert.True(stones.Count >= 120, $"камней {stones.Count}, было 85");
        Assert.DoesNotContain(true, IntersectionFixer.FindIndicesToRemove(stones, 0, 0.01));
    }

    /// <summary>Лист-линза: две дуги, длина <paramref name="length"/>, ширина <paramref name="width"/>.</summary>
    private static Curve Lens(double length, double width)
    {
        var pts = new List<Point2D>();
        for (int i = 0; i < 60; i++)
        {
            double t = Math.PI * i / 59;
            pts.Add(new Point2D(length / 2 - length / 2 * Math.Cos(t), width / 2 * Math.Sin(t)));
        }

        for (int i = 1; i < 59; i++)
        {
            double t = Math.PI * i / 59;
            pts.Add(new Point2D(length / 2 + length / 2 * Math.Cos(t), -width / 2 * Math.Sin(t)));
        }

        return Curve.FromPolyline(pts, true);
    }

    [Fact]
    public void F3_Along_OnLeaf_DenserThanRings_NoOverlaps()
    {
        // Замечание автора: у листа ряды должны идти вдоль формы до середины, без белого завитка,
        // где кольца с двух сторон сходятся под углом.
        Curve[] leaf = { Lens(60, 24) };
        IReadOnlyList<PlacedStone> along = Run(MethodKind.F3, leaf, new MethodParameters { GapMm = 0 }).Stones;
        IReadOnlyList<PlacedStone> rings = Run(MethodKind.F3, leaf, new MethodParameters { GapMm = 0, CenterPattern = MethodChoices.PatternHoneycomb }).Stones;

        Assert.True(along.Count >= rings.Count, $"вдоль формы {along.Count}, кольцами {rings.Count}");
        Assert.DoesNotContain(true, IntersectionFixer.FindIndicesToRemove(along, 0, 0.03));
        var region = new ShapeRegion(leaf, D);
        Assert.All(along, s => Assert.True(region.Fits(s.Center, D / 2 - 0.03, 0)));
    }

    [Fact]
    public void F3_Along_RoundShape_KeepsRings()
    {
        // У круга нет длины — остаются кольца, ровно как при «Середина: соты».
        Curve[] circle = { Circle(15) };
        Assert.Null(AdvancedFillers.Lengthwise(circle, D, 0, 0));
        int along = Run(MethodKind.F3, circle, new MethodParameters { GapMm = 0 }).Stones.Count;
        int rings = Run(MethodKind.F3, circle, new MethodParameters { GapMm = 0, CenterPattern = MethodChoices.PatternHoneycomb }).Stones.Count;
        Assert.Equal(rings, along);
    }

    [Theory]
    [InlineData(MethodKind.F6)]
    [InlineData(MethodKind.F9)]
    [InlineData(MethodKind.F10)]
    public void Fills_LeaveNoNestWhereAWholeStoneFits(MethodKind kind)
    {
        // Добивка ямок: после метода не должно остаться места, где камень касается двух соседей
        // и целиком помещается внутри формы. Раньше «от центра» на круге оставлял 19 таких мест.
        Curve[] shape = { Circle(20) };
        var p = new MethodParameters { GapMm = 0 };
        IReadOnlyList<PlacedStone> stones = Run(kind, shape, p).Stones;

        Assert.Equal(stones.Count, AdvancedFillers.AddMissingStones(stones, shape, D, 0, 0).Count);
        Assert.DoesNotContain(true, IntersectionFixer.FindIndicesToRemove(stones, 0, 0.03));
    }

    [Fact]
    public void F3_Along_LettersAndHolesKeepRings()
    {
        // У букв много прямых углов, у «O» — дырка: кольца там ровнее, и «вдоль формы» не включается.
        Curve bigT = Curve.FromPolyline(new List<Point2D>
        {
            new(0, 0), new(75, 0), new(75, 20), new(47.5, 20), new(47.5, 100), new(27.5, 100), new(27.5, 20), new(0, 20),
        }, true);
        Assert.Null(AdvancedFillers.Lengthwise(new[] { bigT }, D, 0, 0));
        Assert.Null(AdvancedFillers.Lengthwise(new[] { Circle(20), Circle(8) }, D, 0, 0));
        Assert.NotNull(AdvancedFillers.Lengthwise(new[] { Lens(60, 24) }, D, 0, 0));
    }

    [Fact]
    public void CenterPattern_DefaultsToAlong()
    {
        Assert.Equal(MethodChoices.PatternAlong, new MethodParameters().CenterPattern);
    }

    [Fact]
    public void F8_RowsFollowGuide_InsideShape()
    {
        Curve[] shape = { Rect(0, 0, 40, 30) };
        var guide = Curve.FromPolyline(new List<Point2D> { new(0, 5), new(40, 25) }, false); // наклонная
        IReadOnlyList<PlacedStone> stones = Run(MethodKind.F8, shape, new MethodParameters(), guide).Stones;
        AssertClean(stones, shape);

        // Соседи по ряду стоят вдоль направляющей: направление (2; 1).
        PlacedStone s0 = stones.OrderBy(s => Point2D.Distance(s.Center, new Point2D(20, 15))).First();
        PlacedStone next = stones.Where(s => !s.Center.Equals(s0.Center)).OrderBy(s => Point2D.Distance(s.Center, s0.Center)).First();
        Point2D dir = (next.Center - s0.Center).Normalized();
        Assert.True(Math.Abs(Math.Abs(dir.Dot(new Point2D(2, 1).Normalized())) - 1) < 0.1);
    }

    [Fact]
    public void F9_RingsAndSpiral_StartAtCenter()
    {
        Curve[] disk = { Circle(15) };
        foreach (string mode in new[] { MethodChoices.CenterRings, MethodChoices.CenterSpiral })
        {
            IReadOnlyList<PlacedStone> stones = Run(MethodKind.F9, disk, new MethodParameters { CenterMode = mode }).Stones;
            AssertClean(stones, disk);
            Assert.Contains(stones, s => Point2D.Distance(s.Center, Point2D.Zero) < 0.5);
        }
    }

    [Fact]
    public void F10_SameVariant_SameLayout_OtherVariant_Different()
    {
        Curve[] disk = { Circle(15) };
        var a = Run(MethodKind.F10, disk, new MethodParameters { Variant = 1 }).Stones;
        var b = Run(MethodKind.F10, disk, new MethodParameters { Variant = 1 }).Stones;
        var c = Run(MethodKind.F10, disk, new MethodParameters { Variant = 2 }).Stones;
        Assert.Equal(a.Select(s => s.Center), b.Select(s => s.Center));
        Assert.NotEqual(a.Select(s => s.Center), c.Select(s => s.Center));

        // Плотная: не меньше 60% от сот.
        int honeycomb = Run(MethodKind.F2, disk, new MethodParameters()).Stones.Count;
        Assert.True(a.Count >= honeycomb * 0.6, $"{a.Count} против {honeycomb} в сотах");
    }

    [Fact]
    public void F11_BigInCenter_SmallAtEdge()
    {
        Curve[] disk = { Circle(20) };
        IReadOnlyList<PlacedStone> stones = Run(MethodKind.F11, disk, new MethodParameters { FromSize = "ss16", ToSize = "ss5" }).Stones;
        double centerD = stones.Where(s => Point2D.Distance(s.Center, Point2D.Zero) < 5).Average(s => s.DiameterMm);
        double edgeD = stones.Where(s => Point2D.Distance(s.Center, Point2D.Zero) > 16).Average(s => s.DiameterMm);
        Assert.True(centerD > edgeD + 0.8, $"центр {centerD:0.00}, край {edgeD:0.00}");
    }

    [Fact]
    public void F12_AddsSmallStonesToHoneycomb()
    {
        Curve[] disk = { Circle(12) };
        int honeycomb = Run(MethodKind.F2, disk, new MethodParameters()).Stones.Count;
        IReadOnlyList<PlacedStone> filled = Run(MethodKind.F12, disk, new MethodParameters { FillSize = "ss3" }).Stones;
        Assert.True(filled.Count > honeycomb, $"{filled.Count} против {honeycomb}");
        Assert.Contains(filled, s => Math.Abs(s.DiameterMm - 1.4) < 1e-9);
    }

    [Fact]
    public void AutoGrid_NeverWorseThanPlainGrid()
    {
        Curve[] shape = { Circle(9.3) };
        foreach (MethodKind kind in new[] { MethodKind.F1, MethodKind.F2 })
        {
            int plain = Run(kind, shape, new MethodParameters()).Stones.Count;
            int auto = Run(kind, shape, new MethodParameters { AutoGrid = MethodChoices.AutoShift }).Stones.Count;
            int autoAngle = Run(kind, shape, new MethodParameters { AutoGrid = MethodChoices.AutoShiftAngle }).Stones.Count;
            Assert.True(auto >= plain);
            Assert.True(autoAngle >= plain);
        }
    }

    [Fact]
    public void Fills_AreFastEnough_OnBigShape()
    {
        Curve[] big = { Circle(60) };
        var sw = Stopwatch.StartNew();
        foreach (MethodKind kind in new[] { MethodKind.F6, MethodKind.F9, MethodKind.F10, MethodKind.F11, MethodKind.F12 })
        {
            Run(kind, big, new MethodParameters { FromSize = "ss16", ToSize = "ss5", FillSize = "ss5" });
        }

        Assert.True(sw.Elapsed.TotalSeconds < 15, $"{sw.Elapsed.TotalSeconds:0.0} с");
    }
}
