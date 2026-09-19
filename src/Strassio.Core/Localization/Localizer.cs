using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Strassio.Core.Localization
{
    /// <summary>Язык из папки lang: код (имя файла, например "ru") и название на самом этом языке.</summary>
    public sealed class LanguageInfo
    {
        public LanguageInfo(string code, string name)
        {
            Code = code;
            Name = name;
        }

        public string Code { get; }

        public string Name { get; }

        public override string ToString() => Name;
    }

    /// <summary>
    /// Все тексты интерфейса — из файлов lang/&lt;код&gt;.json (CLAUDE.md, правило 5). Файл — плоский
    /// JSON-объект «ключ → текст»; ключ "_language" — название языка для списка в настройках.
    /// Нет текста в выбранном языке — берётся английский, нет и там — «[ключ]», чтобы пропуск
    /// было видно сразу. Язык меняется на лету: WPF-привязки вида {Binding [ключ]} обновляются
    /// сами по событию PropertyChanged("Item[]") — перезапуск CorelDRAW не нужен.
    /// Запас: если файла языка в папке нет (например, установщик не положил папку lang), текст
    /// берётся из копии, встроенной в сборку (<c>builtIn</c>). Файл в папке всегда главнее — его
    /// можно поправить без пересборки.
    /// </summary>
    public sealed class Localizer : INotifyPropertyChanged
    {
        public const string FallbackLanguage = "en";

        private const string LanguageNameKey = "_language";

        private readonly string directory;
        private readonly Func<string, string?> builtIn;
        private readonly IReadOnlyList<string> builtInCodes;
        private Dictionary<string, string> texts = new Dictionary<string, string>();
        private Dictionary<string, string> fallback = new Dictionary<string, string>();

        public Localizer(
            string directory, string languageCode, Func<string, string?>? builtIn = null, IReadOnlyList<string>? builtInCodes = null)
        {
            this.directory = directory;
            this.builtIn = builtIn ?? (code => null);
            this.builtInCodes = builtInCodes ?? Array.Empty<string>();
            LanguageCode = FallbackLanguage;
            Load(languageCode);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string LanguageCode { get; private set; }

        public string this[string key]
        {
            get
            {
                if (texts.TryGetValue(key, out string? text) || fallback.TryGetValue(key, out text))
                {
                    return text;
                }

                return "[" + key + "]";
            }
        }

        /// <summary>Языки, для которых в папке есть файл; название — из ключа "_language" (или код).</summary>
        public IReadOnlyList<LanguageInfo> AvailableLanguages
        {
            get
            {
                var codes = new List<string>(builtInCodes);
                if (Directory.Exists(directory))
                {
                    foreach (string file in Directory.GetFiles(directory, "*.json"))
                    {
                        string code = Path.GetFileNameWithoutExtension(file);
                        if (!codes.Contains(code))
                        {
                            codes.Add(code);
                        }
                    }
                }

                var result = new List<LanguageInfo>();
                foreach (string code in codes)
                {
                    Dictionary<string, string>? dict = TryLoad(code);
                    if (dict != null)
                    {
                        result.Add(new LanguageInfo(code, dict.TryGetValue(LanguageNameKey, out string? name) ? name : code));
                    }
                }

                result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCulture));
                return result;
            }
        }

        public string Format(string key, params object[] args) =>
            string.Format(CultureInfo.CurrentCulture, this[key], args);

        public void SetLanguage(string languageCode)
        {
            Load(languageCode);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LanguageCode)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        }

        /// <summary>Разбирает плоский JSON-объект «ключ → текст» встроенным сериализатором (CLAUDE.md, правило 7).</summary>
        public static Dictionary<string, string> Parse(string json)
        {
            var serializer = new DataContractJsonSerializer(
                typeof(Dictionary<string, string>),
                new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            return (Dictionary<string, string>?)serializer.ReadObject(stream) ?? new Dictionary<string, string>();
        }

        private void Load(string languageCode)
        {
            fallback = TryLoad(FallbackLanguage) ?? new Dictionary<string, string>();

            Dictionary<string, string>? chosen = TryLoad(languageCode);
            if (chosen != null)
            {
                texts = chosen;
                LanguageCode = languageCode;
            }
            else
            {
                texts = fallback;
                LanguageCode = FallbackLanguage;
            }
        }

        /// <summary>Тексты языка: файл из папки, иначе встроенная копия; null — такого языка нет.</summary>
        private Dictionary<string, string>? TryLoad(string code)
        {
            // Испорченный файл языка не должен ронять плагин — пробуем встроенную копию.
            try
            {
                string path = Path.Combine(directory, code + ".json");
                if (File.Exists(path))
                {
                    return Parse(File.ReadAllText(path, Encoding.UTF8));
                }
            }
            catch (Exception)
            {
            }

            try
            {
                string? json = builtIn(code);
                return json == null ? null : Parse(json);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
