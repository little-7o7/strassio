using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Strassio.Core.Stones;

namespace Strassio.Core.Settings
{
    /// <summary>
    /// Файл переноса (docs/SPEC.md, раздел 12, «Экспорт и импорт»): настройки и таблица камней в одном
    /// файле — перенести на другой компьютер или раздать сотрудникам одинаковые настройки.
    /// Метка "format" отличает наш файл от любого другого JSON.
    /// </summary>
    [DataContract]
    public sealed class SettingsBundle
    {
        public const string FormatName = "strassio-settings";

        public const int CurrentVersion = 1;

        [DataMember(Name = "format", Order = 0)]
        public string Format { get; set; } = FormatName;

        [DataMember(Name = "version", Order = 1)]
        public int Version { get; set; } = CurrentVersion;

        [DataMember(Name = "settings", Order = 2)]
        public PluginSettings? Settings { get; set; }

        [DataMember(Name = "stones", Order = 3)]
        public StoneTable? Stones { get; set; }
    }

    /// <summary>Экспорт и импорт файла переноса. Ошибки — ключами текстов в файлах языков.</summary>
    public static class SettingsTransfer
    {
        /// <summary>Расширение файла переноса.</summary>
        public const string Extension = ".strassio";

        public static void Export(string path, PluginSettings settings, StoneTable stones)
        {
            var bundle = new SettingsBundle { Settings = settings, Stones = stones };

            using var stream = new MemoryStream();
            using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, new UTF8Encoding(false), false, true, "  "))
            {
                new DataContractJsonSerializer(typeof(SettingsBundle)).WriteObject(writer, bundle);
                writer.Flush();
            }

            File.WriteAllBytes(path, stream.ToArray());
        }

        /// <summary>
        /// Читает файл переноса. false — файл не подходит, <paramref name="errorKey"/> — почему:
        /// не читается, это не файл Strassio, он из более новой версии плагина, или таблица камней
        /// в нём с ошибками (такую не загружаем, чтобы не испортить рабочую).
        /// </summary>
        public static bool TryImport(string path, out SettingsBundle? bundle, out string errorKey)
        {
            bundle = null;
            errorKey = string.Empty;

            SettingsBundle? read;
            try
            {
                using var stream = new MemoryStream(File.ReadAllBytes(path));
                read = (SettingsBundle?)new DataContractJsonSerializer(typeof(SettingsBundle)).ReadObject(stream);
            }
            catch (Exception)
            {
                errorKey = "transfer.error.unreadable";
                return false;
            }

            if (read == null || read.Format != SettingsBundle.FormatName || read.Settings == null || read.Stones == null)
            {
                errorKey = "transfer.error.notStrassio";
                return false;
            }

            if (read.Version > SettingsBundle.CurrentVersion)
            {
                errorKey = "transfer.error.newerVersion";
                return false;
            }

            if (read.Stones.Sets == null || read.Stones.Sets.Count == 0 || StoneTableRules.Validate(read.Stones.Sets[0]).Count > 0)
            {
                errorKey = "transfer.error.badStones";
                return false;
            }

            bundle = read;
            return true;
        }
    }
}
