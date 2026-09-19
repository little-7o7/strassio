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
            Localizer = new Localizer(Path.Combine(addonDir, "lang"), Settings.Language);

            bool firstRun = !File.Exists(Store.StonesPath);
            Stones = Store.LoadStones(key => Localizer[key]);
            if (firstRun)
            {
                // Сразу кладём стартовую таблицу в файл — пользователь увидит, где она и как устроена.
                TrySave(() => Store.SaveStones(Stones));
            }
        }

        /// <summary>Настройки изменились (язык, единицы…) — докер перерисовывает то, что строит сам.</summary>
        public event EventHandler? SettingsChanged;

        public static PluginContext Instance => Lazy.Value;

        public SettingsStore Store { get; }

        public PluginSettings Settings { get; }

        public Localizer Localizer { get; }

        public StoneTable Stones { get; }

        public void SaveSettings()
        {
            TrySave(() => Store.SaveSettings(Settings));
            if (Localizer.LanguageCode != Settings.Language)
            {
                Localizer.SetLanguage(Settings.Language);
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Сохранить без перерисовки докера — например, запомнить последний выбранный камень.</summary>
        public void SaveSettingsQuietly() => TrySave(() => Store.SaveSettings(Settings));

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
