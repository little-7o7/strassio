using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
    /// <para>
    /// Часто CorelDRAW запрещают выходить в интернет правилом брандмауэра — тогда прямой запрос из
    /// плагина не проходит, и он идёт через Strassio.Connect.exe рядом с аддоном (отдельная программа,
    /// под запрет не попадает). Получилось через неё — дальше сразу через неё.
    /// </para>
    /// </summary>
    public sealed class HttpLicenseApi : ILicenseApi, IDisposable
    {
        private static readonly TimeSpan HelperTimeout = TimeSpan.FromSeconds(25);

        private readonly HttpClient http;
        private readonly Uri baseUri;
        private readonly string? helperPath;
        private readonly bool tryDirect;
        private volatile bool preferHelper;

        /// <summary>
        /// Причина последней неудачи связи (тип ошибки и сообщение, в т. ч. вложенные) — показывается
        /// в окне «Лицензия» под «Нет связи с сервером», чтобы по скриншоту было видно, что случилось.
        /// null — последний запрос прошёл.
        /// </summary>
        public string? LastError { get; private set; }

        /// <param name="helperPath">Путь к Strassio.Connect.exe; null или нет файла — только прямой запрос.</param>
        /// <param name="tryDirect">false — сразу через Strassio.Connect (для тестов).</param>
        public HttpLicenseApi(string baseUrl, TimeSpan? timeout = null, string? helperPath = null, bool tryDirect = true)
        {
            baseUri = new Uri(baseUrl.TrimEnd('/') + "/");
            http = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(15) };
            this.helperPath = helperPath;
            this.tryDirect = tryDirect;
        }

        private bool HasHelper => helperPath != null && File.Exists(helperPath);

        public Task<ApiReply> PostAsync(string action, ApiRequest request, CancellationToken cancel = default) =>
            SendAsync("POST", new Uri(baseUri, "api/" + action), Json.Write(request), cancel);

        public Task<ApiReply> LatestUpdateAsync(string channel, CancellationToken cancel = default) =>
            SendAsync("GET", new Uri(baseUri, "api/update/latest?channel=" + Uri.EscapeDataString(channel)), null, cancel);

        public void Dispose() => http.Dispose();

        private async Task<ApiReply> SendAsync(string method, Uri uri, string? body, CancellationToken cancel)
        {
            string? directError = null;
            if (tryDirect && !(preferHelper && HasHelper))
            {
                try
                {
                    using var content = body == null ? null : new StringContent(body, Encoding.UTF8, "application/json");
                    using HttpResponseMessage response = method == "POST"
                        ? await http.PostAsync(uri, content, cancel).ConfigureAwait(false)
                        : await http.GetAsync(uri, cancel).ConfigureAwait(false);
                    string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return Parse((int)response.StatusCode, text);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException) || !cancel.IsCancellationRequested)
                {
                    directError = Describe(ex);
                }
            }

            if (!HasHelper)
            {
                LastError = directError ?? "Strassio.Connect.exe не найден";
                return ApiReply.Offline();
            }

            string? helperError = null;
            try
            {
                (int status, string text)? answer = await Task.Run(() => RunHelper(method, uri, body, out helperError), cancel).ConfigureAwait(false);
                if (answer.HasValue)
                {
                    preferHelper = true;
                    return Parse(answer.Value.status, answer.Value.text);
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException) || !cancel.IsCancellationRequested)
            {
                helperError = Describe(ex);
            }

            LastError = directError == null ? "Strassio.Connect: " + helperError : directError + " | Strassio.Connect: " + helperError;
            return ApiReply.Offline();
        }

        /// <summary>Запрос через Strassio.Connect.exe: ответ «код\nтело» или null с причиной в <paramref name="error"/>.</summary>
        private (int status, string text)? RunHelper(string method, Uri uri, string? body, out string? error)
        {
            var info = new ProcessStartInfo(helperPath!, method + " \"" + uri.AbsoluteUri + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.UTF8,
            };

            using Process process = Process.Start(info) ?? throw new InvalidOperationException("процесс не запустился");
            using (Stream stdin = process.StandardInput.BaseStream)
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(body ?? string.Empty);
                stdin.Write(bytes, 0, bytes.Length);
            }

            Task<string> output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit((int)HelperTimeout.TotalMilliseconds))
            {
                try
                {
                    process.Kill();
                }
                catch (Exception)
                {
                    // Уже завершился.
                }

                error = "нет ответа за " + HelperTimeout.TotalSeconds + " с";
                return null;
            }

            string text = output.Result;
            int newline = text.IndexOf('\n');
            if (text.StartsWith("ERR ", StringComparison.Ordinal) || newline < 0 || !int.TryParse(text.Substring(0, newline), out int status))
            {
                error = text.StartsWith("ERR ", StringComparison.Ordinal) ? text.Substring(4) : "непонятный ответ: " + Cut(text);
                return null;
            }

            error = null;
            return (status, text.Substring(newline + 1));
        }

        private ApiReply Parse(int status, string text)
        {
            // 429 и 5xx — сервер есть, но сейчас ответить не может: для плагина это то же, что «нет связи».
            if (status == 429 || status >= 500)
            {
                LastError = "HTTP " + status;
                return ApiReply.Offline();
            }

            ApiReply? reply = Json.TryRead<ApiReply>(text);
            if (reply != null && (reply.Ok || !string.IsNullOrEmpty(reply.Error)))
            {
                LastError = null;
                return reply;
            }

            LastError = "HTTP " + status + ": " + Cut(text);
            return ApiReply.Offline();
        }

        private static string Cut(string text) => text.Length > 120 ? text.Substring(0, 120) : text;

        internal static string Describe(Exception ex)
        {
            var parts = new List<string>();
            for (Exception? e = ex; e != null && parts.Count < 4; e = e.InnerException)
            {
                parts.Add(e.GetType().Name + ": " + e.Message);
            }

            return string.Join(" → ", parts);
        }
    }
}
