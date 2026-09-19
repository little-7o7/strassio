using System.IO;
using Strassio.Core.Settings;
using Strassio.Core.Stones;

namespace Strassio.Core.Tests.Settings;

/// <summary>Настройки и таблица камней в %APPDATA%\Strassio (docs/SPEC.md, разделы 3.2 и 12).</summary>
public class SettingsStoreTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "strassio-settings-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void NoFiles_GivesDefaults()
    {
        var store = new SettingsStore(dir);

        PluginSettings s = store.LoadSettings();

        Assert.Equal("ru", s.Language);
        Assert.Equal(LengthUnit.Millimeter, s.Units);
        Assert.Equal(ThemeMode.Auto, s.Theme);
        Assert.Equal(DockerLayout.Vertical, s.Layout);
        Assert.Equal(2, s.Decimals);
        Assert.True(s.CheckUpdates);

        StoneTable table = store.LoadStones();
        Assert.NotEmpty(table.Sets);
        Assert.NotEmpty(table.Sets[0].Sizes);
        Assert.All(table.Sets[0].Sizes, size => Assert.NotEmpty(size.Colors));
    }

    [Fact]
    public void SaveThenLoad_KeepsEverything()
    {
        var store = new SettingsStore(dir);
        var s = new PluginSettings
        {
            Language = "en", Units = LengthUnit.Inch, Theme = ThemeMode.Dark, Layout = DockerLayout.Horizontal,
            Decimals = 3, DefaultSize = "ss10", DefaultColor = "Золото", Outline = OutlineStyle.Width, OutlineWidthMm = 0.05,
            CheckUpdates = false,
        };

        store.SaveSettings(s);
        PluginSettings back = new SettingsStore(dir).LoadSettings();

        Assert.Equal("en", back.Language);
        Assert.Equal(LengthUnit.Inch, back.Units);
        Assert.Equal(ThemeMode.Dark, back.Theme);
        Assert.Equal(DockerLayout.Horizontal, back.Layout);
        Assert.Equal(3, back.Decimals);
        Assert.Equal("ss10", back.DefaultSize);
        Assert.Equal("Золото", back.DefaultColor);
        Assert.Equal(OutlineStyle.Width, back.Outline);
        Assert.Equal(0.05, back.OutlineWidthMm);
        Assert.False(back.CheckUpdates);
    }

    [Fact]
    public void OldFileWithoutNewFields_GetsDefaultsForThem()
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "settings.json"), "{\"language\":\"en\"}");

        PluginSettings s = new SettingsStore(dir).LoadSettings();

        Assert.Equal("en", s.Language);
        Assert.Equal(2, s.Decimals);
        Assert.Equal(LengthUnit.Millimeter, s.Units);
        Assert.True(s.CheckUpdates);
    }

    [Fact]
    public void BrokenStonesFile_IsKeptAsBackup_NotLost()
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "stones.json");
        File.WriteAllText(path, "{ это не json");

        StoneTable table = new SettingsStore(dir).LoadStones();

        Assert.NotEmpty(table.Sets);
        string[] backups = Directory.GetFiles(dir, "stones.json.broken-*");
        Assert.Single(backups);
        Assert.Equal("{ это не json", File.ReadAllText(backups[0]));
    }

    [Fact]
    public void Stones_SaveThenLoad()
    {
        var store = new SettingsStore(dir);
        StoneTable table = store.LoadStones();
        table.Sets[0].Sizes[0].Colors.Add(new StoneColor { Name = "Сапфир", Rgb = "#1E40AF" });

        store.SaveStones(table);
        StoneTable back = new SettingsStore(dir).LoadStones();

        Assert.Contains(back.Sets[0].Sizes[0].Colors, c => c.Name == "Сапфир" && c.Rgb == "#1E40AF");
    }
}
