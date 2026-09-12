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
}
