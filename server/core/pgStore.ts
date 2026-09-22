// Хранилище в Postgres (SPEC 15). Драйвер не импортируется здесь: адаптер платформы передаёт функцию
// query(текст, параметры) → строки. Таблицы создаются сами при первом запросе (CREATE ... IF NOT EXISTS).
import { UNKNOWN } from "./hwid.js";
import type { ActivationRow, AuditRow, LicenseRow, NewActivation, NewLicense, NewRequest, RequestRow, RequestStatus, Store, TrialRow, UpdateRow } from "./store.js";

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
    const rows = q
      ? await this.q(
          `SELECT * FROM licenses WHERE serial ILIKE $1 OR note ILIKE $1 OR client_first ILIKE $1 OR client_last ILIKE $1
             OR client_phone ILIKE $1 OR client_email ILIKE $1 OR client_telegram ILIKE $1 ORDER BY id DESC LIMIT $2`,
          ["%" + escapeLike(q) + "%", limit],
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
    return rows.map((r) => ({ id: Number(r.id), at: new Date(r.at), actor: r.actor, action: r.action, serial: r.serial, details: r.details }));
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
