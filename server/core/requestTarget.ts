// Адрес запроса, каким его отправил клиент, из того, что приходит в функцию Vercel (api/index.ts).
//
// vercel.json переписывает /api/:path* в /api?__path=api/:path*. Vercel при этом сам дописывает именованный кусок
// ещё раз: правило собирается в «/api?__path=api%2F$1&path=$1» (@vercel/routing-utils), а запрос клиента идёт
// следом. Подпись хаба считается от «путь?запрос» без этих добавок — их надо убрать, иначе /api/platform отвечает
// 404 на верные ключи (так было 2026-11-08). Свой параметр path клиента (другое значение) остаётся.
export function requestTarget(rawUrl: string | undefined): { path: string; query: URLSearchParams } {
  const url = new URL(rawUrl ?? "/", "http://localhost");
  const rewritten = url.searchParams.get("__path");
  if (rewritten === null) return { path: url.pathname, query: url.searchParams };
  url.searchParams.delete("__path");
  url.searchParams.delete("path", rewritten.replace(/^\/*api(?:\/|$)/, ""));
  return { path: "/" + rewritten.replace(/^\/+/, ""), query: url.searchParams };
}
