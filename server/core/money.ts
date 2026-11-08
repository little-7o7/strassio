// Деньги для хаба little_7o7 (владелец 2026-11-08, партия «Деньги» и договоры): начисления и поступления (оплата частями),
// доход по месяцам, долги, средний чек и доход на клиента, ключи за месяц, воронка «заявка → пробный → ключ →
// продление», ожидаемые деньги по ценам ключей, текст «предложить продлить» и «напомнить об оплате», итог недели и
// строки утренней сводки для Telegram. Чистые функции: строки из базы на входе, готовые данные на выходе.
import { clientId, clientKey } from "./insights.js";
import { add, formatSum, sumsText, zero, type Sums } from "./sums.js";
import type { AuditRow, ClientInfo, ContractRow, KeyTermsRow, LicenseRow, PaymentPartRow, PaymentRow, RequestRow, TariffRow, TrialRow } from "./store.js";

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
const round2 = (n: number) => Math.round(n * 100) / 100;

/** Месяц ГГГГ-ММ по Ташкенту: 31 октября 20:00 UTC — уже 1 ноября. */
export const monthOf = (d: Date) => new Date(d.getTime() + TASHKENT).toISOString().slice(0, 7);
/** День ГГГГ-ММ-ДД по Ташкенту. */
export const dayOf = (d: Date) => new Date(d.getTime() + TASHKENT).toISOString().slice(0, 10);

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
const dateRu = (d: Date) => dayOf(d).split("-").reverse().join(".");
/** ГГГГ-ММ-ДД → ДД.ММ.ГГГГ. */
const dayRu = (day: string) => day.split("-").reverse().join(".");

/** Ближайший день оплаты по договору (сегодня или позже), ГГГГ-ММ-ДД; 31-е в коротком месяце — последний день. */
export function nextPayDay(now: Date, payDay: number): string {
  const [y, m, d] = dayOf(now).split("-").map(Number);
  const on = (year: number, month: number) => {
    const last = new Date(Date.UTC(year, month, 0)).getUTCDate();
    return `${year}-${String(month).padStart(2, "0")}-${String(Math.min(payDay, last)).padStart(2, "0")}`;
  };
  const here = on(y, m);
  if (Number(here.slice(8)) >= d) return here;
  return m === 12 ? on(y + 1, 1) : on(y, m + 1);
}

// ---------- Начисления и поступления ----------

export interface PartView {
  id: number;
  amount: number;
  currency: "UZS" | "USD";
  rate: number;
  credit: number;
  method: string;
  note: string;
  paidAt: string;
}

export interface PaymentView {
  id: number;
  serial: string;
  kind: PaymentRow["kind"];
  amount: number;
  currency: "UZS" | "USD";
  listPrice: number;
  /** Скидка от цены (listPrice − amount); 0 — без скидки. */
  discount: number;
  free: boolean;
  tariff: string;
  contractId: number | null;
  note: string;
  dueOn: string;
  createdAt: string;
  paidAt: string | null;
  /** Закрыто полностью. */
  paid: boolean;
  /** Засчитано и осталось — в валюте начисления. */
  credited: number;
  left: number;
  /** Способ последнего поступления; "" — денег ещё не было. */
  method: string;
  parts: PartView[];
}

const partView = (p: PaymentPartRow): PartView => ({ id: p.id, amount: p.amount, currency: p.currency, rate: p.rate, credit: p.credit, method: p.method, note: p.note, paidAt: iso(p.paidAt) });

/** Сколько засчитано по начислению. */
export const creditedOf = (parts: PaymentPartRow[]) => round2(parts.reduce((s, p) => s + p.credit, 0));
/** Сколько осталось заплатить; закрытое начисление — 0. */
export const leftOf = (p: PaymentRow, parts: PaymentPartRow[]) => (p.paidAt ? 0 : Math.max(0, round2(p.amount - creditedOf(parts))));

export function paymentView(p: PaymentRow, parts: PaymentPartRow[] = []): PaymentView {
  const mine = parts.filter((x) => x.paymentId === p.id).sort((a, b) => a.paidAt.getTime() - b.paidAt.getTime() || a.id - b.id);
  const credited = creditedOf(mine);
  return {
    id: p.id,
    serial: p.serial,
    kind: p.kind,
    amount: p.amount,
    currency: p.currency,
    listPrice: p.listPrice,
    discount: p.listPrice > p.amount ? round2(p.listPrice - p.amount) : 0,
    free: p.amount === 0,
    tariff: p.tariff,
    contractId: p.contractId,
    note: p.note,
    dueOn: p.dueOn,
    createdAt: iso(p.createdAt),
    paidAt: p.paidAt ? iso(p.paidAt) : null,
    paid: !!p.paidAt,
    credited,
    left: leftOf(p, mine),
    method: mine.at(-1)?.method ?? "",
    parts: mine.map(partView),
  };
}

/** Поступления по начислениям: id начисления → его поступления. */
export function partsByPayment(parts: PaymentPartRow[]): Map<number, PaymentPartRow[]> {
  const out = new Map<number, PaymentPartRow[]>();
  for (const p of parts) out.set(p.paymentId, [...(out.get(p.paymentId) ?? []), p]);
  return out;
}

// ---------- Тарифы ----------

export function tariffView(t: TariffRow) {
  return { id: t.id, name: t.name, days: t.days, price: t.price, currency: t.currency, active: t.active, createdAt: iso(t.createdAt), contractId: t.contractId, kind: t.kind, baseId: t.baseId, discount: t.discount, site: t.site, label: t.label };
}

/** Включённые сверху, дальше ключи раньше обновлений, по сроку (навсегда в конце) и по цене. */
export function sortTariffs(list: TariffRow[]): TariffRow[] {
  return list.slice().sort((a, b) => Number(b.active) - Number(a.active) || Number(a.kind === "updates") - Number(b.kind === "updates") || (a.days ?? Infinity) - (b.days ?? Infinity) || a.price - b.price || a.id - b.id);
}

/** Скидка в процентах (владелец 2026-11-08: «protsentom»): «15», «15%», «12,5 %»; "" — без скидки. null — не 0–99. */
export function parseDiscount(text: string): number | null {
  const t = text.replace(/\s/g, "").replace(",", ".").replace(/%$/, "");
  if (!t) return 0;
  return /^\d{1,2}(\.\d{1,2})?$/.test(t) ? Number(t) : null;
}

/**
 * Цена от основного тарифа: «Год» = цена месяца × 12 − скидка (владелец 2026-11-08: «mesyats*12-skidka=godovoy plan»),
 * так же 3 и 6 месяцев. Месяцев — срок / 30 с округлением (365 дней — 12). Сумы — до целого, доллары — до цента.
 * null — скидка неверна.
 */
export function derivedPrice(base: TariffRow, days: number | null, discount: string): number | null {
  const pct = parseDiscount(discount);
  if (pct === null || !days || !base.days) return null;
  const months = Math.max(1, Math.round(days / 30));
  const price = (base.price / base.days) * 30 * months * (1 - pct / 100);
  const rounded = base.currency === "UZS" ? Math.round(price) : round2(price);
  return rounded > 0 ? rounded : null;
}

// ---------- Отчёты ----------

/** Доход за этот и прошлый месяц (поступления) и долги (остатки начислений) — для Главной хаба. */
export function moneyTotals(payments: PaymentRow[], parts: PaymentPartRow[], now: Date) {
  const [last, current] = lastMonths(now, 2);
  const thisMonth = zero();
  const lastMonth = zero();
  for (const p of parts) {
    const m = monthOf(p.paidAt);
    if (m === current) add(thisMonth, p.currency, p.amount);
    else if (m === last) add(lastMonth, p.currency, p.amount);
  }
  const by = partsByPayment(parts);
  const debts = zero();
  let debtCount = 0;
  for (const p of payments) {
    const left = leftOf(p, by.get(p.id) ?? []);
    if (left > 0) {
      add(debts, p.currency, left);
      debtCount++;
    }
  }
  return { thisMonth, lastMonth, debts, debtCount };
}

export interface MoneyInput {
  now: Date;
  payments: PaymentRow[];
  parts: PaymentPartRow[];
  licenses: LicenseRow[];
  requests: RequestRow[];
  trials: TrialRow[];
  /** Записи журнала «extend» (продления). */
  extended: AuditRow[];
  terms: KeyTermsRow[];
  contracts: ContractRow[];
  /** Курс на сегодня — для поступлений, у которых курс не записан. */
  rate: number;
}

/** Поступление в обеих валютах по курсу его дня (rate поступления, иначе запасной); курса нет — только своя валюта. */
export function bothCurrencies(p: PaymentPartRow, fallback: number): Sums {
  const rate = p.rate || fallback;
  if (p.currency === "UZS") return { UZS: p.amount, USD: rate ? round2(p.amount / rate) : 0 };
  return { UZS: rate ? Math.round(p.amount * rate) : 0, USD: p.amount };
}

/** Раздел «Деньги» хаба целиком. Доход — по поступлениям (что правда пришло), долги — остатки начислений. */
export function moneyReport(d: MoneyInput) {
  const t = d.now.getTime();
  const bySerial = new Map(d.licenses.map((l) => [l.serial, l]));
  const clientOf = (serial: string) => bySerial.get(serial)?.client ?? EMPTY;
  const byPayment = partsByPayment(d.parts);
  const paymentOf = new Map(d.payments.map((p) => [p.id, p]));
  const months = lastMonths(d.now, 12);

  const income = new Map(months.map((month) => [month, { month, ...zero(), count: 0 }]));
  for (const p of d.parts) {
    const row = income.get(monthOf(p.paidAt));
    if (!row) continue;
    row[p.currency] = round2(row[p.currency] + p.amount);
    row.count++;
  }

  // Средний чек — сколько в среднем пришло по одному начислению; доход на клиента — сумма в валюте / сколько клиентов
  // платили в этой валюте.
  const totals = zero();
  const checks: Record<"UZS" | "USD", Set<number>> = { UZS: new Set(), USD: new Set() };
  const payers: Record<"UZS" | "USD", Set<string>> = { UZS: new Set(), USD: new Set() };
  for (const p of d.parts) {
    add(totals, p.currency, p.amount);
    checks[p.currency].add(p.paymentId);
    const serial = paymentOf.get(p.paymentId)?.serial ?? "";
    payers[p.currency].add(clientKey(clientOf(serial)) || "s" + serial);
  }
  // Средние — сумы до целого, доллары до цента: «610 714,29 сум» читать незачем.
  const avg = (sum: number, n: number, cents: boolean) => (n ? (cents ? round2(sum / n) : Math.round(sum / n)) : 0);
  const average = {
    check: { UZS: avg(totals.UZS, checks.UZS.size, false), USD: avg(totals.USD, checks.USD.size, true) },
    perClient: { UZS: avg(totals.UZS, payers.UZS.size, false), USD: avg(totals.USD, payers.USD.size, true) },
    clients: new Set([...payers.UZS, ...payers.USD]).size,
  };

  const withClient = (p: PaymentRow) => {
    const client = clientOf(p.serial);
    const key = clientKey(client);
    return { ...paymentView(p, byPayment.get(p.id) ?? []), client: { ...client }, clientId: key ? clientId(key) : "" };
  };
  const debtRows = d.payments.filter((p) => leftOf(p, byPayment.get(p.id) ?? []) > 0).sort((a, b) => a.createdAt.getTime() - b.createdAt.getTime());
  const debtTotal = zero();
  const debtList = debtRows.map((p) => {
    const v = withClient(p);
    add(debtTotal, p.currency, v.left);
    return { ...v, days: Math.max(0, Math.floor((t - p.createdAt.getTime()) / DAY)), overdue: !!p.dueOn && p.dueOn < dayOf(d.now), reminder: debtReminder(v.client, [v], d.now) };
  });

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

  // Всё в сумах и всё в долларах по курсу дня каждого поступления (владелец 2026-11-08: «v sumax, i po kursu dnya v $»).
  const combined = new Map(months.map((month) => [month, { month, ...zero() }]));
  for (const p of d.parts) {
    const row = combined.get(monthOf(p.paidAt));
    if (!row) continue;
    const both = bothCurrencies(p, d.rate);
    row.UZS = Math.round(row.UZS + both.UZS);
    row.USD = round2(row.USD + both.USD);
  }
  const combinedMonths = [...combined.values()];

  const totalsNow = moneyTotals(d.payments, d.parts, d.now);
  return {
    months: [...income.values()],
    combined: { months: combinedMonths, thisMonth: { UZS: combinedMonths.at(-1)!.UZS, USD: combinedMonths.at(-1)!.USD }, lastMonth: { UZS: combinedMonths.at(-2)!.UZS, USD: combinedMonths.at(-2)!.USD } },
    rate: d.rate,
    clients: bestClients(d),
    thisMonth: totalsNow.thisMonth,
    lastMonth: totalsNow.lastMonth,
    debts: { total: debtTotal, list: debtList },
    expected: expectedThisMonth(d.licenses, d.terms, d.now),
    contracts: contractsSummary(d.contracts, d.terms, d.licenses, d.now),
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

/** Клиент «ушёл»: ни одного действующего ключа, и последний кончился (или заблокирован) больше 30 дней назад. */
export const GONE_DAYS = 30;

/**
 * Лучшие клиенты: сколько каждый принёс за всё время (в каждой валюте и всё в сумах по курсу дня), сколько оплат, с
 * какого времени платит, сколько ключей и сколько из них действуют; «ушёл» — см. GONE_DAYS. По доходу, первые 50.
 */
export function bestClients(d: Pick<MoneyInput, "now" | "payments" | "parts" | "licenses" | "rate">) {
  const t = d.now.getTime();
  const paymentOf = new Map(d.payments.map((p) => [p.id, p]));
  const keyOf = new Map(d.licenses.map((l) => [l.serial, clientKey(l.client)]));
  const groups = new Map<string, { total: Sums; inUZS: number; inUSD: number; payments: Set<number>; first: number; last: number }>();
  for (const p of d.parts) {
    const serial = paymentOf.get(p.paymentId)?.serial ?? "";
    const key = keyOf.get(serial);
    if (!key) continue;
    const g = groups.get(key) ?? { total: zero(), inUZS: 0, inUSD: 0, payments: new Set<number>(), first: Infinity, last: 0 };
    const both = bothCurrencies(p, d.rate);
    add(g.total, p.currency, p.amount);
    g.inUZS = Math.round(g.inUZS + both.UZS);
    g.inUSD = round2(g.inUSD + both.USD);
    g.payments.add(p.paymentId);
    g.first = Math.min(g.first, p.paidAt.getTime());
    g.last = Math.max(g.last, p.paidAt.getTime());
    groups.set(key, g);
  }
  return [...groups.entries()]
    .map(([key, g]) => {
      const keys = d.licenses.filter((l) => clientKey(l.client) === key);
      const live = keys.filter((l) => l.status !== "blocked" && (!l.expiresAt || l.expiresAt.getTime() > t));
      const lastEnd = Math.max(0, ...keys.map((l) => (l.status === "blocked" ? t : l.expiresAt?.getTime() ?? t)));
      const client = keys.slice().sort((a, b) => b.createdAt.getTime() - a.createdAt.getTime())[0].client;
      return {
        clientId: clientId(key),
        name: nameOf(client),
        phone: client.phone,
        total: g.total,
        inUZS: g.inUZS,
        inUSD: g.inUSD,
        payments: g.payments.size,
        first: iso(new Date(g.first)),
        last: iso(new Date(g.last)),
        keys: keys.length,
        active: live.length,
        gone: !live.length && lastEnd < t - GONE_DAYS * DAY,
      };
    })
    .sort((a, b) => b.inUZS - a.inUZS || b.inUSD - a.inUSD)
    .slice(0, 50);
}

/** Ожидается в этом месяце: ключи с ценой, у которых срок кончается в этом месяце (по Ташкенту), — по их цене. */
export function expectedThisMonth(licenses: LicenseRow[], terms: KeyTermsRow[], now: Date) {
  const month = monthOf(now);
  const priced = new Map(terms.filter((x) => x.days).map((x) => [x.serial, x]));
  const total = zero();
  let keys = 0;
  for (const l of licenses) {
    const x = priced.get(l.serial);
    if (!x || l.status === "blocked" || !l.expiresAt || monthOf(l.expiresAt) !== month) continue;
    add(total, x.currency, x.price);
    keys++;
  }
  return { total, keys };
}

/** Договоры для «Денег»: действующие сверху; у каждого — сколько ключей по нему и сколько это в месяц. */
export function contractsSummary(contracts: ContractRow[], terms: KeyTermsRow[], licenses: LicenseRow[], now: Date) {
  const live = new Map(licenses.filter((l) => l.status !== "blocked").map((l) => [l.serial, l]));
  return contracts
    .slice()
    .sort((a, b) => Number(a.status === "closed") - Number(b.status === "closed") || b.createdAt.getTime() - a.createdAt.getTime())
    .map((c) => {
      const mine = terms.filter((x) => x.contractId === c.id && live.has(x.serial));
      const perMonth = zero();
      for (const x of mine) if (x.days) add(perMonth, x.currency, (x.price / x.days) * 30);
      perMonth.UZS = Math.round(perMonth.UZS);
      const client = mine.map((x) => live.get(x.serial)!.client).find((cl) => nameOf(cl)) ?? EMPTY;
      return {
        id: c.id,
        number: c.number,
        party: c.party || nameOf(client),
        clientId: clientId(c.clientKey),
        status: c.status,
        currency: c.currency,
        validUntil: c.validUntil,
        ended: !!c.validUntil && c.validUntil < dayOf(now),
        keys: mine.length,
        perMonth,
      };
    });
}

// ---------- Тексты клиенту ----------

const hello = (c: ClientInfo) => (c.firstName.trim() ? `${c.firstName.trim()}, здравствуйте!` : "Здравствуйте!");
const priceLine = (name: string, price: number, currency: "UZS" | "USD") => `• ${name} — ${formatSum(price, currency)}`;

/**
 * «Предложить продлить»: готовый текст клиенту с датой конца и ценой. Сначала — цена ключа (закреплена за ключом), к ней
 * другие сроки того же договора; цены у ключа нет — тариф последней оплаты, если он ещё включён, иначе все общие
 * тарифы. tariff — какой тариф предложен первым (null — список).
 */
export function renewalOffer(l: LicenseRow, terms: KeyTermsRow | null, payments: PaymentRow[], tariffs: TariffRow[], now: Date): { text: string; tariff: TariffRow | null } {
  const active = sortTariffs(tariffs.filter((x) => x.active && x.kind === "key"));
  const lines = [hello(l.client)];
  if (!l.expiresAt) lines.push(`Ваш ключ Strassio ${l.serial} — бессрочный.`);
  else if (l.expiresAt.getTime() <= now.getTime()) lines.push(`Срок вашего ключа Strassio ${l.serial} закончился ${dateRu(l.expiresAt)}.`);
  else lines.push(`Срок вашего ключа Strassio ${l.serial} заканчивается ${dateRu(l.expiresAt)}.`);

  let list: string[];
  let own: TariffRow | null = null;
  if (terms && terms.days) {
    const others = terms.contractId ? active.filter((x) => x.contractId === terms.contractId && x.days !== terms.days && x.days !== null) : [];
    own = active.find((x) => x.id === terms.tariffId) ?? null;
    list = [priceLine(terms.name || `${terms.days} дн.`, terms.price, terms.currency), ...others.map((x) => priceLine(x.name, x.price, x.currency))];
  } else {
    const general = active.filter((x) => x.contractId === null);
    const last = payments.filter((p) => p.serial === l.serial && p.tariff).sort((a, b) => b.createdAt.getTime() - a.createdAt.getTime())[0];
    own = last ? general.find((x) => x.name === last.tariff) ?? null : null;
    list = (own ? [own] : general).map((x) => priceLine(x.name, x.price, x.currency));
  }
  if (list.length) lines.push("", "Продление:", ...list);
  lines.push("", "Напишите, какой вариант удобен, — продлю ключ сразу после оплаты.");
  return { text: lines.join("\n"), tariff: own };
}

/** Заявка без ключа: готовое сообщение с ценами общих тарифов (пробный плагин телефона не спрашивает — пишем по заявке). */
export function requestOffer(r: RequestRow, tariffs: TariffRow[]): string {
  const list = sortTariffs(tariffs.filter((x) => x.active && x.kind === "key" && x.contractId === null));
  const lines = [hello(r.client), "", "Вы оставляли заявку на Strassio — плагин страз для CorelDRAW."];
  if (list.length) lines.push("", "Цены:", ...list.map((x) => priceLine(x.name, x.price, x.currency) + (x.label ? ` (${x.label})` : "")));
  lines.push("", "Напишите, какой вариант удобен, — пришлю ключ сразу после оплаты.");
  return lines.join("\n");
}

/** «Напомнить об оплате»: по каждому долгу — ключ, за что, сколько осталось и срок; несколько долгов — ещё и итог. */
export function debtReminder(client: ClientInfo, debts: Array<Pick<PaymentView, "serial" | "tariff" | "left" | "currency" | "dueOn" | "kind">>, now: Date): string {
  const lines = [hello(client), "", "Напоминаю об оплате Strassio:"];
  const total = zero();
  for (const p of debts) {
    add(total, p.currency, p.left);
    const what = p.kind === "updates" ? "обновления" : p.tariff || (p.kind === "renewal" ? "продление" : "ключ");
    lines.push(`• ${p.serial} — ${what}: ${formatSum(p.left, p.currency)}${p.dueOn ? `, до ${dayRu(p.dueOn)}` : ""}`);
  }
  if (debts.length > 1) lines.push(`Всего: ${sumsText(total)}`);
  const late = debts.some((p) => p.dueOn && p.dueOn < dayOf(now));
  lines.push("", late ? "Срок оплаты уже прошёл — пожалуйста, оплатите, когда будет удобно." : "Спасибо!");
  return lines.join("\n");
}

// ---------- Telegram ----------

export interface WeeklyInput {
  now: Date;
  payments: PaymentRow[];
  parts: PaymentPartRow[];
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
  const income = [0, 1].map((w) => d.parts.filter((p) => inWeek(p.paidAt, w)).reduce((s, p) => add(s, p.currency, p.amount), zero()));
  const requests = [0, 1].map((w) => d.requests.filter((r) => inWeek(r.createdAt, w)).length);
  const by = partsByPayment(d.parts);
  const debtRows = d.payments.map((p) => ({ p, left: leftOf(p, by.get(p.id) ?? []) })).filter((x) => x.left > 0);
  const debts = debtRows.reduce((s, x) => add(s, x.p.currency, x.left), zero());
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
    lines.push(`Долги: ${sumsText(debts)} · начислений: ${debtRows.length}`);
    for (const { p, left } of debtRows.slice(0, 10)) {
      const c = d.licenses.find((l) => l.serial === p.serial)?.client ?? EMPTY;
      lines.push(`• ${esc(nameOf(c) || p.serial)}${c.phone ? ", " + esc(c.phone) : ""} — ${formatSum(left, p.currency)}`);
    }
    if (debtRows.length > 10) lines.push(`…и ещё ${debtRows.length - 10}`);
  }
  lines.push("", `<a href="${d.hubUrl}/admin/strassio/money">Открыть «Деньги»</a>`);
  return lines.join("\n");
}

/** Напомнить клиенту о продлении — за столько дней до конца ключа (владелец 2026-11-08). */
export const REMIND_CLIENT_DAYS = 3;
/** Долг блокирует ключ через столько дней после срока (billing.ts, BLOCK_AFTER_DAYS). */
const BLOCK_DAYS = 5;

/**
 * Деньги в утренней сводке: ключи, заблокированные за долг этой ночью, поступления за сутки, долги со сроком сегодня и
 * просроченные (с днём блокировки), кому напомнить о продлении (через 3 дня конец ключа), сколько ждать в этом месяце по
 * ценам ключей (1-го числа). Нечего сказать — [].
 */
export function dailyMoneyLines(d: { now: Date; payments: PaymentRow[]; parts: PaymentPartRow[]; licenses: LicenseRow[]; terms: KeyTermsRow[]; requests?: RequestRow[]; blocked?: Array<{ serial: string; left: number; currency: "UZS" | "USD" }>; hubUrl?: string }): string[] {
  const t = d.now.getTime();
  const today = dayOf(d.now);
  const lines: string[] = [];
  const who = (serial: string) => {
    const c = d.licenses.find((l) => l.serial === serial)?.client ?? EMPTY;
    return esc(nameOf(c) || serial) + (c.phone ? ", " + esc(c.phone) : "");
  };
  if (d.blocked?.length) lines.push("🔒 <b>Заблокированы за долг</b>", ...d.blocked.map((x) => `• ${who(x.serial)} — ${formatSum(x.left, x.currency)}`), "");
  const paid = d.parts.filter((p) => p.paidAt.getTime() > t - DAY && p.paidAt.getTime() <= t);
  if (paid.length) lines.push(`💰 Оплаты за сутки: ${paid.length} · ${sumsText(paid.reduce((s, p) => add(s, p.currency, p.amount), zero()))}`);
  const by = partsByPayment(d.parts);
  const debts = d.payments.map((p) => ({ p, left: leftOf(p, by.get(p.id) ?? []) })).filter((x) => x.left > 0 && x.p.dueOn && x.p.dueOn <= today);
  if (debts.length) {
    const due = debts.filter((x) => x.p.dueOn === today);
    const late = debts.filter((x) => x.p.dueOn < today);
    const blockOn = (day: string) => dayRu(new Date(Date.parse(day + "T00:00:00Z") + BLOCK_DAYS * DAY).toISOString().slice(0, 10));
    if (due.length) lines.push("", "💸 <b>Сегодня срок оплаты</b>", ...due.slice(0, 10).map((x) => `• ${who(x.p.serial)} — ${formatSum(x.left, x.p.currency)}`));
    if (late.length) lines.push("", `⚠️ <b>Просрочено: ${late.length}</b> · ${sumsText(late.reduce((s, x) => add(s, x.p.currency, x.left), zero()))}`, ...late.slice(0, 10).map((x) => `• ${who(x.p.serial)} — ${formatSum(x.left, x.p.currency)}, срок ${dayRu(x.p.dueOn)}, блокировка ${blockOn(x.p.dueOn)}`));
  }
  const remindDay = dayOf(new Date(t + REMIND_CLIENT_DAYS * DAY));
  const remind = d.licenses.filter((l) => l.status !== "blocked" && l.expiresAt && l.expiresAt.getTime() > t && dayOf(l.expiresAt) === remindDay);
  if (remind.length) {
    const link = (serial: string) => (d.hubUrl ? ` · <a href="${d.hubUrl}/admin/strassio/k/${encodeURIComponent(serial)}">сообщение</a>` : "");
    lines.push("", `📨 <b>Напомнить о продлении</b> (ключ кончается через ${REMIND_CLIENT_DAYS} дня)`, ...remind.slice(0, 15).map((l) => `• ${who(l.serial)}${link(l.serial)}`));
  }
  // Заявка без ключа уже 2 дня — напомнить (один раз: заявки, которым от 2 до 3 дней).
  const stale = (d.requests ?? []).filter((r) => !r.serial && (r.status === "new" || r.status === "working") && r.createdAt.getTime() <= t - 2 * DAY && r.createdAt.getTime() > t - 3 * DAY);
  if (stale.length) {
    const open = d.hubUrl ? ` · <a href="${d.hubUrl}/admin/strassio/requests">сообщение</a>` : "";
    lines.push("", `📝 <b>Заявки без ключа 2 дня</b>${open}`, ...stale.slice(0, 15).map((r) => `• ${esc(nameOf(r.client) || "Без имени")}${r.client.phone ? ", " + esc(r.client.phone) : ""}`));
  }
  const expected = expectedThisMonth(d.licenses, d.terms, d.now);
  if (expected.keys && dayOf(d.now).endsWith("-01")) lines.push("", `📅 Ждём в этом месяце по ценам ключей: ${sumsText(expected.total)} · ключей: ${expected.keys}`);
  return lines;
}

/** Понедельник по Ташкенту — день итога недели. */
export const isMonday = (now: Date) => new Date(now.getTime() + TASHKENT).getUTCDay() === 1;
