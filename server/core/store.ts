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

/**
 * Тариф: название, срок (null — навсегда) и цена. Общий (contractId null) или строка цены договора клиента. Цена может
 * считаться от другого тарифа: «Год» = цена «Месяца» × 12 − скидка (baseId, discount; владелец 2026-11-08) — тогда
 * price хранит уже посчитанную цену и пересчитывается при правке основного тарифа. kind "updates" — оплата обновлений
 * для ключей «навсегда». Выключенный не предлагается, но старые оплаты и цены ключей его помнят.
 */
export interface TariffRow {
  id: number;
  name: string;
  days: number | null;
  price: number;
  currency: Currency;
  active: boolean;
  createdAt: Date;
  contractId: number | null;
  kind: TariffKind;
  baseId: number | null;
  /** «15%» или сумма «100»; "" — без скидки. */
  discount: string;
  /** Показывать на сайте (только общие тарифы). */
  site: boolean;
  /** Подпись: «выгоднее на 2 месяца». */
  label: string;
}

export type TariffKind = "key" | "updates";

/**
 * Договор клиента («договор есть договор», владелец 2026-11-08). Клиента как строки в базе нет: договор помнит его
 * clientKey (телефон, почта, Telegram или имя). Новый договор того же клиента закрывает прежний — старый остаётся в
 * истории. Даты — ГГГГ-ММ-ДД или "".
 */
export interface ContractRow {
  id: number;
  clientKey: string;
  number: string;
  signedOn: string;
  validFrom: string;
  /** "" — бессрочно. */
  validUntil: string;
  /** ФИО или организация. */
  party: string;
  inn: string;
  address: string;
  /** Условия оплаты словами: «до 5 числа каждого месяца». */
  terms: string;
  /** День оплаты 1–31 (для напоминаний); null — не задан. */
  payDay: number | null;
  currency: Currency;
  note: string;
  status: "active" | "closed";
  closedAt: Date | null;
  createdAt: Date;
}

/** Файл договора (скан, PDF) — base64 в базе, до 3 МБ. */
export interface ContractFileRow {
  id: number;
  contractId: number;
  name: string;
  type: string;
  size: number;
  data: string;
  createdAt: Date;
}

/**
 * Цена ключа — закреплена за ключом при выдаче: договорились по $50, потом подняли до $60 — старые ключи продлеваются
 * по $50, новые стоят $60 (владелец 2026-11-08). Меняется только явно.
 */
export interface KeyTermsRow {
  serial: string;
  contractId: number | null;
  tariffId: number | null;
  name: string;
  /** Срок одного периода; null — навсегда. */
  days: number | null;
  price: number;
  currency: Currency;
  /** Ключ «навсегда»: обновления оплачены до; null — не задано. */
  updatesUntil: Date | null;
  since: Date;
}

export type PaymentKind = "sale" | "renewal" | "updates";

/**
 * Начисление по ключу: продажа, продление или обновления. Сколько должен — amount (0 — бесплатно), цена без скидки —
 * listPrice. Деньги приходят частями (PaymentPartRow); paidAt — когда начисление закрыто полностью, null — долг.
 * Привязано к ключу по serial, а не по id: ключ удалили — доход остаётся.
 */
export interface PaymentRow {
  id: number;
  serial: string;
  kind: PaymentKind;
  amount: number;
  currency: Currency;
  /** Цена по договору или тарифу без скидки; 0 — не задана. */
  listPrice: number;
  /** Название тарифа или цены ключа; "" — своя сумма. */
  tariff: string;
  contractId: number | null;
  note: string;
  /** Срок оплаты долга ГГГГ-ММ-ДД или "". */
  dueOn: string;
  createdAt: Date;
  paidAt: Date | null;
}

/**
 * Поступление денег по начислению. Пришло amount в currency, засчитано credit в валюте начисления: договор в долларах,
 * заплатили сумами — в доходе сумы, долг закрыт на столько долларов, сколько засчитали.
 */
export interface PaymentPartRow {
  id: number;
  paymentId: number;
  amount: number;
  currency: Currency;
  /** Курс дня (сумов за доллар), когда валюта другая; 0 — та же валюта или засчитано вручную. */
  rate: number;
  credit: number;
  method: Exclude<PayMethod, "">;
  note: string;
  paidAt: Date;
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
  /** Удалить начисление вместе с его поступлениями. */
  deletePayment(id: number): Promise<void>;
  /** Все начисления, новые сверху. */
  listPayments(limit: number): Promise<PaymentRow[]>;
  /** Начисления одного ключа, новые сверху. */
  paymentsFor(serial: string): Promise<PaymentRow[]>;
  insertPart(row: Omit<PaymentPartRow, "id">): Promise<PaymentPartRow>;
  findPart(id: number): Promise<PaymentPartRow | null>;
  deletePart(id: number): Promise<void>;
  /** Поступления по этим начислениям, по порядку. */
  partsOf(paymentIds: number[]): Promise<PaymentPartRow[]>;
  /** Все поступления, по порядку. */
  listParts(limit: number): Promise<PaymentPartRow[]>;

  listContracts(): Promise<ContractRow[]>;
  findContract(id: number): Promise<ContractRow | null>;
  insertContract(row: Omit<ContractRow, "id">): Promise<ContractRow>;
  updateContract(row: ContractRow): Promise<void>;
  /** Удалить договор вместе с файлами; его цены выключаются, цены ключей остаются. */
  deleteContract(id: number): Promise<void>;
  /** Файлы договора без содержимого (data ""). */
  contractFiles(contractId: number): Promise<ContractFileRow[]>;
  findContractFile(id: number): Promise<ContractFileRow | null>;
  insertContractFile(row: Omit<ContractFileRow, "id">): Promise<ContractFileRow>;
  deleteContractFile(id: number): Promise<void>;

  keyTerms(serial: string): Promise<KeyTermsRow | null>;
  allKeyTerms(): Promise<KeyTermsRow[]>;
  saveKeyTerms(row: KeyTermsRow): Promise<void>;

  /** Настройки денег (скидка за количество и т. п.): ключ → текст; null — не задано. */
  getSetting(key: string): Promise<string | null>;
  setSetting(key: string, value: string): Promise<void>;
  /** Курс ЦБ (сумов за доллар) на день ГГГГ-ММ-ДД; null — не сохранён. */
  rateFor(day: string): Promise<number | null>;
  saveRate(day: string, rate: number): Promise<void>;
  /** Последний сохранённый курс; null — ни одного. */
  latestRate(): Promise<number | null>;
}
