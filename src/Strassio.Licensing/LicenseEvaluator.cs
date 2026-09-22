using System;

namespace Strassio.Licensing
{
    /// <summary>Что с лицензией сейчас.</summary>
    public enum LicenseState
    {
        /// <summary>Лицензии нет — нужно ввести ключ или взять пробный период.</summary>
        None,

        /// <summary>Действующая полная лицензия.</summary>
        Valid,

        /// <summary>Действующий пробный период.</summary>
        Trial,

        /// <summary>Срок вышел (пробный или ограниченный по времени ключ).</summary>
        Expired,

        /// <summary>Лицензия от другого компьютера (скопировали файл) — нужно перенести.</summary>
        WrongComputer,

        /// <summary>Подпись не сходится — файл подделан или испорчен.</summary>
        BadSignature,

        /// <summary>Больше 14 дней без связи с сервером (раздел 13.3).</summary>
        OfflineTooLong,

        /// <summary>
        /// Часы компьютера переведены назад (или удалены метки времени) — работать нельзя, пока
        /// сверка с сервером не подтвердит настоящее время.
        /// </summary>
        ClockTampered,
    }

    /// <summary>Что помнит <see cref="TimeGuard"/> — для правил лицензии.</summary>
    public sealed class TimeMarks
    {
        public DateTime? LastSeenUtc { get; set; }

        public DateTime? ServerTimeUtc { get; set; }

        public DateTime? ForbiddenUtc { get; set; }

        /// <summary>Меток нет совсем, хотя лицензия лежит — их удалили (или файл лицензии подложили).</summary>
        public bool Missing { get; set; }
    }

    /// <summary>Итог проверки: состояние, сколько дней осталось, пора ли сверяться с сервером.</summary>
    public sealed class LicenseStatus
    {
        public LicenseStatus(LicenseState state, LicenseData? license, int? daysLeft, bool needsOnlineCheck)
        {
            State = state;
            License = license;
            DaysLeft = daysLeft;
            NeedsOnlineCheck = needsOnlineCheck;
        }

        public LicenseState State { get; }

        public LicenseData? License { get; }

        /// <summary>Дней до конца срока (пробного или ограниченного); null — бессрочно.</summary>
        public int? DaysLeft { get; }

        /// <summary>Пора тихо сверяться с сервером (раз в день).</summary>
        public bool NeedsOnlineCheck { get; }

        /// <summary>Можно ли создавать и править стразы. Настройки и таблица камней доступны всегда (раздел 13.5).</summary>
        public bool CanCreate => State == LicenseState.Valid || State == LicenseState.Trial;
    }

    /// <summary>
    /// Правила лицензии (docs/SPEC.md, разделы 13.2–13.5) — без сети и без CorelDRAW, поэтому
    /// проверяются тестами. Сверка с сервером при запуске и раз в день; без связи плагин работает до 14 дней с
    /// последней удачной сверки; лицензия привязана к коду компьютера с нечётким сравнением.
    /// </summary>
    public static class LicenseEvaluator
    {
        public const int CheckEveryDays = 1;
        public const int OfflineGraceDays = 14;

        /// <param name="lastCheckUtc">Когда плагин последний раз удачно сверился с сервером (или получил лицензию).</param>
        public static LicenseStatus Evaluate(
            SignedDocument? document, HardwareCode computer, DateTime nowUtc, DateTime? lastCheckUtc, LicensePublicKey key) =>
            Evaluate(document, computer, nowUtc, lastCheckUtc, key, null);

        /// <param name="marks">Метки времени (<see cref="TimeGuard"/>); null — не проверять перевод часов.</param>
        public static LicenseStatus Evaluate(
            SignedDocument? document, HardwareCode computer, DateTime nowUtc, DateTime? lastCheckUtc, LicensePublicKey key, TimeMarks? marks)
        {
            if (document == null)
            {
                return new LicenseStatus(LicenseState.None, null, null, needsOnlineCheck: false);
            }

            if (!document.IsSignedBy(key))
            {
                return new LicenseStatus(LicenseState.BadSignature, null, null, needsOnlineCheck: false);
            }

            LicenseData? license = document.Read<LicenseData>();
            if (license == null || !HardwareCode.TryParse(license.Hwid, out HardwareCode? licensed))
            {
                return new LicenseStatus(LicenseState.BadSignature, null, null, needsOnlineCheck: false);
            }

            if (!licensed!.Matches(computer))
            {
                return new LicenseStatus(LicenseState.WrongComputer, license, null, needsOnlineCheck: true);
            }

            // Старая копия файла после освобождения/переноса/отзыва — как будто лицензии нет.
            DateTime? issued = license.IssuedUtc;
            if (marks?.ForbiddenUtc != null && (!issued.HasValue || issued.Value <= marks.ForbiddenUtc.Value))
            {
                return new LicenseStatus(LicenseState.None, null, null, needsOnlineCheck: false);
            }

            // Перевод часов назад: раньше, чем сервер выдал лицензию (его время подписано), раньше
            // последнего известного времени сервера или того, что плагин уже видел на этих часах.
            DateTime floor = nowUtc + TimeGuard.Tolerance;
            if ((issued.HasValue && floor < issued.Value) ||
                (marks != null && (marks.Missing ||
                                   (marks.ServerTimeUtc.HasValue && floor < marks.ServerTimeUtc.Value) ||
                                   (marks.LastSeenUtc.HasValue && floor < marks.LastSeenUtc.Value))))
            {
                return new LicenseStatus(LicenseState.ClockTampered, license, null, needsOnlineCheck: true);
            }

            DateTime? expires = license.ExpiresUtc;
            int? daysLeft = expires.HasValue ? (int)Math.Ceiling((expires.Value - nowUtc).TotalDays) : (int?)null;
            if (expires.HasValue && nowUtc >= expires.Value)
            {
                return new LicenseStatus(LicenseState.Expired, license, 0, needsOnlineCheck: true);
            }

            if (license.Offline)
            {
                return new LicenseStatus(license.IsTrial ? LicenseState.Trial : LicenseState.Valid, license, daysLeft, needsOnlineCheck: false);
            }

            DateTime lastCheck = lastCheckUtc ?? license.IssuedUtc ?? nowUtc;
            if ((nowUtc - lastCheck).TotalDays > OfflineGraceDays)
            {
                return new LicenseStatus(LicenseState.OfflineTooLong, license, daysLeft, needsOnlineCheck: true);
            }

            DateTime nextCheck = license.NextCheckUtc ?? lastCheck.AddDays(CheckEveryDays);
            bool needsCheck = nowUtc >= nextCheck || (nowUtc - lastCheck).TotalDays >= CheckEveryDays;
            return new LicenseStatus(license.IsTrial ? LicenseState.Trial : LicenseState.Valid, license, daysLeft, needsCheck);
        }
    }
}
