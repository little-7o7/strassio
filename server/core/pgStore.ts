// Хранилище в Postgres (SPEC 15). Драйвер не импортируется здесь: адаптер платформы передаёт функцию
// query(текст, параметры) → строки. Таблицы создаются сами при первом запросе (CREATE ... IF NOT EXISTS).
import { UNKNOWN } from "./hwid.js";
import { phoneQuery } from "./insights.js";
import type { ActivationRow, AuditFilter, AuditRow, ContractFileRow, ContractRow, Currency, KeyTermsRow, LicenseRow, NewActivation, NewLicense, NewRequest, PaymentPartRow, PaymentRow, RequestRow, RequestStatus, StatsWindow, Store, StoreStats, TariffRow, TrialRow, UpdateRow } from "./store.js";

export type Query = (text: string, params?: unknown[]) => Promise<Record<string, any>[]>;

const SCHEMA = [
  `CREATE TABLE IF NOT EXISTS licenses (
     id SERIAL PRIMARY KEY,
     serial TEXT NOT NULL UNIQUE,
     plan TEXT NOT NULL DEFAULT 'full',
     status TEXT NOT NULL DEFAULT 'active',
     expires_at TIMESTAMPTZ NULL,
     max_pcs INT NOT NULL DEFAULT 1,
     transfers_count INT NOT NULL DEFAULT 0,
     transfers_since TIMESTAMPTZ NOT NULL DEFAULT now(),
     note TEXT NOT NULL DEFAULT '',
     created_at TIMESTAMPTZ NOT NULL DEFAULT now())`,
  `ALTER TABLE licenses ADD COLUMN IF NOT EXISTS recovery_code TEXT NOT NULL DEFAULT ''`,
  `ALTER TABLE licenses ADD COLUMN IF NOT EXISTS client_first TEXT NOT NULL DEFAULT ''`,
  `ALTER TABLE licenses ADD COLUMN IF NOT EXISTS client_last TEXT NOT NULL DEFAULT ''`,
  `ALTER TABLE licenses ADD COLUMN IF NOT EXISTS client_phone TEXT NOT NULL DEFAULT ''`,
  `ALTER TABLE licenses ADD COLUMN IF NOT EXISTS client_email TEXT NOT NULL DEFAULT ''`,
  `ALTER TABLE licenses ADD COLUMN IF NOT EXISTS client_birthday TEXT NOT NULL DEFAULT ''`,
  `ALTER TABLE licenses ADD COLUMN IF NOT EXISTS client_telegram TEXT NOT NULL DEFAULT ''`,
  `CREATE TABLE IF NOT EXISTS requests (
     id SERIAL PRIMARY KEY,
     created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
     client_first TEXT NOT NULL DEFAULT '',
     client_last TEXT NOT NULL DEFAULT '',
     client_phone TEXT NOT NULL DEFAULT '',
     client_email TEXT NOT NULL DEFAULT '',
     client_birthday TEXT NOT NULL DEFAULT '',
     client_telegram TEXT NOT NULL DEFAULT '',
     message TEXT NOT NULL DEFAULT '',
     status TEXT NOT NULL DEFAULT 'new',
     serial TEXT NOT NULL DEFAULT '')`,
  `CREATE TABLE IF NOT EXISTS activations (
     id SERIAL PRIMARY KEY,
     license_id INT NOT NULL REFERENCES licenses(id),
     hwid TEXT NOT NULL,
     status TEXT NOT NULL DEFAULT 'active',
     first_at TIMESTAMPTZ NOT NULL DEFAULT now(),
     last_check_at TIMESTAMPTZ NOT NULL DEFAULT now(),
     plugin_version TEXT NOT NULL DEFAULT '',
     corel_version TEXT NOT NULL DEFAULT '')`,
  `CREATE INDEX IF NOT EXISTS activations_license ON activations(license_id)`,
  `CREATE TABLE IF NOT EXISTS trials (
     id SERIAL PRIMARY KEY,
     hwid TEXT NOT NULL,
     h1 TEXT NULL, h2 TEXT NULL, h3 TEXT NULL, h4 TEXT NULL,
     started_at TIMESTAMPTZ NOT NULL DEFAULT now())`,
  `CREATE INDEX IF NOT EXISTS trials_h1 ON trials(h1)`,
  `CREATE INDEX IF NOT EXISTS trials_h2 ON trials(h2)`,
  `CREATE INDEX IF NOT EXISTS trials_h3 ON trials(h3)`,
  `CREATE INDEX IF NOT EXISTS trials_h4 ON trials(h4)`,
  `CREATE TABLE IF NOT EXISTS audit_log (
     id BIGSERIAL PRIMARY KEY,
     at TIMESTAMPTZ NOT NULL DEFAULT now(),
     actor TEXT NOT NULL,
     action TEXT NOT NULL,
     serial TEXT NOT NULL DEFAULT '',
     details TEXT NOT NULL DEFAULT '')`,
  `CREATE TABLE IF NOT EXISTS updates (
     id SERIAL PRIMARY KEY,
     channel TEXT NOT NULL,
     version TEXT NOT NULL,
     url TEXT NOT NULL,
     sha256 TEXT NOT NULL,
     notes_ru TEXT NOT NULL DEFAULT '',
     notes_en TEXT NOT NULL DEFAULT '',
     min_version TEXT NOT NULL DEFAULT '',
     created_at TIMESTAMPTZ NOT NULL DEFAULT now())`,
  // Хаб little_7o7 (2026-11-08): использованные nonce подписанных запросов, чтобы перехваченный запрос нельзя было
  // повторить. Строка живёт 10 минут, устаревшие стираются при каждой записи.
  `CREATE TABLE IF NOT EXISTS platform_nonces (
     nonce TEXT PRIMARY KEY,
     until_sec BIGINT NOT NULL)`,
  // Деньги (хаб, 2026-11-08): тарифы (общие и цены договоров), договоры клиентов и их файлы, закреплённая цена ключа,
  // начисления и поступления. Начисление помнит ключ по serial, без REFERENCES: удалённый ключ не стирает доход.
  // paid_at NULL — долг.
  `CREATE TABLE IF NOT EXISTS tariffs (
     id SERIAL PRIMARY KEY,
     name TEXT NOT NULL,
     days INT NULL,
     price NUMERIC(14,2) NOT NULL,
     currency TEXT NOT NULL DEFAULT 'UZS',
     active BOOLEAN NOT NULL DEFAULT true,
     created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
     contract_id INT NULL,
     kind TEXT NOT NULL DEFAULT 'key',
     base_id INT NULL,
     discount TEXT NOT NULL DEFAULT '',
     site BOOLEAN NOT NULL DEFAULT false,
     label TEXT NOT NULL DEFAULT '')`,
  `CREATE TABLE IF NOT EXISTS contracts (
     id SERIAL PRIMARY KEY,
     client_key TEXT NOT NULL,
     number TEXT NOT NULL DEFAULT '',
     signed_on TEXT NOT NULL DEFAULT '',
     valid_from TEXT NOT NULL DEFAULT '',
     valid_until TEXT NOT NULL DEFAULT '',
     party TEXT NOT NULL DEFAULT '',
     inn TEXT NOT NULL DEFAULT '',
     address TEXT NOT NULL DEFAULT '',
     terms TEXT NOT NULL DEFAULT '',
     pay_day INT NULL,
     currency TEXT NOT NULL DEFAULT 'UZS',
     note TEXT NOT NULL DEFAULT '',
     status TEXT NOT NULL DEFAULT 'active',
     closed_at TIMESTAMPTZ NULL,
     created_at TIMESTAMPTZ NOT NULL DEFAULT now())`,
  `CREATE TABLE IF NOT EXISTS contract_files (
     id SERIAL PRIMARY KEY,
     contract_id INT NOT NULL,
     name TEXT NOT NULL,
     type TEXT NOT NULL,
     size INT NOT NULL,
     data TEXT NOT NULL,
     created_at TIMESTAMPTZ NOT NULL DEFAULT now())`,
  `CREATE TABLE IF NOT EXISTS key_terms (
     serial TEXT PRIMARY KEY,
     contract_id INT NULL,
     tariff_id INT NULL,
     name TEXT NOT NULL DEFAULT '',
     days INT NULL,
     price NUMERIC(14,2) NOT NULL,
     currency TEXT NOT NULL DEFAULT 'UZS',
     updates_until TIMESTAMPTZ NULL,
     since TIMESTAMPTZ NOT NULL DEFAULT now())`,
  `CREATE TABLE IF NOT EXISTS payments (
     id SERIAL PRIMARY KEY,
     serial TEXT NOT NULL,
     kind TEXT NOT NULL DEFAULT 'sale',
     amount NUMERIC(14,2) NOT NULL,
     currency TEXT NOT NULL DEFAULT 'UZS',
     list_price NUMERIC(14,2) NOT NULL DEFAULT 0,
     tariff TEXT NOT NULL DEFAULT '',
     contract_id INT NULL,
     note TEXT NOT NULL DEFAULT '',
     due_on TEXT NOT NULL DEFAULT '',
     created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
     paid_at TIMESTAMPTZ NULL)`,
  `CREATE INDEX IF NOT EXISTS payments_serial ON payments(serial)`,
  `CREATE TABLE IF NOT EXISTS payment_parts (
     id SERIAL PRIMARY KEY,
     payment_id INT NOT NULL,
     amount NUMERIC(14,2) NOT NULL,
     currency TEXT NOT NULL DEFAULT 'UZS',
     rate NUMERIC(14,2) NOT NULL DEFAULT 0,
     credit NUMERIC(14,2) NOT NULL,
     method TEXT NOT NULL,
     note TEXT NOT NULL DEFAULT '',
     paid_at TIMESTAMPTZ NOT NULL DEFAULT now())`,
  `CREATE INDEX IF NOT EXISTS payment_parts_payment ON payment_parts(payment_id)`,
  `CREATE TABLE IF NOT EXISTS money_settings (
     key TEXT PRIMARY KEY,
     value TEXT NOT NULL)`,
  `CREATE TABLE IF NOT EXISTS usd_rates (
     day TEXT PRIMARY KEY,
     rate NUMERIC(14,2) NOT NULL)`,
];

export class PgStore implements Store {
  private ready: Promise<void> | null = null;

  constructor(private readonly sql: Query) {}

  private async q(text: string, params: unknown[] = []) {
    this.ready ??= (async () => {
      for (const statement of SCHEMA) await this.sql(statement);
    })().catch((e) => {
      this.ready = null;
      throw e;
    });
    await this.ready;
    return this.sql(text, params);
  }

  async findLicense(serial: string) {
    const rows = await this.q("SELECT * FROM licenses WHERE serial = $1", [serial]);
    return rows.length ? license(rows[0]) : null;
  }

  async insertLicense(r: NewLicense) {
    const rows = await this.q(
      `INSERT INTO licenses (serial, plan, status, expires_at, max_pcs, transfers_count, transfers_since, note, created_at,
         client_first, client_last, client_phone, client_email, client_birthday, client_telegram)
       VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15) RETURNING *`,
      [r.serial, r.plan, r.status, r.expiresAt, r.maxPcs, r.transfersCount, r.transfersSince, r.note, r.createdAt,
        r.client.firstName, r.client.lastName, r.client.phone, r.client.email, r.client.birthday, r.client.telegram],
    );
    return license(rows[0]);
  }

  async updateLicense(r: LicenseRow) {
    await this.q(
      `UPDATE licenses SET plan=$2, status=$3, expires_at=$4, max_pcs=$5, transfers_count=$6, transfers_since=$7, note=$8,
         client_first=$9, client_last=$10, client_phone=$11, client_email=$12, client_birthday=$13, client_telegram=$14 WHERE id=$1`,
      [r.id, r.plan, r.status, r.expiresAt, r.maxPcs, r.transfersCount, r.transfersSince, r.note,
        r.client.firstName, r.client.lastName, r.client.phone, r.client.email, r.client.birthday, r.client.telegram],
    );
  }

  async searchLicenses(query: string, limit: number) {
    const q = query.trim();
    // Телефон в любом виде: сравниваются только цифры (последние 9); $3 = '' — запрос не похож на телефон.
    const rows = q
      ? await this.q(
          `SELECT * FROM licenses WHERE serial ILIKE $1 OR note ILIKE $1 OR client_first ILIKE $1 OR client_last ILIKE $1
             OR client_phone ILIKE $1 OR client_email ILIKE $1 OR client_telegram ILIKE $1
             OR ($3 <> '' AND regexp_replace(client_phone, '[^0-9]', '', 'g') LIKE '%' || $3 || '%') ORDER BY id DESC LIMIT $2`,
          ["%" + escapeLike(q) + "%", limit, phoneQuery(q)],
        )
      : await this.q("SELECT * FROM licenses ORDER BY id DESC LIMIT $1", [limit]);
    return rows.map(license);
  }

  async deleteLicense(id: number) {
    await this.q("DELETE FROM activations WHERE license_id = $1", [id]);
    await this.q("DELETE FROM licenses WHERE id = $1", [id]);
  }

  async insertRequest(r: NewRequest) {
    const rows = await this.q(
      `INSERT INTO requests (created_at, client_first, client_last, client_phone, client_email, client_birthday, client_telegram, message, status, serial)
       VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10) RETURNING *`,
      [r.createdAt, r.client.firstName, r.client.lastName, r.client.phone, r.client.email, r.client.birthday, r.client.telegram, r.message, r.status, r.serial],
    );
    return request(rows[0]);
  }

  async listRequests(limit: number) {
    return (await this.q("SELECT * FROM requests ORDER BY id DESC LIMIT $1", [limit])).map(request);
  }

  async findRequest(id: number) {
    const rows = await this.q("SELECT * FROM requests WHERE id = $1", [id]);
    return rows[0] ? request(rows[0]) : null;
  }

  async updateRequest(r: RequestRow) {
    await this.q(
      `UPDATE requests SET client_first=$2, client_last=$3, client_phone=$4, client_email=$5, client_birthday=$6, client_telegram=$7,
         message=$8, status=$9, serial=$10 WHERE id=$1`,
      [r.id, r.client.firstName, r.client.lastName, r.client.phone, r.client.email, r.client.birthday, r.client.telegram, r.message, r.status, r.serial],
    );
  }

  async deleteRequest(id: number) {
    await this.q("DELETE FROM requests WHERE id = $1", [id]);
  }

  async activations(licenseId: number) {
    return (await this.q("SELECT * FROM activations WHERE license_id = $1 ORDER BY id", [licenseId])).map(activation);
  }

  async insertActivation(r: NewActivation) {
    const rows = await this.q(
      `INSERT INTO activations (license_id, hwid, status, first_at, last_check_at, plugin_version, corel_version)
       VALUES ($1,$2,$3,$4,$5,$6,$7) RETURNING *`,
      [r.licenseId, r.hwid, r.status, r.firstAt, r.lastCheckAt, r.pluginVersion, r.corelVersion],
    );
    return activation(rows[0]);
  }

  async updateActivation(r: ActivationRow) {
    await this.q(
      "UPDATE activations SET hwid=$2, status=$3, last_check_at=$4, plugin_version=$5, corel_version=$6 WHERE id=$1",
      [r.id, r.hwid, r.status, r.lastCheckAt, r.pluginVersion, r.corelVersion],
    );
  }

  async findActivation(id: number) {
    const rows = await this.q("SELECT * FROM activations WHERE id = $1", [id]);
    return rows.length ? activation(rows[0]) : null;
  }

  async trialsSharingPart(parts: string[]) {
    const p = parts.map((x) => (x === UNKNOWN ? null : x));
    const rows = await this.q("SELECT * FROM trials WHERE h1 = $1 OR h2 = $2 OR h3 = $3 OR h4 = $4 LIMIT 50", p);
    return rows.map(trial);
  }

  async insertTrial(hwid: string, startedAt: Date) {
    const p = hwid.split(".").map((x) => (x === UNKNOWN ? null : x));
    const rows = await this.q("INSERT INTO trials (hwid, h1, h2, h3, h4, started_at) VALUES ($1,$2,$3,$4,$5,$6) RETURNING *", [hwid, ...p, startedAt]);
    return trial(rows[0]);
  }

  async listTrials(limit: number) {
    return (await this.q("SELECT * FROM trials ORDER BY id DESC LIMIT $1", [limit])).map(trial);
  }

  async deleteTrial(id: number) {
    await this.q("DELETE FROM trials WHERE id = $1", [id]);
  }

  async audit(e: Omit<AuditRow, "id">) {
    await this.q("INSERT INTO audit_log (at, actor, action, serial, details) VALUES ($1,$2,$3,$4,$5)", [e.at, e.actor, e.action, e.serial, e.details]);
  }

  async auditLog(limit: number) {
    const rows = await this.q("SELECT * FROM audit_log ORDER BY id DESC LIMIT $1", [limit]);
    return rows.map(AUDIT_ROW);
  }

  async latestUpdate(channel: string) {
    const rows = await this.q("SELECT * FROM updates WHERE channel = $1 ORDER BY id DESC LIMIT 1", [channel]);
    if (!rows.length) return null;
    const r = rows[0];
    return {
      channel: r.channel,
      version: r.version,
      url: r.url,
      sha256: r.sha256,
      notesRu: r.notes_ru,
      notesEn: r.notes_en,
      minVersion: r.min_version,
      createdAt: new Date(r.created_at),
    };
  }

  async insertUpdate(r: UpdateRow) {
    await this.q(
      "INSERT INTO updates (channel, version, url, sha256, notes_ru, notes_en, min_version, created_at) VALUES ($1,$2,$3,$4,$5,$6,$7,$8)",
      [r.channel, r.version, r.url, r.sha256, r.notesRu, r.notesEn, r.minVersion, r.createdAt],
    );
  }

  // ---------- Хаб little_7o7 (/api/platform) ----------

  /** Одна команда: стереть устаревшие и записать новый; уже есть — ни одной строки в ответе (повтор). */
  async spendNonce(nonce: string, untilSec: number, nowSec: number) {
    const rows = await this.q(
      `WITH gone AS (DELETE FROM platform_nonces WHERE until_sec < $3)
       INSERT INTO platform_nonces (nonce, until_sec) VALUES ($1, $2) ON CONFLICT (nonce) DO NOTHING RETURNING 1 AS ok`,
      [nonce, untilSec, nowSec],
    );
    return rows.length > 0;
  }

  async stats(at: StatsWindow): Promise<StoreStats> {
    const [r] = await this.q(
      `SELECT
         (SELECT count(*) FROM licenses) AS licenses,
         (SELECT count(*) FROM licenses WHERE status = 'blocked') AS blocked,
         (SELECT count(*) FROM licenses WHERE status <> 'blocked' AND expires_at <= $1) AS expired,
         (SELECT count(*) FROM licenses WHERE status <> 'blocked' AND expires_at > $1 AND expires_at <= $2) AS expiring,
         (SELECT count(*) FROM licenses WHERE expires_at IS NULL) AS unlimited,
         (SELECT count(*) FROM activations WHERE status = 'active') AS computers,
         (SELECT count(*) FROM activations WHERE status = 'active' AND last_check_at > $3) AS checked_since,
         (SELECT count(*) FROM trials) AS trials,
         (SELECT count(*) FROM trials WHERE started_at > $4) AS trials_active,
         (SELECT count(*) FROM requests WHERE status = 'new') AS requests_new,
         (SELECT count(*) FROM requests WHERE status = 'working') AS requests_working`,
      [at.now, at.soon, at.dayAgo, at.trialSince],
    );
    return {
      licenses: Number(r.licenses),
      blocked: Number(r.blocked),
      expired: Number(r.expired),
      expiring: Number(r.expiring),
      unlimited: Number(r.unlimited),
      computers: Number(r.computers),
      checkedSince: Number(r.checked_since),
      trials: Number(r.trials),
      trialsActive: Number(r.trials_active),
      requestsNew: Number(r.requests_new),
      requestsWorking: Number(r.requests_working),
    };
  }

  async expiringLicenses(now: Date, soon: Date, limit: number) {
    const rows = await this.q(
      "SELECT * FROM licenses WHERE status <> 'blocked' AND expires_at > $1 AND expires_at <= $2 ORDER BY expires_at LIMIT $3",
      [now, soon, limit],
    );
    return rows.map(license);
  }

  async ping() {
    await this.q("SELECT 1");
  }

  async allLicenses(limit: number) {
    return (await this.q("SELECT * FROM licenses ORDER BY id DESC LIMIT $1", [limit])).map(license);
  }

  async allActivations(limit: number) {
    return (await this.q("SELECT * FROM activations ORDER BY id LIMIT $1", [limit])).map(activation);
  }

  async auditSearch(f: AuditFilter, limit: number) {
    const where: string[] = [];
    const params: unknown[] = [];
    const add = (sql: string, value: unknown) => {
      params.push(value);
      where.push(sql.replace("?", "$" + params.length));
    };
    if (f.serial) add("serial = ?", f.serial);
    if (f.actors?.length) add("actor = ANY(?)", f.actors);
    if (f.action) add("action = ?", f.action);
    if (f.from) add("at >= ?", f.from);
    if (f.to) add("at < ?", f.to);
    params.push(limit);
    const rows = await this.q(`SELECT * FROM audit_log ${where.length ? "WHERE " + where.join(" AND ") : ""} ORDER BY id DESC LIMIT $${params.length}`, params);
    return rows.map(AUDIT_ROW);
  }

  // ---------- Деньги ----------

  async listTariffs() {
    return (await this.q("SELECT * FROM tariffs ORDER BY id")).map(tariff);
  }

  async findTariff(id: number) {
    const rows = await this.q("SELECT * FROM tariffs WHERE id = $1", [id]);
    return rows.length ? tariff(rows[0]) : null;
  }

  async insertTariff(t: Omit<TariffRow, "id">) {
    const rows = await this.q(
      `INSERT INTO tariffs (name, days, price, currency, active, created_at, contract_id, kind, base_id, discount, site, label)
       VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12) RETURNING *`,
      [t.name, t.days, t.price, t.currency, t.active, t.createdAt, t.contractId, t.kind, t.baseId, t.discount, t.site, t.label],
    );
    return tariff(rows[0]);
  }

  async updateTariff(t: TariffRow) {
    await this.q(
      "UPDATE tariffs SET name=$2, days=$3, price=$4, currency=$5, active=$6, contract_id=$7, kind=$8, base_id=$9, discount=$10, site=$11, label=$12 WHERE id=$1",
      [t.id, t.name, t.days, t.price, t.currency, t.active, t.contractId, t.kind, t.baseId, t.discount, t.site, t.label],
    );
  }

  async insertPayment(p: Omit<PaymentRow, "id">) {
    const rows = await this.q(
      `INSERT INTO payments (serial, kind, amount, currency, list_price, tariff, contract_id, note, due_on, created_at, paid_at)
       VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11) RETURNING *`,
      [p.serial, p.kind, p.amount, p.currency, p.listPrice, p.tariff, p.contractId, p.note, p.dueOn, p.createdAt, p.paidAt],
    );
    return payment(rows[0]);
  }

  async findPayment(id: number) {
    const rows = await this.q("SELECT * FROM payments WHERE id = $1", [id]);
    return rows.length ? payment(rows[0]) : null;
  }

  async updatePayment(p: PaymentRow) {
    await this.q(
      "UPDATE payments SET kind=$2, amount=$3, currency=$4, list_price=$5, tariff=$6, contract_id=$7, note=$8, due_on=$9, paid_at=$10 WHERE id=$1",
      [p.id, p.kind, p.amount, p.currency, p.listPrice, p.tariff, p.contractId, p.note, p.dueOn, p.paidAt],
    );
  }

  async deletePayment(id: number) {
    await this.q("DELETE FROM payment_parts WHERE payment_id = $1", [id]);
    await this.q("DELETE FROM payments WHERE id = $1", [id]);
  }

  async listPayments(limit: number) {
    return (await this.q("SELECT * FROM payments ORDER BY id DESC LIMIT $1", [limit])).map(payment);
  }

  async paymentsFor(serial: string) {
    return (await this.q("SELECT * FROM payments WHERE serial = $1 ORDER BY id DESC", [serial])).map(payment);
  }

  async insertPart(p: Omit<PaymentPartRow, "id">) {
    const rows = await this.q(
      "INSERT INTO payment_parts (payment_id, amount, currency, rate, credit, method, note, paid_at) VALUES ($1,$2,$3,$4,$5,$6,$7,$8) RETURNING *",
      [p.paymentId, p.amount, p.currency, p.rate, p.credit, p.method, p.note, p.paidAt],
    );
    return part(rows[0]);
  }

  async findPart(id: number) {
    const rows = await this.q("SELECT * FROM payment_parts WHERE id = $1", [id]);
    return rows.length ? part(rows[0]) : null;
  }

  async deletePart(id: number) {
    await this.q("DELETE FROM payment_parts WHERE id = $1", [id]);
  }

  async partsOf(paymentIds: number[]) {
    if (!paymentIds.length) return [];
    return (await this.q("SELECT * FROM payment_parts WHERE payment_id = ANY($1::int[]) ORDER BY id", [paymentIds])).map(part);
  }

  async listParts(limit: number) {
    return (await this.q("SELECT * FROM payment_parts ORDER BY id LIMIT $1", [limit])).map(part);
  }

  async listContracts() {
    return (await this.q("SELECT * FROM contracts ORDER BY id")).map(contract);
  }

  async findContract(id: number) {
    const rows = await this.q("SELECT * FROM contracts WHERE id = $1", [id]);
    return rows.length ? contract(rows[0]) : null;
  }

  async insertContract(c: Omit<ContractRow, "id">) {
    const rows = await this.q(
      `INSERT INTO contracts (client_key, number, signed_on, valid_from, valid_until, party, inn, address, terms, pay_day, currency, note, status, closed_at, created_at)
       VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15) RETURNING *`,
      [c.clientKey, c.number, c.signedOn, c.validFrom, c.validUntil, c.party, c.inn, c.address, c.terms, c.payDay, c.currency, c.note, c.status, c.closedAt, c.createdAt],
    );
    return contract(rows[0]);
  }

  async updateContract(c: ContractRow) {
    await this.q(
      `UPDATE contracts SET client_key=$2, number=$3, signed_on=$4, valid_from=$5, valid_until=$6, party=$7, inn=$8, address=$9, terms=$10,
         pay_day=$11, currency=$12, note=$13, status=$14, closed_at=$15 WHERE id=$1`,
      [c.id, c.clientKey, c.number, c.signedOn, c.validFrom, c.validUntil, c.party, c.inn, c.address, c.terms, c.payDay, c.currency, c.note, c.status, c.closedAt],
    );
  }

  async deleteContract(id: number) {
    await this.q("DELETE FROM contract_files WHERE contract_id = $1", [id]);
    await this.q("UPDATE tariffs SET active = false WHERE contract_id = $1", [id]);
    await this.q("DELETE FROM contracts WHERE id = $1", [id]);
  }

  async contractFiles(contractId: number) {
    return (await this.q("SELECT id, contract_id, name, type, size, '' AS data, created_at FROM contract_files WHERE contract_id = $1 ORDER BY id", [contractId])).map(contractFile);
  }

  async findContractFile(id: number) {
    const rows = await this.q("SELECT * FROM contract_files WHERE id = $1", [id]);
    return rows.length ? contractFile(rows[0]) : null;
  }

  async insertContractFile(f: Omit<ContractFileRow, "id">) {
    const rows = await this.q(
      "INSERT INTO contract_files (contract_id, name, type, size, data, created_at) VALUES ($1,$2,$3,$4,$5,$6) RETURNING id, contract_id, name, type, size, '' AS data, created_at",
      [f.contractId, f.name, f.type, f.size, f.data, f.createdAt],
    );
    return contractFile(rows[0]);
  }

  async deleteContractFile(id: number) {
    await this.q("DELETE FROM contract_files WHERE id = $1", [id]);
  }

  async keyTerms(serial: string) {
    const rows = await this.q("SELECT * FROM key_terms WHERE serial = $1", [serial]);
    return rows.length ? keyTerms(rows[0]) : null;
  }

  async allKeyTerms() {
    return (await this.q("SELECT * FROM key_terms")).map(keyTerms);
  }

  async saveKeyTerms(t: KeyTermsRow) {
    await this.q(
      `INSERT INTO key_terms (serial, contract_id, tariff_id, name, days, price, currency, updates_until, since)
       VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9)
       ON CONFLICT (serial) DO UPDATE SET contract_id=$2, tariff_id=$3, name=$4, days=$5, price=$6, currency=$7, updates_until=$8, since=$9`,
      [t.serial, t.contractId, t.tariffId, t.name, t.days, t.price, t.currency, t.updatesUntil, t.since],
    );
  }

  async getSetting(key: string) {
    const rows = await this.q("SELECT value FROM money_settings WHERE key = $1", [key]);
    return rows.length ? String(rows[0].value) : null;
  }

  async setSetting(key: string, value: string) {
    await this.q("INSERT INTO money_settings (key, value) VALUES ($1,$2) ON CONFLICT (key) DO UPDATE SET value = $2", [key, value]);
  }

  async rateFor(day: string) {
    const rows = await this.q("SELECT rate FROM usd_rates WHERE day = $1", [day]);
    return rows.length ? Number(rows[0].rate) : null;
  }

  async saveRate(day: string, rate: number) {
    await this.q("INSERT INTO usd_rates (day, rate) VALUES ($1,$2) ON CONFLICT (day) DO UPDATE SET rate = $2", [day, rate]);
  }

  async latestRate() {
    const rows = await this.q("SELECT rate FROM usd_rates ORDER BY day DESC LIMIT 1");
    return rows.length ? Number(rows[0].rate) : null;
  }
}

// NUMERIC приходит строкой («1200000.00») — в число.
const num = (v: unknown) => Number(v);
const intOrNull = (v: unknown) => (v === null || v === undefined ? null : Number(v));
const cur = (v: unknown): Currency => (v === "USD" ? "USD" : "UZS");
const bool = (v: unknown) => v === true || v === "t";

function tariff(r: Record<string, any>): TariffRow {
  return {
    id: Number(r.id),
    name: r.name,
    days: intOrNull(r.days),
    price: num(r.price),
    currency: cur(r.currency),
    active: bool(r.active),
    createdAt: new Date(r.created_at),
    contractId: intOrNull(r.contract_id),
    kind: r.kind === "updates" ? "updates" : "key",
    baseId: intOrNull(r.base_id),
    discount: r.discount ?? "",
    site: bool(r.site),
    label: r.label ?? "",
  };
}

function payment(r: Record<string, any>): PaymentRow {
  return {
    id: Number(r.id),
    serial: r.serial,
    kind: r.kind === "renewal" || r.kind === "updates" ? r.kind : "sale",
    amount: num(r.amount),
    currency: cur(r.currency),
    listPrice: num(r.list_price ?? 0),
    tariff: r.tariff ?? "",
    contractId: intOrNull(r.contract_id),
    note: r.note ?? "",
    dueOn: r.due_on ?? "",
    createdAt: new Date(r.created_at),
    paidAt: r.paid_at ? new Date(r.paid_at) : null,
  };
}

function part(r: Record<string, any>): PaymentPartRow {
  return {
    id: Number(r.id),
    paymentId: Number(r.payment_id),
    amount: num(r.amount),
    currency: cur(r.currency),
    rate: num(r.rate ?? 0),
    credit: num(r.credit),
    method: r.method === "card" || r.method === "transfer" ? r.method : "cash",
    note: r.note ?? "",
    paidAt: new Date(r.paid_at),
  };
}

function contract(r: Record<string, any>): ContractRow {
  return {
    id: Number(r.id),
    clientKey: r.client_key,
    number: r.number ?? "",
    signedOn: r.signed_on ?? "",
    validFrom: r.valid_from ?? "",
    validUntil: r.valid_until ?? "",
    party: r.party ?? "",
    inn: r.inn ?? "",
    address: r.address ?? "",
    terms: r.terms ?? "",
    payDay: intOrNull(r.pay_day),
    currency: cur(r.currency),
    note: r.note ?? "",
    status: r.status === "closed" ? "closed" : "active",
    closedAt: r.closed_at ? new Date(r.closed_at) : null,
    createdAt: new Date(r.created_at),
  };
}

function contractFile(r: Record<string, any>): ContractFileRow {
  return { id: Number(r.id), contractId: Number(r.contract_id), name: r.name, type: r.type, size: Number(r.size), data: r.data ?? "", createdAt: new Date(r.created_at) };
}

function keyTerms(r: Record<string, any>): KeyTermsRow {
  return {
    serial: r.serial,
    contractId: intOrNull(r.contract_id),
    tariffId: intOrNull(r.tariff_id),
    name: r.name ?? "",
    days: intOrNull(r.days),
    price: num(r.price),
    currency: cur(r.currency),
    updatesUntil: r.updates_until ? new Date(r.updates_until) : null,
    since: new Date(r.since),
  };
}

function license(r: Record<string, any>): LicenseRow {
  return {
    id: Number(r.id),
    serial: r.serial,
    plan: r.plan,
    status: r.status,
    expiresAt: r.expires_at ? new Date(r.expires_at) : null,
    maxPcs: Number(r.max_pcs),
    transfersCount: Number(r.transfers_count),
    transfersSince: new Date(r.transfers_since),
    note: r.note,
    createdAt: new Date(r.created_at),
    client: {
      firstName: r.client_first ?? "",
      lastName: r.client_last ?? "",
      phone: r.client_phone ?? "",
      email: r.client_email ?? "",
      birthday: r.client_birthday ?? "",
      telegram: r.client_telegram ?? "",
    },
  };
}

function activation(r: Record<string, any>): ActivationRow {
  return {
    id: Number(r.id),
    licenseId: Number(r.license_id),
    hwid: r.hwid,
    status: r.status,
    firstAt: new Date(r.first_at),
    lastCheckAt: new Date(r.last_check_at),
    pluginVersion: r.plugin_version,
    corelVersion: r.corel_version,
  };
}

function trial(r: Record<string, any>): TrialRow {
  return { id: Number(r.id), hwid: r.hwid, startedAt: new Date(r.started_at) };
}

function escapeLike(text: string): string {
  return text.replace(/[\\%_]/g, (c) => "\\" + c);
}

function request(r: Record<string, any>): RequestRow {
  return {
    id: Number(r.id),
    createdAt: new Date(r.created_at),
    client: {
      firstName: r.client_first ?? "",
      lastName: r.client_last ?? "",
      phone: r.client_phone ?? "",
      email: r.client_email ?? "",
      birthday: r.client_birthday ?? "",
      telegram: r.client_telegram ?? "",
    },
    message: r.message ?? "",
    status: (r.status ?? "new") as RequestStatus,
    serial: r.serial ?? "",
  };
}

const AUDIT_ROW = (r: Record<string, any>): AuditRow => ({ id: Number(r.id), at: new Date(r.at), actor: r.actor, action: r.action, serial: r.serial, details: r.details });
