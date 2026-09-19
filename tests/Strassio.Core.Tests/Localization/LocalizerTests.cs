using System.IO;
using System.Linq;
using Strassio.Core.Localization;

namespace Strassio.Core.Tests.Localization;

/// <summary>Языки интерфейса: lang/ru.json, lang/en.json (CLAUDE.md, правило 5; docs/SPEC.md, раздел 12).</summary>
public class LocalizerTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "strassio-lang-" + Guid.NewGuid().ToString("N"));

    public LocalizerTests()
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "en.json"), "{ \"_language\": \"English\", \"hello\": \"Hello\", \"only.en\": \"Only in English\", \"stones\": \"{0} stones\" }");
        File.WriteAllText(Path.Combine(dir, "ru.json"), "{ \"_language\": \"Русский\", \"hello\": \"Привет\", \"stones\": \"Страз: {0}\" }");
    }

    public void Dispose() => Directory.Delete(dir, recursive: true);

    [Fact]
    public void ReturnsTextOfChosenLanguage()
    {
        var loc = new Localizer(dir, "ru");

        Assert.Equal("ru", loc.LanguageCode);
        Assert.Equal("Привет", loc["hello"]);
        Assert.Equal("Страз: 5", loc.Format("stones", 5));
    }

    [Fact]
    public void MissingKey_FallsBackToEnglish_ThenToKeyInBrackets()
    {
        var loc = new Localizer(dir, "ru");

        Assert.Equal("Only in English", loc["only.en"]);
        Assert.Equal("[no.such.key]", loc["no.such.key"]);
    }

    [Fact]
    public void UnknownLanguage_UsesEnglish()
    {
        var loc = new Localizer(dir, "de");

        Assert.Equal("en", loc.LanguageCode);
        Assert.Equal("Hello", loc["hello"]);
    }

    [Fact]
    public void SetLanguage_SwitchesTextsAndNotifiesInterface()
    {
        var loc = new Localizer(dir, "en");
        var changed = new List<string?>();
        loc.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        loc.SetLanguage("ru");

        Assert.Equal("Привет", loc["hello"]);
        Assert.Contains("Item[]", changed); // так WPF обновляет все привязки вида {Binding [ключ]}
    }

    [Fact]
    public void AvailableLanguages_ListsFilesWithTheirOwnNames()
    {
        var loc = new Localizer(dir, "en");

        var langs = loc.AvailableLanguages.ToDictionary(l => l.Code, l => l.Name);

        Assert.Equal("English", langs["en"]);
        Assert.Equal("Русский", langs["ru"]);
    }

    [Fact]
    public void RealLanguageFiles_HaveSameKeys_AndNoEmptyTexts()
    {
        string langDir = Path.Combine(RepoRoot(), "lang");
        var ru = Localizer.Parse(File.ReadAllText(Path.Combine(langDir, "ru.json")));
        var en = Localizer.Parse(File.ReadAllText(Path.Combine(langDir, "en.json")));

        Assert.Empty(ru.Keys.Except(en.Keys));
        Assert.Empty(en.Keys.Except(ru.Keys));
        Assert.DoesNotContain(ru, kv => string.IsNullOrWhiteSpace(kv.Value));
        Assert.DoesNotContain(en, kv => string.IsNullOrWhiteSpace(kv.Value));
    }

    private static string RepoRoot()
    {
        string? d = AppContext.BaseDirectory;
        while (d != null && !File.Exists(Path.Combine(d, "Strassio.sln")))
        {
            d = Path.GetDirectoryName(d);
        }

        return d ?? throw new InvalidOperationException("Не найден корень репозитория (Strassio.sln).");
    }
}
