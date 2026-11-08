// Дополнения для хаба (владелец 2026-11-08, «da dobav vse perechislenniye»): клиенты и дни рождения, компьютеры и
// версии, «давно не открывали», уборка, подозрительные ключи, пробные без покупки, история ключа, журнал с фильтрами,
// поиск по телефону в любом виде, Telegram (заявка, активация, утренняя сводка) и ежедневный cron.
import { test } from "node:test";
import assert from "node:assert/strict";
import { createHash, createHmac, randomBytes } from "node:crypto";
import { generateKeys, Signer } from "../core/crypto.js";
import { App, type HttpRequest } from "../core/http.js";
import { clientKey, compareVersions, countVersions, dailyDigest, greeting, phoneDigits, phoneQuery, requestMessage } from "../core/insights.js";
import { MemoryStore } from "../core/memoryStore.js";
import { Platform } from "../core/platform.js";
import { platformKeys } from "../core/platformAuth.js";
import { LicenseService } from "../core/service.js";
import { hubUrl, telegram } from "../core/telegram.js";
import type { ClientInfo } from "../core/store.js";

const DAY = 24 * 60 * 60 * 1000;
const keys = generateKeys();
const KEY = "k".repeat(40);
const SECRET = "s".repeat(48);
const CRON = "c".repeat(32);
const pc = (x: string) => [x, x, x, x].map((c, i) => (c + String(i)).repeat(8)).join(".");
const PC1 = pc("a");
const PC2 = pc("b");
const PC3 = pc("c");
const PC4 = pc("d");
const client = (o: Partial<ClientInfo> = {}): ClientInfo => ({ firstName: "", lastName: "", phone: "", email: "", birthday: "", telegram: "", ...o });

function sign(method: string, pathAndQuery: string, time: string, nonce: string, body: string) {
  const text = [method, pathAndQuery, time, nonce, createHash("sha256").update(body).digest("hex")].join("\n");
  return createHmac("sha256", SECRET).update(text).digest("hex");
}

function setup() {
  const store = new MemoryStore();
  let now = new Date("2026-11-08T10:00:00Z");
  const clock = () => now;
  const signer = new Signer(keys.privateKey);
  const sent: string[] = [];
  const notifier = async (html: string) => {
    sent.push(html);
    return true;
  };
  const hub = new LicenseService(store, signer, clock, "hub");
  const site = new LicenseService(store, signer, clock).useNotifier(notifier, "https://hub.example");
  const platform = new Platform({ service: hub, keys: platformKeys(KEY, SECRET), adminPassword: "admin-pass-123", spendNonce: (n, u, s) => store.spendNonce(n, u, s), now: () => now.getTime() });
  const app = new App(site).usePlatform(platform).useCron(CRON);

  async function call(method: string, pathAndQuery: string, body?: Record<string, unknown>) {
    const [path, q] = pathAndQuery.split("?");
    const raw = method === "POST" ? JSON.stringify(body ?? {}) : "";
    const time = String(Math.floor(now.getTime() / 1000));
    const nonce = randomBytes(18).toString("base64url");
    const headers = { "x-hub-key": KEY, "x-hub-time": time, "x-hub-nonce": nonce, "x-hub-signature": sign(method, pathAndQuery, time, nonce, raw) };
    const req: HttpRequest = { method, path, query: new URLSearchParams(q ?? ""), headers, body: raw ? JSON.parse(raw) : null, rawBody: raw, ip: "9.9.9.9" };
    const r = await app.handle(req);
    return { status: r.status, json: r.json as Record<string, any> };
  }

  async function plain(method: string, path: string, body: unknown = null, headers: Record<string, string> = {}) {
    const r = await app.handle({ method, path, query: new URLSearchParams(), headers, body, ip: "1.2.3.4" });
    return { status: r.status, json: r.json as Record<string, any> };
  }

  return { store, hub, site, call, plain, sent, advance: (days: number) => (now = new Date(now.getTime() + days * DAY)), now: () => now };
}

async function key(s: ReturnType<typeof setup>, days: number | null, c: ClientInfo = client(), note = "") {
  const [k] = await s.hub.createKeys({ count: 1, days, client: c, note });
  return k.serial;
}

test("телефон в любом виде: цифры, ключ клиента, похож ли запрос на телефон", () => {
  assert.equal(phoneDigits("+998 90 123-45-67"), "901234567");
  assert.equal(phoneDigits("8 (90) 1234567"), "901234567");
  assert.equal(phoneDigits("12345"), "");
  assert.equal(phoneQuery("+998 90 123 45 67"), "901234567");
  assert.equal(phoneQuery("Алиев 90"), "");
  assert.equal(clientKey(client({ phone: "90 123 45 67", email: "A@b.uz" })), "p901234567");
  assert.equal(clientKey(client({ email: "A@B.uz" })), "ea@b.uz");
  assert.equal(clientKey(client({ telegram: "Some_Name" })), "tsome_name");
  assert.equal(clientKey(client({ firstName: " Азиз ", lastName: "Рахимов" })), "nазиз рахимов");
  assert.equal(clientKey(client()), "");
});

test("версии: сравнение и счёт, «offline» — не версия", () => {
  assert.equal(compareVersions("0.9.1", "0.10.0")! < 0, true);
  assert.equal(compareVersions("1.2", "1.2.0"), 0);
  assert.equal(compareVersions("offline", "1.0.0"), null);
  assert.deepEqual(countVersions(["0.9.1", "1.0.0", "0.9.1", ""]), [
    { version: "1.0.0", count: 1 },
    { version: "0.9.1", count: 2 },
    { version: "—", count: 1 },
  ]);
});

test("поиск ключа по телефону в любом виде: +998, 8, с пробелами и без", async () => {
  const s = setup();
  const serial = await key(s, 30, client({ firstName: "Азиз", phone: "+998 90 123 45 67" }));
  await key(s, 30, client({ firstName: "Другой", phone: "+998 91 000 00 00" }));
  for (const q of ["901234567", "8 90 123 45 67", "+998901234567", "123 45 67", "(90) 123-45-67"]) {
    const r = await s.call("GET", "/api/platform/licenses?" + new URLSearchParams({ q }));
    assert.deepEqual(r.json.licenses.map((l: { serial: string }) => l.serial), [serial], q);
  }
  const all = await s.call("GET", "/api/platform/licenses?all=1");
  assert.equal(all.json.licenses.length, 2);
});

test("клиенты: один телефон в разной записи — одна карточка со всеми ключами, заявками и заметками", async () => {
  const s = setup();
  const a = await key(s, 30, client({ firstName: "Азиз", lastName: "Рахимов", phone: "+998 90 123 45 67", birthday: "1990-11-20" }), "первый");
  s.advance(1);
  const b = await key(s, 365, client({ firstName: "Азиз", phone: "90 123 45 67", telegram: "aziz_r" }), "второй");
  await key(s, null, client({ firstName: "Без", lastName: "Телефона", email: "x@y.uz" }));
  await key(s, 30); // ключ без клиента в клиентах не виден
  await s.plain("POST", "/api/site/request", { client: { firstName: "Азиз", lastName: "Р", phone: "8 90 1234567" }, message: "ещё ключ" });
  await s.site.activate({ serial: a, hwid: PC1, pluginVersion: "0.9.1", corelVersion: "25" });

  const r = await s.call("GET", "/api/platform/clients");
  assert.equal(r.status, 200);
  assert.equal(r.json.clients.length, 2);
  const aziz = r.json.clients.find((c: { name: string }) => c.name.startsWith("Азиз"));
  assert.deepEqual(aziz.keys.map((k: { serial: string }) => k.serial), [b, a], "новые сверху");
  assert.equal(aziz.client.telegram, "aziz_r");
  assert.equal(aziz.client.lastName, "Рахимов", "пустое поле дополнено из старого ключа");
  assert.equal(aziz.client.birthday, "1990-11-20");
  assert.deepEqual(aziz.notes, ["второй", "первый"]);
  assert.equal(aziz.requests.length, 1);
  assert.equal(aziz.computers.length, 1);
  assert.match(aziz.id, /^[A-Za-z0-9_-]{12}$/);
  assert.equal(aziz.id.includes("901234567"), false, "в адресе нет телефона");

  assert.deepEqual(r.json.birthdays.map((x: { name: string; day: number; turns: number; today: boolean }) => [x.name, x.day, x.turns, x.today]), [["Азиз Рахимов", 20, 36, false]]);

  const card = await s.call("GET", `/api/platform/licenses/${a}`);
  assert.equal(card.json.license.clientId, aziz.id, "из карточки ключа — в карточку клиента");
  const one = await s.call("GET", "/api/platform/clients/" + aziz.id);
  assert.equal(one.json.client.name, "Азиз Рахимов");
  const none = await s.call("GET", "/api/platform/clients/" + "x".repeat(12));
  assert.deepEqual([none.status, none.json.error], [404, "missing"]);
});

test("клиенты: двое на одном телефоне — имя и день рождения не смешиваются", async () => {
  const s = setup();
  await key(s, 30, client({ firstName: "Мадина", lastName: "Каримова", phone: "+998 90 123 45 67", birthday: "1995-11-21", telegram: "madina_k" }));
  s.advance(1);
  await key(s, 30, client({ firstName: "Тимур", phone: "90 123 45 67" }));
  const r = await s.call("GET", "/api/platform/clients");
  assert.equal(r.json.clients.length, 1, "один телефон — одна карточка");
  const c = r.json.clients[0];
  assert.equal(c.name, "Тимур");
  assert.deepEqual([c.client.lastName, c.client.birthday, c.client.telegram], ["", "", ""]);
  assert.equal(c.keys.length, 2);
  assert.deepEqual(r.json.birthdays, []);
});

test("компьютеры: версии плагина и CorelDRAW, «не обновился», «давно не открывали», офлайн не считается", async () => {
  const s = setup();
  await s.hub.publishUpdate({ channel: "stable", version: "1.0.0", minVersion: "", url: "https://x/y.exe", sha256: "a".repeat(64), notesRu: "", notesEn: "" });
  const a = await key(s, 365, client({ firstName: "Старый", phone: "+998 90 111 11 11" }));
  const b = await key(s, 365, client({ firstName: "Новый" }));
  const c = await key(s, 365);
  await s.site.activate({ serial: a, hwid: PC1, pluginVersion: "0.9.1", corelVersion: "25" });
  await s.site.activate({ serial: b, hwid: PC2, pluginVersion: "1.0.0", corelVersion: "26" });
  await s.hub.offline(c, PC3);
  s.advance(31);
  await s.site.check({ serial: b, hwid: PC2, pluginVersion: "1.0.0", corelVersion: "26" });

  const r = await s.call("GET", "/api/platform/computers");
  assert.equal(r.status, 200);
  assert.deepEqual(r.json.latest, { stable: "1.0.0", beta: null });
  assert.deepEqual(r.json.plugin, [{ version: "1.0.0", count: 1 }, { version: "0.9.1", count: 1 }]);
  assert.deepEqual(r.json.corel, [{ version: "26", count: 1 }, { version: "25", count: 1 }]);
  const by = (serial: string) => r.json.computers.find((x: { serial: string }) => x.serial === serial);
  assert.deepEqual([by(a).outdated, by(a).silent, by(a).silentDays, by(a).name], [true, true, 31, "Старый"]);
  assert.deepEqual([by(b).outdated, by(b).silent], [false, false]);
  assert.deepEqual([by(c).offline, by(c).silent, by(c).outdated], [true, false, false]);
});

test("на что взглянуть: без клиента, один телефон — разные имена, старые заявки, давно истёкшие, подозрительные, пробные", async () => {
  const s = setup();
  const lonely = await key(s, 30);
  await key(s, 30, client({ firstName: "Азиз", phone: "+998 90 123 45 67" }));
  await key(s, 30, client({ firstName: "Aziz", phone: "901234567" }));
  const old = await key(s, 1, client({ firstName: "Давний" }));
  await s.plain("POST", "/api/site/request", { client: { firstName: "А", lastName: "Б", phone: "+998 93 000 00 00" }, message: "" });
  // Пробные: один купил ключ, второй — нет, третий похож на второй (два признака совпадают).
  await s.site.trial({ hwid: PC1 });
  await s.site.trial({ hwid: PC2 });
  const [p2a, p2b] = PC2.split(".");
  const [, , p4c, p4d] = PC4.split(".");
  await s.site.trial({ hwid: [p2a, p2b, p4c, p4d].join(".").replace(p2b, PC3.split(".")[1]) });
  s.advance(200);
  const busy = await key(s, 365);
  await s.site.activate({ serial: busy, hwid: PC1 });
  await s.site.activate({ serial: busy, hwid: PC3 }); // занято — в журнал
  await s.site.activate({ serial: busy, hwid: PC4 }); // занято — в журнал

  const r = await s.call("GET", "/api/platform/attention");
  assert.equal(r.status, 200);
  assert.ok(r.json.noClient.some((k: { serial: string }) => k.serial === lonely));
  assert.equal(r.json.samePhone.length, 1);
  assert.deepEqual(r.json.samePhone[0].names.sort(), ["Aziz", "Азиз"]);
  assert.equal(r.json.oldRequests.length, 1);
  assert.ok(r.json.longExpired.some((k: { serial: string }) => k.serial === old));
  assert.deepEqual(r.json.suspicious.map((x: { serial: string; computers: number }) => [x.serial, x.computers]), [[busy, 3]]);
  assert.equal(r.json.trials.notBought.length, 2, "PC1 купил ключ");
  assert.equal(r.json.trials.similar.length, 1);
  assert.equal(r.json.trials.similar[0].count, 2);  const tries = (await s.call("GET", "/api/platform/audit?action=occupied")).json.log;
  assert.equal(tries.length, 2);
  for (const e of tries) assert.match(e.details, /^[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}$/, "в журнале хаба — короткий код");
});

test("история ключа: из журнала, коды компьютеров короткие; нет ключа — 404", async () => {
  const s = setup();
  const serial = await key(s, 30);
  await s.site.activate({ serial, hwid: PC1, pluginVersion: "1.0.0" });
  await s.call("POST", `/api/platform/licenses/${serial}/extend`, { days: 30 });
  const r = await s.call("GET", `/api/platform/licenses/${serial}/history`);
  assert.deepEqual(r.json.history.map((e: { action: string }) => e.action), ["extend", "activate", "create_keys"]);
  const activate = r.json.history.find((e: { action: string }) => e.action === "activate");
  assert.match(activate.details, /^[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}$/);
  const none = await s.call("GET", "/api/platform/licenses/STRS-AAAA-BBBB-CCCC/history");
  assert.equal(none.status, 404);
});

test("журнал с фильтрами: кто, ключ, действие, даты по Ташкенту", async () => {
  const s = setup();
  const a = await key(s, 30);
  s.advance(2);
  const b = await key(s, 30);
  await s.site.activate({ serial: b, hwid: PC1 });
  await s.plain("POST", "/api/site/request", { client: { firstName: "А", lastName: "Б", phone: "+998 93 000 00 00" } });

  const actions = async (q: string) => (await s.call("GET", "/api/platform/audit?" + q)).json.log.map((e: { action: string; actor: string }) => e.actor + ":" + e.action);
  assert.deepEqual(await actions("who=client"), ["client:activate"]);
  assert.deepEqual(await actions("who=site"), ["site:request"]);
  assert.deepEqual(await actions("who=hub"), ["hub:create_keys", "hub:create_keys"]);
  assert.deepEqual(await actions("serial=" + a.toLowerCase().replace(/-/g, "")), ["hub:create_keys"]);
  assert.deepEqual(await actions("action=activate"), ["client:activate"]);
  assert.deepEqual(await actions("from=2026-11-08&to=2026-11-08"), ["hub:create_keys"]);
  assert.deepEqual(await actions("from=2026-11-10"), ["site:request", "client:activate", "hub:create_keys"]);
  assert.deepEqual(await actions("serial=bad"), []);
  assert.equal((await actions("action=DROP+TABLE")).length, 4, "плохое действие — без фильтра");
});

test("Telegram: заявка и новая активация — сообщение владельцу; бот молчит при повторной активации", async () => {
  const s = setup();
  await s.plain("POST", "/api/site/request", { client: { firstName: "<b>Азиз</b>", lastName: "Р", phone: "+998 90 123 45 67", telegram: "aziz_r" }, message: "Хочу <ключ>" });
  assert.equal(s.sent.length, 1);
  assert.match(s.sent[0], /Новая заявка #\d+/);
  assert.match(s.sent[0], /&lt;b&gt;Азиз&lt;\/b&gt;/, "HTML клиента экранирован");
  assert.match(s.sent[0], /«Хочу &lt;ключ&gt;»/);
  assert.match(s.sent[0], /href="https:\/\/hub\.example\/admin\/strassio\/requests#r\d+"/);

  const serial = await key(s, 30, client({ firstName: "Азиз", phone: "+998 90 123 45 67" }));
  await s.site.activate({ serial, hwid: PC1, pluginVersion: "1.0.0", corelVersion: "26" });
  assert.equal(s.sent.length, 2);
  assert.match(s.sent[1], /Новая активация/);
  assert.match(s.sent[1], /Плагин 1\.0\.0 · CorelDRAW 26/);
  await s.site.activate({ serial, hwid: PC1 });
  await s.site.check({ serial, hwid: PC1 });
  assert.equal(s.sent.length, 2, "тот же компьютер и сверка — без сообщений");
});

test("Telegram не ответил — заявка всё равно сохраняется", async () => {
  const store = new MemoryStore();
  const service = new LicenseService(store, new Signer(keys.privateKey)).useNotifier(async () => {
    throw new Error("сеть");
  }, "https://hub.example");
  const r = await service.submitRequest({ firstName: "А", lastName: "Б", phone: "+998 90 000 00 00" }, "");
  assert.deepEqual(r, { ok: true });
  assert.equal(store.requests.length, 1);
});

test("telegram(): без токена или с плохим — выключен; запрос к Bot API; ошибки — false", async () => {
  assert.equal(telegram(undefined, "123"), null);
  assert.equal(telegram("bad", "123456"), null);
  assert.equal(telegram("123456:" + "A".repeat(35), "chat"), null);
  const calls: Array<{ url: string; body: any }> = [];
  const ok = telegram("123456:" + "A".repeat(35), "-100123", async (url, init) => {
    calls.push({ url: String(url), body: JSON.parse(String(init?.body)) });
    return new Response("{}", { status: 200 });
  })!;
  assert.equal(await ok("<b>Привет</b>"), true);
  assert.equal(calls[0].url, "https://api.telegram.org/bot123456:" + "A".repeat(35) + "/sendMessage");
  assert.deepEqual(calls[0].body, { chat_id: "-100123", text: "<b>Привет</b>", parse_mode: "HTML", link_preview_options: { is_disabled: true } });
  const refused = telegram("123456:" + "A".repeat(35), "1234", async () => new Response("{}", { status: 400 }))!;
  assert.equal(await refused("x"), false);
  const broken = telegram("123456:" + "A".repeat(35), "1234", async () => {
    throw new Error("offline");
  })!;
  assert.equal(await broken("x"), false);
  assert.equal(hubUrl("https://my.hub.uz/"), "https://my.hub.uz");
  assert.equal(hubUrl("javascript:alert(1)"), "https://little-7o7.vercel.app");
  assert.equal(hubUrl("https://evil.uz/x?y"), "https://little-7o7.vercel.app");
});

test("cron: без секрета или с чужим — 404; с секретом — утреннее сообщение; нечего сказать — бот молчит", async () => {
  const s = setup();
  assert.equal((await s.plain("GET", "/api/cron/daily")).status, 404);
  assert.equal((await s.plain("GET", "/api/cron/daily", null, { authorization: "Bearer wrong" })).status, 404);
  const auth = { authorization: "Bearer " + CRON };
  assert.deepEqual((await s.plain("GET", "/api/cron/daily", null, auth)).json, { ok: true, sent: false, empty: true });
  assert.equal(s.sent.length, 0);

  await key(s, 5, client({ firstName: "Сардор", phone: "+998 99 321 00 99" }));
  await key(s, 20, client({ firstName: "Нескоро" }));
  await key(s, 365, client({ firstName: "Именинник", birthday: "1990-11-08", telegram: "imen" }));
  const r = await s.plain("GET", "/api/cron/daily", null, auth);
  assert.deepEqual(r.json, { ok: true, sent: true, empty: false });
  const text = s.sent.at(-1)!;
  assert.match(text, /Ключи на исходе \(7 дней и меньше\)/);
  assert.match(text, /Сардор, \+998 99 321 00 99 — 5 дн\./);
  assert.equal(text.includes("Нескоро"), false);
  assert.match(text, /День рождения сегодня/);
  assert.match(text, /Именинник \(36\), @imen/);
  assert.match(text, /с днём рождения/);

  const app = new App(new LicenseService(new MemoryStore(), new Signer(keys.privateKey))).useCron("short");
  const r2 = await app.handle({ method: "GET", path: "/api/cron/daily", query: new URLSearchParams(), headers: { authorization: "Bearer short" }, body: null, ip: "1" });
  assert.equal(r2.status, 404, "секрет короче 16 знаков — cron выключен");
});

test("утреннее сообщение: заявки и активации за сутки, закончившиеся пробные", () => {
  const now = new Date("2026-11-08T04:00:00Z");
  const hour = (h: number) => new Date(now.getTime() - h * 3600_000);
  const text = dailyDigest({
    now,
    licenses: [],
    activations: [{ id: 1, licenseId: 1, hwid: PC1, status: "active", firstAt: hour(3), lastCheckAt: hour(3), pluginVersion: "1.0.0", corelVersion: "" }],
    requests: [{ id: 5, createdAt: hour(2), client: client({ firstName: "А" }), message: "", status: "new", serial: "" }],
    trials: [{ id: 7, hwid: PC2, startedAt: new Date(now.getTime() - 14 * DAY + 3600_000 * -1) }],
    clients: [],
    trialDays: 14,
    hubUrl: "https://hub.example",
  })!;
  assert.match(text, /Заявки за сутки: 1 · ждут ответа: 1/);
  assert.match(text, /Новые активации: 1/);
  assert.match(text, /Закончился пробный: 1 · без покупки: 1/);
  assert.match(text, /воскресенье, 8 ноября/);
  assert.match(text, /href="https:\/\/hub\.example\/admin\/strassio"/);
  assert.equal(greeting(""), "С днём рождения! Желаем здоровья, вдохновения и красивых работ. Ваш Strassio");
  assert.match(requestMessage({ id: 1, createdAt: now, client: client(), message: "", status: "new", serial: "" }, "https://h"), /Без имени/);
});
