using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using Strassio.Core.Stones;

namespace Strassio.Core.Settings
{
    /// <summary>
    /// Чтение и запись файлов плагина в папке пользователя (docs/SPEC.md, раздел 12):
    /// settings.json и stones.json. Нет файла — значения по умолчанию. Испорченный файл не
    /// удаляется молча: он переименовывается в «имя.broken-ДАТА», чтобы таблицу камней, которую
    /// пользователь заполнял руками, можно было восстановить.
    /// </summary>
    public sealed class SettingsStore
    {
        public SettingsStore(string directory)
        {
            Directory = directory;
        }

        /// <summary>
        /// Переменная окружения с другой папкой для файлов плагина. Нужна инструментам разработки
        /// (tools/Strassio.UiShots), чтобы не трогать настоящие настройки; в CorelDRAW не задаётся.
        /// </summary>
        public const string DirectoryVariable = "STRASSIO_SETTINGS_DIR";

        /// <summary>%APPDATA%\Strassio — обычное место файлов плагина (или папка из <see cref="DirectoryVariable"/>).</summary>
        public static string DefaultDirectory
        {
            get
            {
                string? overridden = Environment.GetEnvironmentVariable(DirectoryVariable);
                return !string.IsNullOrWhiteSpace(overridden)
                    ? overridden!
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Strassio");
            }
        }

        public string Directory { get; }

        public string SettingsPath => Path.Combine(Directory, "settings.json");

        public string StonesPath => Path.Combine(Directory, "stones.json");

        /// <summary>Папка пресетов (раздел 12): presets\*.json.</summary>
        public string PresetsDirectory => Path.Combine(Directory, "presets");

        public PluginSettings LoadSettings() => Load<PluginSettings>(SettingsPath) ?? new PluginSettings();

        public void SaveSettings(PluginSettings settings) => Save(SettingsPath, settings);

        /// <summary>
        /// Таблица камней. Если файла нет (первый запуск) или он испорчен — стартовая таблица;
        /// <paramref name="text"/> даёт названия стартовых цветов на языке интерфейса.
        /// </summary>
        public StoneTable LoadStones(Func<string, string>? text = null)
        {
            StoneTable? table = Load<StoneTable>(StonesPath);
            if (table == null || table.Sets == null || table.Sets.Count == 0)
            {
                return DefaultStones.Create(text ?? (key => key));
            }

            return table;
        }

        public void SaveStones(StoneTable table) => Save(StonesPath, table);

        private static T? Load<T>(string path)
            where T : class
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                var serializer = new DataContractJsonSerializer(typeof(T));
                using var stream = new MemoryStream(File.ReadAllBytes(path));
                return (T?)serializer.ReadObject(stream);
            }
            catch (Exception)
            {
                string backup = path + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Move(path, backup);
                return null;
            }
        }

        /// <summary>Пишет сначала во временный файл и только потом подменяет — сбой посреди записи не портит старый файл.</summary>
        private void Save<T>(string path, T value)
        {
            System.IO.Directory.CreateDirectory(Directory);

            byte[] bytes;
            using (var stream = new MemoryStream())
            {
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, new UTF8Encoding(false), false, true, "  "))
                {
                    new DataContractJsonSerializer(typeof(T)).WriteObject(writer, value);
                    writer.Flush();
                }

                bytes = stream.ToArray();
            }

            string temp = path + ".tmp";
            File.WriteAllBytes(temp, bytes);
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(temp, path);
        }
    }
}
