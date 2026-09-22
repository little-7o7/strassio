using System;

namespace Strassio.Licensing
{
    /// <summary>
    /// Постоянные лицензии плагина. Открытый ключ проверки подписи — не секрет (docs/SPEC.md, 13.2);
    /// закрытый живёт только на сервере в переменной окружения LICENSE_PRIVATE_KEY.
    /// Сменили пару ключей (server: npm run keys) — поменять <see cref="PublicKey"/> и выпустить новую версию.
    /// </summary>
    public static class LicenseKeys
    {
        public const string PublicKey = "UPeiXzBFzncY7G1XFP6zLssgZJTS0+xUFxmn/2luKWzaEUmZZipizIYr7xYZmdEi8z9OIMoN0HiXVTWC7lBA9g==";

        /// <summary>Адрес сервера лицензий (Vercel; до первой продажи — переезд, см. SPEC 15).</summary>
        public const string DefaultServer = "https://strassio.vercel.app";

        /// <summary>
        /// Адрес сервера: переменная окружения STRASSIO_SERVER (для проверки с локальным сервером,
        /// server: npm run local) или <see cref="DefaultServer"/>. Подменить адрес безопасно —
        /// лицензию без закрытого ключа всё равно не подписать.
        /// </summary>
        public static string Server
        {
            get
            {
                string? env = Environment.GetEnvironmentVariable("STRASSIO_SERVER");
                return !string.IsNullOrWhiteSpace(env) && Uri.TryCreate(env, UriKind.Absolute, out _) ? env!.Trim() : DefaultServer;
            }
        }

        public static LicensePublicKey Key => LicensePublicKey.FromBase64(PublicKey);
    }
}
