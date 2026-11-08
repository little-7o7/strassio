// Закрытый API для хаба little_7o7 (/api/platform/*) — им хаб заменяет админку /adminpanel (владелец 2026-11-08:
// «dobav k little_7o7 admin panel xab, i udali adminku ot strassio»). Описание — docs/platform-api.md.
//
// Без верной подписи (core/platformAuth.ts) ответ тот же, что на несуществующий адрес: 404 not_found — снаружи не видно,
// что API есть. Свои ошибки подписанных запросов — коды: missing (нет такого ключа, заявки…), bad_request, bad_client,
// confirm, already_has_key, password_required, wrong_password, password_not_set, github, too_many_requests.
// Опасное (заблокировать, удалить ключ, отозвать компьютер, удалить пробный, опубликовать обновление) — только с паролем
// админки Strassio (ADMIN_PASSWORD) в теле: хаб его не хранит, человек вводит его каждый раз (решение владельца 2026-11-08).
import { VERSION_PATTERN, isReleaseUrl, latestRelease, type ReleaseResult } from "./githubRelease.js";
import { FailureLimiter, type HttpRequest, type HttpResponse } from "./http.js";
import { same, verifySignature, type PlatformKeys } from "./platformAuth.js";
import { parseClient, type LicenseService } from "./service.js";
import type { UpdateRow } from "./store.js";

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
const ID = "(\\d{1,12})";
const REQUEST_STATUSES = ["new", "working", "rejected"] as const;

/** Клиент из плоских полей формы хаба (firstName, phone…); неверные почта, Telegram или день рождения — null. */
const clientFrom = (body: Body) => parseClient({ firstName: body.firstName, lastName: body.lastName, phone: body.phone, email: body.email, birthday: body.birthday, telegram: body.telegram });

/** Срок в днях: пусто — null (бессрочно), иначе целое 0…36500. */
function daysFrom(value: unknown): number | null | undefined {
  if (value === undefined || value === null || value === "") return null;
  const n = Number(value);
  return Number.isInteger(n) && n >= 0 && n <= 36500 ? n : undefined;
}

export class Platform {
  private readonly service: LicenseService;
  private readonly keys: PlatformKeys | null;
  private readonly adminPassword: string;
  private readonly spendNonce: PlatformOptions["spendNonce"];
  private readonly fetchRelease: (channel: string) => Promise<ReleaseResult>;
  private readonly now: () => number;
  private readonly passwordFailures: FailureLimiter;
  private readonly routes: Route[];

  constructor(o: PlatformOptions) {
    this.service = o.service;
    this.keys = o.keys;
    this.adminPassword = (o.adminPassword ?? "").trim();
    this.spendNonce = o.spendNonce;
    this.fetchRelease = o.fetchRelease ?? ((channel) => latestRelease(channel));
    this.now = o.now ?? Date.now;
    this.passwordFailures = o.passwordFailures ?? new FailureLimiter(10, 15 * 60_000, this.now);
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
      route("GET", "licenses", async (_m, _b, req) => ok({ licenses: await s.licenseViews((req.query.get("q") ?? "").slice(0, 200)) })),
      route("GET", `licenses/${SERIAL}`, async ([serial]) => {
        const license = await s.licenseView(serial);
        return license ? ok({ license }) : missing();
      }),
      route("POST", "licenses", async (_m, b) => {
        const client = clientFrom(b);
        if (!client) return fail(400, "bad_client");
        const days = daysFrom(b.days);
        const count = b.count === undefined || b.count === "" ? 1 : Number(b.count);
        if (days === undefined || !Number.isInteger(count) || count < 1 || count > 500) return bad();
        const keys = await s.createKeys({ count, days, note: typeof b.note === "string" ? b.note : "", client });
        return ok({ keys: keys.map((k) => k.serial) });
      }),
      route("POST", `licenses/${SERIAL}/block`, licenseAction("block"), true),
      route("POST", `licenses/${SERIAL}/unblock`, licenseAction("unblock")),
      route("POST", `licenses/${SERIAL}/extend`, async ([serial], b) => {
        const days = daysFrom(b.days);
        if (days === undefined || days === null) return bad();
        return (await s.adminLicense(serial, "extend", days)) ? ok() : missing();
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
        return file ? ok({ file }) : bad();
      }),
      route("POST", `licenses/${SERIAL}/delete`, async ([serial], b) => {
        const license = await s.licenseView(serial);
        if (!license) return missing();
        if (String(b.confirm ?? "").trim().toUpperCase() !== license.serial.slice(-4)) return fail(400, "confirm");
        return (await s.deleteLicense(license.serial)) ? ok() : missing();
      }, true),
      route("POST", `activations/${ID}/revoke`, async ([id]) => ((await s.revokeActivation(Number(id))) ? ok() : missing()), true),

      // ---------- Заявки ----------
      route("GET", "requests", async () => ok({ requests: await s.listRequests() })),
      route("POST", `requests/${ID}/status`, async ([id], b) => {
        if (!REQUEST_STATUSES.includes(b.status as (typeof REQUEST_STATUSES)[number])) return bad();
        return (await s.adminRequest(Number(id), b.status)) ? ok() : missing();
      }),
      route("POST", `requests/${ID}/key`, async ([id], b) => {
        const request = await s.findRequest(id);
        if (!request) return missing();
        if (request.serial) return fail(409, "already_has_key", { serial: request.serial });
        const days = daysFrom(b.days);
        if (days === undefined) return bad();
        const key = await s.requestKey(request.id, days);
        return key ? ok({ serial: key.serial }) : missing();
      }),
      route("POST", `requests/${ID}/delete`, async ([id]) => ((await s.adminRequest(Number(id), "delete")) ? ok() : missing())),

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

      // ---------- Журнал ----------
      route("GET", "audit", async (_m, _b, req) => {
        const n = Number(req.query.get("limit"));
        return ok({ log: await s.auditLog(Number.isInteger(n) && n > 0 ? Math.min(n, 500) : 200) });
      }),
    ];
  }
}

function updateView(u: UpdateRow) {
  return { channel: u.channel, version: u.version, minVersion: u.minVersion, url: u.url, sha256: u.sha256, notesRu: u.notesRu, notesEn: u.notesEn, createdAt: u.createdAt.toISOString() };
}
