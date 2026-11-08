// Сводки для хаба little_7o7 по всем ключам сразу (только чтение, без записи в базу): клиенты и дни рождения,
// компьютеры и версии, уборка данных, пробные без покупки и похожие компьютеры, подозрительные ключи, утреннее
// сообщение в Telegram. Чистые функции: строки из базы на входе, готовые данные на выходе — легко проверить тестами.
import { createHash } from "node:crypto";
import { hwidDisplay, hwidMatches, parseHwid, UNKNOWN } from "./hwid.js";
import { add, sumsText, zero } from "./sums.js";
import type { ActivationRow, AuditRow, ClientInfo, LicenseRow, PaymentRow, RequestRow, TrialRow } from "./store.js";

const DAY = 24 * 60 * 60 * 1000;
/** Ташкент: UTC+5 круглый год. */
const TASHKENT = 5 * 60 * 60 * 1000;
/** «Давно не открывали»: столько дней без сверки. */
export const SILENT_DAYS = 30;
/** Напоминание о конце ключа в Telegram: столько дней и меньше. */
export const REMIND_DAYS = 7;
/** Подозрительный ключ: столько разных компьютеров за 90 дней (включая занятые попытки). */
export const SUSPICIOUS_PCS = 3;

export type KeyState = "active" | "blocked" | "expired";

export interface KeyBrief {
  serial: string;
  status: KeyState;
  expiresAt: string | null;
  daysLeft: number | null;
  note: string;
  createdAt: string;
}

export interface ComputerBrief {
  id: number;
  code: string;
  serial: string;
  keyStatus: KeyState;
  clientId: string;
  name: string;
  phone: string;
  pluginVersion: string;
  corelVersion: string;
  firstAt: string;
  lastCheckAt: string;
  /** Дней без сверки. */
  silentDays: number;
  /** Офлайн-файл: такой компьютер не сверяется, «молчит» по праву. */
  offline: boolean;
  /** Версия плагина ниже последней в канале stable. */
  outdated: boolean;
  /** Ключ действует, а плагин не сверялся SILENT_DAYS дней и больше. */
  silent: boolean;
}

export interface ClientView {
  id: string;
  name: string;
  client: ClientInfo;
  keys: KeyBrief[];
  computers: ComputerBrief[];
  requests: Array<{ id: number; createdAt: string; status: string; message: string; serial: string }>;
  notes: string[];
  since: string;
  lastSeenAt: string | null;
}

export interface Birthday {
  clientId: string;
  name: string;
  phone: string;
  telegram: string;
  birthday: string;
  day: number;
  today: boolean;
  /** Сколько исполняется в этом году; 0 — год рождения неизвестен. */
  turns: number;
}

const iso = (d: Date) => d.toISOString().replace(/\.\d{3}Z$/, "Z");
const nameOf = (c: ClientInfo) => [c.firstName, c.lastName].filter(Boolean).join(" ");

/** Цифры телефона без кода страны: «+998 90 123-45-67», «8 90 1234567», «901234567» — одинаково. "" — не телефон. */
export function phoneDigits(text: string): string {
  const d = text.replace(/\D/g, "");
  return d.length >= 7 ? d.slice(-9) : "";
}

/** Запрос похож на телефон (только цифры, пробелы, +, -, скобки; 7 цифр и больше) — его 9 последних цифр. */
export function phoneQuery(q: string): string {
  return /^[\d\s()+-]+$/.test(q) ? phoneDigits(q) : "";
}

/** Кто это: по телефону, иначе по почте, Telegram, имени. "" — клиент не указан. */
export function clientKey(c: ClientInfo): string {
  const p = phoneDigits(c.phone);
  if (p) return "p" + p;
  if (c.email) return "e" + c.email.toLowerCase();
  if (c.telegram) return "t" + c.telegram.toLowerCase();
  const name = nameOf(c).toLowerCase().replace(/\s+/g, " ").trim();
  return name ? "n" + name : "";
}

const low = (s: string) => s.trim().toLowerCase();
const fits = (a: string, b: string) => !low(a) || !low(b) || low(a) === low(b);
/** Похоже на одного человека: имя и фамилия совпадают или в одной из записей пусты. */
const samePerson = (a: ClientInfo, b: ClientInfo) => fits(a.firstName, b.firstName) && fits(a.lastName, b.lastName);

/** Адрес клиента в хабе — без телефона и имени в ссылке. */
export const clientId = (key: string) => createHash("sha256").update(key).digest("base64url").slice(0, 12);

/** Сравнить версии «1.2.3»: <0, 0, >0. Не версия (например, «offline») — null. */
export function compareVersions(a: string, b: string): number | null {
  const pa = /^\d+(\.\d+){0,3}/.exec(a.trim())?.[0].split(".").map(Number);
  const pb = /^\d+(\.\d+){0,3}/.exec(b.trim())?.[0].split(".").map(Number);
  if (!pa || !pb) return null;
  for (let i = 0; i < Math.max(pa.length, pb.length); i++) {
    const d = (pa[i] ?? 0) - (pb[i] ?? 0);
    if (d) return d;
  }
  return 0;
}

export function keyState(l: LicenseRow, now: Date): KeyState {
  if (l.status === "blocked") return "blocked";
  return l.expiresAt && l.expiresAt <= now ? "expired" : "active";
}

const daysLeft = (l: LicenseRow, now: Date) => (l.expiresAt ? Math.max(0, Math.ceil((l.expiresAt.getTime() - now.getTime()) / DAY)) : null);

export function keyBrief(l: LicenseRow, now: Date): KeyBrief {
  return { serial: l.serial, status: keyState(l, now), expiresAt: l.expiresAt ? iso(l.expiresAt) : null, daysLeft: daysLeft(l, now), note: l.note, createdAt: iso(l.createdAt) };
}

const isOffline = (a: ActivationRow) => a.pluginVersion === "offline" || a.pluginVersion === "site-offline";

/** Подключённые компьютеры всех ключей: версии, «давно не открывали», «не обновился». */
export function computerBriefs(licenses: LicenseRow[], activations: ActivationRow[], stable: string | null, now: Date): ComputerBrief[] {
  const byId = new Map(licenses.map((l) => [l.id, l]));
  const out: ComputerBrief[] = [];
  for (const a of activations) {
    const l = byId.get(a.licenseId);
    if (!l || a.status !== "active") continue;
    const state = keyState(l, now);
    const silentDays = Math.max(0, Math.floor((now.getTime() - a.lastCheckAt.getTime()) / DAY));
    const offline = isOffline(a);
    const cmp = stable && !offline ? compareVersions(a.pluginVersion, stable) : null;
    out.push({
      id: a.id,
      code: hwidDisplay(parseHwid(a.hwid) ?? a.hwid.split(".")),
      serial: l.serial,
      keyStatus: state,
      clientId: clientKey(l.client) ? clientId(clientKey(l.client)) : "",
      name: nameOf(l.client),
      phone: l.client.phone,
      pluginVersion: a.pluginVersion,
      corelVersion: a.corelVersion,
      firstAt: iso(a.firstAt),
      lastCheckAt: iso(a.lastCheckAt),
      silentDays,
      offline,
      outdated: cmp !== null && cmp < 0,
      silent: state === "active" && !offline && silentDays >= SILENT_DAYS,
    });
  }
  return out.sort((x, y) => y.silentDays - x.silentDays || x.serial.localeCompare(y.serial));
}

/** Сколько компьютеров на каждой версии: самые новые сверху; пустая версия — «—». */
export function countVersions(values: string[]): Array<{ version: string; count: number }> {
  const counts = new Map<string, number>();
  for (const v of values) counts.set(v || "—", (counts.get(v || "—") ?? 0) + 1);
  return [...counts.entries()]
    .map(([version, count]) => ({ version, count }))
    .sort((a, b) => {
      const known = (v: string) => compareVersions(v, "0") !== null;
      if (known(a.version) !== known(b.version)) return known(a.version) ? -1 : 1;
      return (compareVersions(b.version, a.version) ?? 0) || b.count - a.count || a.version.localeCompare(b.version);
    });
}

/** Клиенты: один человек (тот же телефон, почта, Telegram или имя) — одна карточка со всеми ключами. */
export function clientViews(licenses: LicenseRow[], activations: ActivationRow[], requests: RequestRow[], stable: string | null, now: Date): ClientView[] {
  const computers = computerBriefs(licenses, activations, stable, now);
  const groups = new Map<string, { licenses: LicenseRow[]; requests: RequestRow[] }>();
  const group = (key: string) => {
    let g = groups.get(key);
    if (!g) groups.set(key, (g = { licenses: [], requests: [] }));
    return g;
  };
  for (const l of licenses) {
    const key = clientKey(l.client);
    if (key) group(key).licenses.push(l);
  }
  for (const r of requests) {
    const key = clientKey(r.client);
    if (key && groups.has(key)) group(key).requests.push(r);
  }

  const views: ClientView[] = [];
  for (const [key, g] of groups) {
    const sorted = g.licenses.slice().sort((a, b) => b.createdAt.getTime() - a.createdAt.getTime());
    // Данные — из самого нового ключа; пустое поле дополняется из более старых, но только того же человека: на одном
    // телефоне могут быть двое («Тимур» и «Мадина Каримова»), и чужие фамилия или день рождения не подмешиваются.
    const client = { ...sorted[0].client };
    for (const l of sorted.slice(1)) {
      if (!samePerson(client, l.client)) continue;
      for (const f of Object.keys(client) as (keyof ClientInfo)[]) if (!client[f]) client[f] = l.client[f];
    }
    const serials = new Set(sorted.map((l) => l.serial));
    const mine = computers.filter((c) => serials.has(c.serial));
    const seen = mine.filter((c) => !c.offline).map((c) => c.lastCheckAt).sort();
    views.push({
      id: clientId(key),
      name: nameOf(client),
      client,
      keys: sorted.map((l) => keyBrief(l, now)),
      computers: mine,
      requests: g.requests.map((r) => ({ id: r.id, createdAt: iso(r.createdAt), status: r.status, message: r.message, serial: r.serial })),
      notes: sorted.map((l) => l.note.trim()).filter(Boolean),
      since: iso(sorted[sorted.length - 1].createdAt),
      lastSeenAt: seen.length ? seen[seen.length - 1] : null,
    });
  }
  return views.sort((a, b) => (a.name || a.client.phone).localeCompare(b.name || b.client.phone, "ru"));
}

/** Дни рождения в этом месяце (по Ташкенту), по порядку дней. */
export function birthdaysThisMonth(clients: ClientView[], now: Date): Birthday[] {
  const local = new Date(now.getTime() + TASHKENT);
  const month = local.getUTCMonth() + 1;
  const today = local.getUTCDate();
  const out: Birthday[] = [];
  for (const c of clients) {
    const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(c.client.birthday);
    if (!m || Number(m[2]) !== month) continue;
    const year = Number(m[1]);
    out.push({ clientId: c.id, name: c.name, phone: c.client.phone, telegram: c.client.telegram, birthday: c.client.birthday, day: Number(m[3]), today: Number(m[3]) === today, turns: year > 1900 ? local.getUTCFullYear() - year : 0 });
  }
  return out.sort((a, b) => a.day - b.day || a.name.localeCompare(b.name, "ru"));
}

/** Готовое поздравление клиенту. */
export const greeting = (name: string) => `${name ? name.split(" ")[0] + ", с" : "С"} днём рождения! Желаем здоровья, вдохновения и красивых работ. Ваш Strassio`;

/** Пробные: закончились без покупки (компьютер так и не активировал ключ) и похожие компьютеры (обход пробного). */
export function trialInsights(trials: TrialRow[], activations: ActivationRow[], now: Date, trialDays: number) {
  const parsed = trials.map((t) => ({ t, parts: parseHwid(t.hwid) ?? t.hwid.split(".") }));
  const pcs = activations.map((a) => parseHwid(a.hwid)).filter((p): p is string[] => !!p);
  const notBought = parsed
    .filter(({ t, parts }) => t.startedAt.getTime() + trialDays * DAY <= now.getTime() && !pcs.some((p) => hwidMatches(p, parts)))
    .map(({ t, parts }) => ({ id: t.id, code: hwidDisplay(parts), startedAt: iso(t.startedAt), endedAt: iso(new Date(t.startedAt.getTime() + trialDays * DAY)) }));

  // Разные пробные, у которых совпадает хотя бы один признак компьютера: кто-то меняет железо или подделывает код.
  const parent = parsed.map((_, i) => i);
  const find = (i: number): number => (parent[i] === i ? i : (parent[i] = find(parent[i])));
  const firstWith = new Map<string, number>();
  parsed.forEach(({ parts }, i) => {
    parts.forEach((p, k) => {
      if (p === UNKNOWN) return;
      const key = k + ":" + p;
      const j = firstWith.get(key);
      if (j === undefined) firstWith.set(key, i);
      else parent[find(i)] = find(j);
    });
  });
  const groups = new Map<number, typeof parsed>();
  parsed.forEach((x, i) => {
    const root = find(i);
    groups.set(root, [...(groups.get(root) ?? []), x]);
  });
  const similar = [...groups.values()]
    .filter((g) => g.length >= 2)
    .map((g) => {
      const times = g.map((x) => x.t.startedAt.getTime()).sort((a, b) => a - b);
      return { count: g.length, codes: g.map((x) => hwidDisplay(x.parts)), ids: g.map((x) => x.t.id), firstAt: iso(new Date(times[0])), lastAt: iso(new Date(times[times.length - 1])) };
    })
    .sort((a, b) => b.count - a.count);
  return { notBought, similar };
}

/** Подозрительные ключи: за 90 дней на многих разных компьютерах (подключали или пытались, когда место занято). */
export function suspiciousKeys(licenses: LicenseRow[], activations: ActivationRow[], occupied: AuditRow[], now: Date) {
  const since = now.getTime() - 90 * DAY;
  const pcs = new Map<number, string[][]>();
  const add = (licenseId: number, hwid: string) => {
    const parts = parseHwid(hwid);
    if (!parts) return;
    const list = pcs.get(licenseId) ?? [];
    if (!list.some((p) => hwidMatches(p, parts))) list.push(parts);
    pcs.set(licenseId, list);
  };
  for (const a of activations) if (a.firstAt.getTime() >= since) add(a.licenseId, a.hwid);
  const bySerial = new Map(licenses.map((l) => [l.serial, l]));
  for (const e of occupied) {
    const l = bySerial.get(e.serial);
    if (l && e.at.getTime() >= since) add(l.id, e.details);
  }
  return licenses
    .filter((l) => (pcs.get(l.id)?.length ?? 0) >= SUSPICIOUS_PCS)
    .map((l) => ({ serial: l.serial, computers: pcs.get(l.id)!.length, transfers: l.transfersCount, name: nameOf(l.client), phone: l.client.phone }))
    .sort((a, b) => b.computers - a.computers);
}

/** Уборка: ключи без клиента, один телефон — разные имена, старые заявки, ключи с давно вышедшим сроком. */
export function cleanup(licenses: LicenseRow[], requests: RequestRow[], now: Date) {
  const t = now.getTime();
  const noClient = licenses.filter((l) => !clientKey(l.client)).map((l) => keyBrief(l, now));
  const byPhone = new Map<string, Set<string>>();
  for (const l of licenses) {
    const p = phoneDigits(l.client.phone);
    const n = nameOf(l.client).toLowerCase().replace(/\s+/g, " ");
    if (p && n) byPhone.set(p, (byPhone.get(p) ?? new Set()).add(nameOf(l.client)));
  }
  const samePhone = [...byPhone.entries()]
    .filter(([, names]) => new Set([...names].map((n) => n.toLowerCase().replace(/\s+/g, " "))).size > 1)
    .map(([p, names]) => ({ clientId: clientId("p" + p), phone: licenses.find((l) => phoneDigits(l.client.phone) === p)!.client.phone, names: [...names] }));
  const oldRequests = requests
    .filter((r) => ((r.status === "done" || r.status === "rejected") && t - r.createdAt.getTime() > 90 * DAY) || ((r.status === "new" || r.status === "working") && t - r.createdAt.getTime() > 30 * DAY))
    .map((r) => ({ id: r.id, createdAt: iso(r.createdAt), status: r.status, name: nameOf(r.client), phone: r.client.phone }));
  const longExpired = licenses
    .filter((l) => l.status !== "blocked" && l.expiresAt && t - l.expiresAt.getTime() > 180 * DAY)
    .map((l) => ({ ...keyBrief(l, now), name: nameOf(l.client) }));
  return { noClient, samePhone, oldRequests, longExpired };
}

const esc = (s: string) => s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");

/** Новая заявка с сайта — сообщение владельцу в Telegram (HTML). */
export function requestMessage(r: RequestRow, hubUrl: string): string {
  const c = r.client;
  const lines = [`🆕 <b>Новая заявка #${r.id}</b>`, esc(nameOf(c) || "Без имени")];
  if (c.phone) lines.push(`📞 ${esc(c.phone)}`);
  if (c.telegram) lines.push(`✈️ @${esc(c.telegram)}`);
  if (c.email) lines.push(`✉️ ${esc(c.email)}`);
  if (r.message) lines.push(`«${esc(r.message.slice(0, 300))}»`);
  lines.push(`<a href="${hubUrl}/admin/strassio/requests#r${r.id}">Открыть в хабе</a>`);
  return lines.join("\n");
}

/** Ключ впервые включили на компьютере — сообщение владельцу. */
export function activationMessage(l: LicenseRow, a: ActivationRow, hubUrl: string): string {
  const who = nameOf(l.client);
  const lines = [`💎 <b>Новая активация</b>`, `${esc(who || "Клиент не указан")}${l.client.phone ? ", " + esc(l.client.phone) : ""}`, `Ключ ${esc(l.serial)}`];
  if (a.pluginVersion || a.corelVersion) lines.push(`Плагин ${esc(a.pluginVersion || "—")} · CorelDRAW ${esc(a.corelVersion || "—")}`);
  lines.push(`<a href="${hubUrl}/admin/strassio/k/${encodeURIComponent(l.serial)}">Открыть ключ</a>`);
  return lines.join("\n");
}

export interface DigestInput {
  now: Date;
  licenses: LicenseRow[];
  activations: ActivationRow[];
  requests: RequestRow[];
  trials: TrialRow[];
  clients: ClientView[];
  trialDays: number;
  hubUrl: string;
  /** Оплаты (раздел «Деньги»): за сутки — отдельной строкой. */
  payments?: PaymentRow[];
}

/**
 * Утреннее сообщение (9:00 по Ташкенту): заявки и активации за сутки, закончившиеся пробные, ключи на исходе
 * (7 дней и меньше) с телефонами, дни рождения сегодня с готовым поздравлением. Нечего сказать — null (бот молчит).
 */
export function dailyDigest(d: DigestInput): string | null {
  const t = d.now.getTime();
  const dayAgo = t - DAY;
  const newRequests = d.requests.filter((r) => r.createdAt.getTime() > dayAgo);
  const waiting = d.requests.filter((r) => r.status === "new").length;
  const newPcs = d.activations.filter((a) => a.firstAt.getTime() > dayAgo && !isOffline(a));
  const ended = d.trials.filter((x) => {
    const end = x.startedAt.getTime() + d.trialDays * DAY;
    return end <= t && end > dayAgo;
  });
  const notBought = trialInsights(ended, d.activations, d.now, d.trialDays).notBought.length;
  const ending = d.licenses
    .filter((l) => l.status !== "blocked" && l.expiresAt && l.expiresAt.getTime() > t && l.expiresAt.getTime() <= t + REMIND_DAYS * DAY)
    .sort((a, b) => a.expiresAt!.getTime() - b.expiresAt!.getTime());
  const birthdays = birthdaysThisMonth(d.clients, d.now).filter((b) => b.today);
  const paid = (d.payments ?? []).filter((p) => p.paidAt && p.paidAt.getTime() > dayAgo && p.paidAt.getTime() <= t);
  if (!newRequests.length && !newPcs.length && !ended.length && !ending.length && !birthdays.length && !paid.length) return null;

  const date = d.now.toLocaleDateString("ru-RU", { timeZone: "Asia/Tashkent", weekday: "long", day: "numeric", month: "long" });
  const lines = [`☀️ <b>Strassio · ${date}</b>`];
  if (newRequests.length || waiting) lines.push(`Заявки за сутки: ${newRequests.length}${waiting ? ` · ждут ответа: ${waiting}` : ""}`);
  if (newPcs.length) lines.push(`Новые активации: ${newPcs.length}`);
  if (ended.length) lines.push(`Закончился пробный: ${ended.length}${notBought ? ` · без покупки: ${notBought}` : ""}`);
  if (paid.length) lines.push(`💰 Оплаты за сутки: ${paid.length} · ${sumsText(paid.reduce((sum, p) => add(sum, p.currency, p.amount), zero()))}`);
  if (ending.length) {
    lines.push("", `⏳ <b>Ключи на исходе (${REMIND_DAYS} дней и меньше)</b>`);
    for (const l of ending.slice(0, 20)) {
      const left = Math.max(1, Math.ceil((l.expiresAt!.getTime() - t) / DAY));
      lines.push(`• ${esc(nameOf(l.client) || l.serial)}${l.client.phone ? ", " + esc(l.client.phone) : ""} — ${left} дн.`);
    }
    if (ending.length > 20) lines.push(`…и ещё ${ending.length - 20}`);
  }
  if (birthdays.length) {
    lines.push("", "🎂 <b>День рождения сегодня</b>");
    for (const b of birthdays) {
      lines.push(`• ${esc(b.name)}${b.turns ? ` (${b.turns})` : ""}${b.phone ? ", " + esc(b.phone) : ""}${b.telegram ? ", @" + esc(b.telegram) : ""}`);
      lines.push(`  <i>${esc(greeting(b.name))}</i>`);
    }
  }
  lines.push("", `<a href="${d.hubUrl}/admin/strassio">Открыть хаб</a>`);
  return lines.join("\n");
}
