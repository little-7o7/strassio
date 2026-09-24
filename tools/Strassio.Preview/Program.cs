using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Strassio.Core.Editing;
using Strassio.Core.Geometry;
using Strassio.Core.Methods;
using Strassio.Core.Placement;
using Strassio.Core.Vector;
using Strassio.Preview;

string scenario = args.Length > 0 ? args[0] : "line";

if (scenario == "methods")
{
    RenderMethodsScenario();
    return;
}

if (scenario == "icons")
{
    RenderIconsScenario();
    return;
}

if (scenario == "corners")
{
    CornerLab.Render(FindRepoRoot());
    return;
}

if (scenario == "rows")
{
    RowCornerLab.Render(FindRepoRoot());
    return;
}

if (scenario == "site")
{
    RenderSiteImages();
    return;
}

if (scenario == "kant")
{
    KantLab.Render(FindRepoRoot());
    return;
}

if (scenario == "calligraphy")
{
    // Каллиграфия (L4) для объяснения автору: одна и та же волна, 5 рядов, три варианта
    // «Где шире всего» сверху вниз — в середине, к концу, в начале. Серая линия — исходная.
    string dir = Path.Combine(FindRepoRoot(), "out", "preview");
    Directory.CreateDirectory(dir);
    var lines = new List<(IReadOnlyList<Point2D> Points, string Color)>();
    var all = new List<PlacedStone>();
    string[] profiles = { MethodChoices.ProfileMiddle, MethodChoices.ProfileGrow, MethodChoices.ProfileShrink };
    for (int i = 0; i < profiles.Length; i++)
    {
        var pts = new List<Point2D>();
        for (int k = 0; k <= 80; k++)
        {
            double x = 80.0 * k / 80;
            pts.Add(new Point2D(x, i * 28 + 5 * Math.Sin(x / 80 * 2 * Math.PI)));
        }

        Curve wave = Curve.FromPolyline(pts, isClosed: false);
        var p = new MethodParameters { RowCount = 5, GapMm = 0.1, RowGapMm = 0.1, WidthProfile = profiles[i] };
        MethodResult r = MethodRunner.Run(MethodKind.L4, new[] { wave }, 2.4, p);
        all.AddRange(r.Stones);
        lines.Add((pts, "#9e9e9e"));
        Console.WriteLine($"{profiles[i],-8} {r.Stones.Count} страз");
    }

    File.WriteAllText(Path.Combine(dir, "calligraphy.svg"), SvgWriter.RenderMulti(lines, all));
    Console.WriteLine($"SVG сохранён: {Path.Combine(dir, "calligraphy.svg")}");
    return;
}

if (scenario == "lengthwise")
{
    // «Ряды вдоль формы» на разных фигурах рядом с нынешней контурной заливкой: сколько камней,
    // есть ли наложения, сколько времени.
    string dir = Path.Combine(FindRepoRoot(), "out", "preview", "lengthwise");
    Directory.CreateDirectory(dir);
    var shapes = new List<(string, Curve)>
    {
        ("heart", BuildHeart().Item1),
        ("star", BuildStarCurve()),
        ("circle", Curve.FromPolyline(Enumerable.Range(0, 96).Select(i => new Point2D(15 * Math.Cos(i * Math.PI / 48), 15 * Math.Sin(i * Math.PI / 48))).ToList(), isClosed: true)),
        ("strip", Curve.FromPolyline(new[] { new Point2D(0, 0), new Point2D(80, 0), new Point2D(80, 12), new Point2D(0, 12) }, isClosed: true)),
    };
    foreach (string name in new[] { "1.svg", "vector.svg" })
    {
        string file = Path.Combine(FindRepoRoot(), name);
        if (File.Exists(file))
        {
            shapes.Add((Path.GetFileNameWithoutExtension(name) == "1" ? "leaf" : "vector", Strassio.Preview.SvgShape.Load(file)));
        }
    }

    var all = shapes.Select(sh => (sh.Item1, new[] { sh.Item2 })).ToList();
    all.AddRange(BlockLetters());
    // Крупные буквы: штрих 20 мм — шире пяти камней.
    all.AddRange(BlockLetters().Where(l => "TEHO".Contains(l.Item1)).Select(l => (l.Item1 + "big",
        l.Item2.Select(c => Curve.FromPolyline(CurveFlattener.Flatten(c).Points.Select(pt => pt.Position * 2.5).ToList(), isClosed: true)).ToArray())));
    foreach ((string name, Curve[] shape) in all)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        MethodResult along = MethodRunner.Run(MethodKind.F3, shape, 2.4, new MethodParameters { GapMm = 0 });
        long ms = watch.ElapsedMilliseconds;
        MethodResult rings = MethodRunner.Run(MethodKind.F3, shape, 2.4, new MethodParameters { GapMm = 0, CenterPattern = MethodChoices.PatternHoneycomb });
        int overlaps = IntersectionFixer.FindIndicesToRemove(along.Stones, 0, 0.03).Count(x => x);
        SignedDistanceField field = new ShapeRegion(shape, 2.4).Field;
        double depth = 0;
        for (int iy = 0; iy < field.Height; iy++)
        {
            for (int ix = 0; ix < field.Width; ix++)
            {
                depth = Math.Max(depth, field.ValueAt(ix, iy));
            }
        }

        Console.Write($"ширина {2 * depth / 2.4,4:F1} камня  ");
        Console.WriteLine($"{name,-8} вдоль формы {along.Stones.Count,5} ({ms} мс, наложений {overlaps}), кольцами {rings.Stones.Count,5}");
        var outlines = shape.Select(c => ((IReadOnlyList<Point2D>)CurveFlattener.Flatten(c).Points.Select(pt => pt.Position).Append(CurveFlattener.Flatten(c).Points[0].Position).ToList(), "#cccccc")).ToList();
        File.WriteAllText(Path.Combine(dir, name + "-along.svg"), SvgWriter.RenderMulti(outlines, along.Stones));
        File.WriteAllText(Path.Combine(dir, name + "-rings.svg"), SvgWriter.RenderMulti(outlines, rings.Stones));
    }

    return;
}

if (scenario == "vector")
{
    RenderVectorScenario();
    return;
}

if (scenario == "edit")
{
    RenderEditScenario();
    return;
}

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

if (scenario.StartsWith("contour-", StringComparison.Ordinal))
{
    RenderContourScenario(scenario);
    return;
}

(Curve Curve, LineScatterOptions Options) built = scenario switch
{
    "line" => BuildLine(),
    "zigzag" => BuildZigzag(),
    "square" => BuildSquare(),
    "star" => BuildStar(),
    "spike" => BuildSpike(),
    "heart" => BuildHeart(),
    "spiral" => BuildSpiral(),
    "s-curve" => BuildSCurve(),
    "letters" => BuildLetters(),
    _ => throw new ArgumentException($"Неизвестный сценарий '{scenario}'. Доступные: line, zigzag, square, star, spike, heart, spiral, s-curve, letters, offset-square, offset-star."),
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
    Console.WriteLine($"Длина кривой: {flat.TotalLength:0.###} мм, замкнута: {flat.IsClosed}, точек: {flat.Points.Count}");
    Console.WriteLine("Острые углы (расстояние от начала): " + string.Join(", ",
        CornerDetector.FindSharpCornerDistances(flat, built.Options.CornerAngleThresholdDeg).Select(d => d.ToString("0.###"))));
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

// Все методы докера с параметрами (MethodRunner) на обязательных фигурах — out/preview/methods/.
// Незамкнутые фигуры (S-кривая, зигзаг) — только для методов по линии.
static void RenderMethodsScenario()
{
    string outDir = Path.Combine(FindRepoRoot(), "out", "preview", "methods");
    Directory.CreateDirectory(outDir);

    var shapes = new (string Name, Curve Curve)[]
    {
        ("star", BuildStarCurve()),
        ("heart", BuildHeart().Item1),
        ("square", Curve.FromPolyline(
            new[] { new Point2D(0, 0), new Point2D(40, 0), new Point2D(40, 40), new Point2D(0, 40) }, isClosed: true)),
        ("rect", Curve.FromPolyline(
            new[] { new Point2D(0, 0), new Point2D(60, 0), new Point2D(60, 45), new Point2D(0, 45) }, isClosed: true)),
        ("s-curve", BuildSCurve().Item1),
        ("zigzag", BuildZigzag().Item1),
    };

    var previewSizes = new Dictionary<string, double> { ["ss5"] = 2.1, ["ss6"] = 2.4, ["ss10"] = 2.9, ["ss16"] = 3.9 };
    var variants = new List<(string Name, MethodKind Kind, MethodParameters Params)>();
    foreach (MethodInfo info in MethodCatalog.All)
    {
        variants.Add((info.Key.Substring("method.".Length), info.Kind, new MethodParameters()));
    }

    variants.Add(("l2-outside", MethodKind.L2, new MethodParameters { RowSide = MethodChoices.SideOutside }));
    variants.Add(("l2-inside", MethodKind.L2, new MethodParameters { RowSide = MethodChoices.SideInside }));
    variants.Add(("l2-5rows-stagger", MethodKind.L2, new MethodParameters { RowCount = 5, Stagger = true }));
    variants.Add(("l3-inside", MethodKind.L3, new MethodParameters { OffsetSide = MethodChoices.SideInside }));
    variants.Add(("f1-angle30", MethodKind.F1, new MethodParameters { AngleDeg = 30 }));
    variants.Add(("l2-edge-ss5", MethodKind.L2, new MethodParameters { EdgeSize = "ss5" }));
    variants.Add(("l5-ss16-ss5", MethodKind.L5, new MethodParameters { FromSize = "ss16", ToSize = "ss5" }));
    variants.Add(("l6-pattern", MethodKind.L6, new MethodParameters { SizePattern = "ss6, ss6, ss16" }));
    variants.Add(("l8-accents", MethodKind.L8, new MethodParameters { AccentSize = "ss16" }));
    variants.Add(("l4-grow", MethodKind.L4, new MethodParameters { RowCount = 5, WidthProfile = MethodChoices.ProfileGrow }));
    // Зазор 0 — как у автора на скриншотах.
    variants.Add(("l1-gap0", MethodKind.L1, new MethodParameters { GapMm = 0 }));
    variants.Add(("l2-gap0", MethodKind.L2, new MethodParameters { GapMm = 0, RowGapMm = 0 }));
    variants.Add(("f3-gap0", MethodKind.F3, new MethodParameters { GapMm = 0 }));
    variants.Add(("f4-gap0", MethodKind.F4, new MethodParameters { GapMm = 0 }));
    variants.Add(("f5-gap0", MethodKind.F5, new MethodParameters { GapMm = 0 }));
    variants.Add(("f6-gap0", MethodKind.F6, new MethodParameters { GapMm = 0 }));
    variants.Add(("f9-spiral-gap0", MethodKind.F9, new MethodParameters { GapMm = 0, CenterMode = MethodChoices.CenterSpiral }));
    variants.Add(("f9-spiral", MethodKind.F9, new MethodParameters { CenterMode = MethodChoices.CenterSpiral }));
    variants.Add(("f10-mix", MethodKind.F10, new MethodParameters { MixSizes = "ss5, ss10, ss16" }));
    variants.Add(("f11-ss16-ss5", MethodKind.F11, new MethodParameters { FromSize = "ss16", ToSize = "ss5" }));
    variants.Add(("f2-auto", MethodKind.F2, new MethodParameters { AutoGrid = MethodChoices.AutoShiftAngle }));

    foreach ((string shapeName, Curve curve) in shapes)
    {
        FlattenedCurve flat = CurveFlattener.Flatten(curve);
        foreach ((string name, MethodKind kind, MethodParameters p) in variants)
        {
            if ((MethodCatalog.Get(kind).NeedsClosed && !flat.IsClosed) || MethodCatalog.Get(kind).NeedsGuide)
            {
                continue;
            }

            MethodResult result = MethodRunner.Run(kind, new[] { curve }, 2.4, p, previewSizes);
            string file = Path.Combine(outDir, name + "-" + shapeName + ".svg");
            File.WriteAllText(file, SvgWriter.Render(flat.Points.Select(pt => pt.Position).ToList(), flat.IsClosed, result.Stones));

            // При зазоре 0 касающиеся стразы — норма, наложение считается только настоящее.
            int overlaps = IntersectionFixer.FindIndicesToRemove(result.Stones, Math.Min(0.05, p.GapMm / 2), 0.03).Count(x => x);
            Console.WriteLine($"{name,-18} {shapeName,-8} {result.Stones.Count,5} страз, наложений: {overlaps}");
        }
    }

    // F7 и F8 — с двумя линиями.
    Curve waveA = WaveLine(0, 0, 60, 4);
    Curve waveB = WaveLine(0, 22, 60, -3);
    MethodResult blend = MethodRunner.Run(MethodKind.F7, new[] { waveA }, 2.4, new MethodParameters(), previewSizes, new[] { waveB });
    SaveTwo("f7-blend", waveA, waveB, blend);

    // Зазор 0 и зазор между рядами 0 — как у автора: ряды должны касаться, без пропущенных рядов.
    var gap0 = new MethodParameters { GapMm = 0, RowGapMm = 0 };
    SaveTwo("f7-blend-gap0", waveA, waveB, MethodRunner.Run(MethodKind.F7, new[] { waveA }, 2.4, gap0, previewSizes, new[] { waveB }), 0);
    Curve lineA = Curve.FromPolyline(new[] { new Point2D(0, 0), new Point2D(50, 0) }, isClosed: false);
    Curve lineB = Curve.FromPolyline(new[] { new Point2D(0, 16.3), new Point2D(50, 16.3) }, isClosed: false);
    SaveTwo("f7-blend-parallel-gap0", lineA, lineB, MethodRunner.Run(MethodKind.F7, new[] { lineA }, 2.4, gap0, previewSizes, new[] { lineB }), 0);

    Curve heart = BuildHeart().Item1;
    Curve guide = Curve.FromPolyline(new[] { new Point2D(-30, -20), new Point2D(0, 5), new Point2D(30, 0) }, isClosed: false);
    MethodResult guided = MethodRunner.Run(MethodKind.F8, new[] { heart }, 2.4, new MethodParameters(), previewSizes, new[] { guide });
    SaveTwo("f8-guide-heart", heart, guide, guided);

    void SaveTwo(string name, Curve a, Curve b, MethodResult result, double minGapMm = 0.05)
    {
        var lines = new List<(IReadOnlyList<Point2D> Points, string Color)>
        {
            (CurveFlattener.Flatten(a).Points.Select(pt => pt.Position).ToList(), "#cccccc"),
            (CurveFlattener.Flatten(b).Points.Select(pt => pt.Position).ToList(), "#1E88E5"),
        };
        File.WriteAllText(Path.Combine(outDir, name + ".svg"), SvgWriter.RenderMulti(lines, result.Stones));
        int overlaps = IntersectionFixer.FindIndicesToRemove(result.Stones, minGapMm, 0.01).Count(x => x);
        Console.WriteLine($"{name,-18} {result.Stones.Count,5} страз, наложений: {overlaps}");
    }

    Console.WriteLine($"SVG сохранены: {outDir}");
}

static Curve WaveLine(double x0, double y0, double length, double amplitude)
{
    var pts = new List<Point2D>();
    for (int i = 0; i <= 60; i++)
    {
        double x = x0 + length * i / 60;
        pts.Add(new Point2D(x, y0 + amplitude * Math.Sin(x / length * 2 * Math.PI)));
    }

    return Curve.FromPolyline(pts, isClosed: false);
}

// Картинки-схемы методов из списка докера (MethodSamples) — out/preview/icons/.
static void RenderIconsScenario()
{
    string outDir = Path.Combine(FindRepoRoot(), "out", "preview", "icons");
    Directory.CreateDirectory(outDir);

    foreach (MethodInfo info in MethodCatalog.All)
    {
        MethodResult result = MethodSamples.Run(info.Kind, out MethodSample sample);
        FlattenedCurve flat = CurveFlattener.Flatten(sample.Shape);
        string name = info.Key.Substring("method.".Length);
        File.WriteAllText(
            Path.Combine(outDir, name + ".svg"),
            SvgWriter.Render(flat.Points.Select(pt => pt.Position).ToList(), flat.IsClosed, result.Stones, marginMm: 1));
        Console.WriteLine($"{name}: {result.Stones.Count} страз");
    }

    Console.WriteLine($"SVG сохранены: {outDir}");
}

// Правка страз в документе (docs/SPEC.md, раздел 7.1) — out/preview/edit/. Исходник: соты на звезде
// плюс их копия, сдвинутая на 1 мм «поверх» (типичная ошибка копирования), и отдельно — несколько
// точных дублей. Зелёные — остались на месте, красные — сдвинуты, удалённых нет на картинке.
static void RenderEditScenario()
{
    string outDir = Path.Combine(FindRepoRoot(), "out", "preview", "edit");
    Directory.CreateDirectory(outDir);

    Curve star = BuildStarCurve();
    FlattenedCurve starFlat = CurveFlattener.Flatten(star);
    List<Point2D> outline = starFlat.Points.Select(pt => pt.Position).ToList();

    MethodResult fill = MethodRunner.Run(MethodKind.F2, new[] { star }, 2.4, new MethodParameters());
    var stones = new List<DocStone>();
    int order = 0;
    foreach (PlacedStone st in fill.Stones)
    {
        stones.Add(new DocStone(st.Center, st.DiameterMm, order++));
    }

    // Копия правой половины, сдвинутая на 1 мм и лежащая выше.
    foreach (PlacedStone st in fill.Stones.Where(x => x.Center.X > 0))
    {
        stones.Add(new DocStone(new Point2D(st.Center.X + 1, st.Center.Y), st.DiameterMm, order++));
    }

    void Save(string name, IReadOnlyList<DocStone> source, EditResult result, IReadOnlyList<(IReadOnlyList<Point2D> Points, string Color)> extra)
    {
        var moved = result.Moved.ToDictionary(m => m.Index, m => m.NewCenter);
        var deleted = new HashSet<int>(result.Deleted);
        var shown = new List<PlacedStone>();
        for (int i = 0; i < source.Count; i++)
        {
            if (deleted.Contains(i))
            {
                continue;
            }

            bool isMoved = moved.TryGetValue(i, out Point2D c);
            shown.Add(new PlacedStone(isMoved ? c : source[i].Center, source[i].DiameterMm, isCorner: isMoved));
        }

        var lines = new List<(IReadOnlyList<Point2D> Points, string Color)> { (outline, "#cccccc") };
        lines.AddRange(extra);
        File.WriteAllText(Path.Combine(outDir, name + ".svg"), SvgWriter.RenderMulti(lines, shown));
        int overlaps = IntersectionFixer.FindIndicesToRemove(shown, 0.04, 0.01).Count(x => x);
        Console.WriteLine($"{name,-14} удалено {result.Deleted.Count,4}, сдвинуто {result.Moved.Count,4}, осталось наложений {overlaps}");
    }

    var none = new List<(IReadOnlyList<Point2D> Points, string Color)>();
    Save("0-before", stones, EditResult.Nothing, none);
    Save("1-keep-top", stones, StoneEditor.ResolveOverlaps(stones, keepTop: true), none);
    Save("2-keep-bottom", stones, StoneEditor.ResolveOverlaps(stones, keepTop: false), none);
    Save("5-shift", stones, StoneEditor.ShiftApart(stones), none);

    // «Сдвинуть» там, где есть место: редкая сетка, каждая седьмая страза съехала на соседа.
    MethodResult sparse = MethodRunner.Run(MethodKind.F1, new[] { star }, 2.4, new MethodParameters { GapMm = 1.2 });
    var loose = new List<DocStone>();
    for (int i = 0; i < sparse.Stones.Count; i++)
    {
        Point2D c = sparse.Stones[i].Center;
        loose.Add(new DocStone(i % 7 == 3 ? new Point2D(c.X + 1.5, c.Y + 0.3) : c, 2.4, i));
    }

    Save("5-shift-loose-before", loose, EditResult.Nothing, none);
    Save("5-shift-loose", loose, StoneEditor.ShiftApart(loose), none);

    // Режимы 3 и 4 — на исходных сотах без копии.
    List<DocStone> clean = stones.Take(fill.Stones.Count).ToList();
    var cutter = Curve.FromPolyline(new[] { new Point2D(-40, -10), new Point2D(0, 5), new Point2D(40, -5) }, isClosed: false);
    Save("3-along-line", clean, StoneEditor.DeleteAlongLines(clean, new[] { cutter }),
        new List<(IReadOnlyList<Point2D> Points, string Color)> { (CurveFlattener.Flatten(cutter).Points.Select(pt => pt.Position).ToList(), "#1E88E5") });

    var circlePts = new List<Point2D>();
    for (int i = 0; i < 64; i++)
    {
        double a = 2 * Math.PI * i / 64;
        circlePts.Add(new Point2D(12 * Math.Cos(a), 12 * Math.Sin(a)));
    }

    var circle = Curve.FromPolyline(circlePts, isClosed: true);
    var circleLine = new List<(IReadOnlyList<Point2D> Points, string Color)> { (circlePts.Append(circlePts[0]).ToList(), "#1E88E5") };
    Save("4-inside", clean, StoneEditor.DeleteByShape(clean, new[] { circle }, inside: true), circleLine);
    Save("4-outside", clean, StoneEditor.DeleteByShape(clean, new[] { circle }, inside: false), circleLine);

    // Дубли: каждую пятую стразу скопировали точно на место.
    var dup = new List<DocStone>(clean);
    for (int i = 0; i < clean.Count; i += 5)
    {
        dup.Add(new DocStone(clean[i].Center, clean[i].DiameterMm, order++));
    }

    // Поиск дырок: из сот убрали каждую 13-ю стразу; найденные дырки — красные.
    var holey = clean.Where((st, i) => i % 13 != 6).ToList();
    List<Point2D> found = HoleFinder.Find(holey, 2.4, 0.2);
    var holeStones = holey.Select(st => new PlacedStone(st.Center, st.DiameterMm, false))
        .Concat(found.Select(h => new PlacedStone(h, 2.4, isCorner: true))).ToList();
    File.WriteAllText(Path.Combine(outDir, "6-holes.svg"), SvgWriter.RenderMulti(new List<(IReadOnlyList<Point2D> Points, string Color)> { (outline, "#cccccc") }, holeStones));
    Console.WriteLine($"дырки: убрано {clean.Count - holey.Count}, найдено {found.Count}");

    EditResult dups = StoneEditor.FindDuplicates(dup);
    Console.WriteLine($"дубли: добавлено {dup.Count - clean.Count}, найдено {dups.Deleted.Count}");
    Console.WriteLine($"SVG сохранены: {outDir}");
}

// Векторные инструменты (раздел 10) — out/preview/vector/: центральная линия толстых форм,
// смещение и параллельные линии. Исходник — серым, результат — синим.
static void RenderVectorScenario()
{
    string outDir = Path.Combine(FindRepoRoot(), "out", "preview", "vector");
    Directory.CreateDirectory(outDir);

    List<Point2D> Pts(Curve c) => CurveFlattener.Flatten(c).Points.Select(pt => pt.Position).ToList();

    void Save(string name, IEnumerable<Curve> sources, IEnumerable<Curve> results)
    {
        var lines = sources.Select(c => ((IReadOnlyList<Point2D>)ClosePts(Pts(c), c.IsClosed), "#bbbbbb"))
            .Concat(results.Select(c => ((IReadOnlyList<Point2D>)ClosePts(Pts(c), c.IsClosed), "#1E88E5")))
            .ToList();
        File.WriteAllText(Path.Combine(outDir, name + ".svg"), SvgWriter.RenderMulti(lines, new List<PlacedStone>()));
    }

    List<Point2D> ClosePts(List<Point2D> p, bool closed) => closed ? p.Append(p[0]).ToList() : p;

    // Толстая буква «S» из двух дуг и «гантель».
    var sShape = new List<Point2D>();
    for (int i = 0; i <= 40; i++) { double a = Math.PI * i / 40; sShape.Add(new Point2D(10 * Math.Cos(a) , 10 + 10 * Math.Sin(a))); }
    for (int i = 40; i >= 0; i--) { double a = Math.PI * i / 40; sShape.Add(new Point2D(4 * Math.Cos(a), 10 + 4 * Math.Sin(a))); }
    Curve sTop = Curve.FromPolyline(sShape, isClosed: true);

    foreach ((string name, Curve[] shape) in new[]
    {
        ("centerline-dumbbell", new[] { BuildLettersCurve() }),
        ("centerline-star", new[] { BuildStarCurve() }),
        ("centerline-arch", new[] { sTop }),
        ("centerline-heart", new[] { BuildHeart().Item1 }),
    })
    {
        List<Curve> center = Centerline.Build(shape);
        Save(name, shape, center);
        Console.WriteLine($"{name,-22} линий: {center.Count}");
    }

    Curve star = BuildStarCurve();
    Save("offset-star", new[] { star }, new[] { VectorTools.Offset(star, 3, true), VectorTools.Offset(star, -1.5, false) });
    Curve wave = BuildSCurve().Item1;
    Save("parallel-scurve", new[] { wave }, VectorTools.Parallel(wave, 3, 2.6, bothSides: true, roundCorners: true));
    Console.WriteLine($"SVG сохранены: {outDir}");
}

static void RenderRingScenario(string scenario)
{
    Curve curve = scenario switch
    {
        "ring-star" => BuildStarCurve(),
        "ring-square" => Curve.FromPolyline(
            new[] { new Point2D(0, 0), new Point2D(40, 0), new Point2D(40, 40), new Point2D(0, 40) },
            isClosed: true),
        "ring-petal" => BuildPetalCurve(),
        "ring-petal-sharp" => BuildSharpPetalCurve(1.0),
        "ring-petal-small" => BuildSharpPetalCurve(0.4),
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
    // «Удалить» (раздел 6.4 ТЗ): лишние убираются, соседи по ряду раздвигаются, чтобы не было дырок.
    IntersectionFixResult removeResult = IntersectionFixer.Fix(stones, new IntersectionFixOptions { Action = IntersectionAction.Remove });
    IReadOnlyList<PlacedStone> fixedStones = removeResult.Stones;

    FlattenedCurve flat = CurveFlattener.Flatten(curve);
    string outDir = Path.Combine(FindRepoRoot(), "out", "preview");
    Directory.CreateDirectory(outDir);
    string outPath = Path.Combine(outDir, scenario + ".svg");
    string outPathFixed = Path.Combine(outDir, scenario + "-fixed.svg");

    string svg = SvgWriter.Render(flat.Points.Select(p => p.Position).ToList(), flat.IsClosed, stones);
    File.WriteAllText(outPath, svg);
    string svgFixed = SvgWriter.Render(flat.Points.Select(p => p.Position).ToList(), flat.IsClosed, fixedStones);
    File.WriteAllText(outPathFixed, svgFixed);

    // Действие «Сдвинуть» (раздел 6.4 ТЗ): мешающая страза сначала пробует отодвинуться вдоль ряда.
    IntersectionFixResult shiftResult = IntersectionFixer.Fix(stones, new IntersectionFixOptions { Action = IntersectionAction.Shift });
    string outPathShift = Path.Combine(outDir, scenario + "-shift.svg");
    File.WriteAllText(outPathShift, SvgWriter.Render(flat.Points.Select(p => p.Position).ToList(), flat.IsClosed, shiftResult.Stones));

    static int CountOverlaps(IReadOnlyList<PlacedStone> list)
    {
        int count = 0;
        for (int i = 0; i < list.Count; i++)
        {
            for (int j = i + 1; j < list.Count; j++)
            {
                if (Point2D.Distance(list[i].Center, list[j].Center) < list[i].DiameterMm / 2 + list[j].DiameterMm / 2 + 0.09)
                {
                    count++;
                }
            }
        }

        return count;
    }

    Console.WriteLine($"Сценарий '{scenario}': {stones.Count} страз в {rows.Length} рядах, после исправления пересечений — {fixedStones.Count} (убрано {stones.Count - fixedStones.Count}, раздвинуто соседей {removeResult.ShiftedIndices.Count}, наложений {CountOverlaps(fixedStones)}).");
    Console.WriteLine($"Со сдвигом: осталось {shiftResult.Stones.Count}, сдвинуто {shiftResult.ShiftedIndices.Count}, убрано {shiftResult.RemovedIndices.Count}, наложений {CountOverlaps(shiftResult.Stones)}; конфликтов до исправления {shiftResult.ConflictIndices.Count}.");
    Console.WriteLine($"SVG сохранён: {outPathFixed}");
    Console.WriteLine($"SVG сохранён: {outPathShift}");
    Console.WriteLine($"SVG сохранён: {outPath}");
}

/// <summary>
/// Картинки для сайта (раздел «Примеры»): те же алгоритмы, что в плагине, но нарисованные
/// «по-настоящему» — одинаковые камни-стразы, без служебных цветов и без контура фигуры.
/// Кладутся сразу в server/public/img/, поверх старых.
/// </summary>
static void RenderSiteImages()
{
    string root = FindRepoRoot();
    string imgDir = Path.Combine(root, "server", "public", "img");
    Directory.CreateDirectory(imgDir);

    void Save(string name, IReadOnlyList<PlacedStone> stones)
    {
        string path = Path.Combine(imgDir, name);
        File.WriteAllText(path, SvgWriter.RenderSite(stones));
        Console.WriteLine($"  {name} — {stones.Count} страз");
    }

    var contour = new ContourFillOptions { StoneDiameterMm = 2.4, GapMm = 0.2 };
    var honeycomb = new GridFillOptions { StoneDiameterMm = 2.4, GapMm = 0.2, Pattern = GridPattern.Honeycomb };

    Console.WriteLine("Картинки сайта:");
    Save("heart.svg", ContourFiller.Fill(new[] { BuildHeart().Item1 }, contour));
    Save("contour-letters.svg", ContourFiller.Fill(new[] { BuildLettersCurve() }, contour));

    honeycomb.AngleDeg = 12;
    Save("fill-star.svg", GridFiller.Fill(new[] { BuildStarCurve() }, honeycomb));

    honeycomb.AngleDeg = 0;
    Save("fill-honeycomb.svg", GridFiller.Fill(
        new[]
        {
            Curve.FromPolyline(
                new[] { new Point2D(0, 0), new Point2D(40, 0), new Point2D(40, 40), new Point2D(0, 40) },
                isClosed: true),
        },
        honeycomb));

    Console.WriteLine("Готово: " + imgDir);
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
        case "fill-letters":
            options.Pattern = GridPattern.Honeycomb;
            boundaries = new[] { BuildLettersCurve() };
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

static void RenderContourScenario(string scenario)
{
    var options = new ContourFillOptions { StoneDiameterMm = 2.4, GapMm = 0.2 };
    Curve square40 = Curve.FromPolyline(
        new[] { new Point2D(0, 0), new Point2D(40, 0), new Point2D(40, 40), new Point2D(0, 40) },
        isClosed: true);
    Curve[] boundaries;

    switch (scenario)
    {
        case "contour-square":
            boundaries = new[] { square40 };
            break;
        case "contour-star":
            boundaries = new[] { BuildStarCurve() };
            break;
        case "contour-edge-only":
            boundaries = new[] { square40 };
            options.MaxRings = 2;
            options.FillCenter = false;
            break;
        case "contour-combined":
            boundaries = new[] { BuildStarCurve() };
            options.MaxRings = 2;
            options.FillCenter = true;
            break;
        case "contour-letter-o":
            boundaries = new[]
            {
                square40,
                Curve.FromPolyline(
                    new[] { new Point2D(12, 12), new Point2D(28, 12), new Point2D(28, 28), new Point2D(12, 28) },
                    isClosed: true),
            };
            break;
        case "contour-heart":
            boundaries = new[] { BuildHeart().Item1 };
            break;
        case "contour-letters":
            boundaries = new[] { BuildLettersCurve() };
            break;
        default:
            throw new ArgumentException($"Неизвестный сценарий '{scenario}'.");
    }

    var watch = System.Diagnostics.Stopwatch.StartNew();
    List<PlacedStone> stones = ContourFiller.Fill(boundaries, options);
    watch.Stop();

    List<FlattenedCurve> flats = boundaries.Select(b => CurveFlattener.Flatten(b)).ToList();

    // Проверка числами, а не на глаз: наложения и самая большая дыра, куда влезла бы страза.
    int overlaps = 0;
    for (int i = 0; i < stones.Count; i++)
    {
        for (int j = i + 1; j < stones.Count; j++)
        {
            if (Point2D.Distance(stones[i].Center, stones[j].Center) < options.StoneDiameterMm - 0.02)
            {
                overlaps++;
            }
        }
    }

    SignedDistanceField field = SignedDistanceField.Build(flats, 0.25);
    double step = options.StoneDiameterMm + options.GapMm;
    int freeSpots = 0;
    for (int iy = 0; iy < field.Height; iy++)
    {
        for (int ix = 0; ix < field.Width; ix++)
        {
            if (field.ValueAt(ix, iy) < options.StoneDiameterMm / 2 + 0.05)
            {
                continue;
            }

            Point2D p = field.PositionOf(ix, iy);
            if (stones.All(s => Point2D.Distance(s.Center, p) >= step + 0.05))
            {
                freeSpots++;
            }
        }
    }

    var polylines = flats
        .Select(f => (Points: (IReadOnlyList<Point2D>)f.Points.Select(p => p.Position).ToList(), Color: "#cccccc"))
        .ToArray();

    string outDir = Path.Combine(FindRepoRoot(), "out", "preview");
    Directory.CreateDirectory(outDir);
    string outPath = Path.Combine(outDir, scenario + ".svg");
    File.WriteAllText(outPath, SvgWriter.RenderMulti(polylines, stones));

    Console.WriteLine($"Сценарий '{scenario}': {stones.Count} страз за {watch.ElapsedMilliseconds} мс; " +
        $"наложений {overlaps}; мест, куда влезла бы ещё страза, {freeSpots}.");
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

static (Curve, LineScatterOptions) BuildHeart()
{
    // Классическая параметрическая формула сердца — острый угол снизу (настоящий математический
    // "клюв", не приближение) и мягкая выемка сверху — обязательный сценарий (SPEC 6.5).
    var points = new List<Point2D>();
    const int n = 200;
    for (int i = 0; i <= n; i++)
    {
        double t = 2 * Math.PI * i / n;
        double x = 16 * Math.Pow(Math.Sin(t), 3);
        double y = 13 * Math.Cos(t) - 5 * Math.Cos(2 * t) - 2 * Math.Cos(3 * t) - Math.Cos(4 * t);
        points.Add(new Point2D(x * 1.8, -y * 1.8));
    }

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

static (Curve, LineScatterOptions) BuildSpiral()
{
    // Тугая спираль (архимедова) — соседние витки всего в нескольких мм друг от друга.
    var points = new List<Point2D>();
    const int n = 400;
    const double a = 3.0, b = 0.9;
    const double thetaMax = 8 * Math.PI;
    for (int i = 0; i <= n; i++)
    {
        double theta = thetaMax * i / n;
        double r = a + b * theta;
        points.Add(new Point2D(r * Math.Cos(theta), r * Math.Sin(theta)));
    }

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

static (Curve, LineScatterOptions) BuildSCurve()
{
    // Гладкая S-образная кривая Безье — проверка смещения и расстановки в точке перегиба
    // (там, где кривая меняет сторону изгиба).
    var seg = CurveSegment.Cubic(
        new Point2D(-30, 0), new Point2D(-10, 40), new Point2D(10, -40), new Point2D(30, 0));
    var curve = new Curve(new[] { seg }, isClosed: false);
    var options = new LineScatterOptions
    {
        StoneDiameterMm = 2.4,
        GapMm = 0.2,
        Mode = StepMode.FitEven,
        CornerAngleThresholdDeg = 20,
    };
    return (curve, options);
}

static Curve BuildPetalCurve()
{
    // Лист/лепесток — гладкая кривая Безье с острыми кончиками слева и справа, похожая на форму
    // из отчёта автора (реальная кривая, нарисованная в CorelDRAW пером).
    var seg1 = CurveSegment.Cubic(new Point2D(-25, 0), new Point2D(-12, 22), new Point2D(12, 22), new Point2D(25, 0));
    var seg2 = CurveSegment.Cubic(new Point2D(25, 0), new Point2D(12, -22), new Point2D(-12, -22), new Point2D(-25, 0));
    return new Curve(new[] { seg1, seg2 }, isClosed: true);
}

static Curve BuildSharpPetalCurve(double scale)
{
    // Острее и/или мельче — управляющие точки ближе к оси, кончики более "клювастые".
    var seg1 = CurveSegment.Cubic(
        new Point2D(-25 * scale, 0), new Point2D(-18 * scale, 10 * scale),
        new Point2D(18 * scale, 10 * scale), new Point2D(25 * scale, 0));
    var seg2 = CurveSegment.Cubic(
        new Point2D(25 * scale, 0), new Point2D(18 * scale, -10 * scale),
        new Point2D(-18 * scale, -10 * scale), new Point2D(-25 * scale, 0));
    return new Curve(new[] { seg1, seg2 }, isClosed: true);
}

static Curve BuildLettersCurve()
{
    // "Гантель" — два широких блина на узкой перемычке, как у соединённых букв или у "талии"
    // буквы S/B, где соседние штрихи почти соприкасаются.
    var points = new[]
    {
        new Point2D(-20, -15), new Point2D(-8, -15), new Point2D(-3, -3), new Point2D(3, -3),
        new Point2D(8, -15), new Point2D(20, -15), new Point2D(20, 15), new Point2D(8, 15),
        new Point2D(3, 3), new Point2D(-3, 3), new Point2D(-8, 15), new Point2D(-20, 15),
    };
    return Curve.FromPolyline(points, isClosed: true);
}

static (Curve, LineScatterOptions) BuildLetters()
{
    var options = new LineScatterOptions
    {
        StoneDiameterMm = 2.4,
        GapMm = 0.2,
        Mode = StepMode.FitEven,
        CornerAngleThresholdDeg = 20,
    };
    return (BuildLettersCurve(), options);
}

// Печатные буквы высотой 40 мм, штрих 8 мм (три камня ss6 в ширину) — как в надписях на одежде.
// У O, A и 8 есть дырки: второй и третий контуры.
static List<(string, Curve[])> BlockLetters()
{
    Curve P(params double[] xy) => Curve.FromPolyline(
        Enumerable.Range(0, xy.Length / 2).Select(i => new Point2D(xy[2 * i], xy[2 * i + 1])).ToList(), isClosed: true);
    Curve Ring(double cx, double cy, double rx, double ry)
    {
        var pts = new List<Point2D>();
        for (int i = 0; i < 72; i++)
        {
            double a = 2 * Math.PI * i / 72;
            pts.Add(new Point2D(cx + rx * Math.Cos(a), cy + ry * Math.Sin(a)));
        }

        return Curve.FromPolyline(pts, isClosed: true);
    }

    return new List<(string, Curve[])>
    {
        ("T", new[] { P(0, 0, 30, 0, 30, 8, 19, 8, 19, 40, 11, 40, 11, 8, 0, 8) }),
        ("E", new[] { P(0, 0, 26, 0, 26, 8, 8, 8, 8, 16, 22, 16, 22, 24, 8, 24, 8, 32, 26, 32, 26, 40, 0, 40) }),
        ("H", new[] { P(0, 0, 8, 0, 8, 16, 22, 16, 22, 0, 30, 0, 30, 40, 22, 40, 22, 24, 8, 24, 8, 40, 0, 40) }),
        ("L", new[] { P(0, 0, 8, 0, 8, 32, 26, 32, 26, 40, 0, 40) }),
        ("O", new[] { Ring(15, 20, 15, 20), Ring(15, 20, 7, 12) }),
        ("A", new[] { P(11, 0, 19, 0, 30, 40, 22, 40, 20, 32, 10, 32, 8, 40, 0, 40), P(12, 24, 18, 24, 15, 12) }),
        ("8", new[] { Ring(15, 10, 12, 10), Ring(15, 10, 5, 4), }),
    }.Select(l => l.Item1 == "8"
        ? ("8", new[] { Ring(15, 11, 11, 11), Ring(15, 11, 4, 4), Ring(15, 30, 13, 11), Ring(15, 30, 5, 4) })
        : l).ToList();
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
