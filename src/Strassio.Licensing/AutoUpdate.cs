using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

// Этот файл компилируется дважды: в Strassio.Licensing (плагин читает ход обновления) и в
// Strassio.Connect.exe (программа, которая обновляет). Поэтому здесь нет ссылок на другие классы
// Strassio — только .NET.
namespace Strassio.Licensing
{
    /// <summary>Этап автообновления — пишется в файл состояния, плагин показывает его строкой в докере.</summary>
    internal enum UpdateState
    {
        /// <summary>Установщик скачивается.</summary>
        Downloading,

        /// <summary>Скачан и проверен, ждём, пока закроют CorelDRAW.</summary>
        Ready,

        /// <summary>CorelDRAW закрыт, установщик запущен.</summary>
        Installing,

        /// <summary>Не получилось — причина в <see cref="UpdateStatus.Detail"/>.</summary>
        Failed,
    }

    /// <summary>
    /// Файл состояния автообновления (<c>%LOCALAPPDATA%\Strassio\updates\status.txt</c>): строки
    /// «ключ=значение». Пишет Strassio.Connect.exe, читает плагин.
    /// </summary>
    internal sealed class UpdateStatus
    {
        public UpdateState State { get; set; }

        public string Version { get; set; } = string.Empty;

        /// <summary>Номер процесса Strassio.Connect.exe, который ведёт обновление.</summary>
        public int HelperPid { get; set; }

        /// <summary>Причина неудачи (для <see cref="UpdateState.Failed"/>): sha256, cancelled или текст ошибки.</summary>
        public string Detail { get; set; } = string.Empty;

        /// <summary>Записать целиком: сначала во временный файл, потом подменить — плагин не прочитает половину.</summary>
        public void Write(string path)
        {
            var text = new StringBuilder()
                .Append("state=").Append(StateName(State)).Append('\n')
                .Append("version=").Append(OneLine(Version)).Append('\n')
                .Append("pid=").Append(HelperPid.ToString(CultureInfo.InvariantCulture)).Append('\n')
                .Append("detail=").Append(OneLine(Detail)).Append('\n')
                .ToString();

            string temp = path + ".tmp";
            File.WriteAllText(temp, text, new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(temp, path);
        }

        /// <summary>Нет файла или он испорчен — null.</summary>
        public static UpdateStatus? Read(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0)
                    {
                        values[line.Substring(0, eq)] = line.Substring(eq + 1);
                    }
                }

                if (!values.TryGetValue("state", out string? stateText) ||
                    !TryParseState(stateText, out UpdateState state) ||
                    !values.TryGetValue("version", out string? version) || version.Length == 0)
                {
                    return null;
                }

                int pid = 0;
                if (values.TryGetValue("pid", out string? pidText))
                {
                    int.TryParse(pidText, NumberStyles.Integer, CultureInfo.InvariantCulture, out pid);
                }

                values.TryGetValue("detail", out string? detail);
                return new UpdateStatus { State = state, Version = version, HelperPid = pid, Detail = detail ?? string.Empty };
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Имена этапов — строками, а не ToString(): обфускатор переименовывает значения перечислений
        // в Strassio.Licensing.dll, а Strassio.Connect.exe не запутан — имена бы не совпали.
        private static string StateName(UpdateState state)
        {
            switch (state)
            {
                case UpdateState.Downloading: return "downloading";
                case UpdateState.Ready: return "ready";
                case UpdateState.Installing: return "installing";
                default: return "failed";
            }
        }

        private static bool TryParseState(string text, out UpdateState state)
        {
            switch (text)
            {
                case "downloading": state = UpdateState.Downloading; return true;
                case "ready": state = UpdateState.Ready; return true;
                case "installing": state = UpdateState.Installing; return true;
                case "failed": state = UpdateState.Failed; return true;
                default: state = UpdateState.Failed; return false;
            }
        }

        private static string OneLine(string text) => (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
    }

    /// <summary>Что показать в докере по файлу состояния.</summary>
    internal enum UpdateProgress
    {
        /// <summary>Автообновления нет или оно брошено (программа обновления уже не работает).</summary>
        None,
        Downloading,
        Ready,
        Failed,

        /// <summary>Установщик отработал: плагин уже новой версии.</summary>
        Updated,
    }

    /// <summary>
    /// Автообновление (docs/SPEC.md, 14.1): Strassio.Connect.exe скачивает установщик по ссылке из
    /// подписанного ответа сервера, сверяет SHA-256 из того же ответа и, когда CorelDRAW закрыт,
    /// запускает его — Windows один раз спрашивает разрешение (UAC). Непроверенный файл не
    /// запускается никогда.
    /// </summary>
    internal static class AutoUpdate
    {
        public const string StatusFileName = "status.txt";

        /// <summary>
        /// Ключи установщика: только окно с ходом установки, без вопросов мастера, без перезагрузки.
        /// Без /SUPPRESSMSGBOXES: если CorelDRAW снова открыли, установщик спросит «закройте и повторите».
        /// </summary>
        public const string InstallerArguments = "/SILENT /SP- /NORESTART";

        /// <summary>Сколько ждать, пока закроют CorelDRAW; дальше программа обновления сама завершается.</summary>
        public static readonly TimeSpan MaxWaitForCorel = TimeSpan.FromDays(7);

        /// <summary>Папка обновлений: <c>%LOCALAPPDATA%\Strassio\updates</c> (у каждого пользователя своя).</summary>
        public static string DefaultDirectory() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Strassio", "updates");

        /// <summary>64 шестнадцатеричных знака — так выглядит SHA-256.</summary>
        public static bool IsSha256(string? text)
        {
            if (text == null || text.Length != 64)
            {
                return false;
            }

            foreach (char c in text)
            {
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex)
                {
                    return false;
                }
            }

            return true;
        }

        public static string Sha256Of(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(stream);
                var text = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                {
                    text.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                }

                return text.ToString();
            }
        }

        public static bool HashMatches(string path, string sha256) =>
            File.Exists(path) && string.Equals(Sha256Of(path), sha256.Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>«Strassio-Setup-0.9.1.exe»: в имени остаются только цифры, буквы, точка и дефис.</summary>
        public static string InstallerName(string version)
        {
            var safe = new StringBuilder();
            foreach (char c in version ?? string.Empty)
            {
                if ((c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '.' || c == '-')
                {
                    safe.Append(c);
                }
            }

            string name = safe.ToString().Trim('.');
            return "Strassio-Setup-" + (name.Length == 0 ? "update" : name) + ".exe";
        }

        /// <summary>
        /// Что показать в докере. <paramref name="helperAlive"/> — работает ли ещё программа обновления
        /// с номером из файла: если нет, «скачивается» и «ждёт» — брошенное обновление (компьютер
        /// перезагрузили), его можно начать заново.
        /// </summary>
        public static UpdateProgress Describe(UpdateStatus? status, string? currentVersion, Func<int, bool> helperAlive)
        {
            if (status == null)
            {
                return UpdateProgress.None;
            }

            switch (status.State)
            {
                case UpdateState.Downloading:
                case UpdateState.Ready:
                    if (!helperAlive(status.HelperPid))
                    {
                        return UpdateProgress.None;
                    }

                    return status.State == UpdateState.Downloading ? UpdateProgress.Downloading : UpdateProgress.Ready;
                case UpdateState.Installing:
                    // Установщик запущен; плагин той же версии — значит, поставился.
                    return SameVersion(status.Version, currentVersion) ? UpdateProgress.Updated : UpdateProgress.None;
                case UpdateState.Failed:
                    return UpdateProgress.Failed;
                default:
                    return UpdateProgress.None;
            }
        }

        private static bool SameVersion(string a, string? b)
        {
            Version? x = ParseNumbers(a);
            Version? y = ParseNumbers(b);
            return x != null && y != null && x.Equals(y);
        }

        private static Version? ParseNumbers(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            string s = text!.Trim().TrimStart('v', 'V');
            int cut = s.IndexOfAny(new[] { '-', '+' });
            if (cut >= 0)
            {
                s = s.Substring(0, cut);
            }

            if (!System.Version.TryParse(s, out Version? v) || v == null)
            {
                return null;
            }

            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        }

        /// <summary>
        /// Весь путь обновления. Сеть, процессы и ожидание передаются снаружи: в Strassio.Connect.exe —
        /// настоящие, в тестах — подставные.
        /// </summary>
        /// <returns>0 — установщик запущен; 1 — неверные данные; 2 — не получилось (причина в файле состояния).</returns>
        public static int Run(string url, string sha256, string version, string directory, UpdateEnvironment env)
        {
            string statusPath = Path.Combine(directory, StatusFileName);
            var status = new UpdateStatus { Version = version, HelperPid = env.ProcessId };

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri == null || uri.Scheme != Uri.UriSchemeHttps ||
                !IsSha256(sha256) || string.IsNullOrWhiteSpace(version))
            {
                return 1;
            }

            try
            {
                Directory.CreateDirectory(directory);
                string installer = Path.Combine(directory, InstallerName(version));

                // Уже скачан раньше (обновление прервали, пока ждали CorelDRAW) — второй раз не качаем.
                if (!HashMatches(installer, sha256))
                {
                    status.State = UpdateState.Downloading;
                    status.Write(statusPath);

                    string part = installer + ".part";
                    if (File.Exists(part))
                    {
                        File.Delete(part);
                    }

                    env.Download(uri, part);
                    if (!HashMatches(part, sha256))
                    {
                        File.Delete(part);
                        return Fail(status, statusPath, "sha256");
                    }

                    if (File.Exists(installer))
                    {
                        File.Delete(installer);
                    }

                    File.Move(part, installer);
                }

                status.State = UpdateState.Ready;
                status.Write(statusPath);

                DateTime until = env.UtcNow() + MaxWaitForCorel;
                while (env.IsCorelRunning())
                {
                    if (env.UtcNow() > until)
                    {
                        return Fail(status, statusPath, "timeout");
                    }

                    env.Sleep(TimeSpan.FromSeconds(3));
                }

                // CorelDRAW может закрываться ещё пару секунд после окна; и за время ожидания файл
                // могли подменить — сверяем SHA-256 ещё раз прямо перед запуском.
                env.Sleep(TimeSpan.FromSeconds(2));
                if (!HashMatches(installer, sha256))
                {
                    return Fail(status, statusPath, "sha256");
                }

                status.State = UpdateState.Installing;
                status.Write(statusPath);
                env.StartInstaller(installer, InstallerArguments);
                return 0;
            }
            catch (UpdateCancelledException)
            {
                return Fail(status, statusPath, "cancelled");
            }
            catch (Exception ex)
            {
                return Fail(status, statusPath, ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static int Fail(UpdateStatus status, string statusPath, string detail)
        {
            status.State = UpdateState.Failed;
            status.Detail = detail;
            try
            {
                status.Write(statusPath);
            }
            catch (Exception)
            {
                // Писать некуда — плагин просто предложит обновиться снова.
            }

            return 2;
        }
    }

    /// <summary>Пользователь отказался в окне Windows «Разрешить этому приложению вносить изменения?».</summary>
    internal sealed class UpdateCancelledException : Exception
    {
    }

    /// <summary>Всё, что автообновление берёт у системы: сеть, процессы, время.</summary>
    internal sealed class UpdateEnvironment
    {
        public UpdateEnvironment(
            int processId,
            Action<Uri, string> download,
            Func<bool> isCorelRunning,
            Action<string, string> startInstaller,
            Action<TimeSpan> sleep,
            Func<DateTime> utcNow)
        {
            ProcessId = processId;
            Download = download;
            IsCorelRunning = isCorelRunning;
            StartInstaller = startInstaller;
            Sleep = sleep;
            UtcNow = utcNow;
        }

        public int ProcessId { get; }

        /// <summary>Скачать по ссылке в файл.</summary>
        public Action<Uri, string> Download { get; }

        public Func<bool> IsCorelRunning { get; }

        /// <summary>Запустить установщик (путь, ключи); отказ в UAC — <see cref="UpdateCancelledException"/>.</summary>
        public Action<string, string> StartInstaller { get; }

        public Action<TimeSpan> Sleep { get; }

        public Func<DateTime> UtcNow { get; }
    }
}
