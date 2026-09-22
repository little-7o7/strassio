// Хранилище в памяти — для тестов и локального запуска без базы.
import { UNKNOWN } from "./hwid.js";
import type { ActivationRow, AuditRow, LicenseRow, NewActivation, NewLicense, Store, TrialRow, UpdateRow } from "./store.js";

export class MemoryStore implements Store {
  licenses: LicenseRow[] = [];
  activationRows: ActivationRow[] = [];
  trials: TrialRow[] = [];
  log: AuditRow[] = [];
  updates: UpdateRow[] = [];
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
    return this.licenses
      .filter((l) => !q || [l.serial, l.note, l.client.firstName, l.client.lastName, l.client.phone, l.client.email].some((t) => t.toLowerCase().includes(q)))
      .slice()
      .reverse()
      .slice(0, limit)
      .map((l) => ({ ...l }));
  }

  async deleteLicense(id: number) {
    this.activationRows = this.activationRows.filter((a) => a.licenseId !== id);
    this.licenses = this.licenses.filter((l) => l.id !== id);
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
}

function clone<T extends object>(row: T | null): T | null {
  return row ? { ...row } : null;
}
