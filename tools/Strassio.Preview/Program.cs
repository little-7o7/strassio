using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Strassio.Core.Geometry;
using Strassio.Core.Placement;
using Strassio.Preview;

string scenario = args.Length > 0 ? args[0] : "line";

if (scenario.StartsWith("offset-", StringComparison.Ordinal))
{
    RenderOffsetScenario(scenario);
    return;
}

if (scenario.StartsWith("ring-", StringComparison.Ordinal))
{
    RenderRingScenario(scenario);
    return;
}

if (scenario.StartsWith("fill-", StringComparison.Ordinal))
{
    RenderFillScenario(scenario);
    return;
}

(Curve Curve, LineScatterOptions Options) built = scenario switch
{
    "line" => BuildLine(),
    "zigzag" => BuildZigzag(),
    "square" => BuildSquare(),
    "star" => BuildStar(),
    "spike" => BuildSpike(),
    _ => throw new ArgumentException($"Неизвестный сценарий '{scenario}'. Доступные: line, zigzag, square, star, spike, offset-square, offset-star."),
};

FlattenedCurve flat = CurveFlattener.Flatten(built.Curve, built.Options.FlattenToleranceMm);
IReadOnlyList<PlacedStone> stones = LineScatterer.Scatter(built.Curve, built.Options);

string outDir = Path.Combine(FindRepoRoot(), "out", "preview");
Directory.CreateDirectory(outDir);
string outPath = Path.Combine(outDir, scenario + ".svg");

string svg = SvgWriter.Render(flat.Points.Select(p => p.Position).ToList(), flat.IsClosed, stones);
File.WriteAllText(outPath, svg);

Console.WriteLine($"Сценарий '{scenario}': {stones.Count} страз, из них {stones.Count(s => s.IsCorner)} в углах.");
Console.WriteLine($"SVG сохранён: {outPath}");

if (Environment.GetEnvironmentVariable("STRASSIO_DUMP") == "1")
{
    for (int i = 0; i < stones.Count; i++)
    {
        Console.WriteLine($"{i}: ({stones[i].Center.X:0.###}, {stones[i].Center.Y:0.###}) corner={stones[i].IsCorner}");
    }
}

static (Curve, LineScatterOptions) BuildLine()
{
    var curve = Curve.FromPolyline(new[] { new Point2D(0, 0), new Point2D(40, 0) });
    var options = new LineScatterOptions { StoneDiameterMm = 2.4, GapMm = 0.2, Mode = StepMode.FitEven };
    return (curve, options);
}

static (Curve, LineScatterOptions) BuildZigzag()
{
    // Зигзаг с острыми углами (около 20-25°) — обязательный сценарий из docs/SPEC.md, раздел 6.5.
    var points = new[]
    {
        new Point2D(0, 0),
        new Point2D(20, 30),
        new Point2D(0, 60),
        new Point2D(20, 90),
        new Point2D(0, 120),
    };
    var curve = Curve.FromPolyline(points);
    var options = new LineScatterOptions
    {
        StoneDiameterMm = 2.4,
        GapMm = 0.2,
        Mode = StepMode.FitEven,
        CornerAngleThresholdDeg = 20,
    };
    return (curve, options);
}

static (Curve, LineScatterOptions) BuildSquare()
{
    var points = new[]
    {
        new Point2D(0, 0),
        new Point2D(40, 0),
        new Point2D(40, 40),
        new Point2D(0, 40),
    };
    var curve = Curve.FromPolyline(points, isClosed: true);
    var options = new LineScatterOptions
    {
        StoneDiameterMm = 2.4,
        GapMm = 0.2,
        Mode = StepMode.FitEven,
        CornerAngleThresholdDeg = 20,
    };
    return (curve, options);
}

static (Curve, LineScatterOptions) BuildStar()
{
    var options = new LineScatterOptions
    {
        StoneDiameterMm = 2.4,
        GapMm = 0.2,
        Mode = StepMode.FitEven,
        CornerAngleThresholdDeg = 20,
    };
    return (BuildStarCurve(), options);
}

static void RenderOffsetScenario(string scenario)
{
    Curve curve = scenario switch
    {
        "offset-square" => Curve.FromPolyline(
            new[] { new Point2D(0, 0), new Point2D(40, 0), new Point2D(40, 40), new Point2D(0, 40) },
            isClosed: true),
        "offset-star" => BuildStarCurve(),
        _ => throw new ArgumentException($"Неизвестный сценарий '{scenario}'."),
    };

    FlattenedCurve flat = CurveFlattener.Flatten(curve);
    const double distance = 6;
    List<Point2D> a = CurveOffsetter.Offset(flat, distance);
    List<Point2D> b = CurveOffsetter.Offset(flat, -distance);

    var polylines = new (IReadOnlyList<Point2D> Points, string Color)[]
    {
        (flat.Points.Select(p => p.Position).ToList(), "#cccccc"),
        (a, "#1E88E5"),
        (b, "#E53935"),
    };

    string outDir = Path.Combine(FindRepoRoot(), "out", "preview");
    Directory.CreateDirectory(outDir);
    string outPath = Path.Combine(outDir, scenario + ".svg");

    string svg = SvgWriter.RenderMulti(polylines, Array.Empty<PlacedStone>());
    File.WriteAllText(outPath, svg);

    Console.WriteLine($"Сценарий '{scenario}': исходная кривая (серая), смещение +{distance} мм (синее), смещение -{distance} мм (красное).");
    Console.WriteLine($"SVG сохранён: {outPath}");
}

static void RenderRingScenario(string scenario)
{
    Curve curve = scenario switch
    {
        "ring-star" => BuildStarCurve(),
        "ring-square" => Curve.FromPolyline(
            new[] { new Point2D(0, 0), new Point2D(40, 0), new Point2D(40, 40), new Point2D(0, 40) },
            isClosed: true),
        _ => throw new ArgumentException($"Неизвестный сценарий '{scenario}'."),
    };

    // L2 «вокруг линии»: крупный центральный ряд + два ряда по краям поменьше — docs/SPEC.md, раздел 4.
    var rows = new[]
    {
        new RowSpec
        {
            OffsetMm = -3.4,
            ScatterOptions = new LineScatterOptions
            {
                StoneDiameterMm = 2.4, GapMm = 0.2, Mode = StepMode.FitEven, CornerAngleThresholdDeg = 20,
            },
        },
        new RowSpec
        {
            OffsetMm = 0,
            ScatterOptions = new LineScatterOptions
            {
                StoneDiameterMm = 3.2, GapMm = 0.2, Mode = StepMode.FitEven, CornerAngleThresholdDeg = 20,
            },
        },
        new RowSpec
        {
            OffsetMm = 3.4,
            ScatterOptions = new LineScatterOptions
            {
                StoneDiameterMm = 2.4, GapMm = 0.2, Mode = StepMode.FitEven, CornerAngleThresholdDeg = 20,
            },
        },
    };

    IReadOnlyList<PlacedStone> stones = RingScatterer.Scatter(curve, rows);
    List<PlacedStone> fixedStones = IntersectionFixer.RemoveOverlaps(stones);

    FlattenedCurve flat = CurveFlattener.Flatten(curve);
    string outDir = Path.Combine(FindRepoRoot(), "out", "preview");
    Directory.CreateDirectory(outDir);
    string outPath = Path.Combine(outDir, scenario + ".svg");
    string outPathFixed = Path.Combine(outDir, scenario + "-fixed.svg");

    string svg = SvgWriter.Render(flat.Points.Select(p => p.Position).ToList(), flat.IsClosed, stones);
    File.WriteAllText(outPath, svg);
    string svgFixed = SvgWriter.Render(flat.Points.Select(p => p.Position).ToList(), flat.IsClosed, fixedStones);
    File.WriteAllText(outPathFixed, svgFixed);

    Console.WriteLine($"Сценарий '{scenario}': {stones.Count} страз в {rows.Length} рядах, после исправления пересечений — {fixedStones.Count} (убрано {stones.Count - fixedStones.Count}).");
    Console.WriteLine($"SVG сохранён: {outPathFixed}");
    Console.WriteLine($"SVG сохранён: {outPath}");
}

static void RenderFillScenario(string scenario)
{
    var options = new GridFillOptions { StoneDiameterMm = 2.4, GapMm = 0.2, AngleDeg = 0 };
    Curve[] boundaries;

    switch (scenario)
    {
        case "fill-square":
            options.Pattern = GridPattern.Square;
            boundaries = new[]
            {
                Curve.FromPolyline(
                    new[] { new Point2D(0, 0), new Point2D(40, 0), new Point2D(40, 40), new Point2D(0, 40) },
                    isClosed: true),
            };
            break;
        case "fill-honeycomb":
            options.Pattern = GridPattern.Honeycomb;
            boundaries = new[]
            {
                Curve.FromPolyline(
                    new[] { new Point2D(0, 0), new Point2D(40, 0), new Point2D(40, 40), new Point2D(0, 40) },
                    isClosed: true),
            };
            break;
        case "fill-star":
            options.Pattern = GridPattern.Honeycomb;
            options.AngleDeg = 12;
            boundaries = new[] { BuildStarCurve() };
            break;
        case "fill-letter-o":
            options.Pattern = GridPattern.Honeycomb;
            boundaries = new[]
            {
                Curve.FromPolyline(
                    new[] { new Point2D(0, 0), new Point2D(40, 0), new Point2D(40, 40), new Point2D(0, 40) },
                    isClosed: true),
                Curve.FromPolyline(
                    new[] { new Point2D(12, 12), new Point2D(28, 12), new Point2D(28, 28), new Point2D(12, 28) },
                    isClosed: true),
            };
            break;
        default:
            throw new ArgumentException($"Неизвестный сценарий '{scenario}'.");
    }

    List<PlacedStone> stones = GridFiller.Fill(boundaries, options);

    var polylines = boundaries
        .Select(b => (Points: (IReadOnlyList<Point2D>)CurveFlattener.Flatten(b).Points.Select(p => p.Position).ToList(), Color: "#cccccc"))
        .ToArray();

    string outDir = Path.Combine(FindRepoRoot(), "out", "preview");
    Directory.CreateDirectory(outDir);
    string outPath = Path.Combine(outDir, scenario + ".svg");
    File.WriteAllText(outPath, SvgWriter.RenderMulti(polylines, stones));

    Console.WriteLine($"Сценарий '{scenario}': {stones.Count} страз ({options.Pattern}, угол {options.AngleDeg}°).");
    Console.WriteLine($"SVG сохранён: {outPath}");
}

static Curve BuildStarCurve()
{
    const int spikes = 5;
    const double outerR = 40;
    const double innerR = 15;
    var points = new List<Point2D>();

    for (int i = 0; i < spikes * 2; i++)
    {
        double r = i % 2 == 0 ? outerR : innerR;
        double angle = Math.PI / 2 + i * Math.PI / spikes;
        points.Add(new Point2D(r * Math.Cos(angle), -r * Math.Sin(angle)));
    }

    return Curve.FromPolyline(points, isClosed: true);
}

static (Curve, LineScatterOptions) BuildSpike()
{
    var points = new[]
    {
        new Point2D(-30, 0),
        new Point2D(0, 0),
        new Point2D(0, 40),
        new Point2D(30, 0),
    };
    var curve = Curve.FromPolyline(points);
    var options = new LineScatterOptions
    {
        StoneDiameterMm = 2.4,
        GapMm = 0.2,
        Mode = StepMode.FitEven,
        CornerAngleThresholdDeg = 20,
    };
    return (curve, options);
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
    {
        dir = dir.Parent;
    }

    return dir?.FullName ?? Directory.GetCurrentDirectory();
}
