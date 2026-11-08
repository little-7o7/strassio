// Сообщения владельцу (заявка с сайта, новая активация). Своего бота у Strassio нет (владелец 2026-11-08 09:35:
// «сделай через админку, чтобы каждый раз не соединять»): сообщение уходит подписанным запросом в хаб little_7o7,
// а хаб шлёт его своим ботом. Подпись — той же парой PLATFORM_API_KEY / PLATFORM_API_SECRET, что и запросы хаба
// к Strassio (platformAuth.ts). В подписанной строке путь /api/notify/strassio, поэтому подпись не подходит
// ни к одному адресу /api/platform. Нет пары — сообщений нет, всё остальное работает как раньше.
import { randomBytes } from "node:crypto";
import { canonical, signWith, type PlatformKeys } from "./platformAuth.js";

/** Отправить сообщение (HTML Telegram). true — хаб принял. Ошибка сети или отказ — false, без исключений. */
export type Notifier = (html: string) => Promise<boolean>;

/** Путь в хабе, куда Strassio шлёт сообщения владельцу. */
export const NOTIFY_PATH = "/api/notify/strassio";
/** Telegram принимает до 4096 знаков; хаб добавляет строку «Strassio». */
const MAX_TEXT = 4000;

export function hubNotifier(hub: string, keys: PlatformKeys | null, fetchImpl: typeof fetch = fetch, timeoutMs = 4000, now = () => Date.now()): Notifier | null {
  if (!keys) return null;
  return async (html) => {
    try {
      const body = JSON.stringify({ text: html.slice(0, MAX_TEXT) });
      const time = String(Math.floor(now() / 1000));
      const nonce = randomBytes(18).toString("base64url");
      const signature = signWith(keys.secret, canonical("POST", NOTIFY_PATH, time, nonce, body));
      const r = await fetchImpl(hub + NOTIFY_PATH, {
        method: "POST",
        headers: { "content-type": "application/json", "x-hub-key": keys.key, "x-hub-time": time, "x-hub-nonce": nonce, "x-hub-signature": signature },
        body,
        redirect: "error",
        signal: AbortSignal.timeout(timeoutMs),
      });
      return r.ok;
    } catch {
      return false;
    }
  };
}

/** Адрес хаба для ссылок в сообщениях и для самих сообщений: только https, без «/» в конце. */
export function hubUrl(value: string | undefined): string {
  const v = (value ?? "").trim().replace(/\/+$/, "");
  return /^https:\/\/[A-Za-z0-9.-]+(:\d+)?$/.test(v) ? v : "https://little-7o7.vercel.app";
}
