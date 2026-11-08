// Суммы в двух валютах (сумы и доллары) — отдельно, без пересчёта по курсу: так владелец видит, сколько чего пришло.
import type { Currency, PayMethod } from "./store.js";

export type Sums = Record<Currency, number>;

export const CURRENCIES: readonly Currency[] = ["UZS", "USD"];

export const zero = (): Sums => ({ UZS: 0, USD: 0 });

/** Прибавить с точностью до тийина/цента: 0.1 + 0.2 не превращается в 0.30000000000000004. */
export function add(s: Sums, currency: Currency, amount: number): Sums {
  s[currency] = Math.round((s[currency] + amount) * 100) / 100;
  return s;
}

const group = (n: number, sep: string) => String(n).replace(/\B(?=(\d{3})+(?!\d))/g, sep);

/** 1 200 000 сум, $50, $12.50, $1,200. Копейки — только если есть. */
export function formatSum(amount: number, currency: Currency): string {
  const cents = Math.round(Math.abs(amount) * 100);
  const whole = Math.floor(cents / 100);
  const frac = cents % 100 ? String(cents % 100).padStart(2, "0") : "";
  const sign = amount < 0 ? "-" : "";
  return currency === "USD" ? `${sign}$${group(whole, ",")}${frac ? "." + frac : ""}` : `${sign}${group(whole, " ")}${frac ? "," + frac : ""} сум`;
}

/** Обе валюты через запятую, нулевые не пишутся: «500 000 сум, $20»; всё по нулям — «0 сум». */
export function sumsText(s: Sums): string {
  return CURRENCIES.filter((c) => s[c]).map((c) => formatSum(s[c], c)).join(", ") || "0 сум";
}

/** Способ оплаты словом — для журнала. "" — в долг. */
export const METHOD_LABEL: Record<PayMethod, string> = { cash: "наличные", card: "карта", transfer: "перевод", "": "в долг" };
