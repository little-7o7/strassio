// Правила лицензий (SPEC 13.2–13.6) без привязки к платформе: всё время — через clock, данные —
// через Store, подпись — через Signer. Ответы клиенту: { ok: true, license } или { ok: false, error }.
import { Signer, SignedDocument, newSerial, normalizeSerial } from "./crypto.js";
import { hwidMatches, parseHwid } from "./hwid.js";
import type { ActivationRow, LicenseRow, Store, UpdateRow } from "./store.js";

export const CHECK_EVERY_DAYS = 14;
export const TRIAL_DAYS = 14;
export const TRANSFERS_PER_YEAR = 3;
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
  | { ok: false; error: ErrorCode; transfersLeft?: number };

export interface ClientRequest {
  serial?: unknown;
  hwid?: unknown;
  pluginVersion?: unknown;
  corelVersion?: unknown;
}

export interface KeyOptions {
  count?: number;
  days?: number | null;
  maxPcs?: number;
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

    if (active.length >= license!.maxPcs) {
      return { ok: false, error: "occupied", transfersLeft: this.transfersLeft(license!) };
    }

    const created = await this.store.insertActivation(this.newActivation(license!.id, input));
    await this.log("client", "activate", license!.serial, input.hwid);
    return this.issue(license!, created.hwid);
  }

  /** «Перенести лицензию на этот компьютер»: самая давняя активация отзывается (не больше 3 раз за 365 дней). */
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

    if (active.length >= license!.maxPcs) {
      this.resetTransfersIfYearPassed(license!);
      if (license!.transfersCount >= TRANSFERS_PER_YEAR) {
        await this.store.updateLicense(license!);
        return { ok: false, error: "transfer_limit", transfersLeft: 0 };
      }

      const oldest = active.slice().sort((a, b) => a.lastCheckAt.getTime() - b.lastCheckAt.getTime())[0];
      oldest.status = "revoked";
      await this.store.updateActivation(oldest);
      license!.transfersCount++;
      await this.store.updateLicense(license!);
      await this.log("client", "transfer", license!.serial, `${oldest.hwid} → ${input.hwid}`);
    }

    const created = await this.store.insertActivation(this.newActivation(license!.id, input));
    return this.issue(license!, created.hwid);
  }

  /** Тихая сверка раз в 14 дней. */
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

  // ---------- Админка ----------

  async createKeys(options: KeyOptions): Promise<string[]> {
    const count = clamp(Math.floor(options.count ?? 1), 1, 500);
    const maxPcs = clamp(Math.floor(options.maxPcs ?? 1), 1, 100);
    const now = this.clock();
    const expiresAt = options.days && options.days > 0 ? new Date(now.getTime() + options.days * DAY) : null;
    const serials: string[] = [];
    for (let i = 0; i < count; i++) {
      let serial = newSerial();
      while (await this.store.findLicense(serial)) serial = newSerial();
      await this.store.insertLicense({
        serial,
        plan: "full",
        status: "active",
        expiresAt,
        maxPcs,
        transfersCount: 0,
        transfersSince: now,
        note: (options.note ?? "").slice(0, 500),
        createdAt: now,
      });
      serials.push(serial);
    }

    await this.log("admin", "create_keys", serials.length === 1 ? serials[0] : "", `${count} шт., ПК: ${maxPcs}, срок: ${options.days || "бессрочно"}`);
    return serials;
  }

  async search(query: string): Promise<Array<LicenseRow & { activations: ActivationRow[] }>> {
    const serial = normalizeSerial(query);
    const rows = serial ? [await this.store.findLicense(serial)].filter((r): r is LicenseRow => !!r) : await this.store.searchLicenses(query, 100);
    return Promise.all(rows.map(async (l) => ({ ...l, activations: await this.store.activations(l.id) })));
  }

  /** Действие над ключом: block, unblock, extend (days; 0 — бессрочно), reset_transfers, max_pcs, note. */
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
      case "max_pcs":
        license.maxPcs = clamp(Math.floor(Number(value) || 1), 1, 100);
        break;
      case "note":
        license.note = String(value ?? "").slice(0, 500);
        break;
      default:
        return null;
    }

    await this.store.updateLicense(license);
    await this.log("admin", action, license.serial, value === undefined ? "" : String(value));
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
    return this.issue(license, hwid).license;
  }

  async publishUpdate(row: Omit<UpdateRow, "createdAt">): Promise<void> {
    await this.store.insertUpdate({ ...row, channel: row.channel === "beta" ? "beta" : "stable", createdAt: this.clock() });
    await this.log("admin", "publish_update", "", `${row.channel} ${row.version}`);
  }

  auditLog(limit = 200) {
    return this.store.auditLog(limit);
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

  private transfersLeft(license: LicenseRow): number {
    const yearPassed = this.clock().getTime() - license.transfersSince.getTime() >= 365 * DAY;
    return Math.max(0, TRANSFERS_PER_YEAR - (yearPassed ? 0 : license.transfersCount));
  }

  private resetTransfersIfYearPassed(license: LicenseRow) {
    if (this.clock().getTime() - license.transfersSince.getTime() >= 365 * DAY) {
      license.transfersCount = 0;
      license.transfersSince = this.clock();
    }
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

  /** Текст лицензии — поля в том же порядке, что LicenseData в плагине. */
  private sign(serial: string, hwid: string, plan: string, expiresAt: Date | null): SignedDocument {
    const now = this.clock();
    const payload: Record<string, string> = { serial, hwid, plan };
    if (expiresAt) payload.expiresAt = iso(expiresAt);
    payload.issuedAt = iso(now);
    payload.nextCheckAt = iso(new Date(now.getTime() + CHECK_EVERY_DAYS * DAY));
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
