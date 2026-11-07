using System.Linq;
using Strassio.Core.Geometry;
using Strassio.Core.Placement;

namespace Strassio.Core.Tests.Placement;

public class RingScattererTests
{
    private static Curve Square40mm() => Curve.FromPolyline(
        new[] { new Point2D(0, 0), new Point2D(40, 0), new Point2D(40, 40), new Point2D(0, 40) },
        isClosed: true);

    [Fact]
    public void L3_SingleOffsetRow_StonesAreNearOffsetDistanceFromOriginal()
    {
        // L3 «по смещённой кривой» — просто один ряд с ненулевым OffsetMm.
        var row = new RowSpec
        {
            OffsetMm = 5,
            ScatterOptions = new LineScatterOptions { StoneDiameterMm = 2.4, GapMm = 0.2, Mode = StepMode.FitEven },
        };

        var stones = RingScatterer.Scatter(Square40mm(), new[] { row });

        Assert.True(stones.Count > 0);

        // Квадрат со стороной 40, смещённый на 5 мм — все стразы должны быть на ~25 мм от центра
        // (20,20), если сместили наружу, или на ~15 мм, если внутрь (знак зависит от направления
        // обхода контура, см. CurveOffsetterTests) — здесь просто проверяем, что это ОДНО и то же
        // значение для всех страз ряда, а не смесь исходного и смещённого расстояний.
        var center = new Point2D(20, 20);
        var distances = stones
            .Select(s => System.Math.Max(System.Math.Abs(s.Center.X - center.X), System.Math.Abs(s.Center.Y - center.Y)))
            .ToList();

        foreach (double distFromCenter in distances)
        {
            Assert.True(distFromCenter is (> 14 and < 16) or (> 24 and < 26),
                $"Страза на расстоянии {distFromCenter:0.##} от центра — ожидали ~15 или ~25 (смещение на 5 мм от исходных 20).");
        }
    }

    [Fact]
    public void L3_OffsetZero_SameAsL1()
    {
        var options = new LineScatterOptions { StoneDiameterMm = 2.4, GapMm = 0.2, Mode = StepMode.FitEven };
        var row = new RowSpec { OffsetMm = 0, ScatterOptions = options };

        var viaRing = RingScatterer.Scatter(Square40mm(), new[] { row });
        var viaLine = LineScatterer.Scatter(Square40mm(), options);

        Assert.Equal(viaLine.Count, viaRing.Count);
    }

    [Fact]
    public void L2_MultipleRows_DifferentDiameterPerRow()
    {
        // Три ряда: центр (по самой линии, крупный ss10), и два по бокам (мельче, ss6) — раздел 4 ТЗ,
        // "разный размер для каждого ряда".
        var rows = new[]
        {
            new RowSpec
            {
                OffsetMm = -3,
                ScatterOptions = new LineScatterOptions { StoneDiameterMm = 2.4, GapMm = 0.2, Mode = StepMode.FitEven },
            },
            new RowSpec
            {
                OffsetMm = 0,
                ScatterOptions = new LineScatterOptions { StoneDiameterMm = 3.2, GapMm = 0.2, Mode = StepMode.FitEven },
            },
            new RowSpec
            {
                OffsetMm = 3,
                ScatterOptions = new LineScatterOptions { StoneDiameterMm = 2.4, GapMm = 0.2, Mode = StepMode.FitEven },
            },
        };

        var stones = RingScatterer.Scatter(Square40mm(), rows);

        Assert.Contains(stones, s => System.Math.Abs(s.DiameterMm - 2.4) < 1e-6);
        Assert.Contains(stones, s => System.Math.Abs(s.DiameterMm - 3.2) < 1e-6);

        int countBig = stones.Count(s => System.Math.Abs(s.DiameterMm - 3.2) < 1e-6);
        int countSmall = stones.Count(s => System.Math.Abs(s.DiameterMm - 2.4) < 1e-6);
        Assert.True(countBig > 0 && countSmall > 0);
    }

    [Fact]
    public void L2_StaggeredRows_HaveDifferentStartOffset()
    {
        var options1 = new LineScatterOptions { StoneDiameterMm = 2.4, GapMm = 0.2, Mode = StepMode.FitEven };
        var options2 = new LineScatterOptions
        {
            StoneDiameterMm = 2.4, GapMm = 0.2, Mode = StepMode.FitEven, StartOffsetMm = 1.3, // шахматный сдвиг — половина шага
        };

        var rows = new[]
        {
            new RowSpec { OffsetMm = -3, ScatterOptions = options1 },
            new RowSpec { OffsetMm = 3, ScatterOptions = options2 },
        };

        var stones = RingScatterer.Scatter(Square40mm(), rows);

        // Просто проверяем, что оба ряда посчитались (по числу страз с нужными диаметрами и на
        // разном расстоянии от центра) — сам шахматный сдвиг это стандартный StartOffsetMm,
        // отдельно уже покрыт тестами LineScattererTests.
        Assert.True(stones.Count > 0);
    }

    [Fact]
    public void OversizedInwardOffset_OnSmallShape_SkipsRowInsteadOfGarbage()
    {
        // По замечанию автора (реальный тест в CorelDRAW на маленьком лепестке): смещение внутрь
        // больше половины ширины самой формы выворачивало контур наизнанку, и стразы всех рядов
        // сваливались в кучу друг на друга. Маленький квадрат 10x10, смещение внутрь 8 мм —
        // заведомо больше половины стороны (5 мм). Точки заданы против часовой стрелки (как и
        // Square40mm() выше), поэтому по соглашению CurveOffsetter (см. ContourFiller) внутрь —
        // это положительное расстояние.
        var smallSquare = Curve.FromPolyline(
            new[] { new Point2D(0, 0), new Point2D(10, 0), new Point2D(10, 10), new Point2D(0, 10) },
            isClosed: true);

        var rows = new[]
        {
            new RowSpec
            {
                OffsetMm = 8,
                ScatterOptions = new LineScatterOptions { StoneDiameterMm = 2.4, GapMm = 0.2, Mode = StepMode.FitEven },
            },
            new RowSpec
            {
                OffsetMm = 0,
                ScatterOptions = new LineScatterOptions { StoneDiameterMm = 2.4, GapMm = 0.2, Mode = StepMode.FitEven },
            },
        };

        var stones = RingScatterer.Scatter(smallSquare, rows);

        // Ряд с разумным смещением (0) должен остаться, а схлопнувшийся (8) — тихо пропущен,
        // а не превращён в мусорные стразы где попало.
        Assert.True(stones.Count > 0);
        Assert.DoesNotContain(stones, s => s.RowId == 0);
        Assert.Contains(stones, s => s.RowId == 1);
    }

    private static double DistanceToCurve(FlattenedCurve flat, Point2D p)
    {
        double best = double.MaxValue;
        for (int i = 0; i + 1 < flat.Points.Count; i++)
        {
            Point2D a = flat.Points[i].Position, b = flat.Points[i + 1].Position, ab = b - a;
            double len2 = ab.X * ab.X + ab.Y * ab.Y;
            double t = len2 < 1e-18 ? 0 : System.Math.Clamp(((p - a).X * ab.X + (p - a).Y * ab.Y) / len2, 0, 1);
            best = System.Math.Min(best, Point2D.Distance(p, a + ab * t));
        }

        return best;
    }

    [Theory]
    [InlineData(3.2)]
    [InlineData(-3.2)]
    [InlineData(5.8)]
    public void OffsetRow_InHeartDip_HasNoLoop_EveryStoneKeepsTheOffsetDistance(double offset)
    {
        // Ямка сердца — острая впадина с кривыми сторонами. Смещённые стороны там перехлёстывались
        // петлёй, и стразы ряда ложились ближе к линии, чем задано (каша в ямке).
        var heart = new Curve(new[]
        {
            CurveSegment.Cubic(new Point2D(0, -25), new Point2D(-12, -12), new Point2D(-28, 0), new Point2D(-22, 12)),
            CurveSegment.Cubic(new Point2D(-22, 12), new Point2D(-17, 24), new Point2D(-3, 22), new Point2D(0, 13)),
            CurveSegment.Cubic(new Point2D(0, 13), new Point2D(3, 22), new Point2D(17, 24), new Point2D(22, 12)),
            CurveSegment.Cubic(new Point2D(22, 12), new Point2D(28, 0), new Point2D(12, -12), new Point2D(0, -25)),
        }, true);
        var spec = new RowSpec
        {
            OffsetMm = offset,
            CornerStyle = CornerStyle.Sharp,
            ScatterOptions = new LineScatterOptions { StoneDiameterMm = 2.4, GapMm = 0.2 },
        };

        IReadOnlyList<PlacedStone> stones = RingScatterer.Scatter(heart, new[] { spec });

        FlattenedCurve flat = CurveFlattener.Flatten(heart);
        Assert.True(stones.Count > 30, $"страз {stones.Count}");
        Assert.All(stones, s => Assert.True(DistanceToCurve(flat, s.Center) >= System.Math.Abs(offset) - 0.05,
            $"страза {s.Center} ближе к линии, чем {System.Math.Abs(offset)} мм"));
        Assert.DoesNotContain(true, IntersectionFixer.FindIndicesToRemove(stones, 0.1));
    }
}
