using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Strassio.Core.Methods;

namespace Strassio.Core.Settings
{
    /// <summary>
    /// Пресет (docs/SPEC.md, раздел 8, [P2]): метод, камень и параметры под именем — «Стебель тонкий»,
    /// «Лепесток плотный». Применяется в докере одним кликом. Файлы — %APPDATA%\Strassio\presets\*.json.
    /// </summary>
    [DataContract]
    public sealed class Preset
    {
        [DataMember(Name = "name", Order = 0)]
        public string Name { get; set; } = string.Empty;

        /// <summary>Метод: "l1", "f3"…</summary>
        [DataMember(Name = "method", Order = 1)]
        public string Method { get; set; } = string.Empty;

        [DataMember(Name = "size", Order = 2)]
        public string Size { get; set; } = string.Empty;

        [DataMember(Name = "color", Order = 3)]
        public string Color { get; set; } = string.Empty;

        [DataMember(Name = "params", Order = 4)]
        public MethodParameters Parameters { get; set; } = new MethodParameters();

        /// <summary>Одинаковые ли настройки (без имени) — чтобы «Недавние» не повторялись.</summary>
        public bool SameSettings(Preset other) =>
            string.Equals(Method, other.Method, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Size, other.Size, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Color, other.Color, StringComparison.OrdinalIgnoreCase) &&
            ToJson(Parameters) == ToJson(other.Parameters);

        internal static string ToJson<T>(T value)
        {
            using var stream = new MemoryStream();
            new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    /// <summary>Пресеты в папке: список, сохранить, удалить. Имя файла — имя пресета (без запрещённых символов).</summary>
    public sealed class PresetStore
    {
        /// <summary>Сколько последних настроек хранит «Недавние».</summary>
        public const int RecentLimit = 5;

        public PresetStore(string directory)
        {
            Directory = directory;
        }

        public string Directory { get; }

        public List<Preset> List()
        {
            var result = new List<Preset>();
            if (!System.IO.Directory.Exists(Directory))
            {
                return result;
            }

            foreach (string file in System.IO.Directory.GetFiles(Directory, "*.json"))
            {
                try
                {
                    using var stream = new MemoryStream(File.ReadAllBytes(file));
                    if (new DataContractJsonSerializer(typeof(Preset)).ReadObject(stream) is Preset preset && preset.Name.Length > 0)
                    {
                        result.Add(preset);
                    }
                }
                catch (Exception)
                {
                    // Испорченный файл пресета просто не показываем.
                }
            }

            return result.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>Сохраняет (пресет с тем же именем заменяется).</summary>
        public void Save(Preset preset)
        {
            if (string.IsNullOrWhiteSpace(preset.Name))
            {
                throw new ArgumentException("У пресета должно быть имя.", nameof(preset));
            }

            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(PathOf(preset.Name), Preset.ToJson(preset), new UTF8Encoding(false));
        }

        public void Delete(string name)
        {
            string path = PathOf(name);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        /// <summary>Файл пресета: имя без символов, запрещённых в именах файлов Windows.</summary>
        public string PathOf(string name)
        {
            char[] bad = Path.GetInvalidFileNameChars();
            string safe = new string(name.Trim().Select(c => bad.Contains(c) ? '_' : c).ToArray());
            return Path.Combine(Directory, safe + ".json");
        }

        /// <summary>Добавляет настройки в начало «Недавних»: без повторов, не больше <see cref="RecentLimit"/>.</summary>
        public static void Remember(List<Preset> recent, Preset used)
        {
            recent.RemoveAll(r => r.SameSettings(used));
            recent.Insert(0, used);
            if (recent.Count > RecentLimit)
            {
                recent.RemoveRange(RecentLimit, recent.Count - RecentLimit);
            }
        }
    }
}
