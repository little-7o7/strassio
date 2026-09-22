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
export type NewActivation = Omit<ActivationRow, "id">;

export interface Store {
  findLicense(serial: string): Promise<LicenseRow | null>;
  insertLicense(row: NewLicense): Promise<LicenseRow>;
  updateLicense(row: LicenseRow): Promise<void>;
  /** Поиск для админки: по ключу, заметке, имени, фамилии, телефону или почте; пустой запрос — последние. */
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
}
