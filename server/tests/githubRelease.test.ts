// «Заполнить из GitHub» в админке: последний выпуск strassio-releases, SHA-256 по самому файлу.
import { test } from "node:test";
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { type Fetcher, latestRelease, whatsNew } from "../core/githubRelease.js";

const REPO = "little-7o7/strassio-releases";
const EXE = Buffer.from("MZ installer bytes");
const SHA = createHash("sha256").update(EXE).digest("hex");
const EXE_URL = `https://github.com/${REPO}/releases/download/v0.9.0/Strassio-Setup-0.9.0.exe`;

const BODY = [
  "Закройте CorelDRAW и запустите **Strassio-Setup-0.9.0.exe** — он встанет поверх прежней версии,",
  "настройки сохранятся.",
  "",
  "**Что нового**",
  "- **Плагин сам сообщает о новой версии.** При запуске и раз в сутки может",
  "  появиться строка «Доступна новая версия».",
  "- Установщики теперь лежат в strassio-releases.",
  "",
  "Пробный период — 14 дней.",
].join("\r\n");

function release(extra: Record<string, unknown> = {}) {
  return {
    tag_name: "v0.9.0",
    body: BODY,
    draft: false,
    prerelease: false,
    assets: [
      { name: "Strassio-Setup-0.9.0.exe", browser_download_url: EXE_URL, digest: "sha256:" + SHA },
      { name: "Strassio-Setup.exe", browser_download_url: `https://github.com/${REPO}/releases/download/v0.9.0/Strassio-Setup.exe` },
    ],
    ...extra,
  };
}

/** Подменный fetch: адрес → ответ; всё остальное — 404. Запоминает, что спрашивали. */
function fake(routes: Record<string, () => Response>): Fetcher & { calls: string[] } {
  const calls: string[] = [];
  const f = (async (url: string) => {
    calls.push(url);
    const route = routes[url];
    return route ? route() : new Response("nope", { status: 404 });
  }) as Fetcher & { calls: string[] };
  f.calls = calls;
  return f;
}

const api = (data: unknown) => () => Response.json(data);
const file = (bytes: Buffer) => () => new Response(new Uint8Array(bytes), { status: 200 });

test("stable: версия, ссылка, SHA-256 по файлу и «что нового» одним списком", async () => {
  const f = fake({
    [`https://api.github.com/repos/${REPO}/releases/latest`]: api(release()),
    [EXE_URL]: file(EXE),
  });
  const result = await latestRelease("stable", f, REPO, undefined);
  assert.ok(result.ok);
  assert.deepEqual(result.release, {
    tag: "v0.9.0",
    version: "0.9.0",
    url: EXE_URL,
    sha256: SHA,
    notesRu: "• Плагин сам сообщает о новой версии. При запуске и раз в сутки может появиться строка «Доступна новая версия».\n• Установщики теперь лежат в strassio-releases.",
  });
});

test("хеш GitHub не совпал с файлом — не заполнять", async () => {
  const f = fake({
    [`https://api.github.com/repos/${REPO}/releases/latest`]: api(release()),
    [EXE_URL]: file(Buffer.from("подменённый файл")),
  });
  assert.deepEqual(await latestRelease("stable", f, REPO, undefined), { ok: false, error: "digest_mismatch" });
});

test("API недоступен (лимит) — тег из перенаправления …/releases/latest, без «что нового»", async () => {
  const f = fake({
    [`https://api.github.com/repos/${REPO}/releases/latest`]: () => new Response("rate limit", { status: 403 }),
    [`https://github.com/${REPO}/releases/latest`]: () =>
      new Response(null, { status: 302, headers: { location: `https://github.com/${REPO}/releases/tag/v0.9.0` } }),
    [EXE_URL]: file(EXE),
  });
  const result = await latestRelease("stable", f, REPO, undefined);
  assert.ok(result.ok);
  assert.equal(result.release.url, EXE_URL);
  assert.equal(result.release.sha256, SHA);
  assert.equal(result.release.notesRu, "");
});

test("beta: самый свежий выпуск, в том числе пробный; черновики пропускаются", async () => {
  const betaUrl = `https://github.com/${REPO}/releases/download/v1.0.0-beta.1/Strassio-Setup-1.0.0-beta.1.exe`;
  const f = fake({
    [`https://api.github.com/repos/${REPO}/releases?per_page=10`]: api([
      { tag_name: "v1.0.0-beta.2", draft: true, assets: [] },
      { tag_name: "v1.0.0-beta.1", draft: false, prerelease: true, body: "", assets: [{ name: "Strassio-Setup-1.0.0-beta.1.exe", browser_download_url: betaUrl }] },
    ]),
    [betaUrl]: file(EXE),
  });
  const result = await latestRelease("beta", f, REPO, undefined);
  assert.ok(result.ok);
  assert.equal(result.release.version, "1.0.0-beta.1");
});

test("нет установщика, чужая ссылка, странный тег, GitHub молчит", async () => {
  const latest = `https://api.github.com/repos/${REPO}/releases/latest`;
  assert.deepEqual(await latestRelease("stable", fake({ [latest]: api(release()) }), REPO, undefined), { ok: false, error: "installer_not_found" });

  const foreign = release({ assets: [{ name: "Strassio-Setup-0.9.0.exe", browser_download_url: "https://evil.example/x.exe" }] });
  assert.deepEqual(await latestRelease("stable", fake({ [latest]: api(foreign) }), REPO, undefined), { ok: false, error: "bad_url" });

  assert.deepEqual(await latestRelease("stable", fake({ [latest]: api(release({ tag_name: "latest" })) }), REPO, undefined), { ok: false, error: "bad_tag" });
  assert.deepEqual(await latestRelease("stable", fake({}), REPO, undefined), { ok: false, error: "github_unavailable" });
});

test("токен GitHub (если задан) уходит только в API", async () => {
  const seen: Array<[string, string | undefined]> = [];
  const f: Fetcher = async (url, init) => {
    seen.push([url, new Headers(init?.headers).get("authorization") ?? undefined]);
    if (url.includes("api.github.com")) return Response.json(release());
    return new Response(new Uint8Array(EXE));
  };
  assert.ok((await latestRelease("stable", f, REPO, "secret")).ok);
  assert.deepEqual(seen, [
    [`https://api.github.com/repos/${REPO}/releases/latest`, "Bearer secret"],
    [EXE_URL, undefined],
  ]);
});

test("«что нового»: без раздела — весь текст без звёздочек", () => {
  assert.equal(whatsNew("Просто **текст**\nвторая строка"), "Просто текст\nвторая строка");
  assert.equal(whatsNew("**Что нового**\n\n- раз\n- два\n\nхвост"), "• раз\n• два");
});
