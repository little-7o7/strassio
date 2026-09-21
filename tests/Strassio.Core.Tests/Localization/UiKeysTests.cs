using System.IO;
using System.Text.RegularExpressions;
using Strassio.Core.Localization;

namespace Strassio.Core.Tests.Localization;

/// <summary>
/// Каждый ключ текста, который упоминается в аддоне (XAML-привязки {Binding [ключ]} и строки
/// "docker.…", "status.…" и т.п. в коде), есть в обоих файлах языков. Иначе в CorelDRAW вместо
/// текста появится "[ключ]" — а CorelDRAW в тестах не запустить.
/// </summary>
public class UiKeysTests
{
    private static readonly Regex XamlKey = new(@"\{Binding \[([A-Za-z0-9_.]+)\]\}");

    private static readonly Regex CodeKey = new(
        "\"((?:app|docker|settings|stones|status|param|method|unit|size|undo|transfer)\\.[A-Za-z0-9_.]+)\"");

    [Fact]
    public void EveryKeyUsedInAddon_ExistsInBothLanguages()
    {
        string root = FindRepoRoot();
        string addon = Path.Combine(root, "src", "Strassio.Corel");
        var used = new SortedSet<string>();

        foreach (string file in Directory.GetFiles(addon, "*.xaml", SearchOption.AllDirectories))
        {
            foreach (Match m in XamlKey.Matches(File.ReadAllText(file)))
            {
                used.Add(m.Groups[1].Value);
            }
        }

        foreach (string file in Directory.GetFiles(addon, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            {
                continue;
            }

            foreach (Match m in CodeKey.Matches(File.ReadAllText(file)))
            {
                string key = m.Groups[1].Value;

                // "method." + вид метода собирается в коде — такие ключи проверяет MethodRunnerTests.
                if (!key.EndsWith(".", StringComparison.Ordinal))
                {
                    used.Add(key);
                }
            }
        }

        Assert.NotEmpty(used);
        foreach (string lang in new[] { "ru.json", "en.json" })
        {
            IReadOnlyDictionary<string, string> texts = Localizer.Parse(File.ReadAllText(Path.Combine(root, "lang", lang)));
            string[] missing = used.Where(k => !texts.ContainsKey(k)).ToArray();
            Assert.True(missing.Length == 0, lang + ": нет ключей " + string.Join(", ", missing));
        }
    }

    private static string FindRepoRoot()
    {
        string? current = AppContext.BaseDirectory;
        while (current != null && !File.Exists(Path.Combine(current, "Strassio.sln")))
        {
            current = Path.GetDirectoryName(current);
        }

        return current ?? throw new InvalidOperationException("Не найден Strassio.sln");
    }
}
