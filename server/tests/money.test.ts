// Партия «Деньги» (владелец 2026-11-08): тарифы, оплаты и долги, доход по месяцам, ключи за месяц, воронка,
// средний чек, «предложить продлить» с ценой, итог недели в Telegram. Пункты 7, 12, 14, 18, 44–47, 50.
import { test } from "node:test";
import assert from "node:assert/strict";
import { createHash, createHmac, randomBytes } from "node:crypto";
import { generateKeys, Signer } from "../core/crypto.js";
import { App } from "../core/http.js";
import { MemoryStore } from "../core/memoryStore.js";
import { formatSum, moneyReport, monthOf, renewalOffer, weeklyDigest } from "../core/money.js";
import { Platform } from "../core/platform.js";
import { platformKeys } from "../core/platformAuth.js";
import { dailyDigest } from "../core/insights.js";
import { EMPTY_CLIENT, LicenseService } from "../core/service.js";
import type { AuditRow, LicenseRow, PaymentRow, RequestRow, TrialRow } from "../core/store.js";

const DAY = 24 * 60 * 60 * 1000;
const KEY = "k".repeat(40);
const SECRET = "s".repeat(48);
const PASSWORD = "admin-pass-123";
const keys = generateKeys();

// ---------- Чистые расчёты ----------

const NOW = new Date("2026-11-09T04:00:00Z"); // понедельник, 9:00 по Ташкенту

function lic(id: number, serial: string, createdAt: Date, extra: Partial<LicenseRow> = {}): LicenseRow {
  return { id, serial, plan: "full", status: "active", expiresAt: null, maxPcs: 1, transfersCount: 0, transfersSince: createdAt, note: "", createdAt, client: { ...EMPTY_CLIENT }, ...extra };
}
function pay(id: number, serial: string, amount: number, currency: "UZS" | "USD", at: Date, extra: Partial<PaymentRow> = {}): PaymentRow {
  return { id, serial, kind: "sale", amount, currency, method: "cash", tariff: "", note: "", createdAt: at, paidAt: at, ...extra };
}
const ago = (days: number) => new Date(NOW.getTime() - days * DAY);
const ivan = { ...EMPTY_CLIENT, firstName: "Иван", lastName: "Петров", phone: "+998 90 111 22 33" };
const olga = { ...EMPTY_CLIENT, firstName: "Ольга", phone: "+998 91 000 00 00", telegram: "olga_st" };

test("месяц по Ташкенту: 31 октября 20:00 UTC — уже ноябрь", () => {
  assert.equal(monthOf(new Date("2026-10-31T20:00:00Z")), "2026-11");
  assert.equal(monthOf(new Date("2026-10-31T18:59:00Z")), "2026-10");
});

test("сумма словами: сумы с пробелами, доллары со знаком, копейки только если есть", () => {
  assert.equal(formatSum(1200000, "UZS"), "1 200 000 сум");
  assert.equal(formatSum(50, "USD"), "$50");
  assert.equal(formatSum(12.5, "USD"), "$12.50");
});

test("отчёт: доход по месяцам в двух валютах, долги отдельно, средний чек и доход на клиента", () => {
  const licenses = [lic(1, "STRS-A", ago(35), { client: ivan }), lic(2, "STRS-B", ago(5), { client: olga }), lic(3, "STRS-C", ago(3), { client: ivan })];
  const payments = [
    pay(1, "STRS-A", 1000000, "UZS", ago(35)), // 5 октября
    pay(2, "STRS-B", 50, "USD", ago(5), { method: "card" }),
    pay(3, "STRS-C", 500000, "UZS", ago(3)),
    pay(4, "STRS-C", 300000, "UZS", ago(2), { method: "", paidAt: null, kind: "renewal" }), // долг
  ];
  const r = moneyReport({ now: NOW, payments, licenses, requests: [], trials: [], extended: [] });
  assert.equal(r.months.length, 12);
  assert.equal(r.months.at(-1)!.month, "2026-11");
  assert.deepEqual(r.thisMonth, { UZS: 500000, USD: 50 });
  assert.deepEqual(r.lastMonth, { UZS: 1000000, USD: 0 });
  assert.equal(r.months.find((m) => m.month === "2026-10")!.UZS, 1000000);
  assert.equal(r.months[0].month, "2025-12");
  assert.deepEqual(r.debts.total, { UZS: 300000, USD: 0 });
  assert.equal(r.debts.list.length, 1);
  assert.equal(r.debts.list[0].client.firstName, "Иван");
  assert.equal(r.debts.list[0].days, 2);
  // Средний чек — только оплаченные; на клиента: Иван 1 500 000 сум, Ольга $50.
  assert.deepEqual(r.average.check, { UZS: 750000, USD: 50 });
  assert.deepEqual(r.average.perClient, { UZS: 1500000, USD: 50 });
  assert.equal(r.average.clients, 2);
  const odd = moneyReport({ now: NOW, payments: [pay(1, "STRS-A", 100, "UZS", ago(1)), pay(2, "STRS-A", 101, "UZS", ago(1)), pay(3, "STRS-A", 102, "UZS", ago(1)), pay(4, "STRS-B", 10, "USD", ago(1)), pay(5, "STRS-B", 10.01, "USD", ago(1))], licenses, requests: [], trials: [], extended: [] });
  assert.deepEqual(odd.average.check, { UZS: 101, USD: 10.01 }, "сумы — до целого, доллары — до цента");
  assert.equal(r.payments[0].id, 4, "новые сверху");
  assert.equal(r.payments[0].paid, false);
});

test("ключи за месяц: выдано, продлено (по журналу), кончилось без продления", () => {
  const licenses = [
    lic(1, "STRS-A", ago(3)),
    lic(2, "STRS-B", ago(2), { expiresAt: ago(1) }), // кончился в этом месяце и не продлён
    lic(3, "STRS-C", ago(70), { expiresAt: new Date(NOW.getTime() + 300 * DAY) }),
    lic(4, "STRS-D", ago(70), { expiresAt: ago(1), status: "blocked" }), // заблокированный не считается
  ];
  const extended: AuditRow[] = [{ id: 1, at: ago(1), actor: "hub", action: "extend", serial: "STRS-C", details: "365" }];
  const r = moneyReport({ now: NOW, payments: [], licenses, requests: [], trials: [], extended });
  const nov = r.keys.at(-1)!;
  assert.deepEqual(nov, { month: "2026-11", issued: 2, renewed: 1, ended: 1 });
});

test("воронка: заявки → пробные → ключи → продления, за 90 дней и за всё время", () => {
  const requests = [1, 2, 3, 4].map((id) => ({ id, createdAt: ago(id * 10), client: { ...EMPTY_CLIENT }, message: "", status: "new", serial: "" }) as RequestRow);
  const trials: TrialRow[] = [{ id: 1, hwid: "x", startedAt: ago(5) }, { id: 2, hwid: "y", startedAt: ago(200) }];
  const licenses = [lic(1, "STRS-A", ago(10)), lic(2, "STRS-B", ago(300))];
  const extended: AuditRow[] = [
    { id: 1, at: ago(5), actor: "hub", action: "extend", serial: "STRS-B", details: "365" },
    { id: 2, at: ago(4), actor: "hub", action: "extend", serial: "STRS-B", details: "30" },
  ];
  const r = moneyReport({ now: NOW, payments: [], licenses, requests, trials, extended });
  assert.deepEqual(r.funnel.recent, { requests: 4, trials: 1, bought: 1, renewed: 1 });
  assert.deepEqual(r.funnel.all, { requests: 4, trials: 2, bought: 2, renewed: 1 });
});

test("предложить продлить: цена по последнему тарифу ключа, иначе все тарифы", () => {
  const l = lic(1, "STRS-AB12-CD34-EF56", ago(300), { expiresAt: new Date(NOW.getTime() + 5 * DAY), client: ivan });
  const tariffs = [
    { id: 1, name: "Месяц", days: 30, price: 150000, currency: "UZS" as const, active: true, createdAt: ago(100) },
    { id: 2, name: "Год", days: 365, price: 1200000, currency: "UZS" as const, active: true, createdAt: ago(100) },
    { id: 3, name: "Старый", days: 365, price: 1, currency: "UZS" as const, active: false, createdAt: ago(100) },
  ];
  const one = renewalOffer(l, [pay(1, l.serial, 1200000, "UZS", ago(300), { tariff: "Год" })], tariffs, NOW);
  assert.match(one.text, /^Иван, здравствуйте!/);
  assert.match(one.text, /STRS-AB12-CD34-EF56/);
  assert.match(one.text, /14\.11\.2026/);
  assert.match(one.text, /Год — 1 200 000 сум/);
  assert.doesNotMatch(one.text, /Месяц/);
  const all = renewalOffer({ ...l, client: { ...EMPTY_CLIENT } }, [], tariffs, NOW);
  assert.match(all.text, /^Здравствуйте!/);
  assert.match(all.text, /Месяц — 150 000 сум/);
  assert.match(all.text, /Год — 1 200 000 сум/);
  assert.doesNotMatch(all.text, /Старый/);
  assert.equal(all.tariff, null);
  assert.equal(one.tariff?.name, "Год");
});

test("итог недели: ключи, доход, новые клиенты, заявки и долги рядом с прошлой неделей; пусто — бот молчит", () => {
  const licenses = [lic(1, "STRS-A", ago(20), { client: ivan }), lic(2, "STRS-B", ago(3), { client: olga }), lic(3, "STRS-C", ago(2), { client: ivan })];
  const payments = [pay(1, "STRS-A", 1000000, "UZS", ago(10)), pay(2, "STRS-B", 50, "USD", ago(3)), pay(3, "STRS-C", 400000, "UZS", ago(2), { method: "", paidAt: null })];
  const requests = [{ id: 1, createdAt: ago(1), client: { ...EMPTY_CLIENT }, message: "", status: "new", serial: "" } as RequestRow];
  const text = weeklyDigest({ now: NOW, payments, licenses, requests, hubUrl: "https://hub.test" })!;
  assert.match(text, /Итог недели/);
  assert.match(text, /Ключей выдано: 2 \(прошлая неделя: 0\)/);
  assert.match(text, /Доход: \$50 \(прошлая неделя: 1 000 000 сум\)/);
  assert.match(text, /Новых клиентов: 1/, "Иван был и раньше");
  assert.match(text, /Заявок: 1/);
  assert.match(text, /Долги: 400 000 сум/);
  assert.match(text, /https:\/\/hub\.test\/admin\/strassio\/money/);
  assert.equal(weeklyDigest({ now: NOW, payments: [], licenses: [], requests: [], hubUrl: "https://hub.test" }), null);
});

test("утренняя сводка: оплаты за сутки отдельной строкой", () => {
  const payments = [pay(1, "STRS-A", 500000, "UZS", ago(0.5)), pay(2, "STRS-B", 20, "USD", ago(0.2)), pay(3, "STRS-C", 1, "UZS", ago(3))];
  const text = dailyDigest({ now: NOW, licenses: [], activations: [], requests: [], trials: [], clients: [], trialDays: 14, hubUrl: "https://hub.test", payments })!;
  assert.match(text, /Оплаты за сутки: 2 · 500 000 сум, \$20/);
});

// ---------- Через API хаба ----------

function setup() {
  const store = new MemoryStore();
  let now = new Date("2026-11-08T10:00:00Z");
  const clock = () => now;
  const hub = new LicenseService(store, new Signer(keys.privateKey), clock, "hub");
  const sent: string[] = [];
  hub.useNotifier(async (html) => (sent.push(html), true), "https://hub.test");
  const platform = new Platform({ service: hub, keys: platformKeys(KEY, SECRET), adminPassword: PASSWORD, spendNonce: (n, u, s) => store.spendNonce(n, u, s), now: () => now.getTime() });
  const app = new App(hub).usePlatform(platform).useCron("c".repeat(20));
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
  return { store, call, sent, app, advance: (days: number) => (now = new Date(now.getTime() + days * DAY)), setNow: (d: Date) => (now = d) };
}

test("тарифы: создать, поправить, выключить; неверные — 400", async () => {
  const s = setup();
  assert.equal((await s.call("POST", "/api/platform/tariffs", { name: "", days: 30, price: 1, currency: "UZS" })).status, 400);
  assert.equal((await s.call("POST", "/api/platform/tariffs", { name: "Год", days: 365, price: -5, currency: "UZS" })).status, 400);
  assert.equal((await s.call("POST", "/api/platform/tariffs", { name: "Год", days: 365, price: 5, currency: "EUR" })).status, 400);
  const year = await s.call("POST", "/api/platform/tariffs", { name: "Год", days: 365, price: 1200000, currency: "UZS" });
  assert.equal(year.status, 200);
  const forever = await s.call("POST", "/api/platform/tariffs", { name: "Навсегда", days: "", price: 100, currency: "USD" });
  assert.equal(forever.json.tariff.days, null);
  const id = year.json.tariff.id;
  assert.equal((await s.call("POST", `/api/platform/tariffs/${id}`, { name: "Год+", days: 366, price: 1300000, currency: "UZS", active: false })).status, 200);
  const list = (await s.call("GET", "/api/platform/tariffs")).json.tariffs;
  assert.deepEqual(list.map((t: any) => [t.name, t.active]), [["Навсегда", true], ["Год+", false]], "выключенные внизу");
  assert.equal((await s.call("POST", "/api/platform/tariffs/999", { name: "x", days: 1, price: 1, currency: "UZS" })).status, 404);
  assert.ok(s.store.log.some((e) => e.action === "tariff"));
});

test("ключ по тарифу: срок и сумма подставляются сами, оплата или долг записываются на каждый ключ", async () => {
  const s = setup();
  const t = (await s.call("POST", "/api/platform/tariffs", { name: "Год", days: 365, price: 1200000, currency: "UZS" })).json.tariff;
  const made = await s.call("POST", "/api/platform/licenses", { count: 2, tariff: String(t.id), method: "card", firstName: "Иван", phone: "+998901112233" });
  assert.equal(made.status, 200);
  const [a, b] = made.json.keys;
  const view = (await s.call("GET", `/api/platform/licenses/${a}`)).json.license;
  assert.equal(Math.round((Date.parse(view.expiresAt) - Date.parse("2026-11-08T10:00:00Z")) / DAY), 365);
  assert.equal(view.payments.length, 1);
  assert.deepEqual([view.payments[0].amount, view.payments[0].currency, view.payments[0].method, view.payments[0].paid, view.payments[0].tariff, view.payments[0].kind], [1200000, "UZS", "card", true, "Год", "sale"]);
  // Своя сумма и срок перебивают тариф; «в долг» — без даты оплаты.
  const debt = await s.call("POST", "/api/platform/licenses", { tariff: String(t.id), days: 30, amount: 100, currency: "USD", method: "debt" });
  const d = (await s.call("GET", `/api/platform/licenses/${debt.json.keys[0]}`)).json.license;
  assert.equal(Math.round((Date.parse(d.expiresAt) - Date.parse("2026-11-08T10:00:00Z")) / DAY), 30);
  assert.deepEqual([d.payments[0].amount, d.payments[0].currency, d.payments[0].paid, d.payments[0].method], [100, "USD", false, ""]);
  // Без тарифа и суммы — как раньше, без оплаты.
  const free = await s.call("POST", "/api/platform/licenses", { days: 14 });
  assert.equal((await s.call("GET", `/api/platform/licenses/${free.json.keys[0]}`)).json.license.payments.length, 0);
  // Неверные: чужой тариф, отрицательная сумма, валюта, способ.
  for (const body of [{ tariff: "999" }, { amount: -1 }, { amount: 5, currency: "EUR" }, { amount: 5, method: "barter" }, { amount: 1.234 }]) {
    assert.equal((await s.call("POST", "/api/platform/licenses", body)).status, 400, JSON.stringify(body));
  }
  assert.equal((await s.call("GET", `/api/platform/licenses/${b}`)).json.license.payments.length, 1);
  assert.ok(s.store.log.some((e) => e.action === "payment" && e.serial === a && /1 200 000 сум, карта/.test(e.details)));
});

test("продлить по тарифу — продление с оплатой; ключ по заявке — тоже с тарифом", async () => {
  const s = setup();
  const t = (await s.call("POST", "/api/platform/tariffs", { name: "Месяц", days: 30, price: 150000, currency: "UZS" })).json.tariff;
  const serial = (await s.call("POST", "/api/platform/licenses", { days: 10 })).json.keys[0];
  assert.equal((await s.call("POST", `/api/platform/licenses/${serial}/extend`, { tariff: String(t.id), method: "cash" })).status, 200);
  const v = (await s.call("GET", `/api/platform/licenses/${serial}`)).json.license;
  assert.equal(Math.round((Date.parse(v.expiresAt) - Date.parse("2026-11-08T10:00:00Z")) / DAY), 40);
  assert.deepEqual([v.payments[0].kind, v.payments[0].amount], ["renewal", 150000]);
  assert.equal((await s.call("POST", `/api/platform/licenses/${serial}/extend`, {})).status, 400, "без срока и тарифа");
  assert.equal((await s.call("POST", `/api/platform/licenses/${serial}/extend`, { days: 5 })).status, 200, "просто дни — как раньше");

  await s.call("POST", "/api/site/request".replace("/api/platform", ""), {});
  s.store.requests.push({ id: 77, createdAt: new Date(), client: { ...EMPTY_CLIENT, firstName: "Ольга" }, message: "", status: "new", serial: "" });
  const k = await s.call("POST", "/api/platform/requests/77/key", { tariff: String(t.id), method: "debt" });
  assert.equal(k.status, 200);
  const p = (await s.call("GET", `/api/platform/licenses/${k.json.serial}`)).json.license.payments;
  assert.deepEqual([p[0].kind, p[0].paid, p[0].amount], ["sale", false, 150000]);
});

test("оплаты: записать, отметить «оплачено», удалить только с паролем; деньги в отчёте", async () => {
  const s = setup();
  const serial = (await s.call("POST", "/api/platform/licenses", { days: 30, firstName: "Иван" })).json.keys[0];
  assert.equal((await s.call("POST", `/api/platform/licenses/${serial}/payments`, { amount: 0 })).status, 400);
  assert.equal((await s.call("POST", "/api/platform/licenses/STRS-0000-0000-0000/payments", { amount: 5 })).status, 404);
  const debt = await s.call("POST", `/api/platform/licenses/${serial}/payments`, { amount: 300000, currency: "UZS", method: "debt", note: "до пятницы" });
  assert.equal(debt.status, 200);
  const id = debt.json.payment.id;
  let money = (await s.call("GET", "/api/platform/money")).json;
  assert.deepEqual(money.debts.total, { UZS: 300000, USD: 0 });
  assert.equal(money.debts.list[0].note, "до пятницы");
  assert.equal((await s.call("POST", `/api/platform/payments/${id}/paid`, { method: "debt" })).status, 400);
  s.advance(2);
  assert.equal((await s.call("POST", `/api/platform/payments/${id}/paid`, { method: "transfer" })).status, 200);
  assert.equal((await s.call("POST", `/api/platform/payments/${id}/paid`, { method: "cash" })).status, 409, "уже оплачено");
  money = (await s.call("GET", "/api/platform/money")).json;
  assert.deepEqual(money.debts.total, { UZS: 0, USD: 0 });
  assert.deepEqual(money.thisMonth, { UZS: 300000, USD: 0 });
  assert.equal(money.payments[0].method, "transfer");
  assert.equal((await s.call("POST", `/api/platform/payments/${id}/delete`, {})).status, 403);
  assert.equal((await s.call("POST", `/api/platform/payments/${id}/delete`, { adminPassword: PASSWORD })).status, 200);
  assert.equal((await s.call("POST", `/api/platform/payments/${id}/delete`, { adminPassword: PASSWORD })).status, 404);
  assert.deepEqual(["payment", "paid", "payment_delete"].map((a) => s.store.log.some((e) => e.action === a)), [true, true, true]);
  // Удалённый ключ: оплаты остаются в доходе.
  await s.call("POST", `/api/platform/licenses/${serial}/payments`, { amount: 10, currency: "USD", method: "cash" });
  await s.call("POST", `/api/platform/licenses/${serial}/delete`, { adminPassword: PASSWORD, confirm: serial.slice(-4) });
  assert.deepEqual((await s.call("GET", "/api/platform/money")).json.thisMonth, { UZS: 0, USD: 10 });
});

test("предложение продлить в карточке ключа и итог недели по понедельникам из ежедневной задачи", async () => {
  const s = setup();
  await s.call("POST", "/api/platform/tariffs", { name: "Год", days: 365, price: 1200000, currency: "UZS" });
  const serial = (await s.call("POST", "/api/platform/licenses", { days: 5, firstName: "Иван", amount: 1000000, method: "cash" })).json.keys[0];
  const v = (await s.call("GET", `/api/platform/licenses/${serial}`)).json.license;
  assert.match(v.offer.text, /Иван, здравствуйте!/);
  assert.match(v.offer.text, /Год — 1 200 000 сум/);
  const cron = (when: string) => {
    s.setNow(new Date(when));
    return s.app.handle({ method: "GET", path: "/api/cron/daily", query: new URLSearchParams(), headers: { authorization: "Bearer " + "c".repeat(20) }, body: null, ip: "1" });
  };
  s.sent.length = 0;
  const sunday = await cron("2026-11-15T04:00:00Z");
  assert.equal((sunday.json as any).weekly, false);
  assert.equal(s.sent.some((m) => /Итог недели/.test(m)), false);
  const monday = await cron("2026-11-16T04:00:00Z");
  assert.equal((monday.json as any).weekly, true);
  assert.ok(s.sent.some((m) => /Итог недели/.test(m)));
});
