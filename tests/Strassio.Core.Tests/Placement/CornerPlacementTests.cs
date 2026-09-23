using System;
using System.Collections.Generic;
using System.Linq;
using Strassio.Core.Geometry;
using Strassio.Core.Placement;

namespace Strassio.Core.Tests.Placement;

/// <summary>
/// Вид угла (docs/SPEC.md, разделы 4 и 6.1): страза точно в вершине, скругление или смешанно.
/// Камень — ss6 (2,4 мм), зазор 0 — как на скриншотах автора.
/// </summary>
public class CornerPlacementTests
{
    private const double Diameter = 2.4;
    private const double ArmMm = 14;

    /// <summary>Уголок с заданным углом между сторонами: вершина в начале координат, стороны вверх.</summary>
    private static Curve Corner(double interiorAngleDeg)
    {
        double half = interiorAngleDeg / 2 * Math.PI / 180;
        var vertex = new Point2D(0, 0);
        var a = new Point2D(-Math.Sin(half) * ArmMm, -Math.Cos(half) * ArmMm);
        var b = new Point2D(Math.Sin(half) * ArmMm, -Math.Cos(half) * ArmMm);
        return Curve.FromPolyline(new[] { a, vertex, b });
    }

    private static LineScatterOptions Options(CornerPlacement style) => new LineScatterOptions
    {
        StoneDiameterMm = Diameter,
        GapMm = 0,
        Mode = StepMode.FitEven,
        CornerAngleThresholdDeg = 30,
        CornerPlacement = style,
    };

    private static bool HasStoneAtVertex(IReadOnlyList<PlacedStone> stones) =>
        stones.Any(s => Point2D.Distance(s.Center, Point2D.Zero) < 1e-6);

    /// <summary>Самая тесная пара соседних страз — по ней видно, налезают ли камни друг на друга.</summary>
    private static double ClosestPair(IReadOnlyList<PlacedStone> stones)
    {
        double closest = double.MaxValue;
        for (int i = 0; i < stones.Count; i++)
        {
            for (int j = i + 1; j < stones.Count; j++)
            {
                closest = Math.Min(closest, Point2D.Distance(stones[i].Center, stones[j].Center));
            }
        }

        return closest;
    }

    [Fact]
    public void Mixed_ObtuseCorner_KeepsStoneExactlyAtVertex()
    {
        // 90° тупее порога 60° — прежнее поведение: страза точно в вершине (раздел 4 ТЗ).
        var stones = LineScatterer.Scatter(Corner(90), Options(CornerPlacement.Mixed));

        Assert.True(HasStoneAtVertex(stones), "в прямом угле страза должна стоять точно в вершине");
    }

    [Fact]
    public void Mixed_VerySharpCorner_RoundsTipInsteadOfStoneAtVertex()
    {
        // 36° (кончик звезды) острее порога 60° — вершина обходится двумя стразами.
        var stones = LineScatterer.Scatter(Corner(36), Options(CornerPlacement.Mixed));

        Assert.False(HasStoneAtVertex(stones), "на кончике звезды стразы в самой вершине быть не должно");
    }

    [Fact]
    public void Mixed_VerySharpCorner_TwoStonesAroundTipTouchWithoutOverlap()
    {
        // Главное ради чего скругление: у вершины ряд остаётся плотным и без наложений.
        var stones = LineScatterer.Scatter(Corner(36), Options(CornerPlacement.Mixed));

        double closest = ClosestPair(stones);
        Assert.True(closest >= Diameter - 1e-6, $"стразы налезают: {closest:0.000} мм между центрами");
        Assert.True(closest <= Diameter + 0.05, $"у вершины остался просвет: {closest - Diameter:0.000} мм");
    }

    [Fact]
    public void Mixed_ExtremelySharpCorner_StaysSharpSoTheTipIsNotCutAway()
    {
        // 15° (клюв сердца, тонкий шип): чтобы две стразы разошлись, отступить пришлось бы почти
        // на 10 мм — от кончика ничего не осталось бы. Такой угол оставляем острым.
        var stones = LineScatterer.Scatter(Corner(15), Options(CornerPlacement.Mixed));

        Assert.True(HasStoneAtVertex(stones), "у очень тонкого кончика страза должна остаться в вершине");
    }

    [Fact]
    public void Sharp_KeepsStoneAtVertexEvenOnVerySharpCorner()
    {
        var stones = LineScatterer.Scatter(Corner(36), Options(CornerPlacement.Sharp));

        Assert.True(HasStoneAtVertex(stones), "при выборе «острый» страза остаётся в вершине");
    }

    [Fact]
    public void Round_RoundsEvenObtuseCorner()
    {
        var stones = LineScatterer.Scatter(Corner(90), Options(CornerPlacement.Round));

        Assert.False(HasStoneAtVertex(stones), "при выборе «круглый» скругляется и прямой угол");
    }

    [Fact]
    public void Round_ShortArms_FallsBackToSharpCornerInsteadOfEatingTheSide()
    {
        // Стороны по 2 мм: срез (≈1,7 мм при 90°) съел бы почти всю сторону — оставляем острый угол.
        var curve = Curve.FromPolyline(new[] { new Point2D(-2, 0), new Point2D(0, 0), new Point2D(0, -2) });

        var stones = LineScatterer.Scatter(curve, Options(CornerPlacement.Round));

        Assert.True(HasStoneAtVertex(stones), "на коротких сторонах угол должен остаться острым");
    }

    [Fact]
    public void Mixed_ClosedStar_RoundsEveryTipAndKeepsRowDense()
    {
        var stones = LineScatterer.Scatter(Star(5, 14, 6), Options(CornerPlacement.Mixed));

        double closest = ClosestPair(stones);
        Assert.True(closest >= Diameter - 1e-6, $"на звезде стразы налезают: {closest:0.000} мм");
    }

    /// <summary>Замкнутая звезда: острые кончики снаружи, тупые впадины внутри.</summary>
    private static Curve Star(int points, double outerR, double innerR)
    {
        var pts = new List<Point2D>();
        for (int i = 0; i < points * 2; i++)
        {
            double angle = Math.PI / 2 + i * Math.PI / points;
            double r = i % 2 == 0 ? outerR : innerR;
            pts.Add(new Point2D(Math.Cos(angle) * r, -Math.Sin(angle) * r));
        }

        return Curve.FromPolyline(pts, isClosed: true);
    }
}
