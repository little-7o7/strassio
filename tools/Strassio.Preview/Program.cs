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

(Curve Curve, LineScatterOptions Options) built = scenario switch
{
    "line" => BuildLine(),
    "zigzag" => BuildZigzag(),
    "square" => BuildSquare(),
    "star" => BuildStar(),
    _ => throw new ArgumentException($"Неизвестный сценарий '{scenario}'. Доступные: line, zigzag, square, star, offset-square, offset-star."),
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

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
    {
        dir = dir.Parent;
    }

    return dir?.FullName ?? Directory.GetCurrentDirectory();
}
