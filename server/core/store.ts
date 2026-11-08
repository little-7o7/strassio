// Хранилище (SPEC 15, «Таблицы»). Логика лицензий работает только через этот интерфейс — поэтому
// её можно проверить тестами без базы (MemoryStore), а на сервере подставить Postgres (PgStore).

export type LicenseStatus = "active" | "blocked";
export type ActivationStatus = "active" | "revoked";

export interface LicenseRow {
  id: number;
  serial: string;
  plan: string;
  status: LicenseStatus;
  expiresAt: Date | null;
  maxPcs: number;
  transfersCount: number;
  /** С какого момента считаются переносы (раз в 365 дней счётчик обнуляется). */
  transfersSince: Date;
  note: string;
  createdAt: Date;
  /** Клиент (заполняет автор в админке; пустые строки — не указано). */
  client: ClientInfo;
}

export interface ClientInfo {
  firstName: string;
  lastName: string;
  phone: string;
  email: string;
  /** ГГГГ-ММ-ДД или "". */
  birthday: string;
  /** Имя пользователя Telegram без @ или "". */
  telegram: string;
}

export type RequestStatus = "new" | "working" | "done" | "rejected";

/** Заявка на покупку с сайта (страница /buy). Когда по ней создан ключ — status "done", serial — этот ключ. */
export interface RequestRow {
  id: number;
  createdAt: Date;
  client: ClientInfo;
  message: string;
  status: RequestStatus;
  serial: string;
}

export type NewRequest = Omit<RequestRow, "id">;

export interface ActivationRow {
  id: number;
  licenseId: number;
  hwid: string;
  status: ActivationStatus;
  firstAt: Date;
  lastCheckAt: Date;
  pluginVersion: string;
  corelVersion: string;
}

export interface TrialRow {
  id: number;
  hwid: string;
  startedAt: Date;
}

export interface AuditRow {
  id: number;
  at: Date;
  actor: string;
  action: string;
  serial: string;
  details: string;
}

export interface UpdateRow {
  channel: string;
  version: string;
  url: string;
  sha256: string;
  notesRu: string;
  notesEn: string;
  minVersion: string;
  createdAt: Date;
}

export type NewLicense = Omit<LicenseRow, "id">;

// ---------- Деньги (хаб, владелец 2026-11-08) ----------

export type Currency = "UZS" | "USD";
/** Как заплатили; "" — ещё не заплатили (долг). */
export type PayMethod = "cash" | "card" | "transfer" | "";

/** Тариф: название, срок (null — бессрочно) и цена. Выключенный не предлагается, но старые оплаты его помнят по имени. */
export interface TariffRow {
  id: number;
  name: string;
  days: number | null;
  price: number;
  currency: Currency;
  active: boolean;
  createdAt: Date;
}

/**
 * Оплата ключа: продажа или продление. paidAt null — долг (ключ выдан, денег ещё нет). Привязана к ключу по serial,
 * а не по id: ключ удалили — оплата остаётся в доходе.
 */
export interface PaymentRow {
  id: number;
  serial: string;
  kind: "sale" | "renewal";
  amount: number;
  currency: Currency;
  method: PayMethod;
  /** Название тарифа на момент оплаты; "" — своя сумма. */
  tariff: string;
  note: string;
  createdAt: Date;
  paidAt: Date | null;
}

/** Границы для сводки хаба: сейчас, «скоро» (через 30 дней), сутки назад, начало ещё идущих пробных. */
export interface StatsWindow {
  now: Date;
  soon: Date;
  dayAgo: Date;
  trialSince: Date;
}

/** Числа для Главной хаба (только чтение). Истёкшие и «скоро истекают» — среди незаблокированных. */
export interface StoreStats {
  licenses: number;
  blocked: number;
  expired: number;
  expiring: number;
  unlimited: number;
  computers: number;
  checkedSince: number;
  trials: number;
  trialsActive: number;
  requestsNew: number;
  requestsWorking: number;
}
export type NewActivation = Omit<ActivationRow, "id">;

/** Журнал с фильтрами (хаб): ключ, кто (client, site, admin, hub), действие, даты [from, to). Пустое — без фильтра. */
export interface AuditFilter {
  serial?: string;
  actors?: string[];
  action?: string;
  from?: Date;
  to?: Date;
}

export interface Store {
  findLicense(serial: string): Promise<LicenseRow | null>;
  insertLicense(row: NewLicense): Promise<LicenseRow>;
  updateLicense(row: LicenseRow): Promise<void>;
  /**
   * Поиск для админки: по ключу, заметке, имени, фамилии, телефону или почте; пустой запрос — последние.
   * Телефон — в любом виде: «+998 90 123 45 67», «8 90 1234567» и «901234567» находят один и тот же номер.
   */
  searchLicenses(query: string, limit: number): Promise<LicenseRow[]>;
  /** Удалить ключ полностью — вместе со всеми его активациями (журнал остаётся). */
  deleteLicense(id: number): Promise<void>;

  insertRequest(row: NewRequest): Promise<RequestRow>;
  /** Заявки, новые сверху. */
  listRequests(limit: number): Promise<RequestRow[]>;
  findRequest(id: number): Promise<RequestRow | null>;
  updateRequest(row: RequestRow): Promise<void>;
  deleteRequest(id: number): Promise<void>;

  activations(licenseId: number): Promise<ActivationRow[]>;
  insertActivation(row: NewActivation): Promise<ActivationRow>;
  updateActivation(row: ActivationRow): Promise<void>;
  findActivation(id: number): Promise<ActivationRow | null>;

  /** Пробные периоды, у которых совпадает хотя бы один признак компьютера (дальше сравнивает логика). */
  trialsSharingPart(parts: string[]): Promise<TrialRow[]>;
  insertTrial(hwid: string, startedAt: Date): Promise<TrialRow>;
  /** Пробные периоды для админки, новые сверху. */
  listTrials(limit: number): Promise<TrialRow[]>;
  /** Удалить пробный период — компьютер сможет взять его заново. */
  deleteTrial(id: number): Promise<void>;

  audit(entry: Omit<AuditRow, "id">): Promise<void>;
  auditLog(limit: number): Promise<AuditRow[]>;

  latestUpdate(channel: string): Promise<UpdateRow | null>;
  insertUpdate(row: UpdateRow): Promise<void>;

  // ---------- Хаб little_7o7 (/api/platform) ----------
  /** Отметить nonce подписанного запроса. false — он уже был (повтор). Устаревшие (until < nowSec) стираются. */
  spendNonce(nonce: string, untilSec: number, nowSec: number): Promise<boolean>;
  stats(at: StatsWindow): Promise<StoreStats>;
  /** Незаблокированные ключи, срок которых кончается в (now, soon], ближайшие первыми. */
  expiringLicenses(now: Date, soon: Date, limit: number): Promise<LicenseRow[]>;
  /** База отвечает (для «Состояния» в хабе). */
  ping(): Promise<void>;
  /** Все ключи, новые сверху (клиенты, выгрузка, отчёты хаба). */
  allLicenses(limit: number): Promise<LicenseRow[]>;
  /** Все активации всех ключей. */
  allActivations(limit: number): Promise<ActivationRow[]>;
  /** Журнал с фильтрами, новые сверху. */
  auditSearch(filter: AuditFilter, limit: number): Promise<AuditRow[]>;

  // ---------- Деньги ----------
  listTariffs(): Promise<TariffRow[]>;
  findTariff(id: number): Promise<TariffRow | null>;
  insertTariff(row: Omit<TariffRow, "id">): Promise<TariffRow>;
  updateTariff(row: TariffRow): Promise<void>;
  insertPayment(row: Omit<PaymentRow, "id">): Promise<PaymentRow>;
  findPayment(id: number): Promise<PaymentRow | null>;
  updatePayment(row: PaymentRow): Promise<void>;
  deletePayment(id: number): Promise<void>;
  /** Все оплаты, новые сверху. */
  listPayments(limit: number): Promise<PaymentRow[]>;
  /** Оплаты одного ключа, новые сверху. */
  paymentsFor(serial: string): Promise<PaymentRow[]>;
}
