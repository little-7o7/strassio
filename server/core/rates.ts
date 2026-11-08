// Курс доллара ЦБ Узбекистана на день — для оплаты сумами по тарифу в $ и итогов «всё в сумах / всё в $».
// Владелец 2026-11-08: «курс ЦБ подставляется сам, но вручную тоже пишем — договорённый курс».
// Серверам Vercel cbu.uz не отвечает совсем (проверено в sarpobusiness 2026-10-24 из Франкфурта, Вашингтона и Гонконга),
// а узбекские зеркала курса ЦБ — отвечают. Поэтому спрашиваем всех сразу и берём первый ответ: depozit.uz (курс ЦБ на
// сегодня), AllRatesToday (таблица ЦБ, бывает на день позже — тогда не берём), cbu.uz (вдруг откроется). Хаб может сам
// прислать курс дня (POST /api/platform/rates) — тогда никого не спрашиваем.

/** Сумов за доллар на день ГГГГ-ММ-ДД; null — никто не ответил или ответ непонятный (тогда берётся последний известный). */
export type RateSource = (day: string) => Promise<number | null>;

type Fetch = (url: string, init: { signal: AbortSignal; headers: Record<string, string> }) => Promise<{ ok: boolean; json(): Promise<unknown> }>;

/** Курс доллара правдоподобный: от 100 до миллиона сумов (в 2026 — около 12 000). */
export function saneRate(n: unknown): number | null {
  const value = Math.round(Number(n) * 100) / 100;
  return Number.isFinite(value) && value > 100 && value < 1_000_000 ? value : null;
}

/** «11,903.15» → 11903.15 (depozit.uz пишет тысячи через запятую), «11987,16» → 11987.16. */
function num(v: unknown): number {
  const s = String(v ?? "").trim();
  return Number(s.includes(".") ? s.replace(/,/g, "") : s.replace(",", "."));
}

/** Ответ ЦБ: [{ "Ccy": "USD", "Nominal": "1", "Rate": "11987.16", "Date": "07.11.2026" }]. */
export function parseCbuRate(body: unknown): number | null {
  const row = Array.isArray(body) ? (body[0] as Record<string, unknown> | undefined) : undefined;
  if (!row || row.Ccy !== "USD") return null;
  return saneRate(num(row.Rate) / (Number(row.Nominal ?? 1) || 1));
}

/** Виджет depozit.uz: { cb: { usd: { key: "USD", rate: "11,903.15" }, … } } — курс ЦБ на сегодня. */
export function parseDepozitRate(body: unknown): number | null {
  const cb = (body as { cb?: Record<string, { key?: unknown; rate?: unknown }> } | null)?.cb;
  const usd = cb && typeof cb === "object" ? Object.values(cb).find((v) => v?.key === "USD") : undefined;
  return usd ? saneRate(num(usd.rate)) : null;
}

/** AllRatesToday: { rate_date: "2026-11-08", rates: [{ base: "USD", quote: "UZS", value: 11987.16 }] }; другой день — null. */
export function parseAllRatesToday(body: unknown, day: string): number | null {
  const j = body as { rate_date?: unknown; rates?: { base?: unknown; quote?: unknown; value?: unknown }[] } | null;
  if (!j || String(j.rate_date ?? "").slice(0, 10) !== day || !Array.isArray(j.rates)) return null;
  const usd = j.rates.find((r) => r?.base === "USD" && r?.quote === "UZS");
  return usd ? saneRate(usd.value) : null;
}

/** Курс на день от первого ответившего источника; ждём не дольше ms — оплата не должна зависать из-за чужого сайта. */
export function rateSources(get: Fetch = fetch as unknown as Fetch, ms = 4000): RateSource {
  return async (day) => {
    const signal = AbortSignal.timeout(ms);
    const headers = { "User-Agent": "Mozilla/5.0 (compatible; Strassio/1.0)", Accept: "application/json" };
    const ask = async (url: string, parse: (body: unknown) => number | null) => {
      const r = await get(url, { signal, headers });
      const rate = r.ok ? parse(await r.json()) : null;
      if (!rate) throw new Error("нет курса");
      return rate;
    };
    const today = dayInTashkent(new Date());
    const asks = [
      ask(`https://cbu.uz/ru/arkhiv-kursov-valyut/json/USD/${day}/`, parseCbuRate),
      ask("https://allratestoday.com/api/open/central-bank/cbu", (b) => parseAllRatesToday(b, day)),
    ];
    // depozit.uz знает только сегодняшний курс.
    if (day === today) asks.push(ask("https://depozit.uz/api/rates/widget?widget_size=lg", parseDepozitRate));
    return Promise.any(asks).catch(() => null);
  };
}

const dayInTashkent = (d: Date) => new Date(d.getTime() + 5 * 3600_000).toISOString().slice(0, 10);

/** Курс ЦБ на сервере: все источники сразу, первый ответ. */
export const cbuRate: RateSource = rateSources();
