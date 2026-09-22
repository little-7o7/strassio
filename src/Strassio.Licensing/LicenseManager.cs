using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Strassio.Licensing
{
    /// <summary>Итог действия с лицензией (ввод ключа, пробный период, перенос…).</summary>
    public sealed class LicenseActionResult
    {
        public LicenseActionResult(bool ok, string? error, int? transfersLeft)
        {
            Ok = ok;
            Error = error;
            TransfersLeft = transfersLeft;
        }

        public bool Ok { get; }

        /// <summary>
        /// Код ошибки сервера (not_found, blocked, expired, occupied, transfer_limit, not_activated,
        /// revoked, bad_request) или плагина: no_connection, bad_serial, bad_license, bad_file,
        /// wrong_computer. В интерфейсе переводится ключом языка "license.error.&lt;код&gt;".
        /// </summary>
        public string? Error { get; }

        /// <summary>Сколько самостоятельных переносов ещё можно (при «ключ занят другим компьютером»).</summary>
        public int? TransfersLeft { get; }

        /// <summary>Ключ уже активирован на другом компьютере — можно предложить перенос.</summary>
        public bool CanTransfer => Error == "occupied" && (TransfersLeft ?? 0) > 0;

        internal static LicenseActionResult Success() => new LicenseActionResult(true, null, null);

        internal static LicenseActionResult Fail(string error, int? transfersLeft = null) => new LicenseActionResult(false, error, transfersLeft);
    }

    /// <summary>
    /// Лицензия в плагине (docs/SPEC.md, раздел 13): файл license.json в папке настроек, ввод ключа,
    /// пробный период, перенос, освобождение компьютера, офлайн-файл от автора и тихая сверка с
    /// сервером при запуске и раз в день. Без CorelDRAW и без окон — поэтому проверяется тестами.
    ///
    /// В файле хранится ровно то, что подписал сервер (<see cref="SignedDocument"/>); подделать срок или
    /// код компьютера нельзя — подпись перестанет сходиться. Дата последней сверки — issuedAt внутри
    /// лицензии: сервер выдаёт свежую лицензию при каждой удачной сверке.
    /// </summary>
    public sealed class LicenseManager
    {
        public const string FileName = "license.json";

        /// <summary>Ошибки сервера, после которых лицензия на этом компьютере больше не действует.</summary>
        private static readonly string[] FatalErrors = { "revoked", "not_activated", "not_found", "blocked", "expired" };

        private readonly ILicenseApi api;
        private readonly Lazy<HardwareCode> computer;
        private readonly LicensePublicKey key;
        private readonly Func<DateTime> clock;
        private readonly TimeGuard guard;
        private readonly object sync = new object();
        private SignedDocument? document;
        private bool loaded;
        private int checking;

        /// <param name="computer">Код этого компьютера — считается один раз при первом обращении (WMI небыстрый).</param>
        /// <param name="markStores">
        /// Где хранить метки времени (<see cref="TimeGuard"/>): в аддоне — файл и реестр, подальше от
        /// license.json. null — файл рядом с лицензией.
        /// </param>
        public LicenseManager(
            ILicenseApi api, Func<HardwareCode> computer, LicensePublicKey key, string directory,
            Func<DateTime>? clock = null, IReadOnlyList<IMarkStore>? markStores = null)
        {
            this.api = api;
            this.computer = new Lazy<HardwareCode>(computer, LazyThreadSafetyMode.ExecutionAndPublication);
            this.key = key;
            this.clock = clock ?? (() => DateTime.UtcNow);
            FilePath = Path.Combine(directory, FileName);
            guard = new TimeGuard(markStores ?? new IMarkStore[] { new FileMarkStore(Path.Combine(directory, "license.state")) }, () => Computer.ToStorage());
        }

        /// <summary>Лицензия изменилась (получена, продлена, отозвана) — обновить надписи и кнопки.</summary>
        public event EventHandler? Changed;

        public string FilePath { get; }

        /// <summary>Версии для сервера (видны автору в админке).</summary>
        public string? PluginVersion { get; set; }

        public string? CorelVersion { get; set; }

        /// <summary>Что ответил сервер при последней тихой сверке, если лицензию пришлось снять (например, "revoked").</summary>
        public string? LastProblem { get; private set; }

        public HardwareCode Computer => computer.Value;

        /// <summary>Код компьютера уже посчитан (<see cref="Status"/> не будет ждать WMI).</summary>
        public bool IsComputerKnown => computer.IsValueCreated;

        /// <summary>Сообщить подписчикам <see cref="Changed"/>, что стоит перечитать состояние (например, код компьютера готов).</summary>
        public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);

        /// <summary>Состояние лицензии прямо сейчас (читается из памяти, подпись проверяется каждый раз).</summary>
        public LicenseStatus Status
        {
            get
            {
                DateTime now = clock();
                SignedDocument? doc = Document;

                // Часы идут вперёд — запоминаем; назад — метка не опускается (см. TimeGuard).
                // Сначала запись, потом проверка: запрет записи меток виден сразу.
                guard.Observe(now);
                return LicenseEvaluator.Evaluate(doc, Computer, now, null, key, Marks(doc != null));
            }
        }

        /// <summary>Можно создавать и править стразы.</summary>
        public bool CanCreate => Status.CanCreate;

        private SignedDocument? Document
        {
            get
            {
                lock (sync)
                {
                    if (!loaded)
                    {
                        document = ReadFile(FilePath);
                        loaded = true;
                    }

                    return document;
                }
            }
        }

        /// <summary>Ввод ключа. Тот же компьютер (в т. ч. после переустановки Windows) — лицензия восстанавливается.</summary>
        public Task<LicenseActionResult> ActivateAsync(string serial, CancellationToken cancel = default) =>
            RequestWithSerialAsync("activate", serial, cancel);

        /// <summary>«Перенести лицензию на этот компьютер» — после ответа "occupied" на <see cref="ActivateAsync"/>.</summary>
        public Task<LicenseActionResult> TransferAsync(string serial, CancellationToken cancel = default) =>
            RequestWithSerialAsync("transfer", serial, cancel);

        /// <summary>Пробный период 14 дней (сервер помнит компьютер — переустановка не сбрасывает).</summary>
        public async Task<LicenseActionResult> StartTrialAsync(CancellationToken cancel = default)
        {
            ApiReply reply = await api.PostAsync("trial", NewRequest(null), cancel);
            return Accept(reply);
        }

        /// <summary>«Освободить этот компьютер»: место ключа освобождается, файл лицензии удаляется.</summary>
        public async Task<LicenseActionResult> DeactivateAsync(CancellationToken cancel = default)
        {
            LicenseData? license = Status.License;
            if (license == null || license.IsTrial)
            {
                return LicenseActionResult.Fail("not_activated");
            }

            ApiReply reply = await api.PostAsync("deactivate", NewRequest(license.Serial), cancel);
            if (!reply.Ok && reply.Error != "not_activated")
            {
                return LicenseActionResult.Fail(reply.Error ?? ApiReply.NoConnection);
            }

            Remove(license);
            return LicenseActionResult.Success();
        }

        /// <summary>
        /// Офлайн-активация (раздел 13.6): файл лицензии, который автор сделал в админке по коду
        /// компьютера клиента. Принимается, только если подпись верна и он для этого компьютера.
        /// </summary>
        public LicenseActionResult Import(string json)
        {
            SignedDocument? doc = Json.TryRead<SignedDocument>(json);
            if (doc == null || string.IsNullOrEmpty(doc.Payload))
            {
                return LicenseActionResult.Fail("bad_file");
            }

            return Import(doc);
        }

        /// <summary>
        /// Код активации с сайта (<see cref="ActivationCode"/>): клиент вставил на сайте код компьютера
        /// и получил подписанную лицензию для него. Проверки — те же, что у файла лицензии.
        /// </summary>
        public LicenseActionResult ImportCode(string text)
        {
            SignedDocument? doc = ActivationCode.TryDecode(text);
            return doc == null ? LicenseActionResult.Fail("bad_code") : Import(doc);
        }

        private LicenseActionResult Import(SignedDocument doc)
        {
            DateTime now = clock();
            LicenseStatus plain = LicenseEvaluator.Evaluate(doc, Computer, now, null, key);
            if (plain.State == LicenseState.BadSignature || plain.State == LicenseState.WrongComputer)
            {
                return LicenseActionResult.Fail(plain.State == LicenseState.WrongComputer ? "wrong_computer" : "bad_license");
            }

            // Старый файл (после него лицензию с этого компьютера снимали) и перевод часов назад.
            LicenseStatus status = LicenseEvaluator.Evaluate(doc, Computer, now, null, key, Marks(false));
            if (status.State == LicenseState.None)
            {
                return LicenseActionResult.Fail("old_file");
            }

            if (!status.CanCreate)
            {
                return LicenseActionResult.Fail(status.State == LicenseState.ClockTampered ? "clock" :
                    status.State == LicenseState.Expired ? "expired" : "bad_license");
            }

            // issuedAt подписан сервером — это надёжное время, от него и считаются метки.
            guard.AcceptServerTime(plain.License!.IssuedUtc ?? now);
            guard.Observe(now);
            Save(doc);
            return LicenseActionResult.Success();
        }

        /// <summary>
        /// Тихая сверка с сервером (раздел 13.3): только если пора (раз в день) или <paramref name="force"/>.
        /// Нет связи — ничего не меняется, плагин работает до 14 дней. Сервер ответил, что лицензия
        /// отозвана/заблокирована/истекла — файл удаляется, причина — в <see cref="LastProblem"/>.
        /// Возвращает true, если лицензия изменилась.
        /// </summary>
        public async Task<bool> CheckAsync(bool force = false, CancellationToken cancel = default)
        {
            LicenseStatus status = Status;
            LicenseData? license = status.License;
            if (license == null || status.State == LicenseState.BadSignature || !(force || status.NeedsOnlineCheck))
            {
                return false;
            }

            // Одна сверка за раз: докер могут открыть несколько раз подряд.
            if (Interlocked.Exchange(ref checking, 1) == 1)
            {
                return false;
            }

            try
            {
                ApiReply reply = license.IsTrial
                    ? await api.PostAsync("trial", NewRequest(null), cancel)
                    : await api.PostAsync("check", NewRequest(license.Serial), cancel);

                if (reply.Ok)
                {
                    return Accept(reply).Ok;
                }

                if (FatalErrors.Contains(reply.Error))
                {
                    LastProblem = reply.Error;
                    Remove(license);
                    return true;
                }

                return false;
            }
            finally
            {
                Interlocked.Exchange(ref checking, 0);
            }
        }

        /// <summary>
        /// Сверка при каждом запуске CorelDRAW (так решил автор): отозванная или перенесённая лицензия
        /// снимается сразу. Нет интернета — работаем дальше, до 14 дней с последней
        /// удачной сверки. Файл офлайн-активации (для компьютера без интернета) не сверяется.
        /// </summary>
        public Task<bool> CheckOnStartAsync(CancellationToken cancel = default)
        {
            LicenseData? license = Status.License;
            return CheckAsync(force: license != null && !license.Offline, cancel);
        }

        /// <summary>Серийный ключ к виду STRS-XXXX-XXXX-XXXX: регистр, пробелы и дефисы при вводе не важны.</summary>
        public static string? NormalizeSerial(string? text)
        {
            string raw = new string((text ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            if (raw.Length == 16 && raw.StartsWith("STRS", StringComparison.Ordinal))
            {
                raw = raw.Substring(4);
            }

            if (raw.Length != 12)
            {
                return null;
            }

            return "STRS-" + raw.Substring(0, 4) + "-" + raw.Substring(4, 4) + "-" + raw.Substring(8, 4);
        }

        private async Task<LicenseActionResult> RequestWithSerialAsync(string action, string serial, CancellationToken cancel)
        {
            string? normalized = NormalizeSerial(serial);
            if (normalized == null)
            {
                return LicenseActionResult.Fail("bad_serial");
            }

            ApiReply reply = await api.PostAsync(action, NewRequest(normalized), cancel);
            return Accept(reply);
        }

        /// <summary>Ответ сервера с лицензией: сохраняем, только если подпись верна и лицензия для этого компьютера.</summary>
        private LicenseActionResult Accept(ApiReply reply)
        {
            if (!reply.Ok)
            {
                return LicenseActionResult.Fail(reply.Error ?? ApiReply.NoConnection, reply.TransfersLeft);
            }

            if (reply.License == null)
            {
                return LicenseActionResult.Fail("bad_license");
            }

            DateTime now = clock();
            LicenseStatus plain = LicenseEvaluator.Evaluate(reply.License, Computer, now, null, key);
            DateTime? issued = plain.License?.IssuedUtc;
            if (plain.State == LicenseState.BadSignature || plain.State == LicenseState.WrongComputer || !issued.HasValue)
            {
                return LicenseActionResult.Fail("bad_license");
            }

            // Настоящий сервер всегда выдаёт свежую лицензию. Старая (подставной сервер повторяет
            // когда-то полученный ответ, чтобы откатить метки времени) не принимается.
            if ((guard.ServerTimeUtc.HasValue && issued.Value + TimeGuard.Tolerance < guard.ServerTimeUtc.Value) ||
                (guard.ForbiddenUtc.HasValue && issued.Value <= guard.ForbiddenUtc.Value))
            {
                return LicenseActionResult.Fail("bad_license");
            }

            LastProblem = null;
            guard.AcceptServerTime(issued.Value);
            Save(reply.License);

            LicenseStatus status = Status;
            if (status.State == LicenseState.ClockTampered)
            {
                return LicenseActionResult.Fail("clock");
            }

            return status.State == LicenseState.Expired ? LicenseActionResult.Fail("expired") : LicenseActionResult.Success();
        }

        /// <summary>Метки времени для правил; <paramref name="hasLicense"/> — лицензия лежит (тогда метки обязательны).</summary>
        private TimeMarks Marks(bool hasLicense) => new TimeMarks
        {
            LastSeenUtc = guard.LastSeenUtc,
            ServerTimeUtc = guard.ServerTimeUtc,
            ForbiddenUtc = guard.ForbiddenUtc,

            // Время сервера запоминается при каждой полученной лицензии. Лицензия есть, а его нет —
            // метки удалили (например, чтобы вернуть старую копию файла при переведённых часах).
            // Запись меток запрещена — тоже подделка.
            Missing = hasLicense && (!guard.ServerTimeUtc.HasValue || guard.Broken),
        };

        /// <summary>Лицензия снята (освободили, перенесли, отозвали): файл удаляется, его копии больше не примутся.</summary>
        private void Remove(LicenseData license)
        {
            guard.Forbid(license.IssuedUtc ?? clock());
            Save(null);
        }

        private ApiRequest NewRequest(string? serial) => new ApiRequest
        {
            Serial = serial,
            Hwid = Computer.ToStorage(),
            PluginVersion = PluginVersion,
            CorelVersion = CorelVersion,
        };

        private void Save(SignedDocument? doc)
        {
            lock (sync)
            {
                document = doc;
                loaded = true;
                try
                {
                    if (doc == null)
                    {
                        if (File.Exists(FilePath))
                        {
                            File.Delete(FilePath);
                        }
                    }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                        string temp = FilePath + ".tmp";
                        File.WriteAllText(temp, Json.Write(doc), new UTF8Encoding(false));
                        if (File.Exists(FilePath))
                        {
                            File.Delete(FilePath);
                        }

                        File.Move(temp, FilePath);
                    }
                }
                catch (Exception)
                {
                    // Нет доступа к папке — лицензия действует до закрытия CorelDRAW, запишется в следующий раз.
                }
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }

        private static SignedDocument? ReadFile(string path)
        {
            try
            {
                return File.Exists(path) ? Json.TryRead<SignedDocument>(File.ReadAllText(path, Encoding.UTF8)) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
