using System.IO;
using Strassio.Core.Methods;
using Strassio.Core.Settings;
using Strassio.Core.Stones;

namespace Strassio.Core.Tests.Settings;

/// <summary>Пресеты, «Недавние» и наборы таблиц камней (docs/SPEC.md, разделы 3.1, 8, 12).</summary>
public class PresetsTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "strassio-presets-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static Preset P(string name, string method = "l2", int rows = 3) => new()
    {
        Name = name, Method = method, Size = "ss6", Color = "Красный", Parameters = new MethodParameters { RowCount = rows },
    };

    [Fact]
    public void SaveListDelete()
    {
        var store = new PresetStore(dir);
        Assert.Empty(store.List());

        store.Save(P("Стебель тонкий", "l1"));
        store.Save(P("Лепесток: плотный?", "f3")); // запрещённые в имени файла символы
        store.Save(P("Стебель тонкий", "l4")); // то же имя — замена

        List<Preset> list = store.List();
        Assert.Equal(new[] { "Лепесток: плотный?", "Стебель тонкий" }, list.Select(p => p.Name));
        Assert.Equal("l4", list[1].Method);
        Assert.Equal(3, list[1].Parameters.RowCount);

        store.Delete("Лепесток: плотный?");
        Assert.Single(store.List());
    }

    [Fact]
    public void BrokenFile_IsSkipped()
    {
        var store = new PresetStore(dir);
        store.Save(P("Хороший"));
        File.WriteAllText(Path.Combine(dir, "плохой.json"), "{ не json");
        Assert.Equal("Хороший", Assert.Single(store.List()).Name);
    }

    [Fact]
    public void Recent_NoRepeats_KeepsLimit()
    {
        var recent = new List<Preset>();
        for (int i = 0; i < 8; i++)
        {
            PresetStore.Remember(recent, P(string.Empty, rows: i));
        }

        Assert.Equal(PresetStore.RecentLimit, recent.Count);
        Assert.Equal(7, recent[0].Parameters.RowCount);

        PresetStore.Remember(recent, P(string.Empty, rows: 5));
        Assert.Equal(5, recent[0].Parameters.RowCount);
        Assert.Single(recent, r => r.Parameters.RowCount == 5);
    }

    [Fact]
    public void Recent_SavedWithSettings()
    {
        var settingsStore = new SettingsStore(dir);
        var s = new PluginSettings { ActiveSet = "Поставщик 2" };
        PresetStore.Remember(s.Recent, P(string.Empty, "f10"));
        settingsStore.SaveSettings(s);

        PluginSettings loaded = settingsStore.LoadSettings();
        Assert.Equal("Поставщик 2", loaded.ActiveSet);
        Assert.Equal("f10", Assert.Single(loaded.Recent).Method);
    }

    [Fact]
    public void Sets_AddCopy_FindByName_Validate()
    {
        StoneTable table = DefaultStones.Create(k => k.Substring(k.LastIndexOf('.') + 1));
        StoneSet first = table.Sets[0];
        StoneSet second = StoneTableRules.AddSet(table, first, first.Name);

        Assert.Equal(first.Name + " 2", second.Name);
        Assert.Equal(first.Sizes.Count, second.Sizes.Count);
        Assert.NotSame(first.Sizes[0], second.Sizes[0]);
        Assert.Same(second, StoneTableRules.FindSet(table, first.Name.ToUpperInvariant() + " 2"));
        Assert.Same(first, StoneTableRules.FindSet(table, "нет такого"));
        Assert.Empty(StoneTableRules.Validate(table));

        second.Name = first.Name;
        Assert.Contains(StoneTableRules.Validate(table), p => p.Key == "stones.error.setDuplicate");
        second.Name = " ";
        Assert.Contains(StoneTableRules.Validate(table), p => p.Key == "stones.error.setName");
    }
}
