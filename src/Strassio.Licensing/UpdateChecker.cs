using System;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Strassio.Licensing
{
    /// <summary>
    /// Сведения о последней версии (docs/SPEC.md, 14.1): сервер отдаёт их подписанными тем же ключом,
    /// что и лицензии, — подменить ссылку на установщик посередине нельзя.
    /// </summary>
    [DataContract]
    internal sealed class UpdateInfo
    {
        [DataMember(Name = "version", Order = 0)]
        public string Version { get; set; } = string.Empty;

        /// <summary>Ссылка на установщик (страница выпуска strassio-releases).</summary>
        [DataMember(Name = "url", Order = 1)]
        public string Url { get; set; } = string.Empty;

        [DataMember(Name = "sha256", Order = 2, EmitDefaultValue = false)]
        public string? Sha256 { get; set; }

        [DataMember(Name = "notesRu", Order = 3, EmitDefaultValue = false)]
        public string? NotesRu { get; set; }

        [DataMember(Name = "notesEn", Order = 4, EmitDefaultValue = false)]
        public string? NotesEn { get; set; }

        /// <summary>Версии старше этой автор больше не поддерживает — обновиться нужно обязательно.</summary>
        [DataMember(Name = "minVersion", Order = 5, EmitDefaultValue = false)]
        public string? MinVersion { get; set; }

        /// <summary>Что нового — на языке интерфейса; нет текста на этом языке — на другом.</summary>
        public string Notes(string language)
        {
            string? first = language == "en" ? NotesEn : NotesRu;
            string? second = language == "en" ? NotesRu : NotesEn;
            return (string.IsNullOrWhiteSpace(first) ? second : first)?.Trim() ?? string.Empty;
        }

        /// <summary>Текущая версия ниже <see cref="MinVersion"/>.</summary>
        public bool IsRequiredFor(string? currentVersion)
        {
            Version? min = UpdateChecker.ParseVersion(MinVersion);
            Version? current = UpdateChecker.ParseVersion(currentVersion);
            return min != null && current != null && current < min;
        }
    }

    /// <summary>
    /// Есть ли версия новее установленной (docs/SPEC.md, 14.1). Тихо и без исключений: нет связи,
    /// подпись не сходится, ссылка не https, версия не новее — просто null, плагин работает дальше.
    /// Скачивает установщик браузер, а не плагин: CorelDRAW часто закрыт брандмауэром, а установка
    /// всё равно идёт только при закрытом CorelDRAW.
    /// </summary>
    internal sealed class UpdateChecker
    {
        private readonly ILicenseApi api;
        private readonly LicensePublicKey key;
        private readonly string? currentVersion;

        public UpdateChecker(ILicenseApi api, LicensePublicKey key, string? currentVersion)
        {
            this.api = api;
            this.key = key;
            this.currentVersion = currentVersion;
        }

        /// <param name="channel">stable или beta.</param>
        public async Task<UpdateInfo?> CheckAsync(string channel = "stable", CancellationToken cancel = default)
        {
            ApiReply reply;
            try
            {
                reply = await api.LatestUpdateAsync(channel, cancel).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return null;
            }

            if (!reply.Ok || reply.Update == null || !reply.Update.IsSignedBy(key))
            {
                return null;
            }

            UpdateInfo? info = reply.Update.Read<UpdateInfo>();
            if (info == null || !IsHttps(info.Url) || !IsNewer(info.Version, currentVersion))
            {
                return null;
            }

            return info;
        }

        /// <summary>
        /// <paramref name="candidate"/> новее <paramref name="current"/>. Сравниваются числа «1.2.3»;
        /// «1.0.0-beta.1» считается младше «1.0.0». Не разобрать любую из версий — не новее.
        /// </summary>
        public static bool IsNewer(string? candidate, string? current)
        {
            Version? a = ParseVersion(candidate);
            Version? b = ParseVersion(current);
            if (a == null || b == null)
            {
                return false;
            }

            int byNumber = a.CompareTo(b);
            if (byNumber != 0)
            {
                return byNumber > 0;
            }

            // Числа равны: обычная версия новее пробной с тем же номером, но не наоборот.
            return IsPreRelease(current) && !IsPreRelease(candidate);
        }

        /// <summary>«v1.2.3-beta.1» → 1.2.3; «1.2» → 1.2.0; ерунда → null.</summary>
        public static Version? ParseVersion(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            string s = text!.Trim();
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring(1);
            }

            int cut = s.IndexOfAny(new[] { '-', '+' });
            if (cut >= 0)
            {
                s = s.Substring(0, cut);
            }

            if (!System.Version.TryParse(s, out Version? parsed) || parsed == null)
            {
                return null;
            }

            return new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0));
        }

        private static bool IsPreRelease(string? text) => text != null && text.IndexOf('-') >= 0;

        private static bool IsHttps(string? url) =>
            Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri != null && uri.Scheme == Uri.UriSchemeHttps;
    }
}
