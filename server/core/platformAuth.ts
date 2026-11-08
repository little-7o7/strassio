// Подпись запросов хаба little_7o7 к закрытому API /api/platform (владелец 2026-11-08: админка Strassio переезжает
// в хаб). Так же, как в sarpobusiness (src/lib/platform-auth.ts), чтобы хаб говорил со всеми проектами одинаково.
//
// Пара ключей — в переменных Vercel PLATFORM_API_KEY и PLATFORM_API_SECRET (каждый не короче 32 знаков, разные);
// нет пары — API выключен и на любой адрес отвечает 404. Сам secret по сети не ходит: хаб подписывает им запрос.
//
// Заголовки:
//   X-Hub-Key        — PLATFORM_API_KEY;
//   X-Hub-Time       — время запроса, секунды Unix; дальше 5 минут от часов сервера — отказ;
//   X-Hub-Nonce      — случайная строка 16–64 знака [A-Za-z0-9_-], второй раз не принимается;
//   X-Hub-Signature  — HMAC-SHA256 (ключ PLATFORM_API_SECRET), hex, от строки
//                      «МЕТОД\nпуть?запрос\nвремя\nnonce\nsha256(тело) hex».
// Ключ, время и подпись проверяются в памяти; базу (отметку nonce) трогает только верно подписанный запрос.
import { createHash, createHmac, timingSafeEqual } from "node:crypto";

export const WINDOW_SEC = 300;
const MIN_LEN = 32;
const NONCE = /^[A-Za-z0-9_-]{16,64}$/;

export interface PlatformKeys {
  key: string;
  secret: string;
}

const sha = (s: string) => createHash("sha256").update(s).digest();
/** Сравнение без утечки по времени: сравниваются хэши одинаковой длины. */
export const same = (a: string, b: string) => timingSafeEqual(sha(a), sha(b));

/** Пара из переменных окружения. Пробел или перенос по краям легко вставить в поле Vercel — не считаем их. */
export function platformKeys(key: string | undefined, secret: string | undefined): PlatformKeys | null {
  const k = (key ?? "").trim();
  const s = (secret ?? "").trim();
  return k.length >= MIN_LEN && s.length >= MIN_LEN && k !== s ? { key: k, secret: s } : null;
}

export const canonical = (method: string, pathAndQuery: string, time: string, nonce: string, body: string) =>
  [method.toUpperCase(), pathAndQuery, time, nonce, createHash("sha256").update(body).digest("hex")].join("\n");

export const signWith = (secret: string, text: string) => createHmac("sha256", secret).update(text).digest("hex");

export interface SignedInput {
  method: string;
  /** Путь с запросом, как его подписал хаб: /api/platform/licenses?q=abc */
  pathAndQuery: string;
  headers: Record<string, string | undefined>;
  body: string;
}

/** Подпись верна (в памяти, без базы). Ответ — nonce и до какого времени его помнить; иначе null. */
export function verifySignature(keys: PlatformKeys | null, input: SignedInput, nowSec: number): { nonce: string; until: number } | null {
  if (!keys) return null;
  const h = input.headers;
  const time = h["x-hub-time"] ?? "";
  const nonce = h["x-hub-nonce"] ?? "";
  const signature = (h["x-hub-signature"] ?? "").toLowerCase();
  if (!same(h["x-hub-key"] ?? "", keys.key)) return null;
  if (!/^\d{9,11}$/.test(time) || Math.abs(nowSec - Number(time)) > WINDOW_SEC) return null;
  if (!NONCE.test(nonce) || !/^[0-9a-f]{64}$/.test(signature)) return null;
  const expected = signWith(keys.secret, canonical(input.method, input.pathAndQuery, time, nonce, input.body));
  if (!same(signature, expected)) return null;
  // Помним дольше окна: запрос с тем же nonce и временем на краю окна тоже не пройдёт.
  return { nonce, until: Number(time) + WINDOW_SEC * 2 };
}
