// Маршруты API (SPEC 15) без привязки к платформе: адаптер (Vercel, Cloudflare, локальный сервер)
// переводит свой запрос в HttpRequest и отдаёт HttpResponse как есть.
import { createHash, timingSafeEqual } from "node:crypto";
import { ADMIN_PAGE } from "./adminPage.js";
import type { LicenseService } from "./service.js";

export interface HttpRequest {
  method: string;
  path: string;
  query: URLSearchParams;
  headers: Record<string, string | undefined>;
  body: unknown;
  ip: string;
}

export interface HttpResponse {
  status: number;
  json?: unknown;
  html?: string;
}

/**
 * Ограничение частоты: не больше limit запросов за windowMs с одного адреса. Живёт в памяти одного
 * экземпляра функции — от перебора ключей этого хватает; для строгого общего лимита нужен Redis/KV.
 */
export class RateLimiter {
  private readonly hits = new Map<string, number[]>();

  constructor(
    private readonly limit: number,
    private readonly windowMs: number,
    private readonly now: () => number = Date.now,
  ) {}

  allow(key: string): boolean {
    const t = this.now();
    const recent = (this.hits.get(key) ?? []).filter((x) => t - x < this.windowMs);
    if (recent.length >= this.limit) {
      this.hits.set(key, recent);
      return false;
    }

    recent.push(t);
    this.hits.set(key, recent);
    if (this.hits.size > 10000) this.hits.clear(); // память не растёт бесконечно
    return true;
  }
}

const CLIENT_ACTIONS = ["activate", "transfer", "check", "deactivate", "trial"] as const;
const RECOVERY_ACTIONS = ["lookup", "release", "offline"] as const;

/** Считает только неудачи: после limit неверных ключей восстановления за windowMs адрес ждёт. */
export class FailureLimiter {
  private readonly fails = new Map<string, number[]>();

  constructor(
    private readonly limit: number,
    private readonly windowMs: number,
    private readonly now: () => number = Date.now,
  ) {}

  blocked(key: string): boolean {
    const t = this.now();
    const recent = (this.fails.get(key) ?? []).filter((x) => t - x < this.windowMs);
    this.fails.set(key, recent);
    return recent.length >= this.limit;
  }

  fail(key: string): void {
    const list = this.fails.get(key) ?? [];
    list.push(this.now());
    this.fails.set(key, list);
    if (this.fails.size > 10000) this.fails.clear();
  }
}

export class App {
  constructor(
    private readonly service: LicenseService,
    adminPassword: string | undefined,
    private readonly clientLimiter = new RateLimiter(30, 60_000),
    private readonly adminLimiter = new RateLimiter(10, 15 * 60_000),
    private readonly recoveryFailures = new FailureLimiter(10, 15 * 60_000),
  ) {
    // Пробел или перенос строки по краям легко вставить в поле Vercel вместе с паролем — не считаем их.
    this.adminPassword = adminPassword?.trim();
  }

  private readonly adminPassword: string | undefined;

  async handle(req: HttpRequest): Promise<HttpResponse> {
    try {
      return await this.route(req);
    } catch (error) {
      console.error(error);
      return { status: 500, json: { ok: false, error: "server_error" } };
    }
  }

  private async route(req: HttpRequest): Promise<HttpResponse> {
    const path = req.path.replace(/\/+$/, "") || "/";
    const body = (req.body && typeof req.body === "object" ? req.body : {}) as Record<string, unknown>;

    // Админка — /adminpanel (vercel.json переписывает его сюда как /admin). Главная «/» — статический сайт.
    if (req.method === "GET" && (path === "/admin" || path === "/adminpanel")) {
      return { status: 200, html: ADMIN_PAGE };
    }

    if (req.method === "GET" && path === "/api/health") {
      return { status: 200, json: { ok: true } };
    }

    if (req.method === "GET" && path === "/api/update/latest") {
      if (!this.clientLimiter.allow(req.ip)) return tooMany();
      const doc = await this.service.latestUpdate(req.query.get("channel") ?? "stable");
      return doc ? { status: 200, json: { ok: true, update: doc } } : { status: 404, json: { ok: false, error: "no_update" } };
    }

    const clientAction = CLIENT_ACTIONS.find((a) => path === "/api/" + a);
    if (clientAction) {
      if (req.method !== "POST") return { status: 405, json: { ok: false, error: "method" } };
      if (!this.clientLimiter.allow(req.ip)) return tooMany();
      const result = await this.service[clientAction](body);
      return { status: result.ok ? 200 : result.error === "bad_request" ? 400 : 403, json: result };
    }

    // Сайт: восстановление лицензии по ключу восстановления. Неудачные попытки ограничены строго —
    // перебирать ключ восстановления бессмысленно.
    const recoveryAction = RECOVERY_ACTIONS.find((a) => path === "/api/recovery/" + a);
    if (recoveryAction) {
      if (req.method !== "POST") return { status: 405, json: { ok: false, error: "method" } };
      if (!this.clientLimiter.allow(req.ip)) return tooMany();
      if (this.recoveryFailures.blocked(req.ip)) return tooMany();
      const result =
        recoveryAction === "lookup" ? await this.service.recoveryLookup(body.serial, body.recovery) :
        recoveryAction === "release" ? await this.service.recoveryRelease(body.serial, body.recovery, body.activationId) :
        await this.service.recoveryOffline(body.serial, body.recovery, body.hwid);
      if (!result.ok && result.error === "bad_recovery") this.recoveryFailures.fail(req.ip);
      return { status: result.ok ? 200 : result.error === "bad_request" ? 400 : 403, json: result };
    }

    if (path.startsWith("/api/admin/")) {
      return this.admin(req, path.substring("/api/admin/".length), body);
    }

    return { status: 404, json: { ok: false, error: "not_found" } };
  }

  private async admin(req: HttpRequest, action: string, body: Record<string, unknown>): Promise<HttpResponse> {
    if (!this.adminPassword || this.adminPassword.length < 8) {
      return { status: 503, json: { ok: false, error: "admin_disabled" } };
    }

    if (!this.isAdmin(req.headers["authorization"])) {
      // Считаются только неудачные попытки — чтобы работа в админке не упиралась в лимит.
      if (!this.adminLimiter.allow(req.ip)) return tooMany();
      return { status: 401, json: { ok: false, error: "unauthorized" } };
    }

    const s = this.service;
    switch (req.method + " " + action) {
      case "POST login":
        return ok({});
      case "POST keys": {
        const keys = await s.createKeys({
          count: Number(body.count) || 1,
          days: Number(body.days) || null,
          maxPcs: Number(body.maxPcs) || 1,
          note: typeof body.note === "string" ? body.note : "",
        });
        return ok({ keys, serials: keys.map((k) => k.serial) });
      }
      case "GET licenses":
        return ok({ licenses: await s.search(req.query.get("q") ?? "") });
      case "POST license": {
        const license = await s.adminLicense(body.serial, String(body.action ?? ""), body.value);
        return license ? ok({ license }) : { status: 404, json: { ok: false, error: "not_found" } };
      }
      case "POST revoke":
        return (await s.revokeActivation(Number(body.id))) ? ok({}) : { status: 404, json: { ok: false, error: "not_found" } };
      case "POST offline": {
        const license = await s.offline(body.serial, body.hwid);
        return license ? ok({ license }) : { status: 400, json: { ok: false, error: "bad_request" } };
      }
      case "POST update": {
        const version = String(body.version ?? "").trim();
        const url = String(body.url ?? "").trim();
        const sha256 = String(body.sha256 ?? "").trim().toLowerCase();
        if (!/^\d+\.\d+\.\d+$/.test(version) || !/^https:\/\//.test(url) || !/^[0-9a-f]{64}$/.test(sha256)) {
          return { status: 400, json: { ok: false, error: "bad_request" } };
        }

        await s.publishUpdate({
          channel: String(body.channel ?? "stable"),
          version,
          url,
          sha256,
          notesRu: String(body.notesRu ?? ""),
          notesEn: String(body.notesEn ?? ""),
          minVersion: String(body.minVersion ?? ""),
        });
        return ok({});
      }
      case "GET audit":
        return ok({ log: await s.auditLog() });
      default:
        return { status: 404, json: { ok: false, error: "not_found" } };
    }
  }

  private isAdmin(header: string | undefined): boolean {
    // Браузер кодирует пароль (encodeURIComponent): в заголовке нельзя русские буквы.
    const given = decodeHeader((header ?? "").replace(/^Bearer\s+/i, "")).trim();
    const a = createHash("sha256").update(given).digest();
    const b = createHash("sha256").update(this.adminPassword!).digest();
    return given.length > 0 && timingSafeEqual(a, b);
  }
}

function decodeHeader(value: string): string {
  try {
    return decodeURIComponent(value);
  } catch {
    return value;
  }
}

function ok(data: Record<string, unknown>): HttpResponse {
  return { status: 200, json: { ok: true, ...data } };
}

function tooMany(): HttpResponse {
  return { status: 429, json: { ok: false, error: "too_many_requests" } };
}
