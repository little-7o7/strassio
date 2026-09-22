// Правила лицензий (SPEC 13.2–13.6) без привязки к платформе: всё время — через clock, данные —
// через Store, подпись — через Signer. Ответы клиенту: { ok: true, license } или { ok: false, error }.
import { timingSafeEqual } from "node:crypto";
import { Signer, SignedDocument, newRecoveryCode, newSerial, normalizeRecoveryCode, normalizeSerial, toActivationCode } from "./crypto.js";
import { hwidDisplay, hwidMatches, parseHwid } from "./hwid.js";
import type { ActivationRow, LicenseRow, Store, UpdateRow } from "./store.js";

export const CHECK_EVERY_DAYS = 1;
export const TRIAL_DAYS = 14;
/** Переносов сколько угодно, но не чаще раза в 7 дней (решение автора, 22.09.2026). Раньше — через админа. */
export const TRANSFER_COOLDOWN_DAYS = 7;
/** Один ключ — один компьютер (решение автора, 22.09.2026). Колонка max_pcs в базе больше не читается. */
export const PCS_PER_KEY = 1;
const DAY = 24 * 60 * 60 * 1000;

export type ErrorCode =
  | "bad_request"
  | "not_found"
  | "blocked"
  | "expired"
  | "occupied"
  | "transfer_limit"
  | "not_activated"
  | "revoked";

export type ClientResult =
  | { ok: true; license: SignedDocument }
  | { ok: false; error: ErrorCode; transfersLeft?: number; nextTransferAt?: string | null };

export interface ClientRequest {
  serial?: unknown;
  hwid?: unknown;
  pluginVersion?: unknown;
  corelVersion?: unknown;
}

/** Что видит владелец ключа на сайте (страница восстановления). Коды компьютеров — только короткие. */
export interface RecoveryView {
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

export type RecoveryResult<T> = { ok: true } & T | { ok: false; error: ErrorCode | "bad_recovery" };

export interface KeyOptions {
  count?: number;
  days?: number | null;
  note?: string;
}

export class LicenseService {
  constructor(
    private readonly store: Store,
    private readonly signer: Signer,
    private readonly clock: () => Date = () => new Date(),
  ) {}

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
      return { ok: false, error: "occupied", ...this.transferInfo(license!) };
    }

    const created = await this.store.insertActivation(this.newActivation(license!.id, input));
    await this.log("client", "activate", license!.serial, input.hwid);
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

      const oldest = active.slice().sort((a, b) => a.lastCheckAt.getTime() - b.lastCheckAt.getTime())[0];
      oldest.status = "revoked";
      await this.store.updateActivation(oldest);
      this.countTransfer(license!);
      await this.store.updateLicense(license!);
      await this.log("client", "transfer", license!.serial, `${oldest.hwid} → ${input.hwid}`);
    }

    const created = await this.store.insertActivation(this.newActivation(license!.id, input));
    return this.issue(license!, created.hwid);
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
  async siteActivate(serial: unknown, hwid: unknown, transfer: boolean): Promise<{ ok: true; code: string } | Exclude<ClientResult, { ok: true }>> {
    const req: ClientRequest = { serial, hwid: typeof hwid === "string" ? hwid.replace(/\s+/g, "") : hwid, pluginVersion: "site", corelVersion: "" };
    const result = transfer ? await this.transfer(req) : await this.activate(req);
    return result.ok ? { ok: true, code: toActivationCode(result.license) } : result;
  }

  // ---------- Админка ----------

  async createKeys(options: KeyOptions): Promise<Array<{ serial: string; recoveryCode: string }>> {
    const count = clamp(Math.floor(options.count ?? 1), 1, 500);
    const now = this.clock();
    const expiresAt = options.days && options.days > 0 ? new Date(now.getTime() + options.days * DAY) : null;
    const keys: Array<{ serial: string; recoveryCode: string }> = [];
    for (let i = 0; i < count; i++) {
      let serial = newSerial();
      while (await this.store.findLicense(serial)) serial = newSerial();
      const recoveryCode = newRecoveryCode();
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
        recoveryCode,
      });
      keys.push({ serial, recoveryCode });
    }

    await this.log("admin", "create_keys", keys.length === 1 ? keys[0].serial : "", `${count} шт., срок: ${options.days || "бессрочно"}`);
    return keys;
  }

  async search(query: string): Promise<Array<LicenseRow & { activations: ActivationRow[] }>> {
    const serial = normalizeSerial(query);
    const rows = serial ? [await this.store.findLicense(serial)].filter((r): r is LicenseRow => !!r) : await this.store.searchLicenses(query, 100);
    return Promise.all(rows.map(async (l) => ({ ...l, activations: await this.store.activations(l.id) })));
  }

  /** Действие над ключом: block, unblock, extend (days; 0 — бессрочно), reset_transfers, note, new_recovery. */
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
      case "new_recovery":
        // Старый код перестаёт работать (клиент его потерял или показал кому-то).
        license.recoveryCode = newRecoveryCode();
        break;
      default:
        return null;
    }

    await this.store.updateLicense(license);
    await this.log("admin", action, license.serial, value === undefined || action === "new_recovery" ? "" : String(value));
    return license;
  }

  async revokeActivation(id: number): Promise<boolean> {
    const activation = await this.store.findActivation(id);
    if (!activation) return false;
    activation.status = "revoked";
    await this.store.updateActivation(activation);
    await this.log("admin", "revoke", "", `активация ${id}, ${activation.hwid}`);
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

    await this.log("admin", "offline", license.serial, hwid);
    return this.sign(license.serial, hwid, license.plan, license.expiresAt, true);
  }

  async publishUpdate(row: Omit<UpdateRow, "createdAt">): Promise<void> {
    await this.store.insertUpdate({ ...row, channel: row.channel === "beta" ? "beta" : "stable", createdAt: this.clock() });
    await this.log("admin", "publish_update", "", `${row.channel} ${row.version}`);
  }

  auditLog(limit = 200) {
    return this.store.auditLog(limit);
  }

  // ---------- Сайт: восстановление лицензии по ключу восстановления ----------

  /** Ключ + ключ восстановления → сведения о лицензии и её компьютерах. */
  async recoveryLookup(serialText: unknown, codeText: unknown): Promise<RecoveryResult<{ license: RecoveryView }>> {
    const license = await this.recoveryLicense(serialText, codeText);
    if (!license) return { ok: false, error: "bad_recovery" };
    return { ok: true, license: await this.recoveryView(license) };
  }

  /**
   * «Освободить» компьютер с сайта (например, старый сломался или Windows переустановили на другом
   * железе). Считается переносом (раз в 7 дней): иначе ключ можно было бы передавать по кругу.
   */
  async recoveryRelease(serialText: unknown, codeText: unknown, activationId: unknown): Promise<RecoveryResult<{ license: RecoveryView }>> {
    const license = await this.recoveryLicense(serialText, codeText);
    if (!license) return { ok: false, error: "bad_recovery" };
    const activation = await this.store.findActivation(Number(activationId));
    if (!activation || activation.licenseId !== license.id || activation.status !== "active") return { ok: false, error: "not_found" };

    if (!this.canTransfer(license)) return { ok: false, error: "transfer_limit" };

    activation.status = "revoked";
    await this.store.updateActivation(activation);
    this.countTransfer(license);
    await this.store.updateLicense(license);
    await this.log("site", "release", license.serial, `активация ${activation.id}, ${activation.hwid}`);
    return { ok: true, license: await this.recoveryView(license) };
  }

  /**
   * Файл лицензии для компьютера без интернета (сам клиент, без автора): код компьютера из окна
   * «Лицензия» → подписанный файл. Число компьютеров ключа соблюдается — место должно быть свободно.
   */
  async recoveryOffline(serialText: unknown, codeText: unknown, hwidText: unknown): Promise<RecoveryResult<{ file: SignedDocument }>> {
    const license = await this.recoveryLicense(serialText, codeText);
    if (!license) return { ok: false, error: "bad_recovery" };
    const problem = this.licenseProblem(license);
    if (problem) return { ok: false, error: problem };
    const parts = parseHwid(hwidText);
    if (!parts) return { ok: false, error: "bad_request" };

    const hwid = parts.join(".");
    const active = (await this.store.activations(license.id)).filter((a) => a.status === "active");
    if (!active.some((a) => hwidMatches(parseHwid(a.hwid)!, parts))) {
      if (active.length >= PCS_PER_KEY) return { ok: false, error: "occupied" };
      await this.store.insertActivation(this.newActivation(license.id, { hwid, pluginVersion: "site-offline", corelVersion: "" }));
    }

    await this.log("site", "offline", license.serial, hwid);
    return { ok: true, file: this.sign(license.serial, hwid, license.plan, license.expiresAt, true) };
  }

  private async recoveryLicense(serialText: unknown, codeText: unknown): Promise<LicenseRow | null> {
    const serial = normalizeSerial(serialText);
    const code = normalizeRecoveryCode(codeText);
    if (!serial || !code) return null;
    const license = await this.store.findLicense(serial);
    if (!license || !license.recoveryCode) return null;
    const a = Buffer.from(license.recoveryCode);
    const b = Buffer.from(code);
    return a.length === b.length && timingSafeEqual(a, b) ? license : null;
  }

  private async recoveryView(license: LicenseRow): Promise<RecoveryView> {
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
    return this.issue(license, activation.hwid);
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
