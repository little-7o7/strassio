using System;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Strassio.Licensing
{
    /// <summary>Запрос плагина к серверу (docs/SPEC.md, раздел 15, API).</summary>
    [DataContract]
    public sealed class ApiRequest
    {
        [DataMember(Name = "serial", Order = 0, EmitDefaultValue = false)]
        public string? Serial { get; set; }

        [DataMember(Name = "hwid", Order = 1)]
        public string Hwid { get; set; } = string.Empty;

        [DataMember(Name = "pluginVersion", Order = 2, EmitDefaultValue = false)]
        public string? PluginVersion { get; set; }

        [DataMember(Name = "corelVersion", Order = 3, EmitDefaultValue = false)]
        public string? CorelVersion { get; set; }
    }

    /// <summary>Ответ сервера: { ok: true, license } или { ok: false, error, transfersLeft? }.</summary>
    [DataContract]
    public sealed class ApiReply
    {
        /// <summary>Нет связи с сервером (нет интернета, сервер не отвечает, непонятный ответ).</summary>
        public const string NoConnection = "no_connection";

        [DataMember(Name = "ok", Order = 0)]
        public bool Ok { get; set; }

        [DataMember(Name = "license", Order = 1, EmitDefaultValue = false)]
        public SignedDocument? License { get; set; }

        [DataMember(Name = "error", Order = 2, EmitDefaultValue = false)]
        public string? Error { get; set; }

        [DataMember(Name = "transfersLeft", Order = 3, EmitDefaultValue = false)]
        public int? TransfersLeft { get; set; }

        [DataMember(Name = "update", Order = 4, EmitDefaultValue = false)]
        public SignedDocument? Update { get; set; }

        public static ApiReply Offline() => new ApiReply { Ok = false, Error = NoConnection };
    }

    /// <summary>Связь с сервером лицензий. В аддоне — HTTP (<see cref="HttpLicenseApi"/>), в тестах — подставной.</summary>
    public interface ILicenseApi
    {
        /// <param name="action">activate, transfer, check, deactivate, trial.</param>
        Task<ApiReply> PostAsync(string action, ApiRequest request, CancellationToken cancel = default);

        /// <summary>Сведения о последней версии канала stable/beta (раздел 14.1).</summary>
        Task<ApiReply> LatestUpdateAsync(string channel, CancellationToken cancel = default);
    }

    /// <summary>
    /// HTTP-клиент сервера — встроенный HttpClient (сторонних библиотек нет, CLAUDE.md, правило 7).
    /// Любая сетевая беда превращается в ответ <see cref="ApiReply.NoConnection"/>, а не в исключение:
    /// без сети плагин должен спокойно работать дальше (раздел 13.3).
    /// </summary>
    public sealed class HttpLicenseApi : ILicenseApi, IDisposable
    {
        private readonly HttpClient http;
        private readonly Uri baseUri;

        public HttpLicenseApi(string baseUrl, TimeSpan? timeout = null)
        {
            baseUri = new Uri(baseUrl.TrimEnd('/') + "/");
            http = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(15) };
        }

        public async Task<ApiReply> PostAsync(string action, ApiRequest request, CancellationToken cancel = default)
        {
            try
            {
                using var content = new StringContent(Json.Write(request), Encoding.UTF8, "application/json");
                using HttpResponseMessage response = await http.PostAsync(new Uri(baseUri, "api/" + action), content, cancel).ConfigureAwait(false);
                return await ReadReply(response).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException) || !cancel.IsCancellationRequested)
            {
                return ApiReply.Offline();
            }
        }

        public async Task<ApiReply> LatestUpdateAsync(string channel, CancellationToken cancel = default)
        {
            try
            {
                using HttpResponseMessage response = await http.GetAsync(new Uri(baseUri, "api/update/latest?channel=" + Uri.EscapeDataString(channel)), cancel).ConfigureAwait(false);
                return await ReadReply(response).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException) || !cancel.IsCancellationRequested)
            {
                return ApiReply.Offline();
            }
        }

        public void Dispose() => http.Dispose();

        private static async Task<ApiReply> ReadReply(HttpResponseMessage response)
        {
            string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            // 429 и 5xx — сервер есть, но сейчас ответить не может: для плагина это то же, что «нет связи».
            if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
            {
                return ApiReply.Offline();
            }

            ApiReply? reply = Json.TryRead<ApiReply>(text);
            return reply != null && (reply.Ok || !string.IsNullOrEmpty(reply.Error)) ? reply : ApiReply.Offline();
        }
    }
}
