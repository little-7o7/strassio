// Разбор денежных форм хаба (/api/platform): тариф, начисление, поступление, договор, цена ключа, файл договора.
// Всё, что не прошло проверку, — null (маршрут отвечает 400). Правила и запись — billing.ts.
import type { Billing, ChargeInput, ContractInput, PartInput, TariffInput, TermsInput } from "./billing.js";
import { parseDiscount } from "./money.js";
import { saneRate } from "./rates.js";
import { CURRENCIES } from "./sums.js";
import type { Currency, KeyTermsRow, PayMethod, TariffKind, TariffRow } from "./store.js";

type Body = Record<string, unknown>;
const round2 = (n: number) => Math.round(n * 100) / 100;

export const given = (v: unknown) => v !== undefined && v !== null && v !== "";
export const isCurrency = (v: unknown): v is Currency => CURRENCIES.includes(v as Currency);
export const PAID_METHODS = ["cash", "card", "transfer"] as const;
export type PaidMethod = (typeof PAID_METHODS)[number];
export const isPaidMethod = (v: unknown): v is PaidMethod => PAID_METHODS.includes(v as PaidMethod);
const text = (v: unknown, max: number) => (typeof v === "string" ? v.trim().slice(0, max) : typeof v === "number" ? String(v) : "");
const flag = (v: unknown) => v === true || v === "true" || v === "on" || v === "1";
const id = (v: unknown) => (given(v) && /^\d{1,9}$/.test(String(v)) ? Number(v) : null);

/** Сумма: до 12 цифр и 2 знаков после точки (или запятой), пробелы между тысячами можно; больше нуля (или 0, если zero). */
export function parseAmount(value: unknown, zero = false): number | null {
  if (typeof value !== "number" && typeof value !== "string") return null;
  const t = String(value).replace(/[\s ]/g, "").replace(",", ".");
  if (!/^\d{1,12}(\.\d{1,2})?$/.test(t)) return null;
  const n = Number(t);
  return n > 0 || (zero && n === 0) ? n : null;
}

/** Срок в днях: пусто — null (бессрочно), иначе целое 0…36500; undefined — неверно. */
export function daysFrom(value: unknown): number | null | undefined {
  if (!given(value)) return null;
  const n = Number(value);
  return Number.isInteger(n) && n >= 0 && n <= 36500 ? n : undefined;
}

/** День ГГГГ-ММ-ДД (настоящая дата) или ""; undefined — неверно. */
export function dayFrom(value: unknown): string | undefined {
  if (!given(value)) return "";
  const v = String(value).trim();
  const d = /^\d{4}-\d{2}-\d{2}$/.test(v) ? new Date(v + "T00:00:00Z") : null;
  return d && !Number.isNaN(d.getTime()) && d.toISOString().slice(0, 10) === v && Number(v.slice(0, 4)) >= 2000 ? v : undefined;
}

/** Общий тариф: срок пустой или 0 — навсегда; цена своя или от основного тарифа (base) со скидкой. */
export function tariffFrom(b: Body): TariffInput | null {
  const name = text(b.name, 60);
  const days = daysFrom(b.days);
  const kind: TariffKind | null = !given(b.kind) || b.kind === "key" ? "key" : b.kind === "updates" ? "updates" : null;
  const baseId = given(b.base) ? id(b.base) : null;
  const discount = text(b.discount, 20);
  const price = baseId === null ? parseAmount(b.price) : 0;
  const currency = given(b.currency) ? b.currency : "UZS";
  if (!name || days === undefined || !kind || price === null || !isCurrency(currency) || (given(b.base) && baseId === null) || parseDiscount(discount) === null) return null;
  if (baseId !== null && !days) return null;
  return { name, days: days || null, price, currency, active: b.active === undefined ? true : flag(b.active), kind, baseId, discount: baseId === null ? "" : discount, site: flag(b.site), label: text(b.label, 60) };
}

export interface ChargeForm {
  /** null — ни тарифа, ни суммы: оплаты нет. */
  charge: ChargeInput | null;
  /** Срок одного периода по тарифу или цене ключа (null — навсегда); undefined — срок не из тарифа. */
  termDays: number | null | undefined;
  periods: number;
}

/**
 * Начисление из формы: тариф (id) или «key» — по закреплённой цене ключа; периодов 1–36 (заплатил вперёд за 3 месяца);
 * сумма пустая — цена × периоды, своя сумма — со скидкой, 0 или «бесплатно» — бесплатно; способ cash, card, transfer или
 * debt (в долг); «заплатил сейчас» — часть суммы; срок оплаты долга; заметка. null — неверно.
 */
export async function chargeFrom(b: Body, o: { billing: Billing; terms: KeyTermsRow | null; noteField: "note" | "payNote"; kinds: TariffKind[] }): Promise<ChargeForm | null> {
  const sel = given(b.tariff) ? String(b.tariff) : "";
  let tariff: TariffRow | null = null;
  let terms: KeyTermsRow | null = null;
  if (sel === "key") {
    if (!o.terms) return null;
    terms = o.terms;
  } else if (sel) {
    const n = id(sel);
    tariff = n === null ? null : await o.billing.findTariff(n);
    if (!tariff || !tariff.active || !o.kinds.includes(tariff.kind)) return null;
  }
  const periods = given(b.periods) ? Number(b.periods) : 1;
  const method = given(b.method) ? b.method : "cash";
  if (!Number.isInteger(periods) || periods < 1 || periods > 36 || (given(b.currency) && !isCurrency(b.currency)) || (method !== "debt" && !isPaidMethod(method))) return null;

  const unit = tariff?.price ?? terms?.price ?? null;
  const priceCurrency: Currency = tariff?.currency ?? terms?.currency ?? (isCurrency(b.currency) ? b.currency : "UZS");
  let currency = priceCurrency;
  const listPrice = unit === null ? 0 : round2(unit * periods);
  let amount: number | null = unit === null ? null : listPrice;
  if (given(b.amount)) {
    amount = parseAmount(b.amount, true);
    if (amount === null) return null;
    if (isCurrency(b.currency)) currency = b.currency;
  }
  if (flag(b.free)) amount = 0;
  const termDays = tariff ? tariff.days : terms ? terms.days : undefined;
  const paidNow = given(b.paidNow) ? parseAmount(b.paidNow) : null;
  const dueOn = dayFrom(b.dueOn);
  if ((given(b.paidNow) && (paidNow === null || amount === null || paidNow > amount)) || dueOn === undefined) return null;
  if (amount === null) return { charge: null, termDays, periods };

  const same = currency === priceCurrency && unit !== null;
  const ownDays = daysFrom(b.days);
  return {
    termDays,
    periods,
    charge: {
      amount,
      currency,
      listPrice: same ? listPrice : 0,
      tariff: tariff?.name ?? terms?.name ?? "",
      tariffId: tariff?.id ?? terms?.tariffId ?? null,
      contractId: tariff?.contractId ?? terms?.contractId ?? null,
      // Цена одного периода — цена тарифа или ключа в её валюте, даже если заплатили другой валютой или со скидкой.
      unitPrice: unit ?? amount,
      unitCurrency: unit !== null ? priceCurrency : currency,
      days: termDays !== undefined ? termDays : ownDays === undefined ? null : ownDays || null,
      method: method === "debt" ? "" : (method as PayMethod),
      paidNow,
      dueOn,
      note: text(b[o.noteField], 200),
      manual: given(b.amount) || flag(b.free),
    },
  };
}

/**
 * Поступление: способ обязателен; сумма пустая — весь остаток; другая валюта — по курсу дня (сумов за доллар) или
 * сколько засчитать в валюте долга.
 */
export function partFrom(b: Body): PartInput | null {
  if (!isPaidMethod(b.method) || (given(b.currency) && !isCurrency(b.currency))) return null;
  const [amount, rate, credit] = (["amount", "rate", "credit"] as const).map((k) => (given(b[k]) ? parseAmount(b[k]) : null));
  if ((["amount", "rate", "credit"] as const).some((k, i) => given(b[k]) && [amount, rate, credit][i] === null)) return null;
  // Свой курс с опечаткой («120» вместо «12 000») не принимаем: тот же разумный предел, что у курса ЦБ.
  if (rate !== null && !saneRate(rate)) return null;
  return { amount, currency: isCurrency(b.currency) ? b.currency : null, rate, credit, method: b.method, note: text(b.note, 200) };
}

/**
 * Договор: реквизиты, даты, день оплаты, валюта и цены: месяц, скидки в процентах за 3, 6 и 12 месяцев (пусто — такого
 * срока нет, 0 — без скидки), навсегда, обновления на год; «перевести ключи на новые цены».
 */
export function contractFrom(b: Body): ContractInput | null {
  const days = ["signedOn", "validFrom", "validUntil"].map((k) => dayFrom(b[k]));
  const payDay = given(b.payDay) ? Number(b.payDay) : null;
  const currency = given(b.currency) ? b.currency : "UZS";
  const price = (k: string) => (given(b[k]) ? parseAmount(b[k]) : null);
  const prices = { month: price("month"), forever: price("forever"), updates: price("updates") };
  const discount = (k: string) => (given(b[k]) ? text(b[k], 10) : null);
  const discounts = { discount3: discount("discount3"), discount6: discount("discount6"), discount12: discount("discount12") };
  const badPrice = (["month", "forever", "updates"] as const).some((k) => given(b[k]) && prices[k] === null);
  const badDiscount = Object.values(discounts).some((d) => d !== null && (prices.month === null || parseDiscount(d) === null));
  if (days.includes(undefined) || badPrice || badDiscount || !isCurrency(currency)) return null;
  if (payDay !== null && (!Number.isInteger(payDay) || payDay < 1 || payDay > 31)) return null;
  const [signedOn, validFrom, validUntil] = days as string[];
  if (validFrom && validUntil && validUntil < validFrom) return null;
  return {
    number: text(b.number, 40),
    signedOn,
    validFrom,
    validUntil,
    party: text(b.party, 200),
    inn: text(b.inn, 20),
    address: text(b.address, 300),
    terms: text(b.terms, 300),
    payDay,
    currency,
    note: text(b.note, 500),
    prices: { ...prices, ...discounts },
    moveKeys: flag(b.moveKeys),
  };
}

/** Цена ключа: тариф (id, можно и выключенный — например, старый договор) или своя: название, срок, цена, валюта. */
export async function termsFrom(b: Body, billing: Billing): Promise<TermsInput | null> {
  if (given(b.tariff)) {
    const n = id(b.tariff);
    const t = n === null ? null : await billing.findTariff(n);
    return t && t.kind === "key" ? { contractId: t.contractId, tariffId: t.id, name: t.name, days: t.days, price: t.price, currency: t.currency } : null;
  }
  const price = parseAmount(b.price);
  const days = daysFrom(b.days);
  const currency = given(b.currency) ? b.currency : "UZS";
  if (price === null || days === undefined || !isCurrency(currency)) return null;
  return { contractId: null, tariffId: null, name: text(b.name, 60) || (days ? `${days} дн.` : "Навсегда"), days: days || null, price, currency };
}

export const MAX_FILE_BYTES = 3 * 1024 * 1024;
const FILE_TYPES: Record<string, (b: Buffer) => boolean> = {
  "application/pdf": (b) => b.subarray(0, 5).toString("latin1") === "%PDF-",
  "image/jpeg": (b) => b[0] === 0xff && b[1] === 0xd8 && b[2] === 0xff,
  "image/png": (b) => b.subarray(0, 8).equals(Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a])),
  "image/webp": (b) => b.subarray(0, 4).toString("latin1") === "RIFF" && b.subarray(8, 12).toString("latin1") === "WEBP",
};

/** Файл договора: PDF, JPEG, PNG или WebP до 3 МБ, base64; содержимое должно совпадать с типом. null — неверно. */
export function fileFrom(b: Body): { name: string; type: string; data: string; size: number } | null {
  const type = typeof b.type === "string" ? b.type : "";
  const raw = typeof b.data === "string" ? b.data.replace(/^data:[^,]*,/, "").replace(/\s+/g, "") : "";
  if (!Object.hasOwn(FILE_TYPES, type) || !raw || !/^[A-Za-z0-9+/]+={0,2}$/.test(raw) || raw.length > Math.ceil((MAX_FILE_BYTES * 4) / 3) + 4) return null;
  const bytes = Buffer.from(raw, "base64");
  if (!bytes.length || bytes.length > MAX_FILE_BYTES || !FILE_TYPES[type](bytes)) return null;
  // Имя — без пути и управляющих символов: только для показа и скачивания.
  const name = text(b.name, 120).replace(/^.*[\\/]/, "").replace(/[\u0000-\u001f"<>]/g, "") || "договор";
  return { name, type, data: bytes.toString("base64"), size: bytes.length };
}
