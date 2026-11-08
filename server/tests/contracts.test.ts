// Договоры и цены клиентов (владелец 2026-11-08: «для каждого клиента разная цена, потому что договор есть договор»):
// договор с ценами месяца, 3/6/12 месяцев со скидкой в процентах, «навсегда» и обновлениями; цена закреплена за ключом;
// новый договор — старые ключи по старой цене (или «перевести ключи»); оплата частями и сумами по курсу дня; бесплатно
// и скидка; заплатил вперёд; автоблокировка через 5 дней долга; обновления «навсегда»; скидка за количество; цены на
// сайте; файл договора; лучшие клиенты; напоминания.
import { test } from "node:test";
import assert from "node:assert/strict";
import { createHash, createHmac, randomBytes } from "node:crypto";
import { generateKeys, Signer } from "../core/crypto.js";
import { App } from "../core/http.js";
import { MemoryStore } from "../core/memoryStore.js";
import { debtReminder, derivedPrice, nextPayDay, parseDiscount } from "../core/money.js";
import { fileFrom, parseAmount } from "../core/moneyInput.js";
import { Platform } from "../core/platform.js";
import { platformKeys } from "../core/platformAuth.js";
import { Billing } from "../core/billing.js";
import { parseAllRatesToday, parseCbuRate, parseDepozitRate, rateSources } from "../core/rates.js";
import { EMPTY_CLIENT, LicenseService } from "../core/service.js";
import type { TariffRow } from "../core/store.js";

const DAY = 24 * 60 * 60 * 1000;
const KEY = "k".repeat(40);
const SECRET = "s".repeat(48);
const PASSWORD = "admin-pass-123";
const keys = generateKeys();
const START = new Date("2026-11-08T10:00:00Z");

function setup(rate: number | null = null) {
  const store = new MemoryStore();
  let now = START;
  const clock = () => now;
  const hub = new LicenseService(store, new Signer(keys.privateKey), clock, "hub").useRates(rate === null ? null : async () => rate);
  const site = new LicenseService(store, new Signer(keys.privateKey), clock).useRates(rate === null ? null : async () => rate);
  const sent: string[] = [];
  site.useNotifier(async (html) => (sent.push(html), true), "https://hub.test");
  const platform = new Platform({ service: hub, keys: platformKeys(KEY, SECRET), adminPassword: PASSWORD, spendNonce: (n, u, s) => store.spendNonce(n, u, s), now: () => now.getTime() });
  const app = new App(site).usePlatform(platform);
  async function call(method: "GET" | "POST", pathAndQuery: string, body?: Record<string, unknown>) {
    const raw = method === "POST" ? JSON.stringify(body ?? {}) : "";
    const time = String(Math.floor(now.getTime() / 1000));
    const nonce = randomBytes(18).toString("base64url");
    const text = [method, pathAndQuery, time, nonce, createHash("sha256").update(raw).digest("hex")].join("\n");
    const headers = { "x-hub-key": KEY, "x-hub-time": time, "x-hub-nonce": nonce, "x-hub-signature": createHmac("sha256", SECRET).update(text).digest("hex") };
    const [path, q] = pathAndQuery.split("?");
    const r = await app.handle({ method, path, query: new URLSearchParams(q ?? ""), headers, body: raw ? JSON.parse(raw) : null, rawBody: raw, ip: "1.1.1.1" });
    return { status: r.status, json: r.json as Record<string, any> };
  }
  // Утро: хаб вызывает /api/platform/daily и сам шлёт сообщения своим ботом.
  const cron = async () => {
    const r = await call("POST", "/api/platform/daily");
    sent.push(...(r.json.messages as string[]));
    return r;
  };
  const P = "/api/platform/";
  const key = async (serial: string) => (await call("GET", `${P}licenses/${serial}`)).json.license;
  return { app, store, call, cron, sent, key, P, advance: (days: number) => (now = new Date(now.getTime() + days * DAY)) };
}

const ALI = { firstName: "Али", lastName: "Каримов", phone: "+998 90 555 44 33", telegram: "ali_k" };
const daysLeft = (iso: string) => Math.round((Date.parse(iso) - START.getTime()) / DAY);

/** Клиент с первым ключом (без оплаты) — у договора должен быть клиент. */
async function client(s: ReturnType<typeof setup>) {
  const serial = (await s.call("POST", `${s.P}licenses`, { days: 1, ...ALI })).json.keys[0];
  return { serial, id: (await s.key(serial)).clientId as string };
}

const CONTRACT = { number: "12/2026", signedOn: "2026-11-01", validFrom: "2026-11-01", validUntil: "2027-10-31", party: "ООО «Али Стразы»", inn: "301234567", address: "Ташкент", terms: "до 5 числа", payDay: 5, currency: "USD", month: 50, discount3: "5", discount6: "10%", discount12: "15", forever: 1500, updates: 300 };

async function withContract(s: ReturnType<typeof setup>, extra: Record<string, unknown> = {}) {
  const c = await client(s);
  const made = await s.call("POST", `${s.P}clients/${c.id}/contracts`, { ...CONTRACT, ...extra });
  assert.equal(made.status, 200, JSON.stringify(made.json));
  const money = (await s.call("GET", `${s.P}clients/${c.id}`)).json.money;
  const line = (name: string) => money.contracts[0].lines.find((l: any) => l.name === name && l.active);
  return { ...c, contractId: made.json.contract.id as number, money, line };
}

// ---------- Чистые расчёты ----------

test("скидка только процентом; цена 3, 6 и 12 месяцев = месяц × N − скидка", () => {
  assert.equal(parseDiscount(""), 0);
  assert.equal(parseDiscount("15"), 15);
  assert.equal(parseDiscount("12,5 %"), 12.5);
  assert.equal(parseDiscount("100"), null, "100% — не скидка");
  assert.equal(parseDiscount("abc"), null);
  const month = { id: 1, name: "Месяц", days: 30, price: 50, currency: "USD", active: true, createdAt: START, contractId: null, kind: "key", baseId: null, discount: "", site: false, label: "" } as TariffRow;
  assert.equal(derivedPrice(month, 90, "5"), 142.5);
  assert.equal(derivedPrice(month, 180, "10"), 270);
  assert.equal(derivedPrice(month, 365, "15"), 510);
  assert.equal(derivedPrice({ ...month, price: 150000, currency: "UZS" }, 365, "17"), 1494000, "сумы — до целого");
  assert.equal(derivedPrice(month, null, "5"), null);
});

test("день оплаты по договору: сегодня или ближайший; 31-е в ноябре — 30-е", () => {
  assert.equal(nextPayDay(new Date("2026-11-08T10:00:00Z"), 5), "2026-12-05");
  assert.equal(nextPayDay(new Date("2026-11-08T10:00:00Z"), 8), "2026-11-08");
  assert.equal(nextPayDay(new Date("2026-11-08T10:00:00Z"), 31), "2026-11-30");
  assert.equal(nextPayDay(new Date("2026-12-20T10:00:00Z"), 5), "2027-01-05");
});

test("курс ЦБ: из ответа cbu.uz; чужое — null", () => {
  assert.equal(parseCbuRate([{ Ccy: "USD", Nominal: "1", Rate: "11987.16", Date: "07.11.2026" }]), 11987.16);
  assert.equal(parseCbuRate([{ Ccy: "EUR", Nominal: "1", Rate: "13000" }]), null);
  assert.equal(parseCbuRate({}), null);
  assert.equal(parseCbuRate([{ Ccy: "USD", Rate: "abc" }]), null);
  assert.equal(parseCbuRate([{ Ccy: "USD", Nominal: "1", Rate: "11987,16" }]), 11987.16);
});

test("курс ЦБ с зеркал: depozit.uz (сегодня), AllRatesToday (только тот же день); чужое — null", () => {
  assert.equal(parseDepozitRate({ cb: { usd: { key: "USD", rate: "11,903.15" }, eur: { key: "EUR", rate: "13,800.00" } } }), 11903.15);
  assert.equal(parseDepozitRate({ cb: { eur: { key: "EUR", rate: "13,800.00" } } }), null);
  assert.equal(parseDepozitRate({ cb: { usd: { key: "USD", rate: "12" } } }), null, "неправдоподобно мало");
  assert.equal(parseDepozitRate(null), null);
  const art = { rate_date: "2026-11-08", rates: [{ base: "EUR", quote: "UZS", value: 13800 }, { base: "USD", quote: "UZS", value: 11987.16 }] };
  assert.equal(parseAllRatesToday(art, "2026-11-08"), 11987.16);
  assert.equal(parseAllRatesToday(art, "2026-11-09"), null, "курс вчерашний — не берём");
  assert.equal(parseAllRatesToday({ rate_date: "2026-11-08", rates: [] }, "2026-11-08"), null);
  assert.equal(parseAllRatesToday({}, "2026-11-08"), null);
});

test("курс ЦБ: спрашиваем все источники сразу, берём первый ответ; молчат все — null", async () => {
  const today = new Date(Date.now() + 5 * 3600_000).toISOString().slice(0, 10);
  const asked: string[] = [];
  const answers: Record<string, unknown> = {
    "cbu.uz": new Error("не открылся"),
    "allratestoday.com": { rate_date: today, rates: [{ base: "USD", quote: "UZS", value: 12001 }] },
    "depozit.uz": { cb: { usd: { key: "USD", rate: "12,001.00" } } },
  };
  const fake = (map: Record<string, unknown>) => async (url: string) => {
    const host = new URL(url).hostname;
    asked.push(host);
    const a = map[host];
    if (a instanceof Error) throw a;
    return { ok: a !== undefined, json: async () => a };
  };
  assert.equal(await rateSources(fake(answers))(today), 12001);
  assert.deepEqual(asked.sort(), ["allratestoday.com", "cbu.uz", "depozit.uz"]);
  asked.length = 0;
  assert.equal(await rateSources(fake(answers))("2026-01-02"), null, "AllRatesToday — другой день, depozit.uz прошлых дней не знает");
  assert.deepEqual(asked.sort(), ["allratestoday.com", "cbu.uz"]);
  assert.equal(await rateSources(fake({}))(today), null);
});

test("курс: источники промолчали — 15 минут их не спрашиваем (оплата не ждёт), потом снова", async () => {
  const store = new MemoryStore();
  let now = START;
  let calls = 0;
  let answer: number | null = null;
  const billing = new Billing(store, () => now, "hub").useRates(async () => (calls++, answer));
  await store.saveRate("2026-11-07", 11950);
  assert.deepEqual(await billing.rateToday(), { day: "2026-11-08", rate: 11950, fresh: false }, "последний известный");
  await billing.rateToday();
  assert.equal(calls, 1);
  now = new Date(START.getTime() + 16 * 60_000);
  answer = 12010;
  assert.deepEqual(await billing.rateToday(), { day: "2026-11-08", rate: 12010, fresh: true });
  await billing.rateToday();
  assert.equal(calls, 2, "курс дня сохранён — больше не спрашиваем");
});

test("курс дня от хаба: POST rates — сегодня, вчера или завтра, не дальше 20% от прежнего; источники больше не нужны", async () => {
  const s = setup(12000);
  const push = (date: string, USD: unknown) => s.call("POST", `${s.P}rates`, { date, source: "cbu", rates: { USD, EUR: 14000 } });
  const r = await push("2026-11-08", 12650.5);
  assert.equal(r.status, 200);
  assert.deepEqual(r.json.rate, { day: "2026-11-08", rate: 12650.5, fresh: true });
  assert.equal((await push("2026-11-09", 12660)).status, 200, "завтрашний курс ЦБ ставит с вечера");
  assert.equal((await push("2026-11-12", 12660)).json.error, "bad_day");
  assert.equal((await push("2026-02-30", 12660)).json.error, "bad_day");
  assert.equal((await push("08.11.2026", 12660)).json.error, "bad_day");
  assert.equal((await push("2026-11-08", 126505)).json.error, "bad_rate", "лишний ноль");
  assert.equal((await push("2026-11-08", "abc")).json.error, "bad_rate");
  assert.equal((await s.call("POST", `${s.P}rates`, { date: "2026-11-08" })).json.error, "bad_rate");
  assert.equal((await s.call("GET", `${s.P}rate`)).json.rate.rate, 12650.5);
  const log = s.store.log.filter((a) => a.action === "rate");
  assert.equal(log.length, 2);
  assert.equal(log[0].actor, "system");
});

test("сумма: пробелы и запятая; 0 — только когда можно (бесплатно)", () => {
  assert.equal(parseAmount("1 200 000"), 1200000);
  assert.equal(parseAmount("12,5"), 12.5);
  assert.equal(parseAmount("0"), null);
  assert.equal(parseAmount("0", true), 0);
});

test("файл договора: PDF, JPEG, PNG, WebP до 3 МБ, содержимое совпадает с типом, имя без пути", () => {
  const pdf = Buffer.from("%PDF-1.4 test").toString("base64");
  assert.deepEqual(fileFrom({ name: "C:\\docs\\договор.pdf", type: "application/pdf", data: pdf }), { name: "договор.pdf", type: "application/pdf", data: pdf, size: 13 });
  assert.equal(fileFrom({ name: "x.pdf", type: "application/pdf", data: Buffer.from("<html>").toString("base64") }), null, "не PDF внутри");
  assert.equal(fileFrom({ name: "x.exe", type: "application/x-msdownload", data: pdf }), null);
  assert.equal(fileFrom({ name: "x.pdf", type: "application/pdf", data: "не base64!" }), null);
  const big = Buffer.concat([Buffer.from("%PDF-"), Buffer.alloc(3 * 1024 * 1024)]).toString("base64");
  assert.equal(fileFrom({ name: "x.pdf", type: "application/pdf", data: big }), null, "больше 3 МБ");
  const png = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 1, 2]).toString("base64");
  assert.equal(fileFrom({ name: "скан.png", type: "image/png", data: "data:image/png;base64," + png })?.size, 10);
});

test("напомнить об оплате: ключ, за что, остаток, срок; несколько — итог; просрочено — другая концовка", () => {
  const now = new Date("2026-11-08T10:00:00Z");
  const one = debtReminder({ ...EMPTY_CLIENT, firstName: "Али" }, [{ serial: "STRS-A", tariff: "Месяц", left: 30, currency: "USD", dueOn: "2026-11-10", kind: "renewal" }], now);
  assert.match(one, /^Али, здравствуйте!/);
  assert.match(one, /STRS-A — Месяц: \$30, до 10\.11\.2026/);
  assert.match(one, /Спасибо!/);
  const two = debtReminder({ ...EMPTY_CLIENT }, [{ serial: "A", tariff: "", left: 30, currency: "USD", dueOn: "2026-11-01", kind: "updates" }, { serial: "B", tariff: "", left: 100000, currency: "UZS", dueOn: "", kind: "sale" }], now);
  assert.match(two, /A — обновления: \$30/);
  assert.match(two, /B — ключ: 100 000 сум/);
  assert.match(two, /Всего: 100 000 сум, \$30/);
  assert.match(two, /Срок оплаты уже прошёл/);
});

// ---------- Через API хаба ----------

test("договор: цены месяц, 3/6/12 месяцев со скидкой, навсегда и обновления; ключ по договору закрепляет цену", async () => {
  const s = setup();
  const c = await withContract(s);
  const lines = c.money.contracts[0].lines.map((l: any) => [l.name, l.price, l.currency, l.days, l.kind]);
  assert.deepEqual(lines, [
    ["Месяц", 50, "USD", 30, "key"],
    ["3 месяца", 142.5, "USD", 90, "key"],
    ["6 месяцев", 270, "USD", 180, "key"],
    ["Год", 510, "USD", 365, "key"],
    ["Навсегда", 1500, "USD", null, "key"],
    ["Обновления на год", 300, "USD", 365, "updates"],
  ]);
  assert.equal(c.money.current, c.contractId);
  assert.equal(c.money.contracts[0].party, "ООО «Али Стразы»");
  assert.equal(c.money.contracts[0].clientKey, undefined, "телефон одной строкой хабу не отдаётся");
  // Новый ключ по строке «Месяц» — $50, цена закреплена за ключом.
  const k = (await s.call("POST", `${s.P}licenses`, { tariff: String(c.line("Месяц").id), method: "card", ...ALI })).json.keys[0];
  const v = await s.key(k);
  assert.equal(daysLeft(v.expiresAt), 30);
  assert.deepEqual([v.terms.price, v.terms.currency, v.terms.days, v.terms.contractId, v.terms.name], [50, "USD", 30, c.contractId, "Месяц"]);
  assert.equal(v.contract.id, c.contractId);
  assert.match(v.offer.text, /Месяц — \$50/);
  assert.match(v.offer.text, /Год — \$510/, "другие сроки того же договора");
  // Неверные договоры.
  for (const bad of [{ discount3: "120" }, { month: "", discount3: "5" }, { validFrom: "2027-01-01", validUntil: "2026-01-01" }, { payDay: 40 }, { currency: "EUR" }, { signedOn: "2026-02-30" }]) {
    assert.equal((await s.call("POST", `${s.P}clients/${c.id}/contracts`, { ...CONTRACT, ...bad })).status, 400, JSON.stringify(bad));
  }
  assert.equal((await s.call("POST", `${s.P}clients/AAAAAAAAAAAA/contracts`, CONTRACT)).status, 404);
});

test("новый договор с «перевести ключи»: ключи клиента по общим тарифам — на цены договора, по названию или сроку", async () => {
  const s = setup();
  const month = (await s.call("POST", `${s.P}tariffs`, { name: "Месяц", days: 30, price: 10, currency: "USD" })).json.tariff;
  const uzs = (await s.call("POST", `${s.P}tariffs`, { name: "Месяц в сумах", days: 30, price: 120000, currency: "UZS" })).json.tariff;
  const year = (await s.call("POST", `${s.P}tariffs`, { name: "Полгода", days: 180, price: 55, currency: "USD" })).json.tariff;
  const a = (await s.call("POST", `${s.P}licenses`, { tariff: String(month.id), method: "cash", ...ALI })).json.keys[0];
  const b = (await s.call("POST", `${s.P}licenses`, { tariff: String(uzs.id), method: "cash", ...ALI })).json.keys[0];
  const c = (await s.call("POST", `${s.P}licenses`, { tariff: String(year.id), method: "cash", ...ALI })).json.keys[0];
  const other = (await s.call("POST", `${s.P}licenses`, { tariff: String(month.id), method: "cash", firstName: "Другой", lastName: "Клиент", phone: "+998 91 111 22 33" })).json.keys[0];
  const id = (await s.key(a)).clientId;
  // Без галочки — ключи по старым ценам.
  assert.equal((await s.call("POST", `${s.P}clients/${id}/contracts`, CONTRACT)).status, 200);
  assert.equal((await s.key(a)).terms.price, 10);
  assert.equal((await s.call("POST", `${s.P}clients/${id}/contracts`, { ...CONTRACT, number: "13/2026", moveKeys: "on" })).status, 200);
  const contractId = (await s.call("GET", `${s.P}clients/${id}`)).json.money.current;
  const t = async (k: string) => { const x = (await s.key(k)).terms; return [x.name, x.price, x.currency, x.contractId]; };
  assert.deepEqual(await t(a), ["Месяц", 50, "USD", contractId], "по названию");
  assert.deepEqual(await t(b), ["Месяц", 50, "USD", contractId], "по сроку 30 дней");
  assert.deepEqual(await t(c), ["6 месяцев", 270, "USD", contractId], "по сроку 180 дней: 50 × 6 − 10%");
  assert.deepEqual(await t(other), ["Месяц", 10, "USD", null], "чужой клиент не трогается");
});

test("подняли цену новым договором: новый ключ по $60, старый продлевается по $50; «перевести ключи» — все по $60", async () => {
  const s = setup();
  const c = await withContract(s);
  const old = (await s.call("POST", `${s.P}licenses`, { tariff: String(c.line("Месяц").id), method: "cash", ...ALI })).json.keys[0];
  s.advance(100);
  assert.equal((await s.call("POST", `${s.P}clients/${c.id}/contracts`, { ...CONTRACT, number: "13/2027", month: 60 })).status, 200);
  const money = (await s.call("GET", `${s.P}clients/${c.id}`)).json.money;
  assert.deepEqual(money.contracts.map((x: any) => [x.number, x.status]), [["13/2027", "active"], ["12/2026", "closed"]], "старый — в истории");
  assert.ok(money.contracts[1].lines.every((l: any) => !l.active), "цены старого договора больше не предлагаются");
  const month60 = money.contracts[0].lines.find((l: any) => l.name === "Месяц");
  assert.equal((await s.call("POST", `${s.P}licenses`, { tariff: String(c.line("Месяц").id), ...ALI })).status, 400, "старая цена не выдаётся");
  const fresh = (await s.call("POST", `${s.P}licenses`, { tariff: String(month60.id), method: "cash", ...ALI })).json.keys[0];
  assert.equal((await s.key(fresh)).terms.price, 60);
  // Старый ключ — «по цене ключа»: $50.
  assert.equal((await s.call("POST", `${s.P}licenses/${old}/extend`, { tariff: "key", method: "card" })).status, 200);
  const o = await s.key(old);
  assert.deepEqual([o.payments[0].amount, o.payments[0].kind, o.terms.price], [50, "renewal", 50]);
  // Правка договора с «перевести ключи» — старый ключ тоже на $60.
  assert.equal((await s.call("POST", `${s.P}contracts/${money.contracts[0].id}`, { ...CONTRACT, number: "13/2027", month: 60, moveKeys: true })).status, 200);
  assert.deepEqual([(await s.key(old)).terms.price, (await s.key(old)).terms.contractId], [60, money.contracts[0].id]);
  assert.ok(s.store.log.some((e) => e.action === "contract" && /ключей на новых ценах: 1/.test(e.details)));
  // Закрыть и удалить (только с паролем).
  assert.equal((await s.call("POST", `${s.P}contracts/${money.contracts[0].id}/close`)).status, 200);
  assert.equal((await s.call("POST", `${s.P}contracts/${money.contracts[0].id}/delete`, {})).status, 403);
  assert.equal((await s.call("POST", `${s.P}contracts/${money.contracts[0].id}/delete`, { adminPassword: PASSWORD })).status, 200);
  assert.equal((await s.key(old)).terms.price, 60, "цена ключа остаётся");
  assert.equal((await s.call("POST", `${s.P}contracts/999/close`)).status, 404);
});

test("оплата частями, сумами по курсу дня и своим курсом; удалить поступление — снова долг", async () => {
  const s = setup(12000);
  const c = await withContract(s);
  const k = (await s.call("POST", `${s.P}licenses`, { tariff: String(c.line("Месяц").id), method: "card", paidNow: 20, ...ALI })).json.keys[0];
  let p = (await s.key(k)).payments[0];
  assert.deepEqual([p.amount, p.credited, p.left, p.paid, p.method, p.dueOn], [50, 20, 30, false, "card", "2026-12-05"], "срок — день оплаты по договору");
  assert.equal((await s.call("POST", `${s.P}payments/${p.id}/paid`, { method: "cash", amount: 40 })).status, 400, "больше остатка");
  // Остаток сумами по курсу ЦБ: 30 × 12 000 = 360 000 сум.
  const r = await s.call("POST", `${s.P}payments/${p.id}/paid`, { method: "cash", currency: "UZS" });
  assert.equal(r.status, 200);
  p = (await s.key(k)).payments[0];
  assert.deepEqual([p.paid, p.left, p.parts.length, p.parts[1].amount, p.parts[1].currency, p.parts[1].credit, p.parts[1].rate], [true, 0, 2, 360000, "UZS", 30, 12000]);
  assert.equal((await s.call("POST", `${s.P}payments/${p.id}/paid`, { method: "cash" })).status, 409);
  // Удалить поступление — только с паролем, и начисление снова долг.
  assert.equal((await s.call("POST", `${s.P}parts/${p.parts[1].id}/delete`, {})).status, 403);
  assert.equal((await s.call("POST", `${s.P}parts/${p.parts[1].id}/delete`, { adminPassword: PASSWORD })).status, 200);
  p = (await s.key(k)).payments[0];
  assert.deepEqual([p.paid, p.left], [false, 30]);
  // Договорённый курс 12 500: заплатили 250 000 сум — засчитано $20.
  assert.equal((await s.call("POST", `${s.P}payments/${p.id}/paid`, { method: "transfer", currency: "UZS", amount: "250 000", rate: "12 500" })).status, 200);
  p = (await s.key(k)).payments[0];
  assert.deepEqual([p.left, p.parts.at(-1).credit, p.parts.at(-1).rate], [10, 20, 12500]);
  assert.equal((await s.call("POST", `${s.P}payments/${p.id}/paid`, { method: "barter" })).status, 400);
  assert.equal((await s.call("POST", `${s.P}payments/999/paid`, { method: "cash" })).status, 404);
  // Итоги: в сумах и в $ по курсу дня каждого поступления.
  const money = (await s.call("GET", `${s.P}money`)).json;
  assert.deepEqual(money.thisMonth, { UZS: 250000, USD: 20 });
  assert.deepEqual(money.combined.thisMonth, { UZS: 490000, USD: 40 }, "$20 × 12 000 + 250 000 сум; 250 000 / 12 500 + $20");
  assert.deepEqual(money.rateToday, { day: "2026-11-08", rate: 12000, fresh: true });
  assert.equal((await s.call("GET", `${s.P}rate`)).json.rate.rate, 12000);
  assert.equal(money.clients[0].name, "Али Каримов");
  assert.equal(money.clients[0].inUZS, 490000);
  assert.ok(money.debts.list[0].reminder.includes("$10"));
});

test("оплата другой валютой: без курса — отказ, а не «закрыть весь долг»; весь остаток — ровно, без копеек; свой курс с опечаткой — нет", async () => {
  const s = setup(); // источники курса молчат, курсов в базе нет
  const month = (await s.call("POST", `${s.P}tariffs`, { name: "Месяц", days: 30, price: 100, currency: "USD" })).json.tariff;
  const k = (await s.call("POST", `${s.P}licenses`, { tariff: String(month.id), method: "debt", dueOn: "2026-11-20", ...ALI })).json.keys[0];
  const id = (await s.key(k)).payments[0].id;
  for (const body of [{ method: "cash", currency: "UZS", amount: "1000" }, { method: "cash", currency: "UZS" }, { method: "cash", currency: "UZS", credit: "5" }]) {
    const r = await s.call("POST", `${s.P}payments/${id}/paid`, body);
    assert.deepEqual([r.status, r.json.error], [409, "no_rate"], JSON.stringify(body));
  }
  assert.equal((await s.key(k)).payments[0].left, 100, "долг на месте");
  // Своей валютой курс не нужен (и источники не спрашиваются).
  assert.equal((await s.call("POST", `${s.P}payments/${id}/paid`, { method: "cash", amount: "10" })).status, 200);
  // Свой курс «120» вместо «12 000» — опечатка.
  assert.equal((await s.call("POST", `${s.P}payments/${id}/paid`, { method: "cash", currency: "UZS", amount: "12000", rate: "120" })).status, 400);
  // Сумы засчитываются по своему курсу; сумма в «засчитать» без курса — тоже можно.
  assert.equal((await s.call("POST", `${s.P}payments/${id}/paid`, { method: "cash", currency: "UZS", amount: "126500", credit: "10" })).status, 200);
  assert.equal((await s.key(k)).payments[0].left, 80);
  // Долг в сумах, весь остаток долларами по курсу: $8.34 за 100 060 сум — закрыт ровно, без «больше остатка» и хвоста.
  await s.call("POST", `${s.P}rates`, { date: "2026-11-08", source: "cbu", rates: { USD: 12000 } });
  for (const left of [100060, 100000]) {
    const sum = (await s.call("POST", `${s.P}licenses/${k}/payments`, { amount: String(left), currency: "UZS", method: "debt" })).json.payment;
    const r = await s.call("POST", `${s.P}payments/${sum.id}/paid`, { method: "card", currency: "USD" });
    assert.equal(r.status, 200, String(left));
    assert.deepEqual([r.json.payment.paid, r.json.payment.left, r.json.payment.parts[0].credit], [true, 0, left]);
  }
  // Своей суммой долларами за долг в сумах, на копейку больше остатка по курсу, — тоже весь остаток.
  const sum = (await s.call("POST", `${s.P}licenses/${k}/payments`, { amount: "100000", currency: "UZS", method: "debt" })).json.payment;
  const r = await s.call("POST", `${s.P}payments/${sum.id}/paid`, { method: "card", currency: "USD", amount: "8.34" });
  assert.deepEqual([r.status, r.json.payment?.left], [200, 0]);
});

test("удалили ошибочное поступление — долг снова со своим сроком, ключ до срока не блокируется", async () => {
  const s = setup();
  const month = (await s.call("POST", `${s.P}tariffs`, { name: "Месяц", days: 30, price: 50, currency: "USD" })).json.tariff;
  const k = (await s.call("POST", `${s.P}licenses`, { tariff: String(month.id), method: "debt", dueOn: "2026-11-30", ...ALI })).json.keys[0];
  s.advance(10);
  const id = (await s.key(k)).payments[0].id;
  const paid = (await s.call("POST", `${s.P}payments/${id}/paid`, { method: "cash" })).json.payment;
  assert.equal(paid.dueOn, "2026-11-30", "срок остаётся и у оплаченного");
  assert.equal((await s.call("POST", `${s.P}parts/${paid.parts[0].id}/delete`, { adminPassword: PASSWORD })).status, 200);
  assert.deepEqual([(await s.key(k)).payments[0].paid, (await s.key(k)).payments[0].dueOn], [false, "2026-11-30"]);
  s.advance(1);
  await s.cron();
  assert.equal((await s.key(k)).status, "active", "18 ноября — до срока 30-го ещё далеко");
});

test("своя сумма другой валютой за 3 срока: за ключом — цена тарифа за один срок в его валюте", async () => {
  const s = setup(12000);
  const month = (await s.call("POST", `${s.P}tariffs`, { name: "Месяц", days: 30, price: 10, currency: "USD" })).json.tariff;
  const k = (await s.call("POST", `${s.P}licenses`, { tariff: String(month.id), periods: 3, amount: "360 000", currency: "UZS", method: "cash", ...ALI })).json.keys[0];
  const v = await s.key(k);
  assert.deepEqual([v.terms.price, v.terms.currency, v.terms.days], [10, "USD", 30]);
  assert.deepEqual([v.payments[0].amount, v.payments[0].currency], [360000, "UZS"]);
  assert.equal(daysLeft(v.expiresAt), 90);
});

test("утренняя сводка: долг без срока — тоже в «просрочено» с днём блокировки (его блокирует так же)", async () => {
  const s = setup();
  const k = (await s.call("POST", `${s.P}licenses`, { amount: "50", currency: "USD", method: "debt", ...ALI })).json.keys[0];
  s.advance(2);
  await s.cron();
  assert.ok(s.sent.some((m) => /Просрочено: 1/.test(m) && /срок 08\.11\.2026, блокировка 13\.11\.2026/.test(m)), s.sent.join("\n---\n"));
  s.advance(3);
  await s.cron();
  assert.equal((await s.key(k)).status, "blocked");
});

test("бесплатно, своя скидка, заплатил вперёд за 3 месяца; продлить все ключи клиента разом", async () => {
  const s = setup();
  const c = await withContract(s);
  const month = String(c.line("Месяц").id);
  const free = (await s.call("POST", `${s.P}licenses`, { tariff: month, free: true, ...ALI })).json.keys[0];
  let p = (await s.key(free)).payments[0];
  assert.deepEqual([p.amount, p.free, p.paid, p.listPrice, p.discount], [0, true, true, 50, 50]);
  assert.equal((await s.key(free)).terms.price, 50, "дальше — по цене договора");
  const cheap = (await s.call("POST", `${s.P}licenses`, { tariff: month, amount: 40, method: "cash", ...ALI })).json.keys[0];
  p = (await s.key(cheap)).payments[0];
  assert.deepEqual([p.amount, p.discount, p.paid], [40, 10, true]);
  // Заплатил вперёд за 3 месяца: 90 дней и $150.
  assert.equal((await s.call("POST", `${s.P}licenses/${cheap}/extend`, { tariff: "key", periods: 3, method: "cash" })).status, 200);
  const v = await s.key(cheap);
  assert.equal(daysLeft(v.expiresAt), 120);
  assert.deepEqual([v.payments[0].amount, v.payments[0].listPrice], [150, 150]);
  assert.equal((await s.call("POST", `${s.P}licenses/${cheap}/extend`, { tariff: "key", periods: 40 })).status, 400);
  // Все ключи клиента разом (у первого ключа без цены — пропускается), в долг.
  const all = await s.call("POST", `${s.P}clients/${c.id}/extend`, { periods: 1, method: "debt" });
  assert.deepEqual(all.json.keys.sort(), [free, cheap].sort());
  const money = (await s.call("GET", `${s.P}clients/${c.id}`)).json.money;
  assert.equal(money.debts.length, 2);
  assert.equal(money.renewable, 2);
  assert.match(money.reminder, /Всего: \$100/);
  assert.equal((await s.call("POST", `${s.P}clients/${c.id}/extend`, { periods: 0 })).status, 400);
});

test("долг не оплачен 5 дней после срока — ключ блокируется сам; оплатили — разблокируется; разблокировали руками — не трогаем", async () => {
  const s = setup();
  const c = await withContract(s);
  const k = (await s.call("POST", `${s.P}licenses`, { tariff: String(c.line("Месяц").id), method: "debt", dueOn: "2026-11-10", ...ALI })).json.keys[0];
  s.advance(6); // 14 ноября: срок + 4 дня — ещё рано
  await s.cron();
  assert.equal((await s.key(k)).status, "active");
  assert.ok(s.sent.some((m) => /Просрочено: 1/.test(m) && /блокировка 15\.11\.2026/.test(m)));
  s.advance(1); // 15 ноября
  await s.cron();
  assert.equal((await s.key(k)).status, "blocked");
  assert.ok(s.sent.some((m) => /Заблокированы за долг/.test(m)));
  assert.ok(s.store.log.some((e) => e.action === "auto_block" && e.actor === "system"));
  const id = (await s.key(k)).payments[0].id;
  assert.equal((await s.call("POST", `${s.P}payments/${id}/paid`, { method: "cash" })).status, 200);
  assert.equal((await s.key(k)).status, "active", "оплатили — разблокирован");
  assert.ok(s.store.log.some((e) => e.action === "auto_unblock"));
  // Второй долг; владелец разблокировал руками — отсрочка, крон больше не блокирует.
  await s.call("POST", `${s.P}licenses/${k}/payments`, { tariff: "key", method: "debt", dueOn: "2026-11-01" });
  await s.cron();
  assert.equal((await s.key(k)).status, "blocked");
  await s.call("POST", `${s.P}licenses/${k}/unblock`);
  s.advance(1);
  await s.cron();
  assert.equal((await s.key(k)).status, "active");
});

test("«навсегда»: 3 месяца обновлений с даты ключа, дальше «Обновления на год» — от конца прежних или с сегодня", async () => {
  const s = setup();
  const c = await withContract(s);
  const k = (await s.call("POST", `${s.P}licenses`, { tariff: String(c.line("Навсегда").id), method: "cash", ...ALI })).json.keys[0];
  let v = await s.key(k);
  assert.equal(v.expiresAt, null);
  assert.equal(daysLeft(v.updatesUntil), 90);
  const upd = String(c.line("Обновления на год").id);
  assert.equal((await s.call("POST", `${s.P}licenses/${k}/updates`, { tariff: upd, method: "card" })).status, 200);
  v = await s.key(k);
  assert.equal(daysLeft(v.updatesUntil), 455, "90 + 365");
  assert.deepEqual([v.payments[0].kind, v.payments[0].amount], ["updates", 300]);
  s.advance(600); // обновления давно кончились — год с сегодня
  await s.call("POST", `${s.P}licenses/${k}/updates`, { tariff: upd, method: "card" });
  assert.equal(daysLeft((await s.key(k)).updatesUntil), 965, "600 + 365");
  // Ключ на месяц — не «навсегда»: 409; ключ «навсегда» без цены (выдан раньше) — 3 месяца с даты ключа.
  const monthly = (await s.call("POST", `${s.P}licenses`, { tariff: String(c.line("Месяц").id), ...ALI })).json.keys[0];
  assert.equal((await s.call("POST", `${s.P}licenses/${monthly}/updates`, { tariff: upd })).status, 409);
  assert.equal((await s.call("POST", `${s.P}licenses/${k}/updates`, { tariff: String(c.line("Месяц").id) })).status, 400, "тариф ключа — не обновления");
  const old = (await s.call("POST", `${s.P}licenses`, {})).json.keys[0];
  assert.equal(daysLeft((await s.key(old)).updatesUntil), 600 + 90);
});

test("цена ключа руками; скидка за количество — от 3 ключей, своя сумма без неё", async () => {
  const s = setup();
  const c = await withContract(s);
  const month = String(c.line("Месяц").id);
  assert.equal((await s.call("POST", `${s.P}money-settings`, { volumeMin: 3, volumePercent: "10%" })).status, 200);
  assert.equal((await s.call("POST", `${s.P}money-settings`, { volumeMin: 3, volumePercent: 100 })).status, 400);
  // У Али уже 1 ключ; +2 — всего 3: скидка 10% на новые.
  const made = (await s.call("POST", `${s.P}licenses`, { count: 2, tariff: month, method: "cash", ...ALI })).json.keys;
  const p = (await s.key(made[0])).payments[0];
  assert.deepEqual([p.amount, p.listPrice, p.discount], [45, 50, 5]);
  assert.match(p.note, /скидка за количество 10%/);
  assert.equal((await s.key(made[0])).terms.price, 50, "цена ключа — по договору");
  const own = (await s.call("POST", `${s.P}licenses`, { tariff: month, amount: 48, method: "cash", ...ALI })).json.keys[0];
  assert.equal((await s.key(own)).payments[0].amount, 48);
  // Изменить цену ключа: своя или по строке тарифа.
  assert.equal((await s.call("POST", `${s.P}licenses/${own}/terms`, { name: "Спеццена", days: 30, price: 35, currency: "USD" })).status, 200);
  assert.deepEqual([(await s.key(own)).terms.name, (await s.key(own)).terms.price], ["Спеццена", 35]);
  assert.equal((await s.call("POST", `${s.P}licenses/${own}/terms`, { tariff: String(c.line("Год").id) })).status, 200);
  assert.equal((await s.key(own)).terms.price, 510);
  assert.equal((await s.call("POST", `${s.P}licenses/${own}/terms`, { price: -1 })).status, 400);
  assert.equal((await s.call("POST", `${s.P}licenses/STRS-0000-0000-0000/terms`, { price: 1 })).status, 404);
  assert.deepEqual((await s.call("GET", `${s.P}money`)).json.volume, { minKeys: 3, percent: 10 });
});

test("общие тарифы: год от месяца со скидкой пересчитывается сам; на сайте — только отмеченные общие", async () => {
  const s = setup();
  const month = (await s.call("POST", `${s.P}tariffs`, { name: "Месяц", days: 30, price: 10, currency: "USD", site: true })).json.tariff;
  const year = await s.call("POST", `${s.P}tariffs`, { name: "Год", days: 365, base: String(month.id), discount: "20", site: "on", label: "выгоднее на 20%" });
  assert.equal(year.status, 200);
  assert.deepEqual([year.json.tariff.price, year.json.tariff.currency], [96, "USD"]);
  await s.call("POST", `${s.P}tariffs/${month.id}`, { name: "Месяц", days: 30, price: 12, currency: "USD", site: true });
  const list = (await s.call("GET", `${s.P}tariffs`)).json.tariffs;
  assert.equal(list.find((t: any) => t.name === "Год").price, 115.2, "12 × 12 − 20%");
  assert.equal((await s.call("POST", `${s.P}tariffs`, { name: "Плохо", days: 365, base: "999" })).status, 400);
  assert.equal((await s.call("POST", `${s.P}tariffs`, { name: "Цепочка", days: 365, base: String(year.json.tariff.id) })).status, 400);
  await s.call("POST", `${s.P}tariffs`, { name: "Скрытый", days: 90, price: 25, currency: "USD" });
  // Тариф в сумах на сайт не попадает даже с галочкой: там цены только в $.
  const uzs = (await s.call("POST", `${s.P}tariffs`, { name: "В сумах", days: 30, price: 120000, currency: "UZS", site: true })).json.tariff;
  assert.equal(uzs.site, false);
  await withContract(s);
  const prices = (await s.call("GET", "/api/prices")).json.prices;
  assert.deepEqual(prices.map((p: any) => [p.name, p.price, p.label]), [["Месяц", 12, ""], ["Год", 115.2, "выгоднее на 20%"]], "договорные, неотмеченные и в сумах не видны");
  // Контрактную строку через общий маршрут не поправить.
  const line = (await s.call("GET", `${s.P}tariffs`)).json.tariffs.find((t: any) => t.contractId);
  assert.equal((await s.call("POST", `${s.P}tariffs/${line.id}`, { name: "x", days: 30, price: 1 })).status, 404);
});

test("файл договора: загрузить, скачать, удалить с паролем; не больше 10", async () => {
  const s = setup();
  const c = await withContract(s);
  const data = Buffer.from("%PDF-1.7 договор").toString("base64");
  const up = await s.call("POST", `${s.P}contracts/${c.contractId}/files`, { name: "договор.pdf", type: "application/pdf", data });
  assert.equal(up.status, 200);
  assert.equal(up.json.file.data, undefined, "в ответе без содержимого");
  const money = (await s.call("GET", `${s.P}clients/${c.id}`)).json.money;
  assert.equal(money.contracts[0].files[0].name, "договор.pdf");
  const got = await s.call("GET", `${s.P}contract-files/${up.json.file.id}`);
  assert.equal(got.json.file.data, data);
  assert.equal((await s.call("POST", `${s.P}contracts/${c.contractId}/files`, { name: "x", type: "text/html", data })).status, 400);
  assert.equal((await s.call("POST", `${s.P}contracts/999/files`, { name: "x.pdf", type: "application/pdf", data })).status, 404);
  for (let i = 0; i < 9; i++) await s.call("POST", `${s.P}contracts/${c.contractId}/files`, { name: `${i}.pdf`, type: "application/pdf", data });
  assert.equal((await s.call("POST", `${s.P}contracts/${c.contractId}/files`, { name: "11.pdf", type: "application/pdf", data })).status, 409);
  assert.equal((await s.call("POST", `${s.P}contract-files/${up.json.file.id}/delete`, {})).status, 403);
  assert.equal((await s.call("POST", `${s.P}contract-files/${up.json.file.id}/delete`, { adminPassword: PASSWORD })).status, 200);
  assert.equal((await s.call("GET", `${s.P}contract-files/${up.json.file.id}`)).status, 404);
});

test("утренняя сводка: срок оплаты сегодня, напомнить о продлении за 3 дня, заявка без ключа 2 дня; заявке — сообщение с ценами", async () => {
  const s = setup();
  await s.call("POST", `${s.P}tariffs`, { name: "Месяц", days: 30, price: 10, currency: "USD", label: "для начала" });
  const c = await withContract(s);
  await s.call("POST", `${s.P}licenses`, { tariff: String(c.line("Месяц").id), method: "debt", dueOn: "2026-11-11", ...ALI });
  await s.call("POST", `${s.P}licenses`, { days: 6, firstName: "Зарина", phone: "+998 93 000 11 22" });
  s.store.requests.push({ id: 7, createdAt: new Date(START.getTime() + 0.5 * DAY), client: { ...EMPTY_CLIENT, firstName: "Дилноза", phone: "+998 97 111 22 33" }, message: "", status: "new", serial: "" });
  s.advance(3); // 11 ноября
  await s.cron();
  const text = s.sent.join("\n");
  assert.match(text, /Сегодня срок оплаты/);
  assert.match(text, /Али Каримов, \+998 90 555 44 33 — \$50/);
  assert.match(text, /Напомнить о продлении/);
  assert.match(text, /Зарина/);
  assert.match(text, /Заявки без ключа 2 дня/);
  assert.match(text, /Дилноза/);
  const req = (await s.call("GET", `${s.P}requests`)).json.requests.find((r: any) => r.id === 7);
  assert.match(req.offer, /Дилноза, здравствуйте!/);
  assert.match(req.offer, /Месяц — \$10 \(для начала\)/);
  assert.doesNotMatch(req.offer, /\$50/, "цены договоров в заявку не попадают");
});

test("«Деньги»: ожидается в этом месяце по ценам ключей и договоры со счётом ключей и суммой в месяц", async () => {
  const s = setup();
  const c = await withContract(s);
  await s.call("POST", `${s.P}licenses`, { tariff: String(c.line("Месяц").id), days: 10, method: "cash", ...ALI });
  await s.call("POST", `${s.P}licenses`, { tariff: String(c.line("Год").id), method: "cash", ...ALI });
  const money = (await s.call("GET", `${s.P}money`)).json;
  assert.deepEqual(money.expected, { total: { UZS: 0, USD: 50 }, keys: 1 }, "месячный кончается в ноябре, годовой — нет");
  assert.equal(money.contracts[0].number, "12/2026");
  assert.equal(money.contracts[0].keys, 2);
  assert.equal(money.contracts[0].perMonth.USD, 50 + Math.round((510 / 365) * 30 * 100) / 100);
  assert.equal(money.contracts[0].clientId, c.id);
});

test("сайт: заявка с карточки «Купить» — первой строкой тариф и цена из базы; чужой или договорный тариф — без строки", async () => {
  const s = setup();
  const month = (await s.call("POST", `${s.P}tariffs`, { name: "Месяц", days: 30, price: 10, currency: "USD", site: true })).json.tariff;
  const hidden = (await s.call("POST", `${s.P}tariffs`, { name: "Скрытый", days: 30, price: 1, currency: "USD" })).json.tariff;
  const send = (tariff: unknown, message = "Нужно 2 ключа") =>
    s.app.handle({ method: "POST", path: "/api/site/request", query: new URLSearchParams(), headers: {}, body: { client: { firstName: "Иван", lastName: "Петров", phone: "+998 90 000 00 00" }, message, tariff }, ip: "5.5.5.5" });
  assert.equal((await send(String(month.id))).status, 200);
  await send(hidden.id);
  await send("", "");
  await send(month.id, "");
  const messages = (await s.store.listRequests(10)).map((r) => r.message).reverse();
  assert.deepEqual(messages, ["Тариф: Месяц — $10\nНужно 2 ключа", "Нужно 2 ключа", "", "Тариф: Месяц — $10"]);
});

test("сайт: цены на главной и в «Купить» — подписи суммы и срока; скрипты без ошибок", async () => {
  const { readFileSync } = await import("node:fs");
  const vm = await import("node:vm");
  const ctx: Record<string, any> = {};
  vm.runInNewContext(readFileSync(new URL("../../public/prices.js", import.meta.url), "utf8"), ctx);
  assert.equal(ctx.priceText(115.2, "USD"), "$115.20");
  assert.equal(ctx.priceText(1200, "USD"), "$1,200");
  assert.equal(ctx.priceText(1200000, "UZS"), "1\u00a0200\u00a0000\u00a0сум");
  assert.deepEqual([null, 30, 90, 180, 365, 1, 3, 11, 21, 45].map((d) => ctx.periodText(d)), ["навсегда", "на месяц", "на 3 месяца", "на 6 месяцев", "на год", "на 1 день", "на 3 дня", "на 11 дней", "на 21 день", "на 45 дней"]);
  assert.equal(ctx.perMonth({ days: 365, price: 115.2, currency: "USD" }), "$9.60 в месяц");
  assert.equal(ctx.perMonth({ days: 30, price: 10, currency: "USD" }), "");
  assert.equal(ctx.perMonth({ days: null, price: 300, currency: "USD" }), "");
  assert.equal(ctx.perMonth({ days: 90, price: 400000, currency: "UZS" }), "133\u00a0333\u00a0сум в месяц");
  for (const name of ["index.html", "buy.html"]) {
    const page = readFileSync(new URL("../../public/" + name, import.meta.url), "utf8");
    const script = page.substring(page.lastIndexOf("<script>") + 8, page.lastIndexOf("</script>"));
    assert.doesNotThrow(() => new Function(script), name);
    assert.match(page, /<script src="\/prices.js"><\/script>/);
  }
});
