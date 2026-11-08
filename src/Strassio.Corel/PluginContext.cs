#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using Strassio.Core.Localization;
using Strassio.Core.Settings;
using Strassio.Core.Stones;
using Strassio.Licensing;

namespace Strassio.Corel
{
    /// <summary>
    /// Общее для всего аддона: язык, настройки, таблица камней. Один экземпляр на весь сеанс
    /// CorelDRAW — докер могут закрывать и открывать снова, а язык и настройки должны сохраняться.
    /// </summary>
    internal sealed class PluginContext
    {
        // PublicationOnly: сбой при запуске (файл занят) не запоминается — следующее открытие докера пробует снова.
        private static readonly Lazy<PluginContext> Lazy = new Lazy<PluginContext>(() => new PluginContext(), System.Threading.LazyThreadSafetyMode.PublicationOnly);

        private PluginContext()
        {
            Store = new SettingsStore(DataDirectory());
            Settings = Store.LoadSettings();

            string addonDir = Path.GetDirectoryName(typeof(PluginContext).Assembly.Location) ?? string.Empty;
            helperSource = Path.Combine(addonDir, "Strassio.Connect.exe");
            Localizer = new Localizer(Path.Combine(addonDir, "lang"), Settings.Language, BuiltInLanguage, BuiltInLanguages);

            EnableModernTls();
            // CorelDRAW часто закрыт брандмауэром — тогда связь идёт через Strassio.Connect.exe рядом с аддоном.
            Api = new HttpLicenseApi(LicenseKeys.Server, helperPath: Path.Combine(addonDir, "Strassio.Connect.exe"));
            License = new LicenseManager(
                Api, WmiHardwareSource.ReadCode, LicenseKeys.Key, Store.Directory, null, LicenseMarkStores())
            {
                PluginVersion = typeof(PluginContext).Assembly.GetName().Version?.ToString(3),
                DropFolders = LicenseDropFolders(Store.Directory, addonDir),
            };
            NoticeFinishedUpdate();

            bool firstRun = !File.Exists(Store.StonesPath);
            Stones = Store.LoadStones(key => Localizer[key]);
            bool repaired = DefaultStones.RepairUntranslatedNames(Stones, key => Localizer[key]);
            if (firstRun || repaired)
            {
                // Сразу кладём стартовую таблицу в файл — пользователь увидит, где она и как устроена.
                TrySave(() => Store.SaveStones(Stones));
            }
        }

        /// <summary>Связь с сервером лицензий (причина последней неудачи — <see cref="HttpLicenseApi.LastError"/>).</summary>
        public HttpLicenseApi Api { get; }

        /// <summary>Настройки изменились (язык, единицы…) — докер перерисовывает то, что строит сам.</summary>
        public event EventHandler? SettingsChanged;

        public static PluginContext Instance => Lazy.Value;

        /// <summary>Языки, встроенные в Strassio.Corel.dll (запас на случай, если папки lang рядом нет).</summary>
        private static readonly string[] BuiltInLanguages = { "ru", "en" };

        public SettingsStore Store { get; }

        public PluginSettings Settings { get; private set; }

        public Localizer Localizer { get; }

        public StoneTable Stones { get; private set; }

        /// <summary>Лицензия (docs/SPEC.md, раздел 13).</summary>
        public LicenseManager License { get; }

        /// <summary>
        /// Требуется ли лицензия для создания страз. С версии 0.7.1 — да
        /// (см. StrassioEnforceLicense в Strassio.Corel.csproj).
        /// </summary>
#if STRASSIO_ENFORCE_LICENSE
        public const bool LicenseEnforced = true;
#else
        public const bool LicenseEnforced = false;
#endif

        /// <summary>
        /// Можно создавать и править стразы. Спрашивается в нескольких местах докера (SPEC 13.2),
        /// а не один раз при запуске.
        /// </summary>
        public bool CanCreate => !LicenseEnforced || License.CanCreate || (PickUpLicenseFile() && License.CanCreate);

        private long lastLicenseFileScanTicks;

        /// <summary>
        /// Не положили ли в папку плагина (C:\Strassio) файл лицензии с сайта (раздел 13.3b) — если да, взять его.
        /// Без <paramref name="force"/> — не чаще раза в 3 секунды (зовётся и при нажатии кнопок в докере).
        /// </summary>
        public bool PickUpLicenseFile(bool force = false)
        {
            long nowTicks = DateTime.UtcNow.Ticks;
            if (!force && nowTicks - System.Threading.Interlocked.Read(ref lastLicenseFileScanTicks) < TimeSpan.FromSeconds(3).Ticks)
            {
                return false;
            }

            System.Threading.Interlocked.Exchange(ref lastLicenseFileScanTicks, nowTicks);
            try
            {
                return License.ImportDroppedFiles();
            }
            catch (Exception)
            {
                // Папка недоступна — не беда: файл можно загрузить кнопкой «Файл лицензии…».
                return false;
            }
        }

        private int backgroundCheckStarted;

        /// <summary>
        /// Один раз за сеанс CorelDRAW: код компьютера (WMI) и тихая сверка с сервером — в фоне, чтобы
        /// докер открывался сразу. Итог приходит событием License.Changed (не в потоке окна!).
        /// </summary>
        public void StartBackgroundCheck(string? corelVersion)
        {
            if (System.Threading.Interlocked.Exchange(ref backgroundCheckStarted, 1) == 1)
            {
                return;
            }

            License.CorelVersion = corelVersion;
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    _ = License.Computer;
                    PickUpLicenseFile(force: true);
                    License.NotifyChanged();
                    await License.CheckOnStartAsync();
                }
                catch (Exception)
                {
                    // Сверка — дело фоновое: любая беда здесь не должна мешать работе.
                }

                await CheckForUpdateAsync();
            });

            // CorelDRAW могут не закрывать неделями: раз в час смотрим, не пора ли сверка (пора — раз в сутки).
            dailyCheck = new System.Threading.Timer(_ => CheckIfDue(), null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
            WatchLicenseFolders();
        }

        /// <summary>Слежение за папками лицензии — держим ссылки, иначе сборщик мусора их остановит.</summary>
        private readonly System.Collections.Generic.List<FileSystemWatcher> licenseWatchers = new System.Collections.Generic.List<FileSystemWatcher>();

        private System.Threading.Timer? licenseFileTimer;

        /// <summary>
        /// Файл лицензии положили в папку, пока CorelDRAW открыт, — подхватываем сразу, без перезапуска.
        /// Ждём полторы секунды после последнего изменения: копирование могло ещё не закончиться.
        /// </summary>
        private void WatchLicenseFolders()
        {
            licenseFileTimer = new System.Threading.Timer(_ => PickUpLicenseFile(force: true), null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            foreach (string folder in License.DropFolders)
            {
                try
                {
                    if (!Directory.Exists(folder))
                    {
                        continue;
                    }

                    var watcher = new FileSystemWatcher(folder, "*.json")
                    {
                        IncludeSubdirectories = false,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    };
                    watcher.Created += (s, e) => LicenseFolderChanged();
                    watcher.Changed += (s, e) => LicenseFolderChanged();
                    watcher.Renamed += (s, e) => LicenseFolderChanged();
                    watcher.EnableRaisingEvents = true;
                    licenseWatchers.Add(watcher);
                }
                catch (Exception)
                {
                    // Слежение не включилось — файл всё равно найдётся: при открытии окна «Лицензия», при нажатии
                    // кнопки в докере и раз в час.
                }
            }
        }

        private void LicenseFolderChanged() => licenseFileTimer?.Change(1500, System.Threading.Timeout.Infinite);

        /// <summary>Держим ссылку, иначе сборщик мусора остановит таймер.</summary>
        private System.Threading.Timer? dailyCheck;

        private async void CheckIfDue()
        {
            if (DateTime.UtcNow - lastUpdateCheckUtc > TimeSpan.FromDays(1))
            {
                await CheckForUpdateAsync();
            }

            try
            {
                if (PickUpLicenseFile(force: true) | await License.CheckAsync())
                {
                    return;
                }

                // Сверки не было или нет связи — всё равно перечитать состояние: могли кончиться 14 дней
                // без интернета или пробный период, строка в докере должна это показать.
                License.NotifyChanged();
            }
            catch (Exception)
            {
                // См. StartBackgroundCheck.
            }
        }

        /// <summary>
        /// Версия новее установленной (SPEC 14.1), если сервер о ней сообщил; иначе null. Докер
        /// показывает строку «Доступна версия…» — событие <see cref="UpdateFound"/> приходит не из потока окна.
        /// </summary>
        public UpdateInfo? AvailableUpdate { get; private set; }

        public event EventHandler? UpdateFound;

        // ---- Автообновление (SPEC 14.1) ------------------------------------------------------

        /// <summary>Strassio.Connect.exe рядом с аддоном — его копия ведёт автообновление.</summary>
        private readonly string helperSource;

        /// <summary><c>%LOCALAPPDATA%\Strassio\updates</c>: установщик, файл состояния, копия программы обновления.</summary>
        public string UpdatesDirectory { get; } = AutoUpdate.DefaultDirectory();

        private string UpdateStatusPath => Path.Combine(UpdatesDirectory, AutoUpdate.StatusFileName);

        /// <summary>Этот запуск — первый после автообновления до этой версии (докер скажет «Strassio обновлён»).</summary>
        public string? JustUpdatedTo { get; private set; }

        /// <summary>Как идёт автообновление — по файлу состояния, который пишет программа обновления.</summary>
        public UpdateProgress ReadUpdateProgress(out UpdateStatus? status)
        {
            status = UpdateStatus.Read(UpdateStatusPath);
            return AutoUpdate.Describe(status, License.PluginVersion, IsHelperAlive);
        }

        /// <summary>Обновиться само можно, если сервер прислал SHA-256 установщика и программа обновления на месте.</summary>
        public bool CanAutoUpdate(UpdateInfo update) => AutoUpdate.IsSha256(update.Sha256) && File.Exists(helperSource);

        /// <summary>
        /// Запускает автообновление и сразу возвращается. Программа обновления работает из копии в
        /// папке обновлений: файл рядом с аддоном установщик будет перезаписывать.
        /// </summary>
        public void StartAutoUpdate(UpdateInfo update)
        {
            if (!CanAutoUpdate(update))
            {
                throw new InvalidOperationException("no sha256 or Strassio.Connect.exe");
            }

            string helperDir = Path.Combine(UpdatesDirectory, "helper");
            Directory.CreateDirectory(helperDir);
            string helper = Path.Combine(helperDir, "Strassio.Connect.exe");
            try
            {
                File.Copy(helperSource, helper, true);
                if (File.Exists(helperSource + ".config"))
                {
                    File.Copy(helperSource + ".config", helper + ".config", true);
                }
            }
            catch (IOException) when (File.Exists(helper))
            {
                // Копия занята — обновление уже идёт; вторая копия сама увидит это и выйдет.
            }

            // Прошлая неудача не должна висеть в докере, пока новая попытка не запишет своё.
            try
            {
                File.Delete(UpdateStatusPath);
            }
            catch (Exception)
            {
                // Не удалилось — программа обновления перезапишет.
            }

            string arguments = "UPDATE " + Quote(update.Url) + " " + Quote(update.Sha256!.Trim()) + " " + Quote(update.Version) + " " + Quote(UpdatesDirectory);
            var start = new ProcessStartInfo(helper, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = helperDir,
            };
            using (Process.Start(start))
            {
            }
        }

        /// <summary>После обновления: сказать «обновлён» и убрать скачанный установщик.</summary>
        private void NoticeFinishedUpdate()
        {
            try
            {
                if (ReadUpdateProgress(out UpdateStatus? status) != UpdateProgress.Updated || status == null)
                {
                    return;
                }

                JustUpdatedTo = status.Version;
                foreach (string file in Directory.GetFiles(UpdatesDirectory))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception)
                    {
                        // Не удалилось сейчас — удалится после следующего обновления.
                    }
                }
            }
            catch (Exception)
            {
                // Уборка — не условие работы.
            }
        }

        private static bool IsHelperAlive(int pid)
        {
            if (pid <= 0)
            {
                return false;
            }

            try
            {
                using (Process process = Process.GetProcessById(pid))
                {
                    return !process.HasExited && string.Equals(process.ProcessName, "Strassio.Connect", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Аргумент в кавычках. Кавычек внутри у ссылки, SHA-256, версии и пути не бывает — на всякий случай убираем.</summary>
        private static string Quote(string value) => "\"" + value.Replace("\"", string.Empty).TrimEnd('\\') + "\"";

        private DateTime lastUpdateCheckUtc = DateTime.MinValue;

        /// <summary>При запуске и раз в сутки, если в настройках не выключено «Проверять обновления».</summary>
        private async System.Threading.Tasks.Task CheckForUpdateAsync()
        {
            lastUpdateCheckUtc = DateTime.UtcNow;
            if (!Settings.CheckUpdates)
            {
                return;
            }

            try
            {
                UpdateInfo? found = await new UpdateChecker(Api, LicenseKeys.Key, License.PluginVersion).CheckAsync("stable");
                if (found != null && found.Version != AvailableUpdate?.Version)
                {
                    AvailableUpdate = found;
                    UpdateFound?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception)
            {
                // Обновления — подсказка, а не условие работы.
            }
        }

        /// <returns>Текст ошибки, если файл не записался (настройки действуют до закрытия CorelDRAW); null — записан.</returns>
        public string? SaveSettings()
        {
            string? error = TrySave(() => Store.SaveSettings(Settings));
            if (Localizer.LanguageCode != Settings.Language)
            {
                Localizer.SetLanguage(Settings.Language);
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
            return error;
        }

        /// <summary>
        /// Настройки и таблица камней из файла переноса (раздел 12 ТЗ): заменить свои, сохранить оба
        /// файла, сменить язык и перерисовать докер — как будто пользователь всё выставил руками.
        /// </summary>
        public string? ApplyImport(SettingsBundle bundle)
        {
            Settings = bundle.Settings!;
            Stones = bundle.Stones!;
            string? error = TrySave(() => Store.SaveStones(Stones));
            return SaveSettings() ?? error;
        }

        /// <summary>Новая таблица камней из редактора: сохранить в stones.json и перерисовать докер.</summary>
        public string? ReplaceStones(StoneTable table)
        {
            Stones = table;
            string? error = TrySave(() => Store.SaveStones(Stones));
            SettingsChanged?.Invoke(this, EventArgs.Empty);
            return error;
        }

        /// <summary>Сохранить без перерисовки докера — например, запомнить последний выбранный камень.</summary>
        public void SaveSettingsQuietly() => _ = TrySave(() => Store.SaveSettings(Settings));

        /// <summary>
        /// Где лежат настройки, таблица камней, пресеты и лицензия: C:\Strassio\data (установщик пишет
        /// путь в HKLM\Software\Strassio\DataDir — решение автора, всё в одном месте). Нет записи или
        /// папка недоступна для записи — %APPDATA%\Strassio, как раньше. Проверка окон без CorelDRAW
        /// (STRASSIO_SETTINGS_DIR) — всегда её папка.
        /// </summary>
        private static string DataDirectory()
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SettingsStore.DirectoryVariable)))
            {
                return SettingsStore.DefaultDirectory;
            }

            string? installed = InstalledDataDirectory();
            if (installed == null)
            {
                return SettingsStore.DefaultDirectory;
            }

            MoveOldData(SettingsStore.DefaultDirectory, installed);
            return installed;
        }

        /// <summary>
        /// Куда клиент кладёт файл лицензии с сайта: сама папка плагина C:\Strassio (решение автора 2026-11-08),
        /// её папка data и папка аддона в CorelDRAW. Папка плагина — та, где лежит data (из реестра установщика).
        /// </summary>
        private static string[] LicenseDropFolders(string dataDir, string addonDir)
        {
            var folders = new System.Collections.Generic.List<string>();
            foreach (string? dir in new[] { ParentOfData(InstalledDataDirectoryValue()), ParentOfData(dataDir), dataDir, addonDir })
            {
                if (!string.IsNullOrWhiteSpace(dir) && !folders.Contains(dir!, StringComparer.OrdinalIgnoreCase))
                {
                    folders.Add(dir!);
                }
            }

            return folders.ToArray();
        }

        private static string? ParentOfData(string? dataDir)
        {
            try
            {
                string? trimmed = dataDir?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return !string.IsNullOrEmpty(trimmed) && string.Equals(Path.GetFileName(trimmed), "data", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetDirectoryName(trimmed)
                    : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Что записал установщик в HKLM\Software\Strassio\DataDir — без проверки записи.</summary>
        private static string? InstalledDataDirectoryValue()
        {
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                    using RegistryKey? key = root.OpenSubKey(@"Software\Strassio");
                    if (key?.GetValue("DataDir") is string dir && dir.Trim().Length > 0)
                    {
                        return dir.Trim();
                    }
                }
                catch (Exception)
                {
                    // Нет ключа — пробуем другой вид реестра.
                }
            }

            return null;
        }

        private static string? InstalledDataDirectory()
        {
            // 32-битный CorelDRAW на 64-битной Windows иначе смотрел бы в WOW6432Node.
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                    using RegistryKey? key = root.OpenSubKey(@"Software\Strassio");
                    if (key?.GetValue("DataDir") is string dir && dir.Trim().Length > 0)
                    {
                        Directory.CreateDirectory(dir);
                        string probe = Path.Combine(dir, ".write-test");
                        File.WriteAllText(probe, string.Empty);
                        File.Delete(probe);
                        return dir;
                    }
                }
                catch (Exception)
                {
                    // Нет прав или ключа — пробуем другой вид реестра, потом %APPDATA%.
                }
            }

            return null;
        }

        /// <summary>
        /// Один раз: всё из старой папки %APPDATA%\Strassio копируется в новую (если она ещё пуста) —
        /// настройки, камни, пресеты и лицензия не теряются при переходе на C:\Strassio.
        /// </summary>
        private static void MoveOldData(string oldDir, string newDir)
        {
            try
            {
                if (!Directory.Exists(oldDir) || Directory.EnumerateFileSystemEntries(newDir).Any())
                {
                    return;
                }

                foreach (string file in Directory.GetFiles(oldDir, "*", SearchOption.AllDirectories))
                {
                    string target = Path.Combine(newDir, file.Substring(oldDir.Length).TrimStart('\\', '/'));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target, false);
                }
            }
            catch (Exception)
            {
                // Не получилось — пользователь увидит стартовые настройки; старые файлы остаются на месте.
            }
        }

        /// <summary>
        /// CorelDRAW — не .NET-программа, поэтому .NET внутри него включает только старые протоколы
        /// (SSL3/TLS 1.0), а сервер лицензий (Vercel) принимает TLS 1.2 и новее — без этого плагин
        /// видел «нет связи с сервером», хотя интернет есть. 12288 — TLS 1.3 (в .NET 4.8 нет имени).
        /// </summary>
        private static void EnableModernTls()
        {
            try
            {
                System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
            }
            catch (NotSupportedException)
            {
                // Очень старая Windows без TLS 1.2 — связи не будет, плагин работает как без интернета.
            }

            try
            {
                System.Net.ServicePointManager.SecurityProtocol |= (System.Net.SecurityProtocolType)12288;
            }
            catch (NotSupportedException)
            {
                // TLS 1.3 есть не во всех Windows — хватит и TLS 1.2.
            }
        }

        /// <summary>
        /// Метки времени лицензии (защита от перевода часов) — не рядом с license.json (%APPDATA%),
        /// а в %LOCALAPPDATA% и в реестре. При отдельной папке настроек (проверка окон без CorelDRAW,
        /// STRASSIO_SETTINGS_DIR) — только в ней, чтобы не трогать настоящие метки.
        /// </summary>
        private static IMarkStore[] LicenseMarkStores()
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SettingsStore.DirectoryVariable)))
            {
                return new IMarkStore[] { new FileMarkStore(Path.Combine(SettingsStore.DefaultDirectory, "license.state")) };
            }

            string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Strassio", "cache.bin");
            return new IMarkStore[] { new FileMarkStore(local), new RegistryMarkStore() };
        }

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

        /// <summary>Нет доступа к папке, диск полон — работаем дальше с тем, что в памяти; возвращает текст ошибки.</summary>
        private static string? TrySave(Action save)
        {
            try
            {
                save();
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }
}
