#nullable enable
using System;
using System.Text;

namespace Strassio.Licensing
{
    /// <summary>
    /// Код активации с сайта (страница /activate): подписанная лицензия одной строкой
    /// «SA1.&lt;payload&gt;.&lt;подпись&gt;» в base64url. Формат задаёт сервер (toActivationCode в server/core/crypto.ts).
    /// Подпись и компьютер здесь не проверяются — это делает <see cref="LicenseManager.ImportCode"/>.
    /// </summary>
    internal static class ActivationCode
    {
        public const string Prefix = "SA1.";

        /// <summary>Разобрать код; пробелы и переносы строк (так бывает при копировании) не мешают.</summary>
        public static SignedDocument? TryDecode(string? text)
        {
            if (text == null)
            {
                return null;
            }

            var clean = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (!char.IsWhiteSpace(c))
                {
                    clean.Append(c);
                }
            }

            string s = clean.ToString();
            if (!s.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string[] parts = s.Substring(Prefix.Length).Split('.');
            if (parts.Length != 2)
            {
                return null;
            }

            byte[]? payload = FromBase64Url(parts[0]);
            byte[]? signature = FromBase64Url(parts[1]);
            if (payload == null || payload.Length == 0 || signature == null || signature.Length == 0)
            {
                return null;
            }

            try
            {
                return new SignedDocument
                {
                    Payload = new UTF8Encoding(false, true).GetString(payload),
                    Signature = Convert.ToBase64String(signature),
                };
            }
            catch (ArgumentException)
            {
                return null; // не UTF-8
            }
        }

        private static byte[]? FromBase64Url(string text)
        {
            string b64 = text.Replace('-', '+').Replace('_', '/');
            switch (b64.Length % 4)
            {
                case 2:
                    b64 += "==";
                    break;
                case 3:
                    b64 += "=";
                    break;
                case 1:
                    return null;
            }

            try
            {
                return Convert.FromBase64String(b64);
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }
}
