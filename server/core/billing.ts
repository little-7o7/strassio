// Деньги Strassio (хаб little_7o7, владелец 2026-11-08): тарифы, договоры клиентов, закреплённая цена ключа, начисления и
// поступления частями. Входные данные уже проверены (moneyInput.ts); здесь — правила и журнал. Отчёты — money.ts.
import { dayOf, derivedPrice, leftOf, nextPayDay, paymentView, sortTariffs, type PaymentView } from "./money.js";
import { saneRate, type RateSource } from "./rates.js";
import { formatSum, METHOD_LABEL } from "./sums.js";
import type { ContractFileRow, ContractRow, Currency, KeyTermsRow, PayMethod, PaymentKind, PaymentRow, Store, TariffKind, TariffRow } from "./store.js";

const DAY = 24 * 60 * 60 * 1000;
/** Ключ «навсегда»: 3 месяца обновлений с даты ключа, дальше обновления платные (владелец 2026-11-08). */
export const INCLUDED_UPDATES_DAYS = 90;
/** «Обновления на год» — столько дней. */
export const UPDATES_DAYS = 365;
/** Долг не оплачен столько дней после срока — ключ блокируется сам; оплатили — разблокируется (владелец 2026-11-08). */
export const BLOCK_AFTER_DAYS = 5;
/** Файлов у одного договора — не больше. */
export const MAX_CONTRACT_FILES = 10;
/** Сколько не спрашивать источники курса после того, как все промолчали. */
const RATES_RETRY_MS = 15 * 60 * 1000;
const round2 = (n: number) => Math.round(n * 100) / 100;

export interface TariffInput {
  name: string;
  days: number | null;
  price: number;
  currency: Currency;
  active: boolean;
  kind: TariffKind;
  baseId: number | null;
  discount: string;
  site: boolean;
  label: string;
}

/** Начисление из формы хаба. amount и listPrice — за все периоды; unitPrice и days — цена и срок одного периода. */
export interface ChargeInput {
  amount: number;
  currency: Currency;
  listPrice: number;
  tariff: string;
  tariffId: number | null;
  contractId: number | null;
  unitPrice: number;
  days: number | null;
  /** "" — в долг. */
  method: PayMethod;
  /** Сколько заплатили сейчас; null — всё. */
  paidNow: number | null;
  dueOn: string;
  note: string;
  kind?: PaymentKind;
  /** Закрепить цену за ключом (выдача ключа или ключ без цены). */
  lockTerms?: boolean;
  /** Сумму ввели руками (своя скидка) — скидка за количество не добавляется. */
  manual?: boolean;
}

/** Скидка за количество: у клиента minKeys ключей и больше — percent от цены (0 — выключена). */
export interface VolumeDiscount {
  minKeys: number;
  percent: number;
}

export interface PartInput {
  /** null — весь остаток (в валюте начисления). */
  amount: number | null;
  /** null — валюта начисления. */
  currency: Currency | null;
  /** Курс дня: сумов за доллар, когда заплатили другой валютой (тарифы в $, платят и сумами). */
  rate: number | null;
  /** Засчитать в валюте начисления вместо курса; ни курса, ни credit — весь остаток. */
  credit: number | null;
  method: Exclude<PayMethod, "">;
  note: string;
}

export interface ContractPrices {
  month: number | null;
  /** Скидки в процентах от цены месяца за 3, 6 и 12 месяцев; null — такого срока в договоре нет. */
  discount3: string | null;
  discount6: string | null;
  discount12: string | null;
  forever: number | null;
  /** Обновления на год для ключей «навсегда». */
  updates: number | null;
}

export interface ContractInput {
  number: string;
  signedOn: string;
  validFrom: string;
  validUntil: string;
  party: string;
  inn: string;
  address: string;
  terms: string;
  payDay: number | null;
  currency: Currency;
  note: string;
  prices: ContractPrices;
  /** Перевести ключи клиента на новые цены договора (иначе старые ключи — по старой цене). */
  moveKeys: boolean;
}

export interface TermsInput {
  contractId: number | null;
  tariffId: number | null;
  name: string;
  days: number | null;
  price: number;
  currency: Currency;
}

/** Строки цены договора: месяц, 3, 6 и 12 месяцев от цены месяца со скидкой, навсегда, обновления на год. */
export const CONTRACT_LINES = { month: "Месяц", m3: "3 месяца", m6: "6 месяцев", year: "Год", forever: "Навсегда", updates: "Обновления на год" } as const;
const PERIODS = [
  { name: CONTRACT_LINES.m3, days: 90, key: "discount3" },
  { name: CONTRACT_LINES.m6, days: 180, key: "discount6" },
  { name: CONTRACT_LINES.year, days: 365, key: "discount12" },
] as const;

export class Billing {
  constructor(
    private readonly store: Store,
    private readonly clock: () => Date,
    private readonly actor: string,
  ) {}

  private rates: RateSource | null = null;

  /** Откуда брать курс ЦБ (cbuRate на сервере); без него — последний сохранённый или ручной. */
  useRates(source: RateSource | null): this {
    this.rates = source;
    return this;
  }

  private log(action: string, serial: string, details: string, actor = this.actor) {
    return this.store.audit({ at: this.clock(), actor, action, serial, details: details.slice(0, 300) });
  }

  /** Когда источники курса молчали в последний раз: 15 минут их не спрашиваем, чтобы оплаты не ждали по 4 секунды. */
  private ratesFailedAt = -Infinity;

  /** Курс ЦБ на сегодня (по Ташкенту): сохранённый (или присланный хабом), иначе от источников (и сохранить), иначе
   *  последний известный, иначе 0. */
  async rateToday(): Promise<{ day: string; rate: number; fresh: boolean }> {
    const now = this.clock();
    const day = dayOf(now);
    const stored = await this.store.rateFor(day);
    if (stored) return { day, rate: stored, fresh: true };
    const ask = this.rates && now.getTime() - this.ratesFailedAt >= RATES_RETRY_MS;
    const fetched = ask ? await this.rates!(day).catch(() => null) : null;
    if (fetched) {
      await this.store.saveRate(day, fetched);
      return { day, rate: fetched, fresh: true };
    }
    if (ask) this.ratesFailedAt = now.getTime();
    return { day, rate: (await this.store.latestRate()) ?? 0, fresh: false };
  }

  /** Курс дня, присланный хабом (хаб — общий источник курса для всех проектов). Только сегодня, вчера или завтра
   *  (ЦБ с вечера ставит курс на завтра), и не дальше 20% от последнего известного — опечатку не принимаем. */
  async acceptRate(day: string, usd: number): Promise<"ok" | "bad_day" | "bad_rate"> {
    const now = this.clock().getTime();
    const at = Date.parse(`${day}T12:00:00+05:00`);
    if (!/^\d{4}-\d{2}-\d{2}$/.test(day) || Number.isNaN(at) || dayOf(new Date(at)) !== day || Math.abs(at - now) > 2 * DAY) return "bad_day";
    const rate = saneRate(usd);
    const last = await this.store.latestRate();
    if (!rate || (last && Math.abs(rate / last - 1) > 0.2)) return "bad_rate";
    const before = await this.store.rateFor(day);
    await this.store.saveRate(day, rate);
    if (before !== rate) await this.log("rate", "", `Курс ЦБ на ${day}: ${formatSum(rate, "UZS")} за $1 (прислал хаб)`, "system");
    return "ok";
  }

  async volume(): Promise<VolumeDiscount> {
    const v = JSON.parse((await this.store.getSetting("volume")) ?? "{}") as Partial<VolumeDiscount>;
    return { minKeys: Number(v.minKeys) || 0, percent: Number(v.percent) || 0 };
  }

  async saveVolume(v: VolumeDiscount): Promise<VolumeDiscount> {
    await this.store.setSetting("volume", JSON.stringify(v));
    await this.log("money_settings", "", v.percent ? `скидка ${v.percent}% от ${v.minKeys} ключей` : "скидка за количество выключена");
    return v;
  }

  /** Скидка за количество к начислению по цене (не к своей сумме): у клиента keys ключей — цена × (1 − %). */
  async withVolume(c: ChargeInput, keys: number): Promise<ChargeInput> {
    const v = await this.volume();
    if (c.manual || !v.percent || !v.minKeys || keys < v.minKeys || !c.listPrice || c.amount !== c.listPrice) return c;
    const amount = c.currency === "UZS" ? Math.round(c.listPrice * (1 - v.percent / 100)) : round2(c.listPrice * (1 - v.percent / 100));
    const note = [`скидка за количество ${v.percent}%`, c.note].filter(Boolean).join(", ");
    return { ...c, amount, paidNow: c.paidNow === null ? null : Math.min(c.paidNow, amount), note };
  }

  // ---------- Тарифы ----------

  /** Тарифы: включённые сверху, дальше по сроку. */
  async tariffs(): Promise<TariffRow[]> {
    return sortTariffs(await this.store.listTariffs());
  }

  findTariff(id: number): Promise<TariffRow | null> {
    return Number.isInteger(id) && id > 0 ? this.store.findTariff(id) : Promise.resolve(null);
  }

  /**
   * Новый общий тариф (id null) или правка общего. Цена от основного тарифа (baseId) считается сама: основной — общий,
   * ключевой, со сроком и сам не считается от другого. «missing» — нет такого общего тарифа, «bad_base» — основной не
   * подходит или скидка съедает цену.
   */
  async saveTariff(id: number | null, t: TariffInput): Promise<TariffRow | "missing" | "bad_base"> {
    const old = id === null ? null : await this.findTariff(id);
    if (id !== null && (!old || old.contractId !== null)) return "missing";
    let price = t.price;
    let currency = t.currency;
    if (t.baseId !== null) {
      const base = await this.findTariff(t.baseId);
      if (!base || base.id === id || base.baseId !== null || base.contractId !== null || base.kind !== "key") return "bad_base";
      const derived = derivedPrice(base, t.days, t.discount);
      if (derived === null) return "bad_base";
      price = derived;
      currency = base.currency;
    }
    const fields = { ...t, price, currency, contractId: null, site: t.site && t.kind === "key" };
    const row = old ? { ...old, ...fields } : await this.store.insertTariff({ ...fields, createdAt: this.clock() });
    if (old) await this.store.updateTariff(row);
    await this.recompute(row);
    await this.log("tariff", "", `${row.name}: ${formatSum(row.price, row.currency)}, ${row.days ? row.days + " дн." : "навсегда"}${row.discount ? ", скидка " + row.discount : ""}${row.active ? "" : ", выключен"}`);
    return row;
  }

  /** Тарифы, которые считаются от base, — заново по его новой цене. */
  private async recompute(base: TariffRow) {
    for (const t of await this.store.listTariffs()) {
      if (t.baseId !== base.id) continue;
      const price = derivedPrice(base, t.days, t.discount);
      if (price === null || (price === t.price && t.currency === base.currency)) continue;
      await this.store.updateTariff({ ...t, price, currency: base.currency });
    }
  }

  // ---------- Начисления и поступления ----------

  /**
   * Записать начисление: оплачено сразу (целиком или частью), в долг или бесплатно (сумма 0). Долг по договору без срока
   * получает ближайший день оплаты из договора. lockTerms — закрепить цену за ключом.
   */
  async charge(serial: string, c: ChargeInput): Promise<PaymentRow> {
    const now = this.clock();
    const paidNow = c.amount === 0 || c.method === "" ? 0 : Math.min(c.paidNow ?? c.amount, c.amount);
    const closed = paidNow >= c.amount;
    let dueOn = closed ? "" : c.dueOn;
    if (!closed && !dueOn && c.contractId) {
      const contract = await this.store.findContract(c.contractId);
      if (contract?.payDay) dueOn = nextPayDay(now, contract.payDay);
    }
    const row = await this.store.insertPayment({ serial, kind: c.kind ?? "sale", amount: c.amount, currency: c.currency, listPrice: c.listPrice, tariff: c.tariff, contractId: c.contractId, note: c.note, dueOn, createdAt: now, paidAt: closed ? now : null });
    const method = c.method || "cash";
    if (paidNow > 0) await this.store.insertPart({ paymentId: row.id, amount: paidNow, currency: c.currency, rate: (await this.rateToday()).rate, credit: paidNow, method, note: "", paidAt: now });
    // Своя сумма без тарифа — разовая поправка, за ключом не закрепляется.
    if (c.lockTerms && (c.tariffId !== null || c.tariff)) await this.lockTerms(serial, c, now);
    await this.log("payment", serial, chargeText(row, paidNow, c.method));
    return row;
  }

  private async lockTerms(serial: string, c: ChargeInput, now: Date) {
    const old = await this.store.keyTerms(serial);
    const forever = c.days === null && (c.kind ?? "sale") !== "updates";
    await this.store.saveKeyTerms({
      serial,
      contractId: c.contractId,
      tariffId: c.tariffId,
      name: c.tariff,
      days: c.days,
      price: c.unitPrice,
      currency: c.currency,
      updatesUntil: old?.updatesUntil ?? (forever ? new Date(now.getTime() + INCLUDED_UPDATES_DAYS * DAY) : null),
      since: now,
    });
  }

  /**
   * Пришли деньги по начислению (можно частью). Другой валютой — засчитывается credit в валюте начисления.
   * «missing», «already» (закрыто), «too_much» (больше остатка).
   */
  async pay(id: number, input: PartInput): Promise<PaymentView | "missing" | "already" | "too_much"> {
    const row = Number.isInteger(id) && id > 0 ? await this.store.findPayment(id) : null;
    if (!row) return "missing";
    if (row.paidAt) return "already";
    const parts = await this.store.partsOf([row.id]);
    const left = leftOf(row, parts);
    const currency = input.currency ?? row.currency;
    const same = currency === row.currency;
    // Курс — ручной (договорённый) или ЦБ на сегодня; он же нужен итогам «всё в сумах / всё в $».
    const rate = input.rate ?? (await this.rateToday()).rate;
    // Без суммы другой валютой — весь остаток по курсу.
    const amount = input.amount ?? (same ? left : rate ? convert(left, row.currency, rate) : null);
    if (amount === null) return "too_much";
    const credit = same ? amount : input.credit ?? (rate ? convert(amount, currency, rate) : left);
    if (credit > left + 0.001 || amount <= 0) return "too_much";
    const now = this.clock();
    const part = await this.store.insertPart({ paymentId: row.id, amount, currency, rate, credit: Math.min(round2(credit), left), method: input.method, note: input.note, paidAt: now });
    if (round2(left - part.credit) <= 0) {
      row.paidAt = now;
      row.dueOn = "";
      await this.store.updatePayment(row);
    }
    await this.log("paid", row.serial, [formatSum(amount, currency), same ? "" : `засчитано ${formatSum(part.credit, row.currency)}${rate ? ` по курсу ${formatSum(rate, "UZS")}` : ""}`, METHOD_LABEL[input.method], row.paidAt ? "оплачено полностью" : `осталось ${formatSum(round2(left - part.credit), row.currency)}`, input.note].filter(Boolean).join(", "));
    if (row.paidAt) await this.releaseIfPaid(row.serial);
    return paymentView(row, [...parts, part]);
  }

  /** День блокировки за долг: срок оплаты (или день начисления) + 5 дней, ГГГГ-ММ-ДД. */
  static blockDay(p: PaymentRow): string {
    const start = p.dueOn || dayOf(p.createdAt);
    return new Date(Date.parse(start + "T00:00:00Z") + BLOCK_AFTER_DAYS * DAY).toISOString().slice(0, 10);
  }

  /**
   * Заблокировать ключи с долгом, у которых прошёл день блокировки. Ключ, который владелец разблокировал руками после
   * этого дня, не трогается (он дал отсрочку). Возвращает заблокированные: ключ и остаток долга.
   */
  async autoBlock(): Promise<Array<{ serial: string; left: number; currency: Currency }>> {
    const now = this.clock();
    const today = dayOf(now);
    const payments = await this.store.listPayments(20000);
    const parts = await this.store.partsOf(payments.filter((p) => !p.paidAt).map((p) => p.id));
    const out: Array<{ serial: string; left: number; currency: Currency }> = [];
    for (const p of payments) {
      const left = leftOf(p, parts.filter((x) => x.paymentId === p.id));
      const day = Billing.blockDay(p);
      if (left <= 0 || today < day || out.some((x) => x.serial === p.serial)) continue;
      const license = await this.store.findLicense(p.serial);
      if (!license || license.status === "blocked") continue;
      const log = await this.store.auditSearch({ serial: p.serial, action: "unblock", from: new Date(Date.parse(day + "T00:00:00+05:00")) }, 1);
      if (log.length) continue;
      await this.store.updateLicense({ ...license, status: "blocked" });
      await this.log("auto_block", p.serial, `долг ${formatSum(left, p.currency)}, срок ${p.dueOn || dayOf(p.createdAt)}`, "system");
      out.push({ serial: p.serial, left, currency: p.currency });
    }
    return out;
  }

  /** Долги ключа оплачены, а заблокирован он был за долг (последняя блокировка — автоматическая) — разблокировать. */
  private async releaseIfPaid(serial: string) {
    const license = await this.store.findLicense(serial);
    if (!license || license.status !== "blocked") return;
    const payments = (await this.store.paymentsFor(serial)).filter((p) => !p.paidAt);
    const parts = await this.store.partsOf(payments.map((p) => p.id));
    if (payments.some((p) => leftOf(p, parts.filter((x) => x.paymentId === p.id)) > 0)) return;
    const last = (await this.store.auditSearch({ serial }, 200)).find((e) => e.action === "auto_block" || e.action === "block" || e.action === "unblock");
    if (last?.action !== "auto_block") return;
    await this.store.updateLicense({ ...license, status: "active" });
    await this.log("auto_unblock", serial, "долг оплачен", "system");
  }

  /** Удалить поступление (ошиблись суммой): начисление снова становится долгом, если не хватает. */
  async deletePart(id: number): Promise<boolean> {
    const part = Number.isInteger(id) && id > 0 ? await this.store.findPart(id) : null;
    const row = part ? await this.store.findPayment(part.paymentId) : null;
    if (!part || !row) return false;
    await this.store.deletePart(part.id);
    const rest = (await this.store.partsOf([row.id])).reduce((s, p) => s + p.credit, 0);
    if (row.paidAt && row.amount > 0 && round2(rest) < row.amount) {
      row.paidAt = null;
      await this.store.updatePayment(row);
    }
    await this.log("part_delete", row.serial, `${formatSum(part.amount, part.currency)}, ${METHOD_LABEL[part.method]}`);
    return true;
  }

  async deletePayment(id: number): Promise<boolean> {
    const row = Number.isInteger(id) && id > 0 ? await this.store.findPayment(id) : null;
    if (!row) return false;
    await this.store.deletePayment(row.id);
    await this.log("payment_delete", row.serial, chargeText(row, 0, "x"));
    return true;
  }

  // ---------- Цена ключа ----------

  /** Поменять цену ключа явно («Изменить цену ключа»): дата «обновления до» сохраняется. */
  async setTerms(serial: string, t: TermsInput): Promise<KeyTermsRow> {
    const old = await this.store.keyTerms(serial);
    const now = this.clock();
    const forever = t.days === null;
    const row: KeyTermsRow = { serial, ...t, updatesUntil: old?.updatesUntil ?? (forever ? new Date(now.getTime() + INCLUDED_UPDATES_DAYS * DAY) : null), since: now };
    await this.store.saveKeyTerms(row);
    await this.log("terms", serial, `${t.name || "своя цена"}: ${formatSum(t.price, t.currency)}${t.days ? ` за ${t.days} дн.` : ", навсегда"}`);
    return row;
  }

  /** Обновления ключа «навсегда» оплачены ещё на days: с конца прежних, а если они уже кончились — с сегодня. */
  async extendUpdates(serial: string, days: number): Promise<KeyTermsRow | null> {
    const old = await this.store.keyTerms(serial);
    if (!old) return null;
    const now = this.clock();
    const from = old.updatesUntil && old.updatesUntil > now ? old.updatesUntil : now;
    const row = { ...old, updatesUntil: new Date(from.getTime() + days * DAY) };
    await this.store.saveKeyTerms(row);
    return row;
  }

  // ---------- Договоры ----------

  /**
   * Новый договор клиента (id null) — прежний действующий закрывается и остаётся в истории; или правка договора.
   * Цены договора — строки тарифов этого договора: «Месяц», «Год» (месяц × 12 − скидка), «Навсегда», «Обновления на год»
   * и свой срок. Цены ключей, выданных раньше, не меняются.
   */
  async saveContract(id: number | null, clientKey: string, c: ContractInput): Promise<ContractRow | "missing"> {
    const { prices, moveKeys, ...fields } = c;
    let row: ContractRow;
    const all = await this.store.listContracts();
    if (id === null) {
      for (const old of all) if (old.clientKey === clientKey && old.status === "active") await this.close(old);
      row = await this.store.insertContract({ ...fields, clientKey, status: "active", closedAt: null, createdAt: this.clock() });
    } else {
      const old = all.find((x) => x.id === id);
      if (!old) return "missing";
      row = { ...old, ...fields };
      await this.store.updateContract(row);
    }
    // «Перевести ключи на новые цены» — ключи этого договора и прежних договоров того же клиента.
    const earlier = all.filter((x) => x.clientKey === row.clientKey && x.id !== row.id).map((x) => x.id);
    const lines = await this.syncLines(row, prices);
    const moved = moveKeys ? await this.moveKeys([row.id, ...earlier], lines) : 0;
    await this.log("contract", "", `№ ${row.number || row.id}${row.party ? ", " + row.party : ""}${id === null ? ", новый" : ", изменён"}${moved ? `, ключей на новых ценах: ${moved}` : ""}`);
    return row;
  }

  /** Ключи этих договоров — на цены lines (по названию строки: «Месяц» к «Месяцу»). Сколько ключей перевели. */
  private async moveKeys(contractIds: number[], lines: TariffRow[]): Promise<number> {
    let moved = 0;
    for (const t of await this.store.allKeyTerms()) {
      if (t.contractId === null || !contractIds.includes(t.contractId)) continue;
      const line = lines.find((l) => l.active && l.name === t.name);
      if (!line || (line.id === t.tariffId && line.price === t.price && line.currency === t.currency)) continue;
      await this.store.saveKeyTerms({ ...t, contractId: line.contractId, tariffId: line.id, days: line.days, price: line.price, currency: line.currency });
      moved++;
    }
    return moved;
  }

  private async syncLines(contract: ContractRow, p: ContractPrices): Promise<TariffRow[]> {
    const lines = (await this.store.listTariffs()).filter((t) => t.contractId === contract.id);
    const on = contract.status === "active";
    const upsert = async (name: string, spec: { days: number | null; price: number; kind?: TariffKind; baseId?: number | null; discount?: string } | null) => {
      const old = lines.find((l) => l.name === name);
      if (!spec) {
        if (old?.active) await this.store.updateTariff({ ...old, active: false });
        return old ?? null;
      }
      const next = { name, days: spec.days, price: spec.price, currency: contract.currency, active: on, contractId: contract.id, kind: spec.kind ?? "key", baseId: spec.baseId ?? null, discount: spec.discount ?? "", site: false, label: "" };
      if (old) {
        const row = { ...old, ...next };
        await this.store.updateTariff(row);
        return row;
      }
      return this.store.insertTariff({ ...next, createdAt: this.clock() });
    };
    const month = await upsert(CONTRACT_LINES.month, p.month === null ? null : { days: 30, price: p.month });
    for (const period of PERIODS) {
      const discount = p[period.key];
      const price = month && p.month !== null && discount !== null ? derivedPrice(month, period.days, discount) : null;
      await upsert(period.name, price === null ? null : { days: period.days, price, baseId: month!.id, discount: discount ?? "" });
    }
    await upsert(CONTRACT_LINES.forever, p.forever === null ? null : { days: null, price: p.forever });
    await upsert(CONTRACT_LINES.updates, p.updates === null ? null : { days: UPDATES_DAYS, price: p.updates, kind: "updates" });
    return (await this.store.listTariffs()).filter((t) => t.contractId === contract.id);
  }

  private async close(c: ContractRow) {
    await this.store.updateContract({ ...c, status: "closed", closedAt: this.clock() });
    for (const t of await this.store.listTariffs()) if (t.contractId === c.id && t.active) await this.store.updateTariff({ ...t, active: false });
  }

  /** Закрыть договор: его цены больше не предлагаются, ключи продлеваются по своей закреплённой цене. */
  async closeContract(id: number): Promise<ContractRow | null> {
    const c = await this.findContract(id);
    if (!c) return null;
    if (c.status === "active") await this.close(c);
    await this.log("contract_close", "", `№ ${c.number || c.id}`);
    return { ...c, status: "closed" };
  }

  async deleteContract(id: number): Promise<boolean> {
    const c = await this.findContract(id);
    if (!c) return false;
    await this.store.deleteContract(c.id);
    await this.log("contract_delete", "", `№ ${c.number || c.id}${c.party ? ", " + c.party : ""}`);
    return true;
  }

  findContract(id: number): Promise<ContractRow | null> {
    return Number.isInteger(id) && id > 0 ? this.store.findContract(id) : Promise.resolve(null);
  }

  contracts(): Promise<ContractRow[]> {
    return this.store.listContracts();
  }

  /** Файл договора; «missing» — нет договора, «too_many» — файлов уже 10. */
  async addFile(contractId: number, f: { name: string; type: string; data: string; size: number }): Promise<ContractFileRow | "missing" | "too_many"> {
    const c = await this.findContract(contractId);
    if (!c) return "missing";
    if ((await this.store.contractFiles(c.id)).length >= MAX_CONTRACT_FILES) return "too_many";
    const row = await this.store.insertContractFile({ contractId: c.id, ...f, createdAt: this.clock() });
    await this.log("contract_file", "", `№ ${c.number || c.id}: ${f.name}`);
    return row;
  }

  file(id: number): Promise<ContractFileRow | null> {
    return Number.isInteger(id) && id > 0 ? this.store.findContractFile(id) : Promise.resolve(null);
  }

  files(contractId: number): Promise<ContractFileRow[]> {
    return this.store.contractFiles(contractId);
  }

  async deleteFile(id: number): Promise<boolean> {
    const f = await this.file(id);
    if (!f) return false;
    await this.store.deleteContractFile(f.id);
    await this.log("contract_file_delete", "", f.name);
    return true;
  }
}

/** Перевести по курсу (сумов за доллар) из валюты from во вторую: сумы → доллары до цента, доллары → сумы до целого. */
function convert(amount: number, from: Currency, rate: number): number {
  return from === "UZS" ? round2(amount / rate) : Math.round(amount * rate);
}

/** «$50 из $60 (скидка), карта, Месяц, продление, заметка» — для журнала. */
function chargeText(p: PaymentRow, paidNow: number, method: PayMethod | "x"): string {
  const sum = p.amount === 0 ? "бесплатно" : formatSum(p.amount, p.currency) + (p.listPrice > p.amount ? ` (цена ${formatSum(p.listPrice, p.currency)})` : "");
  const how = method === "x" ? "" : method === "" ? "в долг" : paidNow < p.amount ? `${METHOD_LABEL[method]}, заплачено ${formatSum(paidNow, p.currency)}` : METHOD_LABEL[method];
  const kind = p.kind === "renewal" ? "продление" : p.kind === "updates" ? "обновления" : "";
  return [sum, p.amount === 0 ? "" : how, p.tariff, kind, p.dueOn ? "срок " + p.dueOn : "", p.note].filter(Boolean).join(", ");
}

