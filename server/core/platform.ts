// Закрытый API для хаба little_7o7 (/api/platform/*) — им хаб заменяет админку /adminpanel (владелец 2026-11-08:
// «dobav k little_7o7 admin panel xab, i udali adminku ot strassio»). Описание — docs/platform-api.md.
//
// Без верной подписи (core/platformAuth.ts) ответ тот же, что на несуществующий адрес: 404 not_found — снаружи не видно,
// что API есть. Свои ошибки подписанных запросов — коды: missing (нет такого ключа, заявки…), bad_request, bad_client,
// confirm, already_has_key, already_paid, too_much, no_rate, bad_base, not_forever, too_many_files, password_required,
// wrong_password, password_not_set, github, too_many_requests.
// Опасное (заблокировать, удалить ключ, отозвать компьютер, удалить пробный, опубликовать обновление, удалить начисление,
// поступление, договор или его файл) — только с паролем
// админки Strassio (ADMIN_PASSWORD) в теле: хаб его не хранит, человек вводит его каждый раз (решение владельца 2026-11-08).
import { VERSION_PATTERN, isReleaseUrl, latestRelease, type ReleaseResult } from "./githubRelease.js";
import { FailureLimiter, type HttpRequest, type HttpResponse } from "./http.js";
import { same, verifySignature, type PlatformKeys } from "./platformAuth.js";
import { UPDATES_DAYS } from "./billing.js";
import { tariffView } from "./money.js";
import { chargeFrom, contractFrom, dayFrom, daysFrom, fileFrom, given, isPaidMethod, partFrom, tariffFrom, termsFrom, type ChargeForm } from "./moneyInput.js";
import { fileView } from "./moneyViews.js";
import { parseClient, shortCodes, type LicenseService } from "./service.js";
import type { KeyTermsRow, TariffKind, UpdateRow } from "./store.js";
import { normalizeSerial, toActivationCode } from "./crypto.js";
import { backupName, type Backup } from "./backup.js";

export const PREFIX = "/api/platform/";

export interface PlatformOptions {
  /** Сервис лицензий с actor «hub»: в журнале видно, что сделал хаб. */
  service: LicenseService;
  keys: PlatformKeys | null;
  adminPassword: string | undefined;
  spendNonce: (nonce: string, untilSec: number, nowSec: number) => Promise<boolean>;
  fetchRelease?: (channel: string) => Promise<ReleaseResult>;
  now?: () => number;
  /** Неверный пароль админки: 10 раз за 15 минут — дальше ждать. */
  passwordFailures?: FailureLimiter;
  /** Копия всей базы для компьютеров владельца (core/backup.ts); нет — pc/backup отвечает 404. */
  backup?: Backup;
}

type Body = Record<string, unknown>;
type Handler = (m: string[], body: Body, req: HttpRequest) => Promise<HttpResponse>;
type Route = { method: "GET" | "POST"; path: RegExp; password?: boolean; run: Handler };

// Не «not_found»: плагин на этот код удаляет лицензию, а несуществующий адрес — не повод (см. http.ts).
const NOT_FOUND: HttpResponse = { status: 404, json: { ok: false, error: "no_route" } };
const ok = (data: Record<string, unknown> = {}): HttpResponse => ({ status: 200, json: { ok: true, ...data } });
const fail = (status: number, error: string, extra: Record<string, unknown> = {}): HttpResponse => ({ status, json: { ok: false, error, ...extra } });
const missing = () => fail(404, "missing");
const bad = () => fail(400, "bad_request");

const SERIAL = "([A-Za-z0-9-]{12,24})";
const CLIENT = "([A-Za-z0-9_-]{12})";
const DAY = 24 * 60 * 60 * 1000;
/** Кто в журнале: «hub» — хаб и старая админка вместе. */
const WHO: Record<string, string[]> = { hub: ["hub", "admin"], client: ["client"], site: ["site"] };

/** День ГГГГ-ММ-ДД по Ташкенту (UTC+5) → начало этого дня; плюс next — начало следующего. */
function tashkentDay(value: string | null, next = false): Date | undefined {
  if (!value || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return undefined;
  const t = Date.parse(value + "T00:00:00+05:00");
  return Number.isNaN(t) ? undefined : new Date(t + (next ? DAY : 0));
}
const ID = "(\\d{1,12})";
const REQUEST_STATUSES = ["new", "working", "rejected"] as const;

/** Клиент из плоских полей формы хаба (firstName, phone…); неверные почта, Telegram или день рождения — null. */
const clientFrom = (body: Body) => parseClient({ firstName: body.firstName, lastName: body.lastName, phone: body.phone, email: body.email, birthday: body.birthday, telegram: body.telegram });

export { parseAmount } from "./moneyInput.js";

export class Platform {
  private readonly service: LicenseService;
  private readonly keys: PlatformKeys | null;
  private readonly adminPassword: string;
  private readonly spendNonce: PlatformOptions["spendNonce"];
  private readonly fetchRelease: (channel: string) => Promise<ReleaseResult>;
  private readonly now: () => number;
  private readonly passwordFailures: FailureLimiter;
  private readonly backup: Backup | null;
  private readonly routes: Route[];

  constructor(o: PlatformOptions) {
    this.service = o.service;
    this.keys = o.keys;
    this.adminPassword = (o.adminPassword ?? "").trim();
    this.spendNonce = o.spendNonce;
    this.fetchRelease = o.fetchRelease ?? ((channel) => latestRelease(channel));
    this.now = o.now ?? Date.now;
    this.passwordFailures = o.passwordFailures ?? new FailureLimiter(10, 15 * 60_000, this.now);
    this.backup = o.backup ?? null;
    this.routes = this.buildRoutes();
  }

  /** Запрос на /api/platform/*: сначала подпись (в памяти), потом nonce (в базе), потом адрес. */
  async handle(req: HttpRequest): Promise<HttpResponse> {
    const qs = req.query.toString();
    const body = req.method === "POST" ? req.rawBody ?? "" : "";
    const nowSec = Math.floor(this.now() / 1000);
    const signed = verifySignature(this.keys, { method: req.method, pathAndQuery: req.path + (qs ? "?" + qs : ""), headers: req.headers, body }, nowSec);
    if (!signed || !(await this.spendNonce(signed.nonce, signed.until, nowSec))) return NOT_FOUND;

    const sub = req.path.substring(PREFIX.length);
    for (const r of this.routes) {
      const m = r.path.exec(sub);
      if (!m || r.method !== req.method) continue;
      const data = (req.body && typeof req.body === "object" && !Array.isArray(req.body) ? req.body : {}) as Body;
      if (r.password) {
        const problem = this.checkPassword(data.adminPassword);
        if (problem) return problem;
      }
      return r.run(m.slice(1), data, req);
    }
    return NOT_FOUND;
  }

  private checkPassword(given: unknown): HttpResponse | null {
    if (this.adminPassword.length < 8) return fail(503, "password_not_set");
    if (this.passwordFailures.blocked("hub")) return fail(429, "too_many_requests");
    const text = typeof given === "string" ? given.trim() : "";
    if (!text) return fail(403, "password_required");
    if (!same(text, this.adminPassword)) {
      this.passwordFailures.fail("hub");
      return fail(403, "wrong_password");
    }
    return null;
  }

  /** Начисление из формы хаба (moneyInput.ts); terms — цена ключа для «по цене ключа». */
  private charge(b: Body, terms: KeyTermsRow | null, noteField: "note" | "payNote", kinds: TariffKind[] = ["key"]): Promise<ChargeForm | null> {
    return chargeFrom(b, { billing: this.service.billing, terms, noteField, kinds });
  }

  /** Срок: свой (поле days) или тариф × периоды (навсегда — 0); ни того, ни другого — fallback. undefined — неверно. */
  private daysOr(b: Body, form: ChargeForm, fallback: number | null | undefined): number | null | undefined {
    if (given(b.days)) return daysFrom(b.days);
    if (form.termDays !== undefined) return form.termDays === null ? 0 : form.termDays * form.periods;
    return fallback;
  }

  private buildRoutes(): Route[] {
    const s = this.service;
    const route = (method: Route["method"], path: string, run: Handler, password = false): Route => ({ method, path: new RegExp(`^${path}$`), run, password });
    const licenseAction = (action: string, value: (b: Body) => unknown = () => undefined): Handler => async ([serial], b) => {
      const license = await s.adminLicense(serial, action, value(b));
      return license ? ok() : missing();
    };

    return [
      route("GET", "ping", async () => ok({ project: "strassio", time: new Date(this.now()).toISOString() })),
      route("GET", "health", async () => {
        const t = Date.now();
        await s.ping();
        return ok({ db: "ok", ms: Date.now() - t, time: new Date(this.now()).toISOString(), region: process.env.VERCEL_REGION ?? "", commit: (process.env.VERCEL_GIT_COMMIT_SHA ?? "").slice(0, 7) });
      }),
      route("GET", "summary", async () => ok(await s.summary())),

      // ---------- Ключи ----------
      route("GET", "licenses", async (_m, _b, req) => ok({ licenses: req.query.get("all") === "1" ? await s.allKeys() : await s.licenseViews((req.query.get("q") ?? "").slice(0, 200)) })),
      route("GET", `licenses/${SERIAL}/history`, async ([serial]) => {
        const history = await s.keyHistory(serial);
        return history ? ok({ history }) : missing();
      }),
      route("GET", `licenses/${SERIAL}`, async ([serial]) => {
        const license = await s.licenseView(serial);
        return license ? ok({ license }) : missing();
      }),
      route("POST", "licenses", async (_m, b) => {
        const client = clientFrom(b);
        if (!client) return fail(400, "bad_client");
        const pay = await this.charge(b, null, "payNote");
        if (!pay) return bad();
        const days = this.daysOr(b, pay, null);
        const count = b.count === undefined || b.count === "" ? 1 : Number(b.count);
        if (days === undefined || !Number.isInteger(count) || count < 1 || count > 500) return bad();
        const keys = await s.createKeys({ count, days, note: typeof b.note === "string" ? b.note : "", client, payment: pay.charge });
        return ok({ keys: keys.map((k) => k.serial) });
      }),
      route("POST", `licenses/${SERIAL}/block`, licenseAction("block"), true),
      route("POST", `licenses/${SERIAL}/unblock`, licenseAction("unblock")),
      route("POST", `licenses/${SERIAL}/extend`, async ([serial], b) => {
        const pay = await this.charge(b, await s.termsOf(serial), "payNote");
        if (!pay) return bad();
        const days = this.daysOr(b, pay, undefined);
        if (days === undefined || days === null) return bad();
        return (await s.extendKey(serial, days, pay.charge)) ? ok() : missing();
      }),
      route("POST", `licenses/${SERIAL}/payments`, async ([serial], b) => {
        const pay = await this.charge(b, await s.termsOf(serial), "note");
        if (!pay?.charge) return bad();
        const kind = b.kind === "sale" || b.kind === "renewal" ? b.kind : undefined;
        const payment = await s.addPayment(serial, { ...pay.charge, kind });
        return payment ? ok({ payment }) : missing();
      }),
      // Обновления ключа «навсегда»: тариф «Обновления на год» (или своя сумма) — ещё год обновлений.
      route("POST", `licenses/${SERIAL}/updates`, async ([serial], b) => {
        const pay = await this.charge(b, null, "note", ["updates"]);
        if (!pay?.charge) return bad();
        const days = given(b.days) ? daysFrom(b.days) : (pay.termDays ?? UPDATES_DAYS) * pay.periods;
        if (!days) return bad();
        const r = await s.payUpdates(serial, pay.charge, days);
        return r === "missing" ? missing() : r === "not_forever" ? fail(409, "not_forever") : ok({ payment: r });
      }),
      route("POST", `licenses/${SERIAL}/terms`, async ([serial], b) => {
        const t = await termsFrom(b, s.billing);
        if (!t) return bad();
        const key = await s.findKey(serial);
        return key ? ok({ terms: await s.billing.setTerms(key.serial, t) }) : missing();
      }),
      route("POST", `licenses/${SERIAL}/transfers`, licenseAction("reset_transfers")),
      route("POST", `licenses/${SERIAL}/note`, licenseAction("note", (b) => (typeof b.note === "string" ? b.note : ""))),
      route("POST", `licenses/${SERIAL}/client`, async ([serial], b) => {
        const client = clientFrom(b);
        if (!client) return fail(400, "bad_client");
        return (await s.adminLicense(serial, "client", client)) ? ok() : missing();
      }),
      route("POST", `licenses/${SERIAL}/offline`, async ([serial], b) => {
        const hwid = typeof b.hwid === "string" ? b.hwid.replace(/\s+/g, "") : b.hwid;
        if (!(await s.licenseView(serial))) return missing();
        const file = await s.offline(serial, hwid);
        return file ? ok({ file, code: toActivationCode(file) }) : bad();
      }),
      route("POST", `licenses/${SERIAL}/delete`, async ([serial], b) => {
        const license = await s.licenseView(serial);
        if (!license) return missing();
        if (String(b.confirm ?? "").trim().toUpperCase() !== license.serial.slice(-4)) return fail(400, "confirm");
        return (await s.deleteLicense(license.serial)) ? ok() : missing();
      }, true),
      route("POST", `activations/${ID}/revoke`, async ([id]) => ((await s.revokeActivation(Number(id))) ? ok() : missing()), true),

      // ---------- Клиенты, компьютеры, на что взглянуть (только чтение) ----------
      route("GET", "clients", async () => ok(await s.clients())),
      route("GET", `clients/${CLIENT}`, async ([id]) => {
        const client = (await s.clients()).clients.find((c) => c.id === id);
        return client ? ok({ client, money: await s.clientMoney(id) }) : missing();
      }),
      route("POST", `clients/${CLIENT}/contracts`, async ([id], b) => {
        const c = contractFrom(b);
        if (!c) return bad();
        const key = await s.clientKeyOf(id);
        if (!key) return missing();
        const contract = await s.billing.saveContract(null, key, c);
        return contract === "missing" ? missing() : ok({ contract: { id: contract.id } });
      }),
      // Продлить все ключи клиента по их ценам на periods периодов (заплатил вперёд за 3 месяца — periods 3).
      route("POST", `clients/${CLIENT}/extend`, async ([id], b) => {
        const periods = given(b.periods) ? Number(b.periods) : 1;
        const method = given(b.method) ? b.method : "cash";
        const dueOn = dayFrom(b.dueOn);
        if (!Number.isInteger(periods) || periods < 1 || periods > 36 || (method !== "debt" && !isPaidMethod(method)) || dueOn === undefined) return bad();
        const keys = await s.extendClient(id, { periods, method: method === "debt" ? "" : method, dueOn, note: typeof b.note === "string" ? b.note.trim().slice(0, 200) : "" });
        return keys ? ok({ keys }) : missing();
      }),
      route("POST", `contracts/${ID}`, async ([id], b) => {
        const c = contractFrom(b);
        if (!c) return bad();
        const contract = await s.billing.saveContract(Number(id), "", c);
        return contract === "missing" ? missing() : ok({ contract: { id: contract.id } });
      }),
      route("POST", `contracts/${ID}/close`, async ([id]) => ((await s.billing.closeContract(Number(id))) ? ok() : missing())),
      route("POST", `contracts/${ID}/delete`, async ([id]) => ((await s.billing.deleteContract(Number(id))) ? ok() : missing()), true),
      route("POST", `contracts/${ID}/files`, async ([id], b) => {
        const f = fileFrom(b);
        if (!f) return bad();
        const r = await s.billing.addFile(Number(id), f);
        return r === "missing" ? missing() : r === "too_many" ? fail(409, "too_many_files") : ok({ file: fileView(r) });
      }),
      route("GET", `contract-files/${ID}`, async ([id]) => {
        const f = await s.billing.file(Number(id));
        return f ? ok({ file: { ...fileView(f), data: f.data } }) : missing();
      }),
      route("POST", `contract-files/${ID}/delete`, async ([id]) => ((await s.billing.deleteFile(Number(id))) ? ok() : missing()), true),
      route("GET", "computers", async () => ok(await s.computers())),
      route("GET", "attention", async () => ok(await s.attention())),

      // ---------- Заявки ----------
      route("GET", "requests", async () => ok({ requests: await s.requestsWithOffers() })),
      route("POST", `requests/${ID}/status`, async ([id], b) => {
        if (!REQUEST_STATUSES.includes(b.status as (typeof REQUEST_STATUSES)[number])) return bad();
        return (await s.adminRequest(Number(id), b.status)) ? ok() : missing();
      }),
      route("POST", `requests/${ID}/key`, async ([id], b) => {
        const request = await s.findRequest(id);
        if (!request) return missing();
        if (request.serial) return fail(409, "already_has_key", { serial: request.serial });
        const pay = await this.charge(b, null, "payNote");
        if (!pay) return bad();
        const days = this.daysOr(b, pay, null);
        if (days === undefined) return bad();
        const key = await s.requestKey(request.id, days, pay.charge);
        return key ? ok({ serial: key.serial }) : missing();
      }),
      route("POST", `requests/${ID}/delete`, async ([id]) => ((await s.adminRequest(Number(id), "delete")) ? ok() : missing())),

      // ---------- Деньги ----------
      route("GET", "money", async () => ok(await s.money())),
      route("GET", "rate", async () => ok({ rate: await s.billing.rateToday() })),
      // Утро: автоблокировка долгов и сообщения владельцу — их шлёт хаб своим ботом (своего cron у Strassio нет).
      route("POST", "daily", async () => ok(await s.dailyMessages())),
      // Курс дня от хаба: { date: "ГГГГ-ММ-ДД", source: "cbu", rates: { USD: 12650.5, … } } — сумов за единицу; берём доллар.
      route("POST", "rates", async (_m, b) => {
        const usd = b.rates && typeof b.rates === "object" ? Number((b.rates as Record<string, unknown>).USD) : NaN;
        const got = await s.billing.acceptRate(String(b.date ?? ""), usd);
        return got === "ok" ? ok({ rate: await s.billing.rateToday() }) : fail(400, got);
      }),
      // Скидка за количество: от minKeys ключей у клиента — percent от цены (0 — выключена).
      route("POST", "money-settings", async (_m, b) => {
        const minKeys = given(b.volumeMin) ? Number(b.volumeMin) : 0;
        const percent = given(b.volumePercent) ? Number(String(b.volumePercent).replace(",", ".").replace(/%$/, "")) : 0;
        if (!Number.isInteger(minKeys) || minKeys < 0 || minKeys > 1000 || !Number.isFinite(percent) || percent < 0 || percent >= 100) return bad();
        return ok({ volume: await s.billing.saveVolume({ minKeys, percent: Math.round(percent * 100) / 100 }) });
      }),
      route("GET", "tariffs", async () => ok({ tariffs: (await s.billing.tariffs()).map(tariffView) })),
      route("POST", "tariffs", async (_m, b) => {
        const t = tariffFrom(b);
        if (!t) return bad();
        const saved = await s.billing.saveTariff(null, t);
        return typeof saved === "string" ? fail(400, saved) : ok({ tariff: tariffView(saved) });
      }),
      route("POST", `tariffs/${ID}`, async ([id], b) => {
        const t = tariffFrom(b);
        if (!t) return bad();
        const saved = await s.billing.saveTariff(Number(id), t);
        return saved === "missing" ? missing() : typeof saved === "string" ? fail(400, saved) : ok({ tariff: tariffView(saved) });
      }),
      // Пришли деньги по начислению — целиком или частью, другой валютой — по курсу дня.
      route("POST", `payments/${ID}/paid`, async ([id], b) => {
        const part = partFrom(b);
        if (!part) return bad();
        const r = await s.billing.pay(Number(id), part);
        return r === "missing" ? missing() : r === "already" ? fail(409, "already_paid") : r === "too_much" ? fail(400, "too_much") : r === "no_rate" ? fail(409, "no_rate") : ok({ payment: r });
      }),
      route("POST", `payments/${ID}/delete`, async ([id]) => ((await s.billing.deletePayment(Number(id))) ? ok() : missing()), true),
      route("POST", `parts/${ID}/delete`, async ([id]) => ((await s.billing.deletePart(Number(id))) ? ok() : missing()), true),

      // ---------- Пробные ----------
      route("GET", "trials", async () => {
        const trials = await s.listTrials();
        return ok({ total: trials.length, active: trials.filter((t) => t.active).length, trials });
      }),
      route("POST", `trials/${ID}/delete`, async ([id]) => ((await s.deleteTrial(Number(id))) ? ok() : missing()), true),

      // ---------- Обновления ----------
      route("GET", "updates", async () => {
        const u = await s.updates();
        return ok({ updates: { stable: u.stable ? updateView(u.stable) : null, beta: u.beta ? updateView(u.beta) : null } });
      }),
      route("GET", "release", async (_m, _b, req) => {
        const result = await this.fetchRelease(req.query.get("channel") === "beta" ? "beta" : "stable");
        return result.ok ? ok({ release: result.release }) : fail(502, "github", { details: result.error });
      }),
      route("POST", "updates", async (_m, b) => {
        const version = String(b.version ?? "").trim();
        const minVersion = String(b.minVersion ?? "").trim();
        const url = String(b.url ?? "").trim();
        const sha256 = String(b.sha256 ?? "").trim().toLowerCase();
        const channel = b.channel === "beta" ? "beta" : b.channel === "stable" || b.channel === undefined ? "stable" : null;
        if (!channel || !VERSION_PATTERN.test(version) || (minVersion && !VERSION_PATTERN.test(minVersion)) || !isReleaseUrl(url) || !/^[0-9a-f]{64}$/.test(sha256)) return bad();
        await s.publishUpdate({ channel, version, minVersion, url, sha256, notesRu: String(b.notesRu ?? "").slice(0, 4000), notesEn: String(b.notesEn ?? "").slice(0, 4000) });
        return ok();
      }, true),

      // ---------- Копия базы для компьютеров (Little Hub) ----------
      // live=1&have=<время> — та же, что у компьютера (в журнале с тех пор ничего), — 204 без выгрузки. Время копии —
      // последняя запись журнала (не моложе 13 знаков: хаб принимает только такое).
      route("GET", "pc/backup", async (_m, _b, req) => {
        if (!this.backup) return missing();
        const made = Math.max(await this.backup.made(), 1_000_000_000_000);
        if (req.query.get("have") === String(made)) return { status: 204 };
        const body = await this.backup.dump(new Date(this.now()));
        const headers = { "x-backup-name": backupName(this.now()), "x-backup-bytes": String(body.length), "x-backup-made": String(made) };
        return { status: 200, file: { body, type: "application/gzip", headers } };
      }),

      // ---------- Журнал ----------
      route("GET", "audit", async (_m, _b, req) => {
        const n = Number(req.query.get("limit"));
        const limit = Number.isInteger(n) && n > 0 ? Math.min(n, 500) : 200;
        const q = req.query;
        const serial = q.get("serial") ? normalizeSerial(q.get("serial")) : null;
        if (q.get("serial") && !serial) return ok({ log: [] });
        const action = (q.get("action") ?? "").trim();
        const before = Number.isSafeInteger(Number(q.get("before"))) ? Number(q.get("before")) : 0;
        const filter = {
          serial: serial ?? undefined,
          actors: WHO[q.get("who") ?? ""],
          action: /^[a-z_]{2,40}$/.test(action) ? action : undefined,
          from: tashkentDay(q.get("from")),
          to: tashkentDay(q.get("to"), true),
          before: before > 0 ? before : undefined,
        };
        const plain = !filter.serial && !filter.actors && !filter.action && !filter.from && !filter.to && !filter.before;
        const log = plain ? await s.auditLog(limit) : await s.auditSearch(filter, limit);
        // Полные коды компьютеров (в журнале — для поиска подозрительных ключей) хабу — короткими, как в плагине.
        return ok({ log: log.map((e) => ({ ...e, details: shortCodes(e.details) })) });
      }),
    ];
  }
}

function updateView(u: UpdateRow) {
  return { channel: u.channel, version: u.version, minVersion: u.minVersion, url: u.url, sha256: u.sha256, notesRu: u.notesRu, notesEn: u.notesEn, createdAt: u.createdAt.toISOString() };
}
