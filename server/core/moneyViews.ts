// Деньги для карточек хаба: договор, его файлы, цена ключа, всё денежное о клиенте. Чистые функции.
import { debtReminder, paymentView, sortTariffs, tariffView } from "./money.js";
import type { ContractFileRow, ContractRow, KeyTermsRow, LicenseRow, PaymentPartRow, PaymentRow, TariffRow } from "./store.js";

const iso = (d: Date) => d.toISOString().replace(/\.\d{3}Z$/, "Z");

export function termsView(t: KeyTermsRow) {
  return { contractId: t.contractId, tariffId: t.tariffId, name: t.name, days: t.days, price: t.price, currency: t.currency, updatesUntil: t.updatesUntil ? iso(t.updatesUntil) : null, since: iso(t.since) };
}

/** Договор без clientKey (в нём телефон или почта клиента одной строкой — хабу это ни к чему). */
export function contractView(c: ContractRow) {
  const { clientKey: _key, closedAt, createdAt, ...rest } = c;
  return { ...rest, closedAt: closedAt ? iso(closedAt) : null, createdAt: iso(createdAt) };
}

export const fileView = (f: ContractFileRow) => ({ id: f.id, contractId: f.contractId, name: f.name, type: f.type, size: f.size, createdAt: iso(f.createdAt) });

export interface ClientMoneyInput {
  now: Date;
  key: string;
  /** Ключи этого клиента. */
  licenses: LicenseRow[];
  /** Договоры этого клиента (свои и те, по которым выданы его ключи). */
  contracts: ContractRow[];
  tariffs: TariffRow[];
  terms: KeyTermsRow[];
  /** Начисления по ключам клиента. */
  payments: PaymentRow[];
  parts: PaymentPartRow[];
  files: Map<number, ContractFileRow[]>;
}

/**
 * Деньги клиента: договоры (действующий сверху, у каждого — цены, файлы, ключи по нему), цены ключей, начисления
 * (новые сверху), долги и готовое «напомнить об оплате», сколько ключей можно продлить разом.
 */
export function clientMoneyView(d: ClientMoneyInput) {
  const serials = new Set(d.licenses.map((l) => l.serial));
  const mine = d.terms.filter((t) => serials.has(t.serial));
  const contracts = d.contracts
    .slice()
    .sort((a, b) => Number(a.status === "closed") - Number(b.status === "closed") || b.createdAt.getTime() - a.createdAt.getTime())
    .map((c) => ({
      ...contractView(c),
      lines: sortTariffs(d.tariffs.filter((t) => t.contractId === c.id)).map(tariffView),
      files: (d.files.get(c.id) ?? []).map(fileView),
      keys: mine.filter((t) => t.contractId === c.id).map((t) => t.serial),
    }));
  const payments = d.payments
    .slice()
    .sort((a, b) => b.createdAt.getTime() - a.createdAt.getTime() || b.id - a.id)
    .map((p) => paymentView(p, d.parts));
  const debts = payments.filter((p) => p.left > 0).reverse();
  const client = d.licenses.slice().sort((a, b) => b.createdAt.getTime() - a.createdAt.getTime())[0]?.client;
  const live = new Set(d.licenses.filter((l) => l.status !== "blocked").map((l) => l.serial));
  return {
    contracts,
    current: contracts.find((c) => c.status === "active")?.id ?? null,
    terms: Object.fromEntries(mine.map((t) => [t.serial, termsView(t)])),
    payments: payments.slice(0, 100),
    debts,
    reminder: debts.length && client ? debtReminder(client, debts, d.now) : null,
    renewable: mine.filter((t) => t.days && live.has(t.serial)).length,
  };
}
