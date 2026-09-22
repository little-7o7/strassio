using Strassio.Core.Geometry;
using Strassio.Core.Methods;
using Strassio.Core.Placement;

namespace Strassio.Core.Tests.Methods;

/// <summary>
/// Все методы × обычные фигуры × зазор 0 и 0,2 мм. Появился после скриншотов автора: при зазоре 0 на
/// прямоугольнике «Кант» ставил 8 камней, «Контурная» рассыпалась — в тестах зазор всегда был 0,2.
/// Проверяем три вещи: камни не налезают друг на друга, заливка плотная (не хуже 75% от сот), ряд
/// по линии идёт без дыр.
/// </summary>
public class AllMethodsSweepTests
{
    private const double D = 2.4;

    private static readonly Dictionary<string, double> Sizes = new()
    {
        ["ss3"] = 1.4, ["ss5"] = 2.1, ["ss6"] = 2.4, ["ss10"] = 2.9, ["ss16"] = 3.9,
    };

    public static IEnumerable<object[]> Cases()
    {
        foreach (string shape in new[] { "rect", "star", "heart", "circle" })
        {
            foreach (double gap in new[] { 0.0, 0.2 })
            {
                yield return new object[] { shape, gap };
            }
        }
    }

    private static Curve Shape(string name)
    {
        switch (name)
        {
            case "rect":
                return Curve.FromPolyline(new List<Point2D> { new(0, 0), new(60, 0), new(60, 45), new(0, 45) }, true);
            case "circle":
                return Polygon(96, a => new Point2D(20 * Math.Cos(a), 20 * Math.Sin(a)));
            case "heart":
                return Polygon(200, t => new Point2D(
                    16 * Math.Pow(Math.Sin(t), 3) * 1.8,
                    -(13 * Math.Cos(t) - 5 * Math.Cos(2 * t) - 2 * Math.Cos(3 * t) - Math.Cos(4 * t)) * 1.8));
            default:
                var pts = new List<Point2D>();
                for (int i = 0; i < 10; i++)
                {
                    double a = Math.PI / 2 + i * Math.PI / 5;
                    double r = i % 2 == 0 ? 30 : 12;
                    pts.Add(new Point2D(r * Math.Cos(a), r * Math.Sin(a)));
                }

                return Curve.FromPolyline(pts, true);
        }
    }

    private static Curve Polygon(int n, Func<double, Point2D> at)
    {
        var pts = new List<Point2D>();
        for (int i = 0; i < n; i++)
        {
            pts.Add(at(2 * Math.PI * i / n));
        }

        return Curve.FromPolyline(pts, true);
    }

    private static MethodParameters Params(double gap) => new()
    {
        GapMm = gap, RowGapMm = gap, FromSize = "ss16", ToSize = "ss5", SizePattern = "ss6, ss6, ss10",
        AccentSize = "ss16", FillSize = "ss3", MixSizes = "ss6, ss10",
    };

    /// <summary>Самое глубокое налезание двух камней друг на друга, мм (0 — не налезают).</summary>
    private static double WorstPenetration(IReadOnlyList<PlacedStone> stones)
    {
        double worst = 0;
        for (int i = 0; i < stones.Count; i++)
        {
            for (int j = i + 1; j < stones.Count; j++)
            {
                double dist = Point2D.Distance(stones[i].Center, stones[j].Center);
                double need = (stones[i].DiameterMm + stones[j].DiameterMm) / 2;
                if (dist < need)
                {
                    worst = Math.Max(worst, need - dist);
                }
            }
        }

        return worst;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void NoMethod_OverlapsStones(string shape, double gap)
    {
        Curve curve = Shape(shape);
        foreach (MethodInfo info in MethodCatalog.All.Where(m => !m.NeedsGuide))
        {
            MethodResult result = MethodRunner.Run(info.Kind, new[] { curve }, D, Params(gap), Sizes);
            Assert.True(result.Stones.Count > 0, $"{info.Key}: нет камней");
            double worst = WorstPenetration(result.Stones);
            Assert.True(worst <= 0.03, $"{info.Key} на {shape}, зазор {gap}: камни налезают на {worst:0.000} мм");
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Fills_AreDense(string shape, double gap)
    {
        Curve curve = Shape(shape);
        int honeycomb = MethodRunner.Run(MethodKind.F2, new[] { curve }, D, Params(gap), Sizes).Stones.Count;
        foreach (MethodKind kind in new[] { MethodKind.F1, MethodKind.F3, MethodKind.F4, MethodKind.F6, MethodKind.F9 })
        {
            // «По центральной линии» — для вытянутых форм (стебель, буква); у звезды узкие лучи, ряды от
            // середины туда не доходят целиком — это свойство метода, а не ошибка.
            if (kind == MethodKind.F6 && shape == "star")
            {
                continue;
            }

            int count = MethodRunner.Run(kind, new[] { curve }, D, Params(gap), Sizes).Stones.Count;
            Assert.True(count >= honeycomb * 0.75, $"{kind} на {shape}, зазор {gap}: {count} камней против {honeycomb} в сотах");
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Border_HasFullRows(string shape, double gap)
    {
        Curve curve = Shape(shape);
        double perimeter = CurveFlattener.Flatten(curve).TotalLength;
        int oneRow = (int)(perimeter / (D + gap));
        int count = MethodRunner.Run(MethodKind.F5, new[] { curve }, D, new MethodParameters { GapMm = gap, Rings = 2 }, Sizes).Stones.Count;
        // Внутренний ряд короче внешнего; у звезды — намного (острые лучи).
        double rows = shape == "star" ? 1.05 : 1.5;
        Assert.True(count >= oneRow * rows, $"кант на {shape}, зазор {gap}: {count} камней, один ряд — примерно {oneRow}");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void AlongLine_HasNoHoles(string shape, double gap)
    {
        Curve curve = Shape(shape);
        IReadOnlyList<PlacedStone> stones = MethodRunner.Run(MethodKind.L1, new[] { curve }, D, Params(gap), Sizes).Stones;
        FlattenedCurve flat = CurveFlattener.Flatten(curve);
        int expected = (int)(flat.TotalLength / (D + gap));
        // У острых углов (клюв сердца, лучи звезды) стороны сходятся, и налезающие стразы честно убираются.
        Assert.True(stones.Count >= expected * 0.8, $"по линии {shape}, зазор {gap}: {stones.Count} из ~{expected}");

        // Каждая страза рядом с соседом: дыр больше полутора камней нет.
        var ordered = stones.Select(s => (s, t: Strassio.Core.Placement.VariableLineScatterer.ProjectFraction(flat, s.Center)))
            .OrderBy(x => x.t).Select(x => x.s).ToList();
        for (int i = 1; i < ordered.Count; i++)
        {
            double step = Point2D.Distance(ordered[i - 1].Center, ordered[i].Center);
            // У выемки сердца угловая страза стоит чуть в стороне от сторон — так без наложений.
            Assert.True(step < (D + gap) * 2.5, $"по линии {shape}, зазор {gap}: дыра {step:0.00} мм");
        }
    }
}
