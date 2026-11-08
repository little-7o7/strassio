// Хранилище в памяти — для тестов и локального запуска без базы.
import { UNKNOWN } from "./hwid.js";
import { phoneQuery } from "./insights.js";
import type { ActivationRow, AuditFilter, AuditRow, ContractFileRow, ContractRow, KeyTermsRow, LicenseRow, NewActivation, NewLicense, NewRequest, PaymentPartRow, PaymentRow, RequestRow, StatsWindow, Store, StoreStats, TariffRow, TrialRow, UpdateRow } from "./store.js";

export class MemoryStore implements Store {
  licenses: LicenseRow[] = [];
  activationRows: ActivationRow[] = [];
  trials: TrialRow[] = [];
  log: AuditRow[] = [];
  updates: UpdateRow[] = [];
  requests: RequestRow[] = [];
  tariffs: TariffRow[] = [];
  payments: PaymentRow[] = [];
  parts: PaymentPartRow[] = [];
  contracts: ContractRow[] = [];
  files: ContractFileRow[] = [];
  terms: KeyTermsRow[] = [];
  settings = new Map<string, string>();
  rates = new Map<string, number>();
  /** nonce → до какого времени (секунды Unix) помнить. */
  nonces = new Map<string, number>();
  private nextId = 1;

  async findLicense(serial: string) {
    return clone(this.licenses.find((l) => l.serial === serial) ?? null);
  }

  async insertLicense(row: NewLicense) {
    const created = { ...row, id: this.nextId++ };
    this.licenses.push(created);
    return { ...created };
  }

  async updateLicense(row: LicenseRow) {
    this.licenses = this.licenses.map((l) => (l.id === row.id ? { ...row } : l));
  }

  async searchLicenses(query: string, limit: number) {
    const q = query.trim().toLowerCase();
    const digits = phoneQuery(q);
    return this.licenses
      .filter((l) => !q || [l.serial, l.note, l.client.firstName, l.client.lastName, l.client.phone, l.client.email, l.client.telegram].some((t) => t.toLowerCase().includes(q)) || (!!digits && l.client.phone.replace(/\D/g, "").includes(digits)))
      .slice()
      .reverse()
      .slice(0, limit)
      .map((l) => ({ ...l }));
  }

  async deleteLicense(id: number) {
    this.activationRows = this.activationRows.filter((a) => a.licenseId !== id);
    this.licenses = this.licenses.filter((l) => l.id !== id);
  }

  async insertRequest(row: NewRequest) {
    const created = { ...row, client: { ...row.client }, id: this.nextId++ };
    this.requests.push(created);
    return { ...created, client: { ...created.client } };
  }

  async listRequests(limit: number) {
    return this.requests.slice().reverse().slice(0, limit).map((r) => ({ ...r, client: { ...r.client } }));
  }

  async findRequest(id: number) {
    const r = this.requests.find((x) => x.id === id);
    return r ? { ...r, client: { ...r.client } } : null;
  }

  async updateRequest(row: RequestRow) {
    this.requests = this.requests.map((r) => (r.id === row.id ? { ...row, client: { ...row.client } } : r));
  }

  async deleteRequest(id: number) {
    this.requests = this.requests.filter((r) => r.id !== id);
  }

  async activations(licenseId: number) {
    return this.activationRows.filter((a) => a.licenseId === licenseId).map((a) => ({ ...a }));
  }

  async insertActivation(row: NewActivation) {
    const created = { ...row, id: this.nextId++ };
    this.activationRows.push(created);
    return { ...created };
  }

  async updateActivation(row: ActivationRow) {
    this.activationRows = this.activationRows.map((a) => (a.id === row.id ? { ...row } : a));
  }

  async findActivation(id: number) {
    return clone(this.activationRows.find((a) => a.id === id) ?? null);
  }

  async trialsSharingPart(parts: string[]) {
    return this.trials.filter((t) => t.hwid.split(".").some((p, i) => p !== UNKNOWN && p === parts[i])).map((t) => ({ ...t }));
  }

  async insertTrial(hwid: string, startedAt: Date) {
    const row = { id: this.nextId++, hwid, startedAt };
    this.trials.push(row);
    return { ...row };
  }

  async listTrials(limit: number) {
    return this.trials.slice().reverse().slice(0, limit).map((t) => ({ ...t }));
  }

  async deleteTrial(id: number) {
    this.trials = this.trials.filter((t) => t.id !== id);
  }

  async audit(entry: Omit<AuditRow, "id">) {
    this.log.push({ ...entry, id: this.nextId++ });
  }

  async auditLog(limit: number) {
    return this.log.slice().reverse().slice(0, limit);
  }

  async latestUpdate(channel: string) {
    const rows = this.updates.filter((u) => u.channel === channel);
    return rows.length ? { ...rows[rows.length - 1] } : null;
  }

  async insertUpdate(row: UpdateRow) {
    this.updates.push({ ...row });
  }

  async spendNonce(nonce: string, untilSec: number, nowSec: number) {
    for (const [n, until] of this.nonces) if (until < nowSec) this.nonces.delete(n);
    if (this.nonces.has(nonce)) return false;
    this.nonces.set(nonce, untilSec);
    return true;
  }

  async stats(at: StatsWindow): Promise<StoreStats> {
    const open = this.licenses.filter((l) => l.status !== "blocked");
    const active = this.activationRows.filter((a) => a.status === "active");
    return {
      licenses: this.licenses.length,
      blocked: this.licenses.length - open.length,
      expired: open.filter((l) => l.expiresAt && l.expiresAt <= at.now).length,
      expiring: open.filter((l) => l.expiresAt && l.expiresAt > at.now && l.expiresAt <= at.soon).length,
      unlimited: this.licenses.filter((l) => !l.expiresAt).length,
      computers: active.length,
      checkedSince: active.filter((a) => a.lastCheckAt > at.dayAgo).length,
      trials: this.trials.length,
      trialsActive: this.trials.filter((t) => t.startedAt > at.trialSince).length,
      requestsNew: this.requests.filter((r) => r.status === "new").length,
      requestsWorking: this.requests.filter((r) => r.status === "working").length,
    };
  }

  async expiringLicenses(now: Date, soon: Date, limit: number) {
    return this.licenses
      .filter((l) => l.status !== "blocked" && l.expiresAt && l.expiresAt > now && l.expiresAt <= soon)
      .sort((a, b) => a.expiresAt!.getTime() - b.expiresAt!.getTime())
      .slice(0, limit)
      .map((l) => ({ ...l, client: { ...l.client } }));
  }

  async ping() {}

  async allLicenses(limit: number) {
    return this.licenses.slice().reverse().slice(0, limit).map((l) => ({ ...l, client: { ...l.client } }));
  }

  async allActivations(limit: number) {
    return this.activationRows.slice(0, limit).map((a) => ({ ...a }));
  }

  async auditSearch(f: AuditFilter, limit: number) {
    return this.log
      .filter((e) => (!f.serial || e.serial === f.serial) && (!f.actors?.length || f.actors.includes(e.actor)) && (!f.action || e.action === f.action) && (!f.from || e.at >= f.from) && (!f.to || e.at < f.to) && (!f.before || e.id < f.before))
      .reverse()
      .slice(0, limit)
      .map((e) => ({ ...e }));
  }

  async listTariffs() {
    return this.tariffs.map((t) => ({ ...t }));
  }

  async findTariff(id: number) {
    return clone(this.tariffs.find((t) => t.id === id) ?? null);
  }

  async insertTariff(row: Omit<TariffRow, "id">) {
    const created = { ...row, id: this.nextId++ };
    this.tariffs.push(created);
    return { ...created };
  }

  async updateTariff(row: TariffRow) {
    this.tariffs = this.tariffs.map((t) => (t.id === row.id ? { ...row } : t));
  }

  async insertPayment(row: Omit<PaymentRow, "id">) {
    const created = { ...row, id: this.nextId++ };
    this.payments.push(created);
    return { ...created };
  }

  async findPayment(id: number) {
    return clone(this.payments.find((p) => p.id === id) ?? null);
  }

  async updatePayment(row: PaymentRow) {
    this.payments = this.payments.map((p) => (p.id === row.id ? { ...row } : p));
  }

  async deletePayment(id: number) {
    this.payments = this.payments.filter((p) => p.id !== id);
    this.parts = this.parts.filter((p) => p.paymentId !== id);
  }

  async listPayments(limit: number) {
    return this.payments.slice().reverse().slice(0, limit).map((p) => ({ ...p }));
  }

  async paymentsFor(serial: string) {
    return this.payments.filter((p) => p.serial === serial).reverse().map((p) => ({ ...p }));
  }

  async insertPart(row: Omit<PaymentPartRow, "id">) {
    const created = { ...row, id: this.nextId++ };
    this.parts.push(created);
    return { ...created };
  }

  async findPart(id: number) {
    return clone(this.parts.find((p) => p.id === id) ?? null);
  }

  async deletePart(id: number) {
    this.parts = this.parts.filter((p) => p.id !== id);
  }

  async partsOf(paymentIds: number[]) {
    return this.parts.filter((p) => paymentIds.includes(p.paymentId)).map((p) => ({ ...p }));
  }

  async listParts(limit: number) {
    return this.parts.slice(0, limit).map((p) => ({ ...p }));
  }

  async listContracts() {
    return this.contracts.map((c) => ({ ...c }));
  }

  async findContract(id: number) {
    return clone(this.contracts.find((c) => c.id === id) ?? null);
  }

  async insertContract(row: Omit<ContractRow, "id">) {
    const created = { ...row, id: this.nextId++ };
    this.contracts.push(created);
    return { ...created };
  }

  async updateContract(row: ContractRow) {
    this.contracts = this.contracts.map((c) => (c.id === row.id ? { ...row } : c));
  }

  async deleteContract(id: number) {
    this.contracts = this.contracts.filter((c) => c.id !== id);
    this.files = this.files.filter((f) => f.contractId !== id);
    this.tariffs = this.tariffs.map((t) => (t.contractId === id ? { ...t, active: false } : t));
  }

  async contractFiles(contractId: number) {
    return this.files.filter((f) => f.contractId === contractId).map((f) => ({ ...f, data: "" }));
  }

  async findContractFile(id: number) {
    return clone(this.files.find((f) => f.id === id) ?? null);
  }

  async insertContractFile(row: Omit<ContractFileRow, "id">) {
    const created = { ...row, id: this.nextId++ };
    this.files.push(created);
    return { ...created, data: "" };
  }

  async deleteContractFile(id: number) {
    this.files = this.files.filter((f) => f.id !== id);
  }

  async keyTerms(serial: string) {
    return clone(this.terms.find((t) => t.serial === serial) ?? null);
  }

  async allKeyTerms() {
    return this.terms.map((t) => ({ ...t }));
  }

  async saveKeyTerms(row: KeyTermsRow) {
    this.terms = [...this.terms.filter((t) => t.serial !== row.serial), { ...row }];
  }

  async getSetting(key: string) {
    return this.settings.get(key) ?? null;
  }

  async setSetting(key: string, value: string) {
    this.settings.set(key, value);
  }

  async rateFor(day: string) {
    return this.rates.get(day) ?? null;
  }

  async saveRate(day: string, rate: number) {
    this.rates.set(day, rate);
  }

  async latestRate() {
    const days = [...this.rates.keys()].sort();
    return days.length ? this.rates.get(days.at(-1)!)! : null;
  }
}

function clone<T extends object>(row: T | null): T | null {
  return row ? { ...row } : null;
}
