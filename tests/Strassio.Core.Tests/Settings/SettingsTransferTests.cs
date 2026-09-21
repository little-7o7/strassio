using System.IO;
using Strassio.Core.Methods;
using Strassio.Core.Settings;
using Strassio.Core.Stones;

namespace Strassio.Core.Tests.Settings;

/// <summary>Экспорт и импорт настроек и таблицы камней (docs/SPEC.md, раздел 12).</summary>
public class SettingsTransferTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "strassio-transfer-" + Guid.NewGuid().ToString("N"));

    public SettingsTransferTests() => Directory.CreateDirectory(dir);

    public void Dispose() => Directory.Delete(dir, recursive: true);

    private string File(string name) => Path.Combine(dir, name);

    private static StoneTable Stones() => DefaultStones.Create(key => key.Substring(key.LastIndexOf('.') + 1));

    [Fact]
    public void ExportThenImport_KeepsEverything()
    {
        var settings = new PluginSettings
        {
            Language = "en", Units = LengthUnit.Inch, Theme = ThemeMode.Dark, Decimals = 3,
            DefaultSize = "ss8", Outline = OutlineStyle.Hairline, LastMethod = "f4",
        };
        settings.Method.RowCount = 7;
        settings.Method.CenterPattern = MethodChoices.PatternSquare;
        StoneTable stones = Stones();
        stones.Sets[0].Sizes[0].Name = "SS5-мой";

        string path = File("мои" + SettingsTransfer.Extension);
        SettingsTransfer.Export(path, settings, stones);

        Assert.True(SettingsTransfer.TryImport(path, out SettingsBundle? bundle, out string error), error);
        Assert.Equal("en", bundle!.Settings!.Language);
        Assert.Equal(LengthUnit.Inch, bundle.Settings.Units);
        Assert.Equal(ThemeMode.Dark, bundle.Settings.Theme);
        Assert.Equal(OutlineStyle.Hairline, bundle.Settings.Outline);
        Assert.Equal("f4", bundle.Settings.LastMethod);
        Assert.Equal(7, bundle.Settings.Method.RowCount);
        Assert.Equal(MethodChoices.PatternSquare, bundle.Settings.Method.CenterPattern);
        Assert.Equal("SS5-мой", bundle.Stones!.Sets[0].Sizes[0].Name);
        Assert.Equal(stones.Sets[0].Sizes.Count, bundle.Stones.Sets[0].Sizes.Count);
    }

    [Fact]
    public void NotJson_IsUnreadable()
    {
        System.IO.File.WriteAllText(File("x.strassio"), "это не json");
        Assert.False(SettingsTransfer.TryImport(File("x.strassio"), out _, out string error));
        Assert.Equal("transfer.error.unreadable", error);
    }

    [Fact]
    public void MissingFile_IsUnreadable()
    {
        Assert.False(SettingsTransfer.TryImport(File("нет.strassio"), out _, out string error));
        Assert.Equal("transfer.error.unreadable", error);
    }

    [Fact]
    public void OtherJson_IsNotStrassio()
    {
        System.IO.File.WriteAllText(File("x.json"), "{\"language\":\"ru\"}");
        Assert.False(SettingsTransfer.TryImport(File("x.json"), out _, out string error));
        Assert.Equal("transfer.error.notStrassio", error);
    }

    [Fact]
    public void NewerVersion_IsRejected()
    {
        string path = File("new.strassio");
        SettingsTransfer.Export(path, new PluginSettings(), Stones());
        System.IO.File.WriteAllText(path, System.IO.File.ReadAllText(path).Replace("\"version\": 1", "\"version\": 99"));

        Assert.False(SettingsTransfer.TryImport(path, out _, out string error));
        Assert.Equal("transfer.error.newerVersion", error);
    }

    [Fact]
    public void BrokenStoneTable_IsRejected()
    {
        StoneTable stones = Stones();
        stones.Sets[0].Sizes[0].DiameterMm = 0;
        string path = File("bad.strassio");
        SettingsTransfer.Export(path, new PluginSettings(), stones);

        Assert.False(SettingsTransfer.TryImport(path, out _, out string error));
        Assert.Equal("transfer.error.badStones", error);
    }
}
