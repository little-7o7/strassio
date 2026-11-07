using Strassio.Core.Stones;

namespace Strassio.Core.Tests.Stones;

/// <summary>Подсчёт камней для заказа: по размерам в порядке таблицы, по цветам, итог.</summary>
public class StoneCountTests
{
    private static readonly string[] Table = { "ss6", "ss10", "ss16" };

    private static IEnumerable<(string, string)> Many(string size, string color, int n) =>
        Enumerable.Repeat((size, color), n);

    [Fact]
    public void CountsBySizeAndColor_InTableOrder()
    {
        var stones = Many("ss16", "Золото", 3)
            .Concat(Many("ss6", "Кристалл", 10))
            .Concat(Many("ss6", "Цитрин", 25))
            .Concat(Many("ss10", "Кристалл", 7));

        StoneCount count = StoneCount.Of(stones, Table);

        Assert.Equal(45, count.Total);
        Assert.Equal(3, count.ColorCount);
        Assert.Equal(
            new[] { ("ss6", "Цитрин", 25), ("ss6", "Кристалл", 10), ("ss10", "Кристалл", 7), ("ss16", "Золото", 3) },
            count.Rows.Select(r => (r.Size, r.Color, r.Count)));
        Assert.Equal(new[] { 35, 7, 3 }, count.BySize.Select(p => p.Value));
    }

    [Fact]
    public void CaseDoesNotSplit_AndUnknownSizesGoLast()
    {
        var stones = Many("SS6", "кристалл", 2)
            .Concat(Many("ss6", "Кристалл", 3))
            .Concat(Many("ss8", "Красный", 1))
            .Concat(Many("", " ", 1));

        StoneCount count = StoneCount.Of(stones, Table);

        Assert.Equal(new[] { ("SS6", "кристалл", 5), ("?", "?", 1), ("ss8", "Красный", 1) }, count.Rows.Select(r => (r.Size, r.Color, r.Count)));
    }

    [Fact]
    public void Empty_IsZero()
    {
        StoneCount count = StoneCount.Of(Array.Empty<(string, string)>(), Table);

        Assert.Equal(0, count.Total);
        Assert.Empty(count.Rows);
    }

    [Fact]
    public void Table_PastesIntoExcel()
    {
        StoneCount count = StoneCount.Of(Many("ss6", "Кристалл", 1234).Concat(Many("ss10", "Золото", 5)), Table);

        string table = count.ToTable("Размер", "Цвет", "Штук", "Всего");

        Assert.Equal("Размер\tЦвет\tШтук\r\nss6\tКристалл\t1234\r\nss10\tЗолото\t5\r\nВсего\t\t1239\r\n", table);
    }

    [Fact]
    public void Times_ScalesEveryRow_AndRoundsUp()
    {
        StoneCount tile = StoneCount.Of(Many("ss6", "Кристалл", 10).Concat(Many("ss10", "Золото", 3)), Table);

        // 2,5 плитки на метр: 10 × 2,5 = 25 ровно, 3 × 2,5 = 7,5 → 8 (с запасом).
        StoneCount metre = tile.Times(2.5);

        Assert.Equal(new[] { ("ss6", "Кристалл", 25), ("ss10", "Золото", 8) }, metre.Rows.Select(r => (r.Size, r.Color, r.Count)));
        Assert.Equal(33, metre.Total);
    }

    [Fact]
    public void TenThousandStones_Fast()
    {
        var stones = Enumerable.Range(0, 10000).Select(i => (Table[i % 3], "Цвет " + (i % 7)));
        var watch = System.Diagnostics.Stopwatch.StartNew();

        StoneCount count = StoneCount.Of(stones, Table);

        Assert.Equal(10000, count.Total);
        Assert.True(watch.ElapsedMilliseconds < 200, $"{watch.ElapsedMilliseconds} мс");
    }
}
