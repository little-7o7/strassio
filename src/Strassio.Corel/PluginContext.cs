#nullable enable
using System;
using System.IO;
using Strassio.Core.Localization;
using Strassio.Core.Settings;
using Strassio.Core.Stones;

namespace Strassio.Corel
{
    /// <summary>
    /// Общее для всего аддона: язык, настройки, таблица камней. Один экземпляр на весь сеанс
    /// CorelDRAW — докер могут закрывать и открывать снова, а язык и настройки должны сохраняться.
    /// </summary>
    internal sealed class PluginContext
    {
        private static readonly Lazy<PluginContext> Lazy = new Lazy<PluginContext>(() => new PluginContext());

        private PluginContext()
        {
            Store = new SettingsStore(SettingsStore.DefaultDirectory);
            Settings = Store.LoadSettings();

            string addonDir = Path.GetDirectoryName(typeof(PluginContext).Assembly.Location) ?? string.Empty;
            Localizer = new Localizer(Path.Combine(addonDir, "lang"), Settings.Language, BuiltInLanguage, BuiltInLanguages);

            bool firstRun = !File.Exists(Store.StonesPath);
            Stones = Store.LoadStones(key => Localizer[key]);
            bool repaired = DefaultStones.RepairUntranslatedNames(Stones, key => Localizer[key]);
            if (firstRun || repaired)
            {
                // Сразу кладём стартовую таблицу в файл — пользователь увидит, где она и как устроена.
                TrySave(() => Store.SaveStones(Stones));
            }
        }

        /// <summary>Настройки изменились (язык, единицы…) — докер перерисовывает то, что строит сам.</summary>
        public event EventHandler? SettingsChanged;

        public static PluginContext Instance => Lazy.Value;

        /// <summary>Языки, встроенные в Strassio.Corel.dll (запас на случай, если папки lang рядом нет).</summary>
        private static readonly string[] BuiltInLanguages = { "ru", "en" };

        public SettingsStore Store { get; }

        public PluginSettings Settings { get; private set; }

        public Localizer Localizer { get; }

        public StoneTable Stones { get; private set; }

        public void SaveSettings()
        {
            TrySave(() => Store.SaveSettings(Settings));
            if (Localizer.LanguageCode != Settings.Language)
            {
                Localizer.SetLanguage(Settings.Language);
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Настройки и таблица камней из файла переноса (раздел 12 ТЗ): заменить свои, сохранить оба
        /// файла, сменить язык и перерисовать докер — как будто пользователь всё выставил руками.
        /// </summary>
        public void ApplyImport(SettingsBundle bundle)
        {
            Settings = bundle.Settings!;
            Stones = bundle.Stones!;
            TrySave(() => Store.SaveStones(Stones));
            SaveSettings();
        }

        /// <summary>Новая таблица камней из редактора: сохранить в stones.json и перерисовать докер.</summary>
        public void ReplaceStones(StoneTable table)
        {
            Stones = table;
            TrySave(() => Store.SaveStones(Stones));
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Сохранить без перерисовки докера — например, запомнить последний выбранный камень.</summary>
        public void SaveSettingsQuietly() => TrySave(() => Store.SaveSettings(Settings));

        private static string? BuiltInLanguage(string code)
        {
            using Stream? stream = typeof(PluginContext).Assembly.GetManifestResourceStream("Strassio.lang." + code + ".json");
            if (stream == null)
            {
                return null;
            }

            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
            return reader.ReadToEnd();
        }

        private static void TrySave(Action save)
        {
            try
            {
                save();
            }
            catch (Exception)
            {
                // Нет доступа к %APPDATA% — работаем дальше с тем, что в памяти; файл запишется в следующий раз.
            }
        }
    }
}
