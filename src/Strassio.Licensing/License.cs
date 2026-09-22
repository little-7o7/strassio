using System;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace Strassio.Licensing
{
    /// <summary>
    /// Содержимое лицензии (docs/SPEC.md, раздел 13.2): {serial, hwid, plan, expiresAt, issuedAt,
    /// nextCheckAt}. Сервер подписывает РОВНО этот текст JSON — поэтому плагин хранит и проверяет его
    /// как строку (<see cref="SignedDocument.Payload"/>), а не пересобирает заново.
    /// </summary>
    [DataContract]
    public sealed class LicenseData
    {
        public const string PlanFull = "full";
        public const string PlanTrial = "trial";

        [DataMember(Name = "serial", Order = 0)]
        public string Serial { get; set; } = string.Empty;

        /// <summary>Код компьютера (<see cref="HardwareCode.ToStorage"/>).</summary>
        [DataMember(Name = "hwid", Order = 1)]
        public string Hwid { get; set; } = string.Empty;

        /// <summary>"full" или "trial".</summary>
        [DataMember(Name = "plan", Order = 2)]
        public string Plan { get; set; } = PlanFull;

        /// <summary>До какого момента действует (ISO 8601, UTC); пусто — бессрочно.</summary>
        [DataMember(Name = "expiresAt", Order = 3, EmitDefaultValue = false)]
        public string? ExpiresAt { get; set; }

        [DataMember(Name = "issuedAt", Order = 4)]
        public string IssuedAt { get; set; } = string.Empty;

        /// <summary>Когда сверяться с сервером в следующий раз (раздел 13.3 — раз в 14 дней).</summary>
        [DataMember(Name = "nextCheckAt", Order = 5)]
        public string NextCheckAt { get; set; } = string.Empty;

        public bool IsTrial => Plan == PlanTrial;

        public DateTime? ExpiresUtc => ParseUtc(ExpiresAt);

        public DateTime? IssuedUtc => ParseUtc(IssuedAt);

        public DateTime? NextCheckUtc => ParseUtc(NextCheckAt);

        public static DateTime? ParseUtc(string? iso) =>
            DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime value)
                ? value
                : (DateTime?)null;

        public static string ToIso(DateTime utc) => utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Подписанный документ — лицензия или сведения об обновлении: текст JSON и подпись ECDSA P-256 /
    /// SHA-256 в формате IEEE P1363 (r‖s, 64 байта), base64. Подписывает сервер своим закрытым ключом;
    /// в плагине только открытый (раздел 13.2).
    /// </summary>
    [DataContract]
    public sealed class SignedDocument
    {
        [DataMember(Name = "payload", Order = 0)]
        public string Payload { get; set; } = string.Empty;

        [DataMember(Name = "signature", Order = 1)]
        public string Signature { get; set; } = string.Empty;

        /// <summary>Подпись верна для открытого ключа <paramref name="key"/>.</summary>
        public bool IsSignedBy(LicensePublicKey key)
        {
            try
            {
                byte[] signature = Convert.FromBase64String(Signature);
                return key.Verify(Encoding.UTF8.GetBytes(Payload), signature);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        /// <summary>Содержимое как объект; null — текст не того вида.</summary>
        public T? Read<T>()
            where T : class => Json.TryRead<T>(Payload);
    }

    /// <summary>
    /// Открытый ключ проверки подписи: точка кривой P-256 (X и Y по 32 байта). Задаётся как
    /// base64 от 64 байт X‖Y — тот же вид, что печатает server/scripts/generate-keys.
    /// </summary>
    public sealed class LicensePublicKey
    {
        private readonly ECParameters parameters;

        private LicensePublicKey(ECParameters parameters)
        {
            this.parameters = parameters;
        }

        public static LicensePublicKey FromBase64(string xy)
        {
            byte[] raw = Convert.FromBase64String(xy);
            if (raw.Length != 64)
            {
                throw new ArgumentException("Открытый ключ P-256 — это 64 байта (X и Y).", nameof(xy));
            }

            var x = new byte[32];
            var y = new byte[32];
            Buffer.BlockCopy(raw, 0, x, 0, 32);
            Buffer.BlockCopy(raw, 32, y, 0, 32);
            return new LicensePublicKey(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = x, Y = y },
            });
        }

        public bool Verify(byte[] data, byte[] signatureP1363)
        {
            if (signatureP1363.Length != 64)
            {
                return false;
            }

            try
            {
                using ECDsa ecdsa = ECDsa.Create(parameters);
                return ecdsa.VerifyData(data, signatureP1363, HashAlgorithmName.SHA256);
            }
            catch (CryptographicException)
            {
                return false;
            }
        }
    }

    /// <summary>JSON через встроенный DataContractJsonSerializer (CLAUDE.md, правило 7).</summary>
    internal static class Json
    {
        public static string Write<T>(T value)
        {
            using var stream = new MemoryStream();
            new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        public static T? TryRead<T>(string? json)
            where T : class
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
                return new DataContractJsonSerializer(typeof(T)).ReadObject(stream) as T;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
