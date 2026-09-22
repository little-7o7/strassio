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

        /// <summary>Больше 30 дней без связи с сервером (раздел 13.3).</summary>
        OfflineTooLong,
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

        /// <summary>Пора тихо сверяться с сервером (раз в 14 дней).</summary>
        public bool NeedsOnlineCheck { get; }

        /// <summary>Можно ли создавать и править стразы. Настройки и таблица камней доступны всегда (раздел 13.5).</summary>
        public bool CanCreate => State == LicenseState.Valid || State == LicenseState.Trial;
    }

    /// <summary>
    /// Правила лицензии (docs/SPEC.md, разделы 13.2–13.5) — без сети и без CorelDRAW, поэтому
    /// проверяются тестами. Сверка с сервером раз в 14 дней; без связи плагин работает до 30 дней с
    /// последней удачной сверки; лицензия привязана к коду компьютера с нечётким сравнением.
    /// </summary>
    public static class LicenseEvaluator
    {
        public const int CheckEveryDays = 14;
        public const int OfflineGraceDays = 30;

        /// <param name="lastCheckUtc">Когда плагин последний раз удачно сверился с сервером (или получил лицензию).</param>
        public static LicenseStatus Evaluate(
            SignedDocument? document, HardwareCode computer, DateTime nowUtc, DateTime? lastCheckUtc, LicensePublicKey key)
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

            DateTime? expires = license.ExpiresUtc;
            int? daysLeft = expires.HasValue ? (int)Math.Ceiling((expires.Value - nowUtc).TotalDays) : (int?)null;
            if (expires.HasValue && nowUtc >= expires.Value)
            {
                return new LicenseStatus(LicenseState.Expired, license, 0, needsOnlineCheck: true);
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
