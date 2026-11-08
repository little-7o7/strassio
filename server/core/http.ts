// Маршруты API (SPEC 15) без привязки к платформе: адаптер (Vercel, Cloudflare, локальный сервер)
// переводит свой запрос в HttpRequest и отдаёт HttpResponse как есть.
import type { Platform } from "./platform.js";
import type { LicenseService } from "./service.js";

export interface HttpRequest {
  method: string;
  path: string;
  query: URLSearchParams;
  headers: Record<string, string | undefined>;
  body: unknown;
  /** Тело как пришло (текст) — по нему проверяется подпись хаба (/api/platform). */
  rawBody?: string;
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

/**
 * Несуществующий адрес. Не «not_found»: на этот код (нет такого ключа) плагин удаляет лицензию, и сменённый
 * адрес или правило в vercel.json разом отключили бы всех покупателей.
 */
const NO_ROUTE: HttpResponse = { status: 404, json: { ok: false, error: "no_route" } };

const CLIENT_ACTIONS = ["activate", "transfer", "check", "deactivate", "trial"] as const;
const MY_LICENSE_ACTIONS = ["lookup", "release", "offline"] as const;

/** Считает только неудачи: после limit несуществующих ключей за windowMs адрес ждёт (перебор бессмыслен). */
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
    private readonly clientLimiter = new RateLimiter(30, 60_000),
    private readonly lookupFailures = new FailureLimiter(10, 15 * 60_000),
    private readonly requestLimiter = new RateLimiter(5, 60 * 60_000),
  ) {}

  private platform: Platform | null = null;

  /** Закрытый API для хаба little_7o7 (/api/platform/*). Без него эти адреса отвечают 404, как любые несуществующие. */
  usePlatform(platform: Platform): this {
    this.platform = platform;
    return this;
  }

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

    // Хаб little_7o7: подпись проверяется по пути как он пришёл, поэтому — до обрезки «/» в конце.
    if (req.path.startsWith("/api/platform/")) {
      return this.platform ? this.platform.handle(req) : NO_ROUTE;
    }

    // Старой админки (/adminpanel, /api/admin/*) больше нет: всё в хабе little_7o7 (владелец 2026-11-08), здесь
    // эти адреса отвечают «нет такого адреса». Главная «/» — статический сайт.

    // Своего cron у Strassio нет (владелец 2026-11-08 09:35: всё через хаб): утреннюю сводку и автоблокировку
    // запускает хаб каждое утро подписанным POST /api/platform/daily.

    if (req.method === "GET" && path === "/api/health") {
      return { status: 200, json: { ok: true } };
    }

    // Цены для сайта (тарифы с галочкой «на сайте» из хаба). Открыто: это то же, что видно на странице.
    if (req.method === "GET" && path === "/api/prices") {
      if (!this.clientLimiter.allow(req.ip)) return tooMany();
      return { status: 200, json: { ok: true, prices: await this.service.sitePrices() } };
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
      // Ввод ключа и перенос прямо из плагина (старые версии) — тоже только с почтой ключа.
      if (clientAction === "activate" || clientAction === "transfer") {
        if (this.lookupFailures.blocked(req.ip)) return tooMany();
        const emailProblem = await this.service.verifyEmail(body.serial, body.email);
        if (emailProblem) {
          if (emailProblem === "wrong_email") this.lookupFailures.fail(req.ip);
          return { status: 403, json: { ok: false, error: emailProblem } };
        }
      }
      const result = await this.service[clientAction](body);
      return { status: result.ok ? 200 : result.error === "bad_request" ? 400 : 403, json: result };
    }

    // Сайт: «Моя лицензия» — по одному ключу. Неудачные попытки (нет такого ключа) ограничены строго.
    const myAction = MY_LICENSE_ACTIONS.find((a) => path === "/api/mylicense/" + a);
    if (myAction) {
      if (req.method !== "POST") return { status: 405, json: { ok: false, error: "method" } };
      if (!this.clientLimiter.allow(req.ip)) return tooMany();
      if (this.lookupFailures.blocked(req.ip)) return tooMany();
      const result =
        myAction === "lookup" ? await this.service.siteLookup(body.serial, body.email) :
        myAction === "release" ? await this.service.siteRelease(body.serial, body.activationId, body.email) :
        await this.service.siteOffline(body.serial, body.hwid, body.email);
      if (!result.ok && (result.error === "wrong_email" || (result.error === "not_found" && myAction === "lookup"))) this.lookupFailures.fail(req.ip);
      return { status: result.ok ? 200 : result.error === "bad_request" ? 400 : 403, json: result };
    }

    // Сайт: заявка на покупку (страница /buy). Не больше 5 заявок в час с одного адреса;
    // поле-ловушка «website» заполняют только боты — им отвечаем «готово», но ничего не сохраняем.
    if (path === "/api/site/request") {
      if (req.method !== "POST") return { status: 405, json: { ok: false, error: "method" } };
      if (!this.requestLimiter.allow(req.ip)) return tooMany();
      if (typeof body.website === "string" && body.website.trim()) return { status: 200, json: { ok: true } };
      const result = await this.service.submitRequest(body.client, body.message, body.tariff);
      return { status: result.ok ? 200 : 400, json: result };
    }

    // Сайт: активация по коду компьютера (страница /activate).
    if (path === "/api/site/activate") {
      if (req.method !== "POST") return { status: 405, json: { ok: false, error: "method" } };
      if (!this.clientLimiter.allow(req.ip)) return tooMany();
      if (this.lookupFailures.blocked(req.ip)) return tooMany();
      const result = await this.service.siteActivate(body.serial, body.hwid, body.transfer === true, body.email);
      if (!result.ok && (result.error === "wrong_email" || result.error === "not_found")) this.lookupFailures.fail(req.ip);
      return { status: result.ok ? 200 : result.error === "bad_request" ? 400 : 403, json: result };
    }

    return NO_ROUTE;
  }
}

function ok(data: Record<string, unknown>): HttpResponse {
  return { status: 200, json: { ok: true, ...data } };
}

function tooMany(): HttpResponse {
  return { status: 429, json: { ok: false, error: "too_many_requests" } };
}
