// Копия всей базы Strassio для компьютеров владельца (Little Hub через хаб; пункт 52, core/backup.ts).
import { test } from "node:test";
import assert from "node:assert/strict";
import { createHash, createHmac, randomBytes } from "node:crypto";
import { gunzipSync } from "node:zlib";
import { backupName, dumpSql, pgBackup, type Query } from "../core/backup.js";
import { generateKeys, Signer } from "../core/crypto.js";
import { App } from "../core/http.js";
import { MemoryStore } from "../core/memoryStore.js";
import { Platform } from "../core/platform.js";
import { platformKeys } from "../core/platformAuth.js";
import { LicenseService } from "../core/service.js";

const KEY = "k".repeat(40);
const SECRET = "s".repeat(48);

/** Подставная база: таблицы, связи и строки; запросы пачками по id — по $1. */
function fakeDb(rows: Record<string, { id?: number; [k: string]: unknown }[]>, asked: string[] = []): Query {
  const cols = [
    { t: "licenses", c: "id", serial: true }, { t: "licenses", c: "serial", serial: false },
    { t: "activations", c: "id", serial: true }, { t: "activations", c: "license_id", serial: false },
    { t: "money_settings", c: "key", serial: false }, { t: "money_settings", c: "value", serial: false },
    { t: "platform_nonces", c: "nonce", serial: false },
  ];
  return async (text, params) => {
    asked.push(text);
    if (text.includes("information_schema.columns")) return cols;
    if (text.includes("pg_constraint")) return [{ t: "activations", ref: "licenses" }];
    if (text.includes("FROM audit_log")) return [{ t: "1794135900123" }];
    const t = /FROM "([a-z_]+)"/.exec(text)![1];
    let list = rows[t] ?? [];
    if (params?.length) list = list.filter((r) => (r.id ?? 0) > Number(params[0])).slice(0, 50);
    return [{ n: list.length, last: list.length ? String(list.at(-1)!.id) : null, json: JSON.stringify(list) }];
  };
}

test("копия: таблицы в порядке связей, большие — пачками по id, одноразовые отметки подписи не берём", async () => {
  const asked: string[] = [];
  const lic = Array.from({ length: 120 }, (_, i) => ({ id: i + 1, serial: `S${i + 1}` }));
  const sql = await dumpSql(fakeDb({ licenses: lic, activations: [{ id: 1, license_id: 1 }], money_settings: [{ key: "rate", value: "12000" }] }, asked), new Date("2026-11-08T10:00:00Z"));
  assert.match(sql, /^-- Strassio: копия базы данных, 2026-11-08T10:00:00.000Z/);
  assert.match(sql, /TRUNCATE "licenses", "activations", "money_settings" RESTART IDENTITY CASCADE;/);
  assert.equal((sql.match(/INSERT INTO "licenses"/g) ?? []).length, 3, "120 ключей — тремя пачками по 50");
  assert.ok(sql.indexOf('INSERT INTO "licenses"') < sql.indexOf('INSERT INTO "activations"'));
  assert.ok(sql.includes('"S120"') && sql.includes('"rate"'));
  assert.ok(!sql.includes("platform_nonces"));
  assert.match(sql, /SELECT setval\(pg_get_serial_sequence\('"licenses"', 'id'\)/);
  assert.match(sql, /COMMIT;\n$/);
  assert.ok(!asked.some((q) => q.includes('"platform_nonces"')));
  await assert.rejects(dumpSql(async (t) => (t.includes("information_schema") ? [{ t: "x; drop", c: "id", serial: false }] : []), new Date()), /Неожиданное имя/);
});

test("pc/backup: копия gzip с именем дня, размером и временем; та же — 204; без копии — 404; без подписи — 404", async () => {
  const keys = generateKeys();
  const store = new MemoryStore();
  const now = new Date("2026-11-08T20:30:00Z"); // в Ташкенте уже 9 ноября
  const make = (backup?: ReturnType<typeof pgBackup>) =>
    new App(new LicenseService(store, new Signer(keys.privateKey), () => now)).usePlatform(
      new Platform({ service: new LicenseService(store, new Signer(keys.privateKey), () => now, "hub"), keys: platformKeys(KEY, SECRET), adminPassword: "x".repeat(10), spendNonce: (n, u, s) => store.spendNonce(n, u, s), now: () => now.getTime(), backup }),
    );
  const call = async (app: App, pq: string, sign = true) => {
    const [path, q] = pq.split("?");
    const time = String(Math.floor(now.getTime() / 1000));
    const nonce = randomBytes(18).toString("base64url");
    const text = ["GET", pq, time, nonce, createHash("sha256").update("").digest("hex")].join("\n");
    const signature = sign ? createHmac("sha256", SECRET).update(text).digest("hex") : "0".repeat(64);
    return app.handle({ method: "GET", path, query: new URLSearchParams(q ?? ""), headers: { "x-hub-key": KEY, "x-hub-time": time, "x-hub-nonce": nonce, "x-hub-signature": signature }, body: null, rawBody: "", ip: "1.1.1.1" });
  };
  const app = make(pgBackup(fakeDb({ licenses: [{ id: 1, serial: "S1" }] })));
  const r = await call(app, "/api/platform/pc/backup?live=1&t=0");
  assert.equal(r.status, 200);
  assert.equal(r.file!.type, "application/gzip");
  assert.equal(r.file!.headers["x-backup-name"], "strassio-2026-11-09.sql.gz");
  assert.equal(r.file!.headers["x-backup-made"], "1794135900123");
  assert.equal(Number(r.file!.headers["x-backup-bytes"]), r.file!.body.length);
  assert.match(gunzipSync(r.file!.body).toString(), /INSERT INTO "licenses"/);
  assert.equal((await call(app, "/api/platform/pc/backup?have=1794135900123&live=1&t=0")).status, 204);
  assert.equal((await call(app, "/api/platform/pc/backup?have=1794135900122&live=1&t=0")).status, 200);
  assert.equal((await call(app, "/api/platform/pc/backup?live=1", false)).status, 404);
  assert.equal((await call(make(), "/api/platform/pc/backup?live=1")).status, 404);
  assert.equal(backupName(Date.parse("2026-11-08T18:59:59Z")), "strassio-2026-11-08.sql.gz");
});
