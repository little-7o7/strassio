// Деньги для хаба little_7o7 (владелец 2026-11-08, партия «Деньги»): доход по месяцам, долги, средний чек и доход на
// клиента, ключи за месяц, воронка «заявка → пробный → ключ → продление», текст «предложить продлить» с ценой и итог
// недели для Telegram. Чистые функции: строки из базы на входе, готовые данные на выходе.
import { clientId, clientKey } from "./insights.js";
import { add, formatSum, sumsText, zero, type Sums } from "./sums.js";
import type { AuditRow, ClientInfo, LicenseRow, PaymentRow, RequestRow, TariffRow, TrialRow } from "./store.js";

export { formatSum, sumsText, type Sums } from "./sums.js";

const DAY = 24 * 60 * 60 * 1000;
/** Ташкент: UTC+5 круглый год. */
const TASHKENT = 5 * 60 * 60 * 1000;
/** Воронка «недавно»: столько дней. */
export const FUNNEL_DAYS = 90;
const EMPTY: ClientInfo = { firstName: "", lastName: "", phone: "", email: "", birthday: "", telegram: "" };

const iso = (d: Date) => d.toISOString().replace(/\.\d{3}Z$/, "Z");
const nameOf = (c: ClientInfo) => [c.firstName, c.lastName].filter(Boolean).join(" ");
const esc = (s: string) => s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");

/** Месяц ГГГГ-ММ по Ташкенту: 31 октября 20:00 UTC — уже 1 ноября. */
export const monthOf = (d: Date) => new Date(d.getTime() + TASHKENT).toISOString().slice(0, 7);

/** Последние n месяцев по Ташкенту, старые первыми; последний — текущий. */
export function lastMonths(now: Date, n: number): string[] {
  const [y, m] = monthOf(now).split("-").map(Number);
  const out: string[] = [];
  for (let i = n - 1; i >= 0; i--) {
    const total = y * 12 + (m - 1) - i;
    out.push(`${Math.floor(total / 12)}-${String((total % 12) + 1).padStart(2, "0")}`);
  }
  return out;
}

/** ДД.ММ.ГГГГ по Ташкенту. */
const dateRu = (d: Date) => new Date(d.getTime() + TASHKENT).toISOString().slice(0, 10).split("-").reverse().join(".");

export interface PaymentView {
  id: number;
  serial: string;
  kind: "sale" | "renewal";
  amount: number;
  currency: "UZS" | "USD";
  method: string;
  tariff: string;
  note: string;
  createdAt: string;
  paidAt: string | null;
  paid: boolean;
}

export function paymentView(p: PaymentRow): PaymentView {
  return { id: p.id, serial: p.serial, kind: p.kind, amount: p.amount, currency: p.currency, method: p.method, tariff: p.tariff, note: p.note, createdAt: iso(p.createdAt), paidAt: p.paidAt ? iso(p.paidAt) : null, paid: !!p.paidAt };
}

export function tariffView(t: TariffRow) {
  return { id: t.id, name: t.name, days: t.days, price: t.price, currency: t.currency, active: t.active, createdAt: iso(t.createdAt) };
}

/** Включённые сверху, дальше по сроку (бессрочные в конце) и по цене. */
export function sortTariffs(list: TariffRow[]): TariffRow[] {
  return list.slice().sort((a, b) => Number(b.active) - Number(a.active) || (a.days ?? Infinity) - (b.days ?? Infinity) || a.price - b.price || a.id - b.id);
}

/** Доход за этот и прошлый месяц и долги — для Главной хаба. */
export function moneyTotals(payments: PaymentRow[], now: Date) {
  const [last, current] = lastMonths(now, 2);
  const thisMonth = zero();
  const lastMonth = zero();
  const debts = zero();
  let debtCount = 0;
  for (const p of payments) {
    if (!p.paidAt) {
      add(debts, p.currency, p.amount);
      debtCount++;
      continue;
    }
    const m = monthOf(p.paidAt);
    if (m === current) add(thisMonth, p.currency, p.amount);
    else if (m === last) add(lastMonth, p.currency, p.amount);
  }
  return { thisMonth, lastMonth, debts, debtCount };
}

export interface MoneyInput {
  now: Date;
  payments: PaymentRow[];
  licenses: LicenseRow[];
  requests: RequestRow[];
  trials: TrialRow[];
  /** Записи журнала «extend» (продления). */
  extended: AuditRow[];
}

/** Раздел «Деньги» хаба целиком. Доход — по дате оплаты, долги — отдельно и в доход не входят. */
export function moneyReport(d: MoneyInput) {
  const t = d.now.getTime();
  const bySerial = new Map(d.licenses.map((l) => [l.serial, l]));
  const clientOf = (serial: string) => bySerial.get(serial)?.client ?? EMPTY;
  const months = lastMonths(d.now, 12);

  const income = new Map(months.map((month) => [month, { month, ...zero(), count: 0 }]));
  const paid = d.payments.filter((p) => p.paidAt);
  for (const p of paid) {
    const row = income.get(monthOf(p.paidAt!));
    if (!row) continue;
    row[p.currency] = Math.round((row[p.currency] + p.amount) * 100) / 100;
    row.count++;
  }

  // Средний чек — по оплаченным; доход на клиента — сумма в валюте / сколько клиентов платили в этой валюте.
  const totals = zero();
  const checks = zero();
  const payers: Record<"UZS" | "USD", Set<string>> = { UZS: new Set(), USD: new Set() };
  for (const p of paid) {
    add(totals, p.currency, p.amount);
    checks[p.currency]++;
    payers[p.currency].add(clientKey(clientOf(p.serial)) || "s" + p.serial);
  }
  // Средние — сумы до целого, доллары до цента: «610 714,29 сум» читать незачем.
  const avg = (sum: number, n: number, cents: boolean) => (n ? (cents ? Math.round((sum / n) * 100) / 100 : Math.round(sum / n)) : 0);
  const average = {
    check: { UZS: avg(totals.UZS, checks.UZS, false), USD: avg(totals.USD, checks.USD, true) },
    perClient: { UZS: avg(totals.UZS, payers.UZS.size, false), USD: avg(totals.USD, payers.USD.size, true) },
    clients: new Set([...payers.UZS, ...payers.USD]).size,
  };

  const debtRows = d.payments.filter((p) => !p.paidAt).sort((a, b) => a.createdAt.getTime() - b.createdAt.getTime());
  const debtTotal = zero();
  for (const p of debtRows) add(debtTotal, p.currency, p.amount);
  const withClient = (p: PaymentRow) => {
    const client = clientOf(p.serial);
    const key = clientKey(client);
    return { ...paymentView(p), client: { ...client }, clientId: key ? clientId(key) : "" };
  };

  // Ключи за месяц: выдано, продлено (разных ключей по журналу), кончилось и не продлено (срок вышел в этом месяце).
  const keys = months.map((month) => ({ month, issued: 0, renewed: 0, ended: 0 }));
  const keyRow = new Map(keys.map((k) => [k.month, k]));
  for (const l of d.licenses) {
    const issued = keyRow.get(monthOf(l.createdAt));
    if (issued) issued.issued++;
    if (l.status !== "blocked" && l.expiresAt && l.expiresAt.getTime() <= t) {
      const ended = keyRow.get(monthOf(l.expiresAt));
      if (ended) ended.ended++;
    }
  }
  const renewedIn = new Map<string, Set<string>>();
  for (const e of d.extended) {
    const m = monthOf(e.at);
    renewedIn.set(m, (renewedIn.get(m) ?? new Set()).add(e.serial));
  }
  for (const k of keys) k.renewed = renewedIn.get(k.month)?.size ?? 0;

  const since = t - FUNNEL_DAYS * DAY;
  const funnel = {
    recent: {
      requests: d.requests.filter((r) => r.createdAt.getTime() > since).length,
      trials: d.trials.filter((x) => x.startedAt.getTime() > since).length,
      bought: d.licenses.filter((l) => l.createdAt.getTime() > since).length,
      renewed: new Set(d.extended.filter((e) => e.at.getTime() > since).map((e) => e.serial)).size,
    },
    all: { requests: d.requests.length, trials: d.trials.length, bought: d.licenses.length, renewed: new Set(d.extended.map((e) => e.serial)).size },
  };

  const totalsNow = moneyTotals(d.payments, d.now);
  return {
    months: [...income.values()],
    thisMonth: totalsNow.thisMonth,
    lastMonth: totalsNow.lastMonth,
    debts: { total: debtTotal, list: debtRows.map((p) => ({ ...withClient(p), days: Math.max(0, Math.floor((t - p.createdAt.getTime()) / DAY)) })) },
    average,
    payments: d.payments
      .slice()
      .sort((a, b) => b.createdAt.getTime() - a.createdAt.getTime() || b.id - a.id)
      .slice(0, 500)
      .map(withClient),
    keys,
    funnel,
  };
}

/**
 * «Предложить продлить»: готовый текст клиенту с датой конца и ценой. Цена — тариф последней оплаты ключа, если он ещё
 * включён; иначе все включённые тарифы. tariff — какой тариф предложен (null — список).
 */
export function renewalOffer(l: LicenseRow, payments: PaymentRow[], tariffs: TariffRow[], now: Date): { text: string; tariff: TariffRow | null } {
  const active = sortTariffs(tariffs.filter((x) => x.active));
  const last = payments.filter((p) => p.serial === l.serial && p.tariff).sort((a, b) => b.createdAt.getTime() - a.createdAt.getTime())[0];
  const own = last ? active.find((x) => x.name === last.tariff) ?? null : null;
  const list = own ? [own] : active;
  const name = l.client.firstName.trim();
  const lines = [name ? `${name}, здравствуйте!` : "Здравствуйте!"];
  if (!l.expiresAt) lines.push(`Ваш ключ Strassio ${l.serial} — бессрочный.`);
  else if (l.expiresAt.getTime() <= now.getTime()) lines.push(`Срок вашего ключа Strassio ${l.serial} закончился ${dateRu(l.expiresAt)}.`);
  else lines.push(`Срок вашего ключа Strassio ${l.serial} заканчивается ${dateRu(l.expiresAt)}.`);
  if (list.length) lines.push("", "Продление:", ...list.map((x) => `• ${x.name} — ${formatSum(x.price, x.currency)}`));
  lines.push("", "Напишите, какой вариант удобен, — продлю ключ сразу после оплаты.");
  return { text: lines.join("\n"), tariff: own };
}

export interface WeeklyInput {
  now: Date;
  payments: PaymentRow[];
  licenses: LicenseRow[];
  requests: RequestRow[];
  hubUrl: string;
}

/**
 * Итог недели (понедельник утром, в Telegram): ключи, доход, новые клиенты и заявки за 7 дней рядом с прошлыми
 * 7 днями, долги. Ни ключей, ни денег, ни заявок, ни долгов — null (бот молчит).
 */
export function weeklyDigest(d: WeeklyInput): string | null {
  const t = d.now.getTime();
  const inWeek = (x: Date, back: number) => x.getTime() >= t - (back + 1) * 7 * DAY && x.getTime() < t - back * 7 * DAY;
  const keys = [0, 1].map((w) => d.licenses.filter((l) => inWeek(l.createdAt, w)).length);
  const income = [0, 1].map((w) => d.payments.filter((p) => p.paidAt && inWeek(p.paidAt, w)).reduce((s, p) => add(s, p.currency, p.amount), zero()));
  const requests = [0, 1].map((w) => d.requests.filter((r) => inWeek(r.createdAt, w)).length);
  const debtRows = d.payments.filter((p) => !p.paidAt);
  const debts = debtRows.reduce((s, p) => add(s, p.currency, p.amount), zero());
  // Новый клиент — первый ключ этого человека выдан на этой неделе.
  const first = new Map<string, number>();
  for (const l of d.licenses) {
    const key = clientKey(l.client);
    if (key) first.set(key, Math.min(first.get(key) ?? Infinity, l.createdAt.getTime()));
  }
  const newClients = [...first.values()].filter((at) => inWeek(new Date(at), 0)).length;
  const money = (s: Sums) => s.UZS || s.USD;
  if (!keys[0] && !keys[1] && !money(income[0]) && !money(income[1]) && !requests[0] && !requests[1] && !debtRows.length) return null;

  const day = (x: number) => new Date(x).toLocaleDateString("ru-RU", { timeZone: "Asia/Tashkent", day: "numeric", month: "long" });
  const lines = [
    `📊 <b>Strassio · Итог недели</b> (${day(t - 7 * DAY)} – ${day(t - DAY)})`,
    `Ключей выдано: ${keys[0]} (прошлая неделя: ${keys[1]})`,
    `Доход: ${sumsText(income[0])} (прошлая неделя: ${sumsText(income[1])})`,
    `Новых клиентов: ${newClients}`,
    `Заявок: ${requests[0]} (прошлая неделя: ${requests[1]})`,
  ];
  if (debtRows.length) {
    lines.push(`Долги: ${sumsText(debts)} · оплат: ${debtRows.length}`);
    for (const p of debtRows.slice(0, 10)) {
      const c = d.licenses.find((l) => l.serial === p.serial)?.client ?? EMPTY;
      lines.push(`• ${esc(nameOf(c) || p.serial)}${c.phone ? ", " + esc(c.phone) : ""} — ${formatSum(p.amount, p.currency)}`);
    }
    if (debtRows.length > 10) lines.push(`…и ещё ${debtRows.length - 10}`);
  }
  lines.push("", `<a href="${d.hubUrl}/admin/strassio/money">Открыть «Деньги»</a>`);
  return lines.join("\n");
}

/** Понедельник по Ташкенту — день итога недели. */
export const isMonday = (now: Date) => new Date(now.getTime() + TASHKENT).getUTCDay() === 1;
