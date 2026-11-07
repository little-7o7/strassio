using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Strassio.Core.Motifs
{
    /// <summary>
    /// Библиотека мотивов (docs/SPEC.md, раздел 9): каждый мотив — свой файл JSON в папке
    /// (в плагине — %APPDATA%\Strassio\motifs). Файлы можно копировать между компьютерами.
    /// </summary>
    public sealed class MotifLibrary
    {
        private const string Extension = ".json";

        public MotifLibrary(string folder)
        {
            Folder = folder;
        }

        public string Folder { get; }

        /// <summary>Все мотивы по имени (битые файлы пропускаются).</summary>
        public List<Motif> LoadAll()
        {
            var motifs = new List<Motif>();
            if (!Directory.Exists(Folder))
            {
                return motifs;
            }

            foreach (string file in Directory.GetFiles(Folder, "*" + Extension))
            {
                try
                {
                    Motif motif = Motif.FromJson(File.ReadAllText(file, Encoding.UTF8));
                    if (motif.Stones.Count > 0)
                    {
                        if (string.IsNullOrWhiteSpace(motif.Name))
                        {
                            motif.Name = Path.GetFileNameWithoutExtension(file);
                        }

                        motifs.Add(motif);
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Runtime.Serialization.SerializationException)
                {
                    // Чужой или испорченный файл — просто не показываем.
                }
            }

            return motifs.OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>Сохранить (мотив с тем же именем заменяется).</summary>
        public void Save(Motif motif)
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(PathFor(motif.Name), motif.ToJson(), new UTF8Encoding(false));
        }

        public bool Exists(string name) => File.Exists(PathFor(name));

        /// <summary>Готовые мотивы этой версии уже клали в папку (даже если пользователь потом какие-то удалил).</summary>
        public bool StarterInstalled(int version) => File.Exists(StarterMarker(version));

        /// <summary>
        /// Кладёт готовые мотивы, которых ещё нет (свои мотивы пользователя с тем же именем не трогает),
        /// и запоминает, что эта версия набора установлена. Возвращает, сколько добавлено.
        /// </summary>
        public int InstallStarter(IEnumerable<Motif> starter, int version)
        {
            Directory.CreateDirectory(Folder);
            int added = 0;
            foreach (Motif motif in starter)
            {
                if (!Exists(motif.Name))
                {
                    Save(motif);
                    added++;
                }
            }

            File.WriteAllText(StarterMarker(version), DateTime.UtcNow.ToString("o"), new UTF8Encoding(false));
            return added;
        }

        private string StarterMarker(int version) => Path.Combine(Folder, $".starter-{version}");

        public void Delete(string name)
        {
            string path = PathFor(name);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        /// <summary>Имя файла из имени мотива: запрещённые в Windows знаки заменяются на «_».</summary>
        public string PathFor(string name)
        {
            char[] bad = Path.GetInvalidFileNameChars().Concat(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' }).ToArray();
            string safe = new string((name ?? string.Empty).Trim().Select(c => bad.Contains(c) || char.IsControl(c) ? '_' : c).ToArray());
            if (safe.Length == 0)
            {
                safe = "motif";
            }

            return Path.Combine(Folder, safe + Extension);
        }
    }
}
