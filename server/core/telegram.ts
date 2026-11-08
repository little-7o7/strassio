// Сообщения владельцу в Telegram (владелец 2026-11-08: заявка с сайта, напоминания, утренняя сводка).
// Бот пишет только в один чат — владельцу. Токен и чат — только в переменных Vercel (TELEGRAM_BOT_TOKEN,
// TELEGRAM_CHAT_ID); без них бот выключен, всё остальное работает как раньше. Токен нигде не пишется в журнал.

/** Отправить сообщение (HTML Telegram). true — Telegram принял. Ошибка сети или отказ — false, без исключений. */
export type Notifier = (html: string) => Promise<boolean>;

const TOKEN = /^\d{5,15}:[A-Za-z0-9_-]{30,60}$/;
const CHAT = /^-?\d{3,20}$/;

export function telegram(token: string | undefined, chatId: string | undefined, fetchImpl: typeof fetch = fetch, timeoutMs = 4000): Notifier | null {
  const t = (token ?? "").trim();
  const c = (chatId ?? "").trim();
  if (!TOKEN.test(t) || !CHAT.test(c)) return null;
  return async (html) => {
    try {
      const r = await fetchImpl(`https://api.telegram.org/bot${t}/sendMessage`, {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ chat_id: c, text: html.slice(0, 4000), parse_mode: "HTML", link_preview_options: { is_disabled: true } }),
        signal: AbortSignal.timeout(timeoutMs),
      });
      return r.ok;
    } catch {
      return false;
    }
  };
}

/** Адрес хаба для ссылок в сообщениях: только https, без «/» в конце. */
export function hubUrl(value: string | undefined): string {
  const v = (value ?? "").trim().replace(/\/+$/, "");
  return /^https:\/\/[A-Za-z0-9.-]+(:\d+)?$/.test(v) ? v : "https://little-7o7.vercel.app";
}
