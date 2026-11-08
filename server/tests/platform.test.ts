// Закрытый API для хаба little_7o7 (/api/platform/*): подпись, повтор, пароль для опасного и все адреса.
import { test } from "node:test";
import assert from "node:assert/strict";
import { createHash, createHmac, randomBytes } from "node:crypto";
import { generateKeys, Signer, verifyDocument } from "../core/crypto.js";
import { App, type HttpRequest } from "../core/http.js";
import { MemoryStore } from "../core/memoryStore.js";
import { Platform } from "../core/platform.js";
import { platformKeys } from "../core/platformAuth.js";
import { LicenseService } from "../core/service.js";

const DAY = 24 * 60 * 60 * 1000;
const keys = generateKeys();
const KEY = "k".repeat(40);
const SECRET = "s".repeat(48);
const PASSWORD = "admin-pass-123";
const HWID = ["a", "b", "c", "d"].map((x) => x.repeat(16)).join(".");

/** Подпись ровно как в хабе (little_7o7 src/lib/platform-client.ts) — написана здесь заново, не из кода сервера. */
function hubSign(secret: string, method: string, pathAndQuery: string, time: string, nonce: string, body: string) {
  const text = [method, pathAndQuery, time, nonce, createHash("sha256").update(body).digest("hex")].join("\n");
  return createHmac("sha256", secret).update(text).digest("hex");
}

type Opts = { key?: string; secret?: string; time?: number; nonce?: string; signature?: string };

function setup(options: { keys?: boolean; password?: string } = {}) {
  const store = new MemoryStore();
  let now = new Date("2026-11-08T10:00:00Z");
  const clock = () => now;
  const signer = new Signer(keys.privateKey);
  const release = { ok: true as const, release: { tag: "v0.9.2", version: "0.9.2", url: "https://github.com/x/y.exe", sha256: "a".repeat(64), notesRu: "Новое", notesEn: "New" } };
  const platform = new Platform({
    service: new LicenseService(store, signer, clock, "hub"),
    keys: options.keys === false ? null : platformKeys(KEY, SECRET),
    adminPassword: options.password ?? PASSWORD,
    spendNonce: (n, until, nowSec) => store.spendNonce(n, until, nowSec),
    fetchRelease: async () => release,
    now: () => now.getTime(),
  });
  // Сайт и плагин — через обычный App: так же, как на Vercel.
  const site = new App(new LicenseService(store, signer, clock), PASSWORD).usePlatform(platform);

  function req(method: string, pathAndQuery: string, body?: Record<string, unknown>, o: Opts = {}): HttpRequest {
    const [path, q] = pathAndQuery.split("?");
    const raw = method === "POST" ? JSON.stringify(body ?? {}) : "";
    const time = String(o.time ?? Math.floor(now.getTime() / 1000));
    const nonce = o.nonce ?? randomBytes(18).toString("base64url");
    const signature = o.signature ?? hubSign(o.secret ?? SECRET, method, pathAndQuery, time, nonce, raw);
    const headers = { "x-hub-key": o.key ?? KEY, "x-hub-time": time, "x-hub-nonce": nonce, "x-hub-signature": signature };
    return { method, path, query: new URLSearchParams(q ?? ""), headers, body: raw ? JSON.parse(raw) : null, rawBody: raw, ip: "9.9.9.9" };
  }

  async function call(method: string, pathAndQuery: string, body?: Record<string, unknown>, o: Opts = {}) {
    const r = await site.handle(req(method, pathAndQuery, body, o));
    return { status: r.status, json: r.json as Record<string, any> };
  }

  return { store, platform, site, req, call, advance: (days: number) => (now = new Date(now.getTime() + days * DAY)), now: () => now };
}

const NOT_FOUND = { ok: false, error: "no_route" };

test("API выключен без PLATFORM_API_KEY/SECRET: 404 на всё, как несуществующий адрес", async () => {
  const s = setup({ keys: false });
  const r = await s.call("GET", "/api/platform/ping");
  assert.equal(r.status, 404);
  assert.deepEqual(r.json, NOT_FOUND);
  assert.equal(platformKeys("short", SECRET), null);
  assert.equal(platformKeys(KEY, KEY), null, "key и secret не могут совпадать");
  assert.notEqual(platformKeys(" " + KEY + "\n", SECRET), null, "пробелы по краям из поля Vercel не мешают");
});

test("подпись: чужой key, чужой secret, старое время, плохой nonce, без заголовков — 404 и nonce не тратится", async () => {
  const s = setup();
  assert.equal((await s.call("GET", "/api/platform/ping")).status, 200);
  for (const o of [{ key: "x".repeat(40) }, { secret: "z".repeat(48) }, { time: Math.floor(s.now().getTime() / 1000) - 301 }, { time: Math.floor(s.now().getTime() / 1000) + 301 }, { nonce: "short" }, { signature: "0".repeat(64) }]) {
    const r = await s.call("GET", "/api/platform/ping", undefined, o);
    assert.equal(r.status, 404, JSON.stringify(o));
    assert.deepEqual(r.json, NOT_FOUND);
  }
  const bare = await s.site.handle({ method: "GET", path: "/api/platform/ping", query: new URLSearchParams(), headers: {}, body: null, ip: "1.1.1.1" });
  assert.equal(bare.status, 404);
  assert.equal(s.store.nonces.size, 1, "неверная подпись базу не трогает");
});

test("подпись: тот же nonce второй раз — 404; тело и запрос входят в подпись", async () => {
  const s = setup();
  const nonce = "n".repeat(20);
  assert.equal((await s.call("GET", "/api/platform/ping", undefined, { nonce })).status, 200);
  assert.equal((await s.call("GET", "/api/platform/ping", undefined, { nonce })).status, 404);

  // Подписано одно тело, пришло другое.
  const good = s.req("POST", "/api/platform/licenses", { count: 1 });
  const forged = await s.site.handle({ ...good, rawBody: JSON.stringify({ count: 500 }), body: { count: 500 } });
  assert.equal(forged.status, 404);
  // Подписан один запрос, пришёл другой.
  const q = s.req("GET", "/api/platform/licenses?q=a");
  assert.equal((await s.site.handle({ ...q, query: new URLSearchParams("q=b") })).status, 404);
  assert.equal((await s.site.handle(q)).status, 200);
});

test("ping и health", async () => {
  const s = setup();
  const ping = await s.call("GET", "/api/platform/ping");
  assert.equal(ping.json.project, "strassio");
  const health = await s.call("GET", "/api/platform/health");
  assert.equal(health.status, 200);
  assert.equal(health.json.db, "ok");
  assert.equal(typeof health.json.ms, "number");
  assert.equal((await s.call("GET", "/api/platform/nope")).status, 404);
  assert.equal((await s.call("POST", "/api/platform/ping")).status, 404, "не тот метод — как нет адреса");
});

test("создать ключи: клиент плоскими полями, срок, заметка; журнал — «hub»", async () => {
  const s = setup();
  const r = await s.call("POST", "/api/platform/licenses", { count: 2, days: 30, note: "оптом", firstName: "Иван", lastName: "Петров", phone: "+998 90 123", email: "i@example.com", birthday: "", telegram: "@ivan_p" });
  assert.equal(r.status, 200);
  assert.equal(r.json.keys.length, 2);
  const card = await s.call("GET", `/api/platform/licenses/${r.json.keys[0]}`);
  assert.equal(card.json.license.client.telegram, "ivan_p");
  assert.equal(card.json.license.note, "оптом");
  assert.equal(card.json.license.daysLeft, 30);
  assert.equal(card.json.license.status, "active");
  assert.equal(s.store.log.at(-1)!.actor, "hub");

  assert.equal((await s.call("POST", "/api/platform/licenses", { email: "плохо" })).json.error, "bad_client");
  assert.equal((await s.call("POST", "/api/platform/licenses", { count: 1, days: -5 })).json.error, "bad_request");
  const forever = await s.call("POST", "/api/platform/licenses", {});
  assert.equal((await s.call("GET", `/api/platform/licenses/${forever.json.keys[0]}`)).json.license.expiresAt, null);
});

test("поиск ключей и карточка: компьютеры, нет ключа — 404 missing", async () => {
  const s = setup();
  const { json } = await s.call("POST", "/api/platform/licenses", { firstName: "Мария", email: "m@example.com" });
  const serial = json.keys[0];
  assert.equal((await s.site.handle({ method: "POST", path: "/api/activate", query: new URLSearchParams(), headers: {}, body: { serial, hwid: HWID, email: "m@example.com" }, ip: "2.2.2.2" })).status, 200);

  const found = await s.call("GET", "/api/platform/licenses?q=" + encodeURIComponent("Мар"));
  assert.equal(found.json.licenses.length, 1);
  const card = (await s.call("GET", `/api/platform/licenses/${serial.toLowerCase().replace(/-/g, "")}`)).json.license;
  assert.equal(card.serial, serial);
  assert.equal(card.computers.length, 1);
  assert.equal(card.computers[0].active, true);
  assert.equal(card.computers[0].hwid, HWID);
  assert.match(card.computers[0].code, /^[0-9A-F]{4}(-[0-9A-F]{4}){3}$/);

  const none = await s.call("GET", "/api/platform/licenses/STRS-AAAA-BBBB-CCCC");
  assert.equal(none.status, 404);
  assert.equal(none.json.error, "missing", "свой 404 отличается от 404 «нет подписи»");
  assert.equal((await s.call("GET", "/api/platform/licenses?q=" + encodeURIComponent("нетакого"))).json.licenses.length, 0);
});

test("опасное — только с паролем админки Strassio: заблокировать, отозвать, удалить", async () => {
  const s = setup();
  const serial = (await s.call("POST", "/api/platform/licenses", {})).json.keys[0];
  assert.equal((await s.call("POST", `/api/platform/licenses/${serial}/block`, {})).json.error, "password_required");
  const wrong = await s.call("POST", `/api/platform/licenses/${serial}/block`, { adminPassword: "nope" });
  assert.equal(wrong.status, 403);
  assert.equal(wrong.json.error, "wrong_password");
  assert.equal((await s.call("POST", `/api/platform/licenses/${serial}/block`, { adminPassword: " " + PASSWORD + " " })).status, 200);
  assert.equal((await s.call("GET", `/api/platform/licenses/${serial}`)).json.license.status, "blocked");
  assert.equal((await s.call("POST", `/api/platform/licenses/${serial}/unblock`, {})).status, 200, "разблокировать — без пароля");
  assert.equal((await s.call("GET", `/api/platform/licenses/${serial}`)).json.license.status, "active");

  const noPassword = setup({ password: "" });
  const k = (await noPassword.call("POST", "/api/platform/licenses", {})).json.keys[0];
  assert.equal((await noPassword.call("POST", `/api/platform/licenses/${k}/block`, { adminPassword: "x" })).json.error, "password_not_set");
});

test("неверный пароль 10 раз — дальше 429", async () => {
  const s = setup();
  const serial = (await s.call("POST", "/api/platform/licenses", {})).json.keys[0];
  const statuses: number[] = [];
  for (let i = 0; i < 11; i++) statuses.push((await s.call("POST", `/api/platform/licenses/${serial}/block`, { adminPassword: "guess" + i })).status);
  assert.deepEqual(statuses.slice(0, 10), Array(10).fill(403));
  assert.equal(statuses[10], 429);
  assert.equal((await s.call("POST", `/api/platform/licenses/${serial}/block`, { adminPassword: PASSWORD })).status, 429, "и верный пароль ждёт");
});

test("продлить, бессрочно, перенос сейчас, заметка, клиент", async () => {
  const s = setup();
  const serial = (await s.call("POST", "/api/platform/licenses", { days: 10 })).json.keys[0];
  const get = async () => (await s.call("GET", `/api/platform/licenses/${serial}`)).json.license;

  assert.equal((await s.call("POST", `/api/platform/licenses/${serial}/extend`, { days: 20 })).status, 200);
  assert.equal((await get()).daysLeft, 30);
  assert.equal((await s.call("POST", `/api/platform/licenses/${serial}/extend`, { days: "x" })).json.error, "bad_request");
  await s.call("POST", `/api/platform/licenses/${serial}/extend`, { days: 0 });
  assert.equal((await get()).expiresAt, null);

  assert.equal((await s.call("POST", `/api/platform/licenses/${serial}/transfers`, {})).status, 200);
  await s.call("POST", `/api/platform/licenses/${serial}/note`, { note: "звонил" });
  assert.equal((await get()).note, "звонил");
  await s.call("POST", `/api/platform/licenses/${serial}/client`, { firstName: "Олег", phone: "+998", telegram: "t.me/oleg_s" });
  assert.equal((await get()).client.telegram, "oleg_s");
  assert.equal((await s.call("POST", `/api/platform/licenses/${serial}/client`, { birthday: "31.12.1990" })).json.error, "bad_client");
  assert.equal((await s.call("POST", "/api/platform/licenses/STRS-AAAA-BBBB-CCCC/note", { note: "x" })).json.error, "missing");
});

test("удалить ключ: последние 4 знака и пароль", async () => {
  const s = setup();
  const serial: string = (await s.call("POST", "/api/platform/licenses", {})).json.keys[0];
  assert.equal((await s.call("POST", `/api/platform/licenses/${serial}/delete`, { confirm: "0000", adminPassword: PASSWORD })).json.error, "confirm");
  assert.equal((await s.call("POST", `/api/platform/licenses/${serial}/delete`, { confirm: serial.slice(-4).toLowerCase(), adminPassword: PASSWORD })).status, 200);
  assert.equal((await s.call("GET", `/api/platform/licenses/${serial}`)).status, 404);
});

test("отозвать компьютер и офлайн-файл", async () => {
  const s = setup();
  const serial = (await s.call("POST", "/api/platform/licenses", {})).json.keys[0];
  const off = await s.call("POST", `/api/platform/licenses/${serial}/offline`, { hwid: " " + HWID + " " });
  assert.equal(off.status, 200);
  assert.ok(verifyDocument(off.json.file, keys.publicKey), "файл подписан ключом лицензий");
  assert.equal(JSON.parse(off.json.file.payload).offline, true);
  assert.equal((await s.call("POST", `/api/platform/licenses/${serial}/offline`, { hwid: "плохо" })).json.error, "bad_request");

  const id = (await s.call("GET", `/api/platform/licenses/${serial}`)).json.license.computers[0].id;
  assert.equal((await s.call("POST", `/api/platform/activations/${id}/revoke`, {})).json.error, "password_required");
  assert.equal((await s.call("POST", `/api/platform/activations/${id}/revoke`, { adminPassword: PASSWORD })).status, 200);
  assert.equal((await s.call("GET", `/api/platform/licenses/${serial}`)).json.license.computers[0].active, false);
  assert.equal((await s.call("POST", "/api/platform/activations/999/revoke", { adminPassword: PASSWORD })).json.error, "missing");
});

test("заявки: список, состояние, ключ по заявке (второй раз — 409), удалить", async () => {
  const s = setup();
  const site = (body: unknown) => s.site.handle({ method: "POST", path: "/api/site/request", query: new URLSearchParams(), headers: {}, body, ip: "3.3.3.3" });
  await site({ client: { firstName: "Анна", lastName: "К", phone: "+998901234567", email: "a@example.com" }, message: "две лицензии" });
  await site({ client: { firstName: "Борис", lastName: "Л", phone: "+998907654321" }, message: "" });

  const list = (await s.call("GET", "/api/platform/requests")).json.requests;
  assert.equal(list.length, 2);
  const anna = list.find((r: any) => r.client.firstName === "Анна");
  assert.equal(anna.status, "new");

  assert.equal((await s.call("POST", `/api/platform/requests/${anna.id}/status`, { status: "working" })).status, 200);
  assert.equal((await s.call("POST", `/api/platform/requests/${anna.id}/status`, { status: "done" })).json.error, "bad_request", "«ключ создан» ставится только созданием ключа");
  const key = await s.call("POST", `/api/platform/requests/${anna.id}/key`, { days: 365 });
  assert.equal(key.status, 200);
  const card = (await s.call("GET", `/api/platform/licenses/${key.json.serial}`)).json.license;
  assert.equal(card.client.email, "a@example.com");
  assert.equal(card.daysLeft, 365);
  const again = await s.call("POST", `/api/platform/requests/${anna.id}/key`, {});
  assert.equal(again.status, 409);
  assert.equal(again.json.error, "already_has_key");

  const boris = list.find((r: any) => r.client.firstName === "Борис");
  assert.equal((await s.call("POST", `/api/platform/requests/${boris.id}/delete`, {})).status, 200);
  assert.equal((await s.call("GET", "/api/platform/requests")).json.requests.length, 1);
  assert.equal((await s.call("POST", "/api/platform/requests/999/status", { status: "new" })).json.error, "missing");
});

test("пробные периоды: список и удалить с паролем", async () => {
  const s = setup();
  await s.site.handle({ method: "POST", path: "/api/trial", query: new URLSearchParams(), headers: {}, body: { hwid: HWID }, ip: "4.4.4.4" });
  const t = (await s.call("GET", "/api/platform/trials")).json;
  assert.equal(t.total, 1);
  assert.equal(t.active, 1);
  assert.equal(t.trials[0].daysLeft, 14);
  assert.equal((await s.call("POST", `/api/platform/trials/${t.trials[0].id}/delete`, {})).json.error, "password_required");
  assert.equal((await s.call("POST", `/api/platform/trials/${t.trials[0].id}/delete`, { adminPassword: PASSWORD })).status, 200);
  assert.equal((await s.call("GET", "/api/platform/trials")).json.total, 0);
  assert.equal((await s.call("POST", "/api/platform/trials/999/delete", { adminPassword: PASSWORD })).json.error, "missing");
});

test("обновления: что опубликовано, «из GitHub», опубликовать с паролем", async () => {
  const s = setup();
  assert.deepEqual((await s.call("GET", "/api/platform/updates")).json.updates, { stable: null, beta: null });
  const rel = await s.call("GET", "/api/platform/release?channel=beta");
  assert.equal(rel.json.release.version, "0.9.2");

  const update = { channel: "stable", version: "0.9.2", minVersion: "", url: "https://github.com/little-7o7/strassio-releases/releases/download/v0.9.2/Strassio-Setup-0.9.2.exe", sha256: "A".repeat(64), notesRu: "Новое", notesEn: "New" };
  assert.equal((await s.call("POST", "/api/platform/updates", update)).json.error, "password_required");
  assert.equal((await s.call("POST", "/api/platform/updates", { ...update, version: "v1", adminPassword: PASSWORD })).json.error, "bad_request");
  assert.equal((await s.call("POST", "/api/platform/updates", { ...update, url: "http://x", adminPassword: PASSWORD })).json.error, "bad_request");
  assert.equal((await s.call("POST", "/api/platform/updates", { ...update, url: "https://evil.example/x.exe", adminPassword: PASSWORD })).json.error, "bad_request");
  assert.equal((await s.call("POST", "/api/platform/updates", { ...update, adminPassword: PASSWORD })).status, 200);
  const now = (await s.call("GET", "/api/platform/updates")).json.updates;
  assert.equal(now.stable.version, "0.9.2");
  assert.equal(now.stable.sha256, "a".repeat(64));
  assert.equal(now.beta, null);
  // Плагин видит то же обновление.
  const plugin = await s.site.handle({ method: "GET", path: "/api/update/latest", query: new URLSearchParams("channel=stable"), headers: {}, body: null, ip: "5.5.5.5" });
  assert.equal(plugin.status, 200);
});

test("«из GitHub» не получилось — 502 с причиной", async () => {
  const s = setup();
  const p = new Platform({
    service: new LicenseService(s.store, new Signer(keys.privateKey), s.now, "hub"),
    keys: platformKeys(KEY, SECRET),
    adminPassword: PASSWORD,
    spendNonce: (n, until, nowSec) => s.store.spendNonce(n, until, nowSec),
    fetchRelease: async () => ({ ok: false, error: "Нет выпусков" }),
    now: () => s.now().getTime(),
  });
  const r = await p.handle(s.req("GET", "/api/platform/release?channel=stable"));
  assert.equal(r.status, 502);
  assert.deepEqual(r.json, { ok: false, error: "github", details: "Нет выпусков" });
});

test("сводка для Главной: ключи, компьютеры, пробные, заявки, обновления, «требует внимания»", async () => {
  const s = setup();
  const make = async (body: Record<string, unknown>) => (await s.call("POST", "/api/platform/licenses", body)).json.keys[0] as string;
  const soon = await make({ days: 10, firstName: "Скоро" });
  await make({ days: 100 });
  await make({});
  const blocked = await make({});
  await s.call("POST", `/api/platform/licenses/${blocked}/block`, { adminPassword: PASSWORD });
  const old = await make({ days: 1 });
  s.advance(2); // old истёк, soon — через 8 дней
  await s.site.handle({ method: "POST", path: "/api/activate", query: new URLSearchParams(), headers: {}, body: { serial: soon, hwid: HWID, email: "s@example.com" }, ip: "6.6.6.6" });
  await s.site.handle({ method: "POST", path: "/api/trial", query: new URLSearchParams(), headers: {}, body: { hwid: HWID }, ip: "6.6.6.6" });
  await s.site.handle({ method: "POST", path: "/api/site/request", query: new URLSearchParams(), headers: {}, body: { client: { firstName: "Н", lastName: "З", phone: "+998901111111" } }, ip: "7.7.7.7" });

  const sum = (await s.call("GET", "/api/platform/summary")).json;
  assert.deepEqual(sum.licenses, { total: 5, active: 3, blocked: 1, expired: 1, expiringSoon: 1, unlimited: 2 });
  assert.deepEqual(sum.computers, { active: 1, checkedToday: 1 });
  assert.deepEqual(sum.trials, { active: 1, total: 1 });
  assert.deepEqual(sum.requests, { new: 1, working: 0 });
  assert.deepEqual(sum.updates, { stable: null, beta: null });
  assert.equal(sum.attention.expiring.length, 1);
  assert.equal(sum.attention.expiring[0].serial, soon);
  assert.equal(sum.attention.expiring[0].daysLeft, 8);
  assert.equal(sum.attention.newRequests.length, 1);
  assert.ok(old);
});

test("журнал: действия хаба и сайта, limit", async () => {
  const s = setup();
  await s.call("POST", "/api/platform/licenses", { count: 3 });
  const log = (await s.call("GET", "/api/platform/audit?limit=1")).json.log;
  assert.equal(log.length, 1);
  assert.equal(log[0].actor, "hub");
  assert.equal(log[0].action, "create_keys");
  assert.equal((await s.call("GET", "/api/platform/audit?limit=abc")).status, 200);
});

test("nonce в памяти: устаревшие стираются", async () => {
  const store = new MemoryStore();
  assert.equal(await store.spendNonce("a".repeat(20), 100, 50), true);
  assert.equal(await store.spendNonce("a".repeat(20), 100, 60), false);
  assert.equal(await store.spendNonce("b".repeat(20), 300, 150), true);
  assert.equal(store.nonces.has("a".repeat(20)), false, "срок вышел — стёрт");
});
