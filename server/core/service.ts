// Правила лицензий (SPEC 13.2–13.6) без привязки к платформе: всё время — через clock, данные —
// через Store, подпись — через Signer. Ответы клиенту: { ok: true, license } или { ok: false, error }.
import { Signer, SignedDocument, newSerial, normalizeSerial, toActivationCode } from "./crypto.js";
import { hwidDisplay, hwidMatches, parseHwid } from "./hwid.js";
import { birthdaysThisMonth, cleanup, clientId, clientKey, clientViews, computerBriefs, countVersions, dailyDigest, requestMessage, activationMessage, suspiciousKeys, trialInsights, type ClientView } from "./insights.js";
import { Billing, INCLUDED_UPDATES_DAYS, type ChargeInput } from "./billing.js";
import { dailyMoneyLines, isMonday, moneyReport, moneyTotals, paymentView, renewalOffer, requestOffer, tariffView, weeklyDigest, type PaymentView } from "./money.js";
import { clientMoneyView, termsView } from "./moneyViews.js";
import type { ActivationRow, AuditFilter, ClientInfo, KeyTermsRow, LicenseRow, RequestRow, Store, UpdateRow } from "./store.js";
import type { RateSource } from "./rates.js";
import type { Notifier } from "./telegram.js";
import { formatSum } from "./sums.js";

export const CHECK_EVERY_DAYS = 1;
export const TRIAL_DAYS = 14;
/** Переносов сколько угодно, но не чаще раза в 7 дней (решение автора, 22.09.2026). Раньше — через админа. */
export const TRANSFER_COOLDOWN_DAYS = 7;
/** Один ключ — один компьютер (решение автора, 22.09.2026). Колонка max_pcs в базе больше не читается. */
export const PCS_PER_KEY = 1;
const DAY = 24 * 60 * 60 * 1000;
/** «Срок скоро кончается» на Главной хаба. */
export const SOON_DAYS = 30;

export type ErrorCode =
  | "bad_request"
  | "not_found"
  | "blocked"
  | "expired"
  | "occupied"
  | "transfer_limit"
  | "not_activated"
  | "revoked"
  | "email_required"
  | "wrong_email"
  | "offline_seat";

export type ClientResult =
  | { ok: true; license: SignedDocument; client?: ClientInfo }
  | { ok: false; error: ErrorCode; transfersLeft?: number; nextTransferAt?: string | null };

export interface ClientRequest {
  serial?: unknown;
  hwid?: unknown;
  pluginVersion?: unknown;
  corelVersion?: unknown;
}

/** Что видит владелец ключа на сайте (страница «Моя лицензия»). Коды компьютеров — только короткие. */
export interface SiteLicenseView {
  serial: string;
  plan: string;
  status: string;
  expiresAt: string | null;
  maxPcs: number; // всегда PCS_PER_KEY
  /** 1 — перенос можно сделать сейчас, 0 — только после nextTransferAt (или через админа). */
  transfersLeft: number;
  nextTransferAt: string | null;
  computers: Array<{ id: number; code: string; active: boolean; firstAt: string; lastCheckAt: string; pluginVersion: string; corelVersion: string }>;
}

export type SiteResult<T> = { ok: true } & T | { ok: false; error: ErrorCode };

/** Пробный период в админке. */
export interface TrialView {
  id: number;
  /** Короткий код компьютера — как в окне «Лицензия». */
  code: string;
  startedAt: string;
  endsAt: string;
  daysLeft: number;
  active: boolean;
}

/** Ключ, как его показывает хаб little_7o7. status «expired» — срок вышел (в базе остаётся «active»). */
export interface AdminLicenseView {
  serial: string;
  status: "active" | "blocked" | "expired";
  expiresAt: string | null;
  daysLeft: number | null;
  transfersCount: number;
  nextTransferAt: string | null;
  note: string;
  createdAt: string;
  client: ClientInfo;
  /** Карточка клиента в хабе; "" — клиент не указан. */
  clientId: string;
  computers: Array<{ id: number; code: string; hwid: string; active: boolean; firstAt: string; lastCheckAt: string; pluginVersion: string; corelVersion: string }>;
  /**
   * Только в карточке ключа: начисления (новые сверху), готовое «предложить продлить» (null — ключ бессрочный или
   * заблокирован), закреплённая цена ключа, договор (по цене ключа или действующий договор клиента) и до какого числа
   * оплачены обновления ключа «навсегда».
   */
  payments?: PaymentView[];
  offer?: { text: string; tariffId: number | null; tariff: string | null } | null;
  terms?: ReturnType<typeof termsView> | null;
  contract?: { id: number; number: string; status: "active" | "closed" } | null;
  updatesUntil?: string | null;
}

export interface KeyOptions {
  count?: number;
  days?: number | null;
  note?: string;
  client?: unknown;
  /** Оплата за каждый ключ (продажа) — цена закрепляется за ключом; null — без оплаты. */
  payment?: ChargeInput | null;
}

/** Данные клиента из админки: обрезаются по длине; почта и день рождения проверяются. null — ошибка. */
export function parseClient(value: unknown): ClientInfo | null {
  const v = (value && typeof value === "object" ? value : {}) as Record<string, unknown>;
  const text = (x: unknown, max: number) => (typeof x === "string" ? x.trim().slice(0, max) : "");
  const client: ClientInfo = {
    firstName: text(v.firstName, 100),
    lastName: text(v.lastName, 100),
    phone: text(v.phone, 40),
    email: text(v.email, 200),
    birthday: text(v.birthday, 10),
    // «@name», «t.me/name», «https://t.me/name» → «name».
    telegram: text(v.telegram, 100).replace(/^(https?:\/\/)?(www\.)?(t\.me|telegram\.me)\//i, "").replace(/^@/, ""),
  };
  if (client.telegram && !/^[A-Za-z0-9_]{4,32}$/.test(client.telegram)) return null;
  if (client.email && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(client.email)) return null;
  if (client.birthday) {
    const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(client.birthday);
    const d = m ? new Date(client.birthday + "T00:00:00Z") : null;
    if (!m || !d || isNaN(d.getTime()) || d.toISOString().slice(0, 10) !== client.birthday || +m[1] < 1900) return null;
  }
  return client;
}

export const EMPTY_CLIENT: ClientInfo = { firstName: "", lastName: "", phone: "", email: "", birthday: "", telegram: "" };

const REQUEST_ACTIONS = ["new", "working", "rejected", "delete"] as const;
/** Сводки хаба читают все ключи разом: для малого бизнеса это сотни строк; больше — значит пора считать в базе. */
const ALL = 20000;

export class LicenseService {
  constructor(
    private readonly store: Store,
    private readonly signer: Signer,
    private readonly clock: () => Date = () => new Date(),
    /** Кто делает действия «админки» — для журнала: «admin» (старая /adminpanel) или «hub» (хаб little_7o7). */
    private readonly adminActor: string = "admin",
  ) {
    this.billing = new Billing(store, clock, adminActor);
  }

  /** Деньги: тарифы, договоры, цены ключей, начисления и поступления. */
  readonly billing: Billing;

  private notifier: Notifier | null = null;
  private hub = "https://little-7o7.vercel.app";

  /** Курс ЦБ для оплат сумами и итогов (rates.ts, cbuRate). */
  useRates(source: RateSource | null): this {
    this.billing.useRates(source);
    return this;
  }

  /** Сколько незаблокированных ключей у клиента (для скидки за количество); клиент не указан — 0. */
  private async liveKeysOf(client: ClientInfo): Promise<number> {
    const key = clientKey(client);
    return key ? (await this.store.allLicenses(ALL)).filter((l) => l.status !== "blocked" && clientKey(l.client) === key).length : 0;
  }

  /** Telegram владельцу: новая заявка, новая активация, утренняя сводка. null — бот выключен. */
  useNotifier(notifier: Notifier | null, hubUrl: string): this {
    this.notifier = notifier;
    this.hub = hubUrl;
    return this;
  }

  /** Сообщение не ушло (нет сети, Telegram не ответил) — не мешает основному действию. */
  private async notify(html: string): Promise<boolean> {
    return this.notifier ? this.notifier(html).catch(() => false) : false;
  }

  // ---------- Клиент (плагин) ----------

  /** Ввод ключа. Тот же компьютер (в т. ч. после переустановки Windows) — активация восстанавливается. */
  async activate(req: ClientRequest): Promise<ClientResult> {
    const input = this.parse(req);
    if (!input) return fail("bad_request");
    const license = await this.store.findLicense(input.serial);
    const problem = this.licenseProblem(license);
    if (problem) return fail(problem);

    const all = await this.store.activations(license!.id);
    const active = all.filter((a) => a.status === "active");
    const mine = active.find((a) => hwidMatches(parseHwid(a.hwid)!, input.parts));
    if (mine) {
      return this.refresh(license!, mine, input, "reactivate");
    }

    if (active.length >= PCS_PER_KEY) {
      // Попытка на другом компьютере, когда место занято: по таким попыткам хаб замечает подозрительные ключи.
      await this.log("client", "occupied", license!.serial, input.hwid);
      return { ok: false, error: "occupied", ...this.transferInfo(license!) };
    }

    const created = await this.store.insertActivation(this.newActivation(license!.id, input));
    if (await this.overLimit(created)) return { ok: false, error: "occupied", ...this.transferInfo(license!) };
    await this.log("client", "activate", license!.serial, input.hwid);
    await this.notify(activationMessage(license!, created, this.hub));
    return this.issue(license!, created.hwid);
  }

  /** «Перенести лицензию на этот компьютер»: самая давняя активация отзывается (не чаще раза в 7 дней). */
  async transfer(req: ClientRequest): Promise<ClientResult> {
    const input = this.parse(req);
    if (!input) return fail("bad_request");
    const license = await this.store.findLicense(input.serial);
    const problem = this.licenseProblem(license);
    if (problem) return fail(problem);

    const active = (await this.store.activations(license!.id)).filter((a) => a.status === "active");
    if (active.some((a) => hwidMatches(parseHwid(a.hwid)!, input.parts))) {
      return this.activate(req); // уже здесь — переносить нечего
    }

    if (active.length >= PCS_PER_KEY) {
      if (!this.canTransfer(license!)) return { ok: false, error: "transfer_limit", ...this.transferInfo(license!) };

      // Компьютер с файлом лицензии без интернета не отключить: файл работает и дальше — освобождает только автор.
      const oldest = active.filter((a) => !isOfflineSeat(a)).sort((a, b) => a.lastCheckAt.getTime() - b.lastCheckAt.getTime())[0];
      if (!oldest) return { ok: false, error: "occupied", ...this.transferInfo(license!) };
      oldest.status = "revoked";
      await this.store.updateActivation(oldest);
      this.countTransfer(license!);
      await this.store.updateLicense(license!);
      await this.log("client", "transfer", license!.serial, `${oldest.hwid} → ${input.hwid}`);
    }

    const created = await this.store.insertActivation(this.newActivation(license!.id, input));
    if (await this.overLimit(created)) return { ok: false, error: "transfer_limit", ...this.transferInfo(license!) };
    await this.notify(activationMessage(license!, created, this.hub));
    return this.issue(license!, created.hwid);
  }

  /**
   * Места проверяются и записываются разными запросами к базе, поэтому два одновременных запроса могли
   * поставить один ключ на два компьютера. После записи сверяемся: место остаётся за активациями
   * с меньшим номером, лишняя (наша) отзывается.
   */
  private async overLimit(created: ActivationRow): Promise<boolean> {
    const active = (await this.store.activations(created.licenseId)).filter((a) => a.status === "active").sort((a, b) => a.id - b.id);
    if (active.slice(0, PCS_PER_KEY).some((a) => a.id === created.id)) return false;
    created.status = "revoked";
    await this.store.updateActivation(created);
    return true;
  }

  /** Тихая сверка: при каждом запуске плагина и раз в день. */
  async check(req: ClientRequest): Promise<ClientResult> {
    const input = this.parse(req);
    if (!input) return fail("bad_request");
    const license = await this.store.findLicense(input.serial);
    const problem = this.licenseProblem(license);
    if (problem) return fail(problem);

    const all = await this.store.activations(license!.id);
    const matching = all.filter((a) => hwidMatches(parseHwid(a.hwid)!, input.parts));
    const mine = matching.find((a) => a.status === "active");
    if (mine) return this.refresh(license!, mine, input, null);
    return fail(matching.length ? "revoked" : "not_activated");
  }

  /** Кнопка «Освободить этот компьютер» в плагине — не считается переносом. */
  async deactivate(req: ClientRequest): Promise<{ ok: boolean; error?: ErrorCode }> {
    const input = this.parse(req);
    if (!input) return fail("bad_request");
    const license = await this.store.findLicense(input.serial);
    if (!license) return fail("not_found");
    const mine = (await this.store.activations(license.id)).find(
      (a) => a.status === "active" && hwidMatches(parseHwid(a.hwid)!, input.parts),
    );
    if (!mine) return fail("not_activated");
    mine.status = "revoked";
    await this.store.updateActivation(mine);
    await this.log("client", "deactivate", license.serial, input.hwid);
    return { ok: true };
  }

  /** Пробный период 14 дней, привязан к компьютеру: переустановка его не сбрасывает. */
  async trial(req: ClientRequest): Promise<ClientResult> {
    const parts = parseHwid(req.hwid);
    if (!parts) return fail("bad_request");
    const hwid = parts.join(".");
    const existing = (await this.store.trialsSharingPart(parts)).find((t) => hwidMatches(parseHwid(t.hwid)!, parts));
    const trial = existing ?? (await this.store.insertTrial(hwid, this.clock()));
    if (!existing) await this.log("client", "trial", "", hwid);
    const expires = new Date(trial.startedAt.getTime() + TRIAL_DAYS * DAY);
    return { ok: true, license: this.sign("TRIAL", hwid, "trial", expires) };
  }

  /** Сведения об обновлении, подписанные тем же ключом (SPEC 14.1). */
  async latestUpdate(channel: string): Promise<SignedDocument | null> {
    const row = await this.store.latestUpdate(channel === "beta" ? "beta" : "stable");
    if (!row) return null;
    return this.signer.sign(
      JSON.stringify({
        version: row.version,
        url: row.url,
        sha256: row.sha256,
        notesRu: row.notesRu,
        notesEn: row.notesEn,
        minVersion: row.minVersion,
      }),
    );
  }

  // ---------- Сайт: активация по коду компьютера ----------

  /**
   * Страница /activate: ключ + код компьютера (кнопка «Копировать» в плагине) → код активации для
   * окна «Лицензия». Правила те же, что при вводе ключа в плагине: тот же компьютер восстанавливается,
   * занято — ответ occupied, перенос (transfer = true) — не чаще раза в 7 дней. Лицензия обычная
   * (не офлайн): плагин сверяет её с сервером каждый день.
   */
  async siteActivate(serial: unknown, hwid: unknown, transfer: boolean, email?: unknown): Promise<{ ok: true; code: string } | Exclude<ClientResult, { ok: true }>> {
    const emailProblem = await this.verifyEmail(serial, email);
    if (emailProblem) return fail(emailProblem);
    const req: ClientRequest = { serial, hwid: typeof hwid === "string" ? hwid.replace(/\s+/g, "") : hwid, pluginVersion: "site", corelVersion: "" };
    const result = transfer ? await this.transfer(req) : await this.activate(req);
    return result.ok ? { ok: true, code: toActivationCode(result.license) } : result;
  }

  /**
   * Почта — второй секрет к ключу (решение автора, 22.09.2026): без неё по чужому ключу нельзя получить
   * код активации, перенести лицензию или открыть «Моя лицензия». У ключа почта уже есть (заполнил
   * автор или пришла из заявки) — должна совпасть; ещё нет — первая введённая привязывается к ключу.
   * null — всё в порядке (или ключа нет: тогда дальше ответит обычная проверка — not_found).
   */
  async verifyEmail(serialText: unknown, emailText: unknown): Promise<"email_required" | "wrong_email" | null> {
    const serial = normalizeSerial(serialText);
    const license = serial ? await this.store.findLicense(serial) : null;
    if (!license) return null;
    const email = typeof emailText === "string" ? emailText.trim() : "";
    if (!email) return "email_required";
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email) || email.length > 200) return "wrong_email";
    if (license.client.email) {
      return license.client.email.toLowerCase() === email.toLowerCase() ? null : "wrong_email";
    }

    license.client = { ...license.client, email };
    await this.store.updateLicense(license);
    await this.log("site", "bind_email", license.serial, email);
    return null;
  }

  // ---------- Сайт: заявка на покупку ----------

  /**
   * Форма «Купить» (/buy): имя, фамилия и телефон обязательны; почта, день рождения, Telegram и
   * комментарий — по желанию. Заявка попадает в админку (вкладка «Заявки»).
   */
  /** Заявка с сайта. tariff — id тарифа с карточки «Купить» на сайте: его название и цена идут первой строкой заявки
   *  (цену берём из базы, а не из браузера; чужой или выключенный тариф — просто без строки). */
  async submitRequest(clientValue: unknown, messageValue: unknown, tariffValue?: unknown): Promise<{ ok: true } | { ok: false; error: "bad_client" | "missing" }> {
    const client = parseClient(clientValue);
    if (!client) return { ok: false, error: "bad_client" };
    if (!client.firstName || !client.lastName || client.phone.replace(/\D/g, "").length < 5) return { ok: false, error: "missing" };
    const text = typeof messageValue === "string" ? messageValue.trim().slice(0, 1000) : "";
    const chosen = tariffValue === undefined || tariffValue === null || tariffValue === "" ? undefined : (await this.sitePrices()).find((t) => String(t.id) === String(tariffValue));
    const message = chosen ? [`Тариф: ${chosen.name} — ${formatSum(chosen.price, chosen.currency)}`, text].filter(Boolean).join("\n") : text;
    const row = await this.store.insertRequest({ createdAt: this.clock(), client, message, status: "new", serial: "" });
    await this.log("site", "request", "", `#${row.id} ${client.firstName} ${client.lastName}, ${client.phone}`);
    await this.notify(requestMessage(row, this.hub));
    return { ok: true };
  }

  /** Заявки для хаба: у заявки без ключа — готовое сообщение с ценами («напомнить»). */
  async requestsWithOffers() {
    const [requests, tariffs] = await Promise.all([this.listRequests(), this.store.listTariffs()]);
    return requests.map((r) => (r.serial || r.status === "rejected" ? r : { ...r, offer: requestOffer(r, tariffs) }));
  }

  /** Цены для сайта: включённые общие тарифы с галочкой «на сайте», по сроку. */
  async sitePrices() {
    return (await this.billing.tariffs()).filter((t) => t.active && t.site && t.contractId === null && t.kind === "key").map((t) => ({ id: t.id, name: t.name, days: t.days, price: t.price, currency: t.currency, label: t.label }));
  }

  listRequests(): Promise<RequestRow[]> {
    return this.store.listRequests(200);
  }

  /** Заявка: new / working (в работе) / rejected (отказ) / delete (удалить). */
  async adminRequest(id: unknown, action: unknown): Promise<boolean> {
    const request = await this.store.findRequest(Number(id));
    if (!request || !REQUEST_ACTIONS.includes(action as (typeof REQUEST_ACTIONS)[number])) return false;
    if (action === "delete") {
      await this.store.deleteRequest(request.id);
    } else {
      request.status = action as RequestRow["status"];
      await this.store.updateRequest(request);
    }

    await this.log(this.adminActor, "request_" + action, request.serial, `#${request.id}`);
    return true;
  }

  /** Создать ключ по заявке: данные клиента переходят в ключ, заявка — «выполнена» и привязана к ключу. */
  async requestKey(id: unknown, days: unknown, payment: ChargeInput | null = null): Promise<{ serial: string } | null> {
    const request = await this.store.findRequest(Number(id));
    if (!request || request.serial) return null;
    const [key] = await this.createKeys({
      count: 1,
      days: Number(days) || null,
      client: request.client,
      note: ("Заявка #" + request.id + (request.message ? ": " + request.message : "")).slice(0, 500),
      payment,
    });
    request.status = "done";
    request.serial = key.serial;
    await this.store.updateRequest(request);
    return key;
  }

  // ---------- Админка ----------

  /** Данные клиента проверяет http (parseClient); неверные сюда не доходят — на всякий случай пустые. */
  async createKeys(options: KeyOptions): Promise<Array<{ serial: string }>> {
    const client = (options.client === undefined ? null : parseClient(options.client)) ?? EMPTY_CLIENT;
    const count = clamp(Math.floor(options.count ?? 1), 1, 500);
    const now = this.clock();
    const expiresAt = options.days && options.days > 0 ? new Date(now.getTime() + options.days * DAY) : null;
    const keys: Array<{ serial: string }> = [];
    for (let i = 0; i < count; i++) {
      let serial = newSerial();
      while (await this.store.findLicense(serial)) serial = newSerial();
      await this.store.insertLicense({
        serial,
        plan: "full",
        status: "active",
        expiresAt,
        maxPcs: PCS_PER_KEY,
        transfersCount: 0,
        transfersSince: now,
        note: (options.note ?? "").slice(0, 500),
        createdAt: now,
        client: { ...client },
      });
      keys.push({ serial });
    }

    await this.log(this.adminActor, "create_keys", keys.length === 1 ? keys[0].serial : "", `${count} шт., срок: ${options.days || "бессрочно"}`);
    if (options.payment) {
      // Скидка за количество: считаются и уже выданные ключи клиента, и эти.
      const charge = await this.billing.withVolume(options.payment, await this.liveKeysOf(client));
      for (const k of keys) await this.billing.charge(k.serial, { ...charge, kind: "sale", lockTerms: true });
    }
    return keys;
  }

  async search(query: string): Promise<Array<LicenseRow & { activations: ActivationRow[] }>> {
    // Похоже на ключ — сначала точный ключ; не нашёлся — обычный поиск: 12 цифр телефона тоже «похожи на ключ».
    const serial = normalizeSerial(query);
    const exact = serial ? await this.store.findLicense(serial) : null;
    const rows = exact ? [exact] : await this.store.searchLicenses(query, 100);
    return Promise.all(rows.map(async (l) => ({ ...l, activations: await this.store.activations(l.id) })));
  }

  /** Удалить ключ полностью (с активациями). Плагин с этим ключом при следующей сверке отключится (not_found). */
  async deleteLicense(serialText: unknown): Promise<boolean> {
    const serial = normalizeSerial(serialText);
    const license = serial ? await this.store.findLicense(serial) : null;
    if (!license) return false;
    await this.store.deleteLicense(license.id);
    const who = [license.client.firstName, license.client.lastName].filter(Boolean).join(" ");
    await this.log(this.adminActor, "delete", license.serial, who || license.note);
    return true;
  }

  /** Действие над ключом: block, unblock, extend (days; 0 — бессрочно), reset_transfers, note, client. */
  async adminLicense(serialText: unknown, action: string, value: unknown): Promise<LicenseRow | null> {
    const serial = normalizeSerial(serialText);
    const license = serial ? await this.store.findLicense(serial) : null;
    if (!license) return null;
    switch (action) {
      case "block":
        license.status = "blocked";
        break;
      case "unblock":
        license.status = "active";
        break;
      case "extend": {
        const days = Number(value);
        if (!Number.isFinite(days) || days < 0) return null;
        const from = license.expiresAt && license.expiresAt > this.clock() ? license.expiresAt : this.clock();
        license.expiresAt = days === 0 ? null : new Date(from.getTime() + days * DAY);
        break;
      }
      case "reset_transfers":
        license.transfersCount = 0;
        license.transfersSince = this.clock();
        break;
      case "note":
        license.note = String(value ?? "").slice(0, 500);
        break;
      case "client": {
        const client = parseClient(value);
        if (!client) return null;
        license.client = client;
        break;
      }
      default:
        return null;
    }

    await this.store.updateLicense(license);
    await this.log(this.adminActor, action, license.serial, value === undefined || action === "client" ? "" : String(value));
    return license;
  }

  /** Продлить ключ (days; 0 — бессрочно) и, если есть, записать оплату продления. Ключ без цены получает её. */
  async extendKey(serialText: unknown, days: number, payment: ChargeInput | null = null): Promise<LicenseRow | null> {
    const license = await this.adminLicense(serialText, "extend", days);
    if (license && payment) {
      const charge = await this.billing.withVolume(payment, await this.liveKeysOf(license.client));
      await this.billing.charge(license.serial, { ...charge, kind: payment.kind ?? "renewal", lockTerms: !(await this.store.keyTerms(license.serial)) });
    }
    return license;
  }

  // ---------- Деньги ----------

  /** Цена ключа (для «по цене ключа»); null — ключа нет или цены у него нет. */
  async termsOf(serialText: unknown): Promise<KeyTermsRow | null> {
    const serial = normalizeSerial(serialText);
    return serial ? this.store.keyTerms(serial) : null;
  }

  async findKey(serialText: unknown): Promise<LicenseRow | null> {
    const serial = normalizeSerial(serialText);
    return serial ? this.store.findLicense(serial) : null;
  }

  /** Записать начисление по ключу. Вид не указан — «продажа» для первого, дальше «продление». */
  async addPayment(serialText: unknown, input: ChargeInput): Promise<PaymentView | null> {
    const license = await this.findKey(serialText);
    if (!license) return null;
    const kind = input.kind ?? ((await this.store.paymentsFor(license.serial)).length ? "renewal" : "sale");
    const lockTerms = kind !== "updates" && !(await this.store.keyTerms(license.serial));
    return paymentView(await this.billing.charge(license.serial, { ...input, kind, lockTerms }));
  }

  /**
   * Обновления ключа «навсегда» оплачены (владелец 2026-11-08: «навсегда», но обновления платные): начисление и ещё
   * days к дате «обновления до». Ключ без цены (выдан раньше) — его год обновлений считается с даты выдачи.
   */
  async payUpdates(serialText: unknown, input: ChargeInput, days: number): Promise<PaymentView | "missing" | "not_forever"> {
    const license = await this.findKey(serialText);
    if (!license) return "missing";
    if (license.expiresAt) return "not_forever";
    if (!(await this.store.keyTerms(license.serial))) {
      await this.store.saveKeyTerms({ serial: license.serial, contractId: null, tariffId: null, name: "", days: null, price: 0, currency: input.currency, updatesUntil: new Date(license.createdAt.getTime() + INCLUDED_UPDATES_DAYS * DAY), since: this.clock() });
    }
    const row = await this.billing.charge(license.serial, { ...input, kind: "updates", lockTerms: false });
    await this.billing.extendUpdates(license.serial, days);
    return paymentView(row);
  }

  /** clientKey клиента по его адресу в хабе; null — нет такого клиента. */
  async clientKeyOf(id: string): Promise<string | null> {
    for (const l of await this.store.allLicenses(ALL)) {
      const key = clientKey(l.client);
      if (key && clientId(key) === id) return key;
    }
    return null;
  }

  /** Деньги клиента для его карточки: договоры (с ценами и файлами), цены ключей, начисления, долги и напоминание. */
  async clientMoney(id: string) {
    const [licenses, contracts, tariffs, terms, payments] = await Promise.all([this.store.allLicenses(ALL), this.store.listContracts(), this.store.listTariffs(), this.store.allKeyTerms(), this.store.listPayments(ALL)]);
    const key = licenses.map((l) => clientKey(l.client)).find((k) => k && clientId(k) === id);
    if (!key) return null;
    const mine = licenses.filter((l) => clientKey(l.client) === key);
    const serials = new Set(mine.map((l) => l.serial));
    const own = payments.filter((p) => serials.has(p.serial));
    const ids = new Set([...contracts.filter((c) => c.clientKey === key).map((c) => c.id), ...terms.filter((t) => serials.has(t.serial) && t.contractId).map((t) => t.contractId!)]);
    const files = new Map(await Promise.all([...ids].map(async (c) => [c, await this.store.contractFiles(c)] as const)));
    return clientMoneyView({ now: this.clock(), key, licenses: mine, contracts: contracts.filter((c) => ids.has(c.id)), tariffs, terms, payments: own, parts: await this.store.partsOf(own.map((p) => p.id)), files });
  }

  /** Продлить все ключи клиента разом по их цене (на periods периодов); ключи без цены и заблокированные — пропускаются. */
  async extendClient(id: string, o: { periods: number; method: ChargeInput["method"]; dueOn: string; note: string }): Promise<string[] | null> {
    const key = await this.clientKeyOf(id);
    if (!key) return null;
    const done: string[] = [];
    for (const l of await this.store.allLicenses(ALL)) {
      if (clientKey(l.client) !== key || l.status === "blocked") continue;
      const t = await this.store.keyTerms(l.serial);
      if (!t?.days) continue;
      const amount = Math.round(t.price * o.periods * 100) / 100;
      await this.extendKey(l.serial, t.days * o.periods, { amount, currency: t.currency, listPrice: amount, tariff: t.name, tariffId: t.tariffId, contractId: t.contractId, unitPrice: t.price, days: t.days, method: o.method, paidNow: null, dueOn: o.dueOn, note: o.note });
      done.push(l.serial);
    }
    return done;
  }

  /** Раздел «Деньги»: доход, долги, начисления, ожидаемое, договоры, ключи за месяц, воронка, средний чек и тарифы. */
  async money() {
    const d = await this.everything();
    const [payments, parts, extended, tariffs, terms, contracts, rate, volume] = await Promise.all([
      this.store.listPayments(ALL),
      this.store.listParts(ALL * 5),
      this.store.auditSearch({ action: "extend" }, ALL),
      this.billing.tariffs(),
      this.store.allKeyTerms(),
      this.store.listContracts(),
      this.billing.rateToday(),
      this.billing.volume(),
    ]);
    const report = moneyReport({ now: d.now, payments, parts, licenses: d.licenses, requests: d.requests, trials: d.trials, extended, terms, contracts, rate: rate.rate });
    return { ...report, rateToday: rate, volume, tariffs: tariffs.map(tariffView) };
  }

  async revokeActivation(id: number): Promise<boolean> {
    const activation = await this.store.findActivation(id);
    if (!activation) return false;
    activation.status = "revoked";
    await this.store.updateActivation(activation);
    await this.log(this.adminActor, "revoke", "", `активация ${id}, ${activation.hwid}`);
    return true;
  }

  /** Офлайн-активация (SPEC 13.6): код компьютера клиента → файл лицензии. Лимит ПК не проверяется — решает автор. */
  async offline(serialText: unknown, hwidText: unknown): Promise<SignedDocument | null> {
    const serial = normalizeSerial(serialText);
    const parts = parseHwid(hwidText);
    const license = serial ? await this.store.findLicense(serial) : null;
    if (!license || !parts) return null;
    const hwid = parts.join(".");
    const active = (await this.store.activations(license.id)).find((a) => a.status === "active" && hwidMatches(parseHwid(a.hwid)!, parts));
    if (!active) {
      await this.store.insertActivation(this.newActivation(license.id, { hwid, pluginVersion: "offline", corelVersion: "" }));
    }

    await this.log(this.adminActor, "offline", license.serial, hwid);
    return this.sign(license.serial, hwid, license.plan, license.expiresAt, true);
  }

  async publishUpdate(row: Omit<UpdateRow, "createdAt">): Promise<void> {
    await this.store.insertUpdate({ ...row, channel: row.channel === "beta" ? "beta" : "stable", createdAt: this.clock() });
    await this.log(this.adminActor, "publish_update", "", `${row.channel} ${row.version}`);
  }

  auditLog(limit = 200) {
    return this.store.auditLog(limit);
  }

  /** Журнал с фильтрами (хаб): ключ, кто, действие, даты. */
  auditSearch(filter: AuditFilter, limit = 200) {
    return this.store.auditSearch(filter, limit);
  }

  /** История ключа: активации, переносы, продления, блокировки — всё из журнала по этому ключу. */
  async keyHistory(serialText: unknown) {
    const serial = normalizeSerial(serialText);
    const license = serial ? await this.store.findLicense(serial) : null;
    if (!license) return null;
    const rows = await this.store.auditSearch({ serial: license.serial }, 200);
    return rows.map((e) => ({ at: iso(e.at), actor: e.actor, action: e.action, details: shortCodes(e.details) }));
  }

  // ---------- Сайт: «Моя лицензия» (по одному ключу, решение автора 22.09.2026) ----------

  // ---------- Хаб little_7o7 (/api/platform) ----------

  /** Ключ для хаба: состояние словом, сколько дней осталось, когда можно перенести, компьютеры (код и полный). */
  adminView(license: LicenseRow, activations: ActivationRow[]): AdminLicenseView {
    const now = this.clock();
    const expired = !!license.expiresAt && license.expiresAt <= now;
    const next = this.nextTransferAt(license);
    return {
      serial: license.serial,
      status: license.status === "blocked" ? "blocked" : expired ? "expired" : "active",
      expiresAt: license.expiresAt ? iso(license.expiresAt) : null,
      daysLeft: license.expiresAt ? Math.max(0, Math.ceil((license.expiresAt.getTime() - now.getTime()) / DAY)) : null,
      transfersCount: license.transfersCount,
      nextTransferAt: next ? iso(next) : null,
      note: license.note,
      createdAt: iso(license.createdAt),
      client: { ...license.client },
      clientId: clientKey(license.client) ? clientId(clientKey(license.client)) : "",
      computers: activations.map((a) => ({
        id: a.id,
        code: hwidDisplay(parseHwid(a.hwid) ?? a.hwid.split(".")),
        hwid: a.hwid,
        active: a.status === "active",
        firstAt: iso(a.firstAt),
        lastCheckAt: iso(a.lastCheckAt),
        pluginVersion: a.pluginVersion,
        corelVersion: a.corelVersion,
      })),
    };
  }

  async licenseViews(query: string): Promise<AdminLicenseView[]> {
    return (await this.search(query)).map(({ activations, ...l }) => this.adminView(l, activations));
  }

  /** Карточка ключа: ещё оплаты и «предложить продлить» с ценой. */
  async licenseView(serialText: unknown): Promise<AdminLicenseView | null> {
    const serial = normalizeSerial(serialText);
    const license = serial ? await this.store.findLicense(serial) : null;
    if (!license) return null;
    const [activations, payments, tariffs, terms, contracts] = await Promise.all([this.store.activations(license.id), this.store.paymentsFor(license.serial), this.store.listTariffs(), this.store.keyTerms(license.serial), this.store.listContracts()]);
    const parts = await this.store.partsOf(payments.map((p) => p.id));
    const offer = license.expiresAt && license.status !== "blocked" ? renewalOffer(license, terms, payments, tariffs, this.clock()) : null;
    const key = clientKey(license.client);
    const contract = contracts.find((c) => c.id === terms?.contractId) ?? contracts.filter((c) => key && c.clientKey === key && c.status === "active").at(-1) ?? null;
    const updatesUntil = license.expiresAt ? null : terms?.updatesUntil ?? new Date(license.createdAt.getTime() + INCLUDED_UPDATES_DAYS * DAY);
    return {
      ...this.adminView(license, activations),
      payments: payments.map((p) => paymentView(p, parts)),
      offer: offer ? { text: offer.text, tariffId: offer.tariff?.id ?? null, tariff: offer.tariff?.name ?? null } : null,
      terms: terms ? termsView(terms) : null,
      contract: contract ? { id: contract.id, number: contract.number, status: contract.status } : null,
      updatesUntil: updatesUntil ? iso(updatesUntil) : null,
    };
  }

  findRequest(id: unknown): Promise<RequestRow | null> {
    const n = Number(id);
    return Number.isInteger(n) && n > 0 ? this.store.findRequest(n) : Promise.resolve(null);
  }

  /** Что сейчас получают плагины в каждом канале. */
  async updates(): Promise<{ stable: UpdateRow | null; beta: UpdateRow | null }> {
    const [stable, beta] = await Promise.all([this.store.latestUpdate("stable"), this.store.latestUpdate("beta")]);
    return { stable, beta };
  }

  ping(): Promise<void> {
    return this.store.ping();
  }

  /** Всё сразу для сводок хаба: ключи, активации, заявки, пробные, последний выпуск stable. */
  private async everything() {
    const [licenses, activations, requests, trials, updates] = await Promise.all([
      this.store.allLicenses(ALL),
      this.store.allActivations(ALL * 2),
      this.store.listRequests(ALL),
      this.store.listTrials(ALL),
      this.updates(),
    ]);
    return { licenses, activations, requests, trials, updates, stable: updates.stable?.version ?? null, now: this.clock() };
  }

  /** Клиенты: один человек — одна карточка (все ключи, компьютеры, заявки, заметки); дни рождения в этом месяце. */
  async clients(): Promise<{ clients: ClientView[]; birthdays: ReturnType<typeof birthdaysThisMonth> }> {
    const d = await this.everything();
    const clients = clientViews(d.licenses, d.activations, d.requests, d.stable, d.now);
    return { clients, birthdays: birthdaysThisMonth(clients, d.now) };
  }

  /** Подключённые компьютеры: версии плагина и CorelDRAW, «не обновился», «давно не открывали». */
  async computers() {
    const d = await this.everything();
    const computers = computerBriefs(d.licenses, d.activations, d.stable, d.now);
    const online = computers.filter((c) => !c.offline);
    return {
      latest: { stable: d.stable, beta: d.updates.beta?.version ?? null },
      plugin: countVersions(online.map((c) => c.pluginVersion)),
      corel: countVersions(online.map((c) => c.corelVersion)),
      computers,
    };
  }

  /** Все ключи для выгрузки в Excel. */
  async allKeys(): Promise<AdminLicenseView[]> {
    const d = await this.everything();
    const byLicense = new Map<number, ActivationRow[]>();
    for (const a of d.activations) byLicense.set(a.licenseId, [...(byLicense.get(a.licenseId) ?? []), a]);
    return d.licenses.map((l) => this.adminView(l, byLicense.get(l.id) ?? []));
  }

  /** Уборка данных и то, на что стоит взглянуть: ключи без клиента, один телефон — разные имена, старые заявки,
   * давно истёкшие ключи, подозрительные ключи, пробные без покупки и похожие компьютеры. */
  async attention() {
    const d = await this.everything();
    const occupied = await this.store.auditSearch({ action: "occupied", from: new Date(d.now.getTime() - 90 * DAY) }, 5000);
    return {
      ...cleanup(d.licenses, d.requests, d.now),
      suspicious: suspiciousKeys(d.licenses, d.activations, occupied, d.now),
      trials: trialInsights(d.trials, d.activations, d.now, TRIAL_DAYS),
    };
  }

  /**
   * Утреннее сообщение владельцу (cron в 9:00 по Ташкенту), по понедельникам — ещё итог недели. Нечего сказать или бот
   * выключен — не отправляется. weekly — ушёл ли итог недели.
   */
  async sendDailyDigest(): Promise<{ sent: boolean; empty: boolean; weekly: boolean }> {
    const d = await this.everything();
    const [payments, parts, terms] = await Promise.all([this.store.listPayments(ALL), this.store.listParts(ALL * 5), this.store.allKeyTerms()]);
    const clients = clientViews(d.licenses, d.activations, d.requests, d.stable, d.now);
    const blocked = await this.billing.autoBlock();
    const licenses = blocked.length ? await this.store.allLicenses(ALL) : d.licenses;
    const money = dailyMoneyLines({ now: d.now, payments, parts, licenses, terms, requests: d.requests, blocked, hubUrl: this.hub });
    const text = dailyDigest({ now: d.now, licenses: d.licenses, activations: d.activations, requests: d.requests, trials: d.trials, clients, trialDays: TRIAL_DAYS, hubUrl: this.hub, money });
    const week = isMonday(d.now) ? weeklyDigest({ now: d.now, payments, parts, licenses: d.licenses, requests: d.requests, hubUrl: this.hub }) : null;
    const weekly = week ? await this.notify(week) : false;
    if (!text) return { sent: false, empty: true, weekly };
    return { sent: await this.notify(text), empty: false, weekly };
  }

  /** Главная хаба: числа, «требует внимания» (ключи, кончающиеся за 30 дней, и новые заявки), что опубликовано. */
  async summary() {
    const now = this.clock();
    const soon = new Date(now.getTime() + SOON_DAYS * DAY);
    const at = { now, soon, dayAgo: new Date(now.getTime() - DAY), trialSince: new Date(now.getTime() - TRIAL_DAYS * DAY) };
    const [st, expiring, requests, updates, payments, parts] = await Promise.all([
      this.store.stats(at),
      this.store.expiringLicenses(now, soon, 20),
      this.store.listRequests(50),
      this.updates(),
      this.store.listPayments(ALL),
      this.store.listParts(ALL * 5),
    ]);
    return {
      licenses: { total: st.licenses, active: st.licenses - st.blocked - st.expired, blocked: st.blocked, expired: st.expired, expiringSoon: st.expiring, unlimited: st.unlimited },
      computers: { active: st.computers, checkedToday: st.checkedSince },
      trials: { active: st.trialsActive, total: st.trials },
      requests: { new: st.requestsNew, working: st.requestsWorking },
      money: moneyTotals(payments, parts, now),
      updates: { stable: updates.stable ? { version: updates.stable.version, createdAt: iso(updates.stable.createdAt) } : null, beta: updates.beta ? { version: updates.beta.version, createdAt: iso(updates.beta.createdAt) } : null },
      attention: {
        expiring: expiring.map((l) => ({ serial: l.serial, expiresAt: iso(l.expiresAt!), daysLeft: Math.max(0, Math.ceil((l.expiresAt!.getTime() - now.getTime()) / DAY)), client: { ...l.client }, note: l.note })),
        newRequests: requests.filter((r) => r.status === "new").slice(0, 10),
      },
    };
  }

  /** Ключ → сведения о лицензии и её компьютерах. */
  async siteLookup(serialText: unknown, email?: unknown): Promise<SiteResult<{ license: SiteLicenseView }>> {
    const emailProblem = await this.verifyEmail(serialText, email);
    if (emailProblem) return { ok: false, error: emailProblem };
    const license = await this.siteLicense(serialText);
    if (!license) return { ok: false, error: "not_found" };
    return { ok: true, license: await this.siteView(license) };
  }

  /**
   * «Освободить» компьютер с сайта (например, старый сломался или Windows переустановили на другом
   * железе). Считается переносом (раз в 7 дней): иначе ключ можно было бы передавать по кругу.
   */
  async siteRelease(serialText: unknown, activationId: unknown, email?: unknown): Promise<SiteResult<{ license: SiteLicenseView }>> {
    const emailProblem = await this.verifyEmail(serialText, email);
    if (emailProblem) return { ok: false, error: emailProblem };
    const license = await this.siteLicense(serialText);
    if (!license) return { ok: false, error: "not_found" };
    const activation = await this.store.findActivation(Number(activationId));
    if (!activation || activation.licenseId !== license.id || activation.status !== "active") return { ok: false, error: "not_found" };
    // Файл лицензии без интернета продолжит работать и после «освобождения» — иначе один ключ давал бы
    // сколько угодно таких файлов. Такое место освобождает только автор (админка).
    if (isOfflineSeat(activation)) return { ok: false, error: "offline_seat" };

    if (!this.canTransfer(license)) return { ok: false, error: "transfer_limit" };

    activation.status = "revoked";
    await this.store.updateActivation(activation);
    this.countTransfer(license);
    await this.store.updateLicense(license);
    await this.log("site", "release", license.serial, `активация ${activation.id}, ${activation.hwid}`);
    return { ok: true, license: await this.siteView(license) };
  }

  /**
   * Файл лицензии для компьютера без интернета (сам клиент, без автора): код компьютера из окна
   * «Лицензия» → подписанный файл. Один ключ — один компьютер: место должно быть свободно.
   */
  async siteOffline(serialText: unknown, hwidText: unknown, email?: unknown): Promise<SiteResult<{ file: SignedDocument }>> {
    const emailProblem = await this.verifyEmail(serialText, email);
    if (emailProblem) return { ok: false, error: emailProblem };
    const license = await this.siteLicense(serialText);
    if (!license) return { ok: false, error: "not_found" };
    const problem = this.licenseProblem(license);
    if (problem) return { ok: false, error: problem };
    const parts = parseHwid(hwidText);
    if (!parts) return { ok: false, error: "bad_request" };

    const hwid = parts.join(".");
    const active = (await this.store.activations(license.id)).filter((a) => a.status === "active");
    if (!active.some((a) => hwidMatches(parseHwid(a.hwid)!, parts))) {
      if (active.length >= PCS_PER_KEY) return { ok: false, error: "occupied" };
      const created = await this.store.insertActivation(this.newActivation(license.id, { hwid, pluginVersion: "site-offline", corelVersion: "" }));
      if (await this.overLimit(created)) return { ok: false, error: "occupied" };
    }

    await this.log("site", "offline", license.serial, hwid);
    return { ok: true, file: this.sign(license.serial, hwid, license.plan, license.expiresAt, true) };
  }

  private async siteLicense(serialText: unknown): Promise<LicenseRow | null> {
    const serial = normalizeSerial(serialText);
    return serial ? this.store.findLicense(serial) : null;
  }

  private async siteView(license: LicenseRow): Promise<SiteLicenseView> {
    const activations = await this.store.activations(license.id);
    return {
      serial: license.serial,
      plan: license.plan,
      status: license.status,
      expiresAt: license.expiresAt ? iso(license.expiresAt) : null,
      maxPcs: PCS_PER_KEY,
      ...this.transferInfo(license),
      computers: activations.map((a) => ({
        id: a.id,
        code: hwidDisplay(parseHwid(a.hwid) ?? a.hwid.split(".")),
        active: a.status === "active",
        firstAt: iso(a.firstAt),
        lastCheckAt: iso(a.lastCheckAt),
        pluginVersion: a.pluginVersion,
        corelVersion: a.corelVersion,
      })),
    };
  }

  // ---------- Админка: пробные периоды ----------

  async listTrials(): Promise<TrialView[]> {
    const now = this.clock().getTime();
    return (await this.store.listTrials(300)).map((t) => {
      const ends = t.startedAt.getTime() + TRIAL_DAYS * DAY;
      return {
        id: t.id,
        code: hwidDisplay(parseHwid(t.hwid) ?? t.hwid.split(".")),
        startedAt: iso(t.startedAt),
        endsAt: iso(new Date(ends)),
        daysLeft: Math.max(0, Math.ceil((ends - now) / DAY)),
        active: ends > now,
      };
    });
  }

  /** Удалить пробный период: компьютер сможет взять 14 дней заново. */
  async deleteTrial(id: unknown): Promise<boolean> {
    const trials = await this.store.listTrials(10000);
    const trial = trials.find((t) => t.id === Number(id));
    if (!trial) return false;
    await this.store.deleteTrial(trial.id);
    await this.log(this.adminActor, "trial_delete", "", trial.hwid);
    return true;
  }

  // ---------- Внутреннее ----------

  private parse(req: ClientRequest) {
    const serial = normalizeSerial(req.serial);
    const parts = parseHwid(req.hwid);
    if (!serial || !parts) return null;
    return {
      serial,
      parts,
      hwid: parts.join("."),
      pluginVersion: short(req.pluginVersion),
      corelVersion: short(req.corelVersion),
    };
  }

  private licenseProblem(license: LicenseRow | null): ErrorCode | null {
    if (!license) return "not_found";
    if (license.status === "blocked") return "blocked";
    if (license.expiresAt && license.expiresAt <= this.clock()) return "expired";
    return null;
  }

  // transfersSince — время последнего переноса; transfersCount = 0 — переносов не было (или админ сбросил).
  private nextTransferAt(license: LicenseRow): Date | null {
    if (license.transfersCount === 0) return null;
    const next = new Date(license.transfersSince.getTime() + TRANSFER_COOLDOWN_DAYS * DAY);
    return next > this.clock() ? next : null;
  }

  private canTransfer(license: LicenseRow): boolean {
    return this.nextTransferAt(license) === null;
  }

  private countTransfer(license: LicenseRow) {
    license.transfersCount++;
    license.transfersSince = this.clock();
  }

  private transferInfo(license: LicenseRow): { transfersLeft: number; nextTransferAt: string | null } {
    const next = this.nextTransferAt(license);
    return { transfersLeft: next ? 0 : 1, nextTransferAt: next ? iso(next) : null };
  }

  private newActivation(licenseId: number, input: { hwid: string; pluginVersion: string; corelVersion: string }) {
    const now = this.clock();
    return {
      licenseId,
      hwid: input.hwid,
      status: "active" as const,
      firstAt: now,
      lastCheckAt: now,
      pluginVersion: input.pluginVersion,
      corelVersion: input.corelVersion,
    };
  }

  /** Та же активация: код компьютера обновляется (мог смениться один признак), версии — тоже. */
  private async refresh(
    license: LicenseRow,
    activation: ActivationRow,
    input: { hwid: string; pluginVersion: string; corelVersion: string },
    action: string | null,
  ): Promise<ClientResult> {
    activation.hwid = input.hwid;
    activation.lastCheckAt = this.clock();
    if (input.pluginVersion) activation.pluginVersion = input.pluginVersion;
    if (input.corelVersion) activation.corelVersion = input.corelVersion;
    await this.store.updateActivation(activation);
    if (action) await this.log("client", action, license.serial, input.hwid);
    // Данные клиента — для блока «О программе» в настройках плагина. Получает их только компьютер,
    // на котором этот ключ сейчас активирован (сверка по коду компьютера). Ключ и код компьютера видны
    // в коде активации, а почта — второй секрет для переноса, поэтому сверка (action = null) отдаёт её скрытой.
    const client = { ...license.client };
    if (!action && client.email) client.email = maskEmail(client.email);
    return { ...this.issue(license, activation.hwid), client };
  }

  private issue(license: LicenseRow, hwid: string): { ok: true; license: SignedDocument } {
    return { ok: true, license: this.sign(license.serial, hwid, license.plan, license.expiresAt) };
  }

  /**
   * Текст лицензии — поля в том же порядке, что LicenseData в плагине. offline — файл для клиента
   * без интернета (SPEC 13.6): плагин не требует ежедневной сверки, действует до expiresAt.
   */
  private sign(serial: string, hwid: string, plan: string, expiresAt: Date | null, offline = false): SignedDocument {
    const now = this.clock();
    const payload: Record<string, string | boolean> = { serial, hwid, plan };
    if (expiresAt) payload.expiresAt = iso(expiresAt);
    payload.issuedAt = iso(now);
    payload.nextCheckAt = iso(new Date(now.getTime() + CHECK_EVERY_DAYS * DAY));
    if (offline) payload.offline = true;
    return this.signer.sign(JSON.stringify(payload));
  }

  private log(actor: string, action: string, serial: string, details: string) {
    return this.store.audit({ at: this.clock(), actor, action, serial, details });
  }
}

function fail(error: ErrorCode): { ok: false; error: ErrorCode } {
  return { ok: false, error };
}

function iso(date: Date): string {
  return date.toISOString().replace(/\.\d{3}Z$/, "Z");
}

function short(value: unknown): string {
  return typeof value === "string" ? value.slice(0, 40) : "";
}

function clamp(value: number, min: number, max: number): number {
  return Number.isFinite(value) ? Math.min(max, Math.max(min, value)) : min;
}

/** m***@example.com: в «О программе» видно, какая почта, но для переноса лицензии она не годится. */
export function maskEmail(email: string): string {
  const at = email.indexOf("@");
  return at > 0 ? email[0] + "***" + email.substring(at) : "***";
}

/** Активация файлом лицензии без интернета (из админки или с сайта): плагин с ним сервер не спрашивает. */
function isOfflineSeat(a: ActivationRow): boolean {
  return a.pluginVersion === "offline" || a.pluginVersion === "site-offline";
}

/** Полные коды компьютеров в тексте журнала → короткие XXXX-XXXX-XXXX-XXXX, как в окне «Лицензия». */
export function shortCodes(text: string): string {
  return text.replace(/(?:[0-9a-f]{16}|-)(?:\.(?:[0-9a-f]{16}|-)){3}/g, (m) => hwidDisplay(parseHwid(m) ?? m.split(".")));
}
