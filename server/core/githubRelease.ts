// Последний выпуск из публичного strassio-releases — чтобы в админке («Обновления») не копировать ссылку и
// SHA-256 руками. SHA-256 сервер считает сам по скачанному файлу: подписывается ровно то, что скачает плагин.
import { createHash } from "node:crypto";

export const RELEASES_REPO = "little-7o7/strassio-releases";

/** Тот же формат, что принимает «Опубликовать»: 1.2.3 или пробная 1.2.3-beta.1. */
export const VERSION_PATTERN = /^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$/;

export interface ReleaseDraft {
  tag: string;
  version: string;
  url: string;
  sha256: string;
  notesRu: string;
}

export type ReleaseResult = { ok: true; release: ReleaseDraft } | { ok: false; error: string };

/** Нужная часть fetch: тесты подставляют свою. */
export type Fetcher = (url: string, init?: RequestInit) => Promise<Response>;

const MAX_INSTALLER_BYTES = 200 * 1024 * 1024;

interface ApiAsset {
  name?: unknown;
  browser_download_url?: unknown;
  digest?: unknown;
}

interface ApiRelease {
  tag_name?: unknown;
  body?: unknown;
  draft?: unknown;
  prerelease?: unknown;
  assets?: ApiAsset[];
}

/**
 * stable — последний обычный выпуск (как ссылка …/releases/latest), beta — самый свежий, в том числе пробный.
 * Через API GitHub приходят и тег, и «что нового». Если API недоступен (лимит запросов на общий адрес Vercel),
 * тег берётся из перенаправления …/releases/latest, а «что нового» остаётся пустым.
 */
export async function latestRelease(
  channel: string,
  fetcher: Fetcher = fetch,
  repo: string = RELEASES_REPO,
  token: string | undefined = process.env.GITHUB_TOKEN,
): Promise<ReleaseResult> {
  const beta = channel === "beta";
  let release: ApiRelease | null = null;
  try {
    release = await fromApi(beta, fetcher, repo, token);
  } catch {
    release = null;
  }

  let tag = release && typeof release.tag_name === "string" ? release.tag_name : null;
  if (!tag && !beta) {
    tag = await tagFromRedirect(fetcher, repo).catch(() => null);
  }
  if (!tag) return { ok: false, error: "github_unavailable" };

  const version = tag.replace(/^v/i, "");
  if (!VERSION_PATTERN.test(version)) return { ok: false, error: "bad_tag" };

  const name = `Strassio-Setup-${version}.exe`;
  const prefix = `https://github.com/${repo}/releases/download/`;
  const asset = release?.assets?.find((a) => a.name === name);
  const url = typeof asset?.browser_download_url === "string" ? asset.browser_download_url : prefix + encodeURIComponent(tag) + "/" + name;
  if (!url.startsWith(prefix)) return { ok: false, error: "bad_url" };

  const response = await fetcher(url, { redirect: "follow" });
  if (!response.ok) return { ok: false, error: "installer_not_found" };
  const length = Number(response.headers.get("content-length") ?? 0);
  if (length > MAX_INSTALLER_BYTES) return { ok: false, error: "installer_too_big" };
  const bytes = Buffer.from(await response.arrayBuffer());
  if (bytes.length === 0 || bytes.length > MAX_INSTALLER_BYTES) return { ok: false, error: "installer_not_found" };
  const sha256 = createHash("sha256").update(bytes).digest("hex");

  // GitHub сам хранит хеш файла выпуска — если он есть и не совпал, файл подменили по дороге.
  const digest = typeof asset?.digest === "string" ? asset.digest.toLowerCase() : "";
  if (digest.startsWith("sha256:") && digest.substring(7) !== sha256) return { ok: false, error: "digest_mismatch" };

  const notesRu = typeof release?.body === "string" ? whatsNew(release.body) : "";
  return { ok: true, release: { tag, version, url, sha256, notesRu } };
}

async function fromApi(beta: boolean, fetcher: Fetcher, repo: string, token: string | undefined): Promise<ApiRelease | null> {
  const headers: Record<string, string> = {
    Accept: "application/vnd.github+json",
    "User-Agent": "strassio-server",
    "X-GitHub-Api-Version": "2022-11-28",
  };
  if (token) headers.Authorization = "Bearer " + token;

  const base = `https://api.github.com/repos/${repo}/releases`;
  const response = await fetcher(beta ? base + "?per_page=10" : base + "/latest", { headers });
  if (!response.ok) return null;
  const data = (await response.json()) as unknown;
  if (!beta) return data as ApiRelease;
  return Array.isArray(data) ? ((data as ApiRelease[]).find((r) => r.draft !== true) ?? null) : null;
}

/** github.com/…/releases/latest перенаправляет на …/releases/tag/vX.Y.Z — без API и его лимитов. */
async function tagFromRedirect(fetcher: Fetcher, repo: string): Promise<string | null> {
  const response = await fetcher(`https://github.com/${repo}/releases/latest`, { redirect: "manual" });
  const location = response.headers.get("location") ?? "";
  const match = /\/releases\/tag\/([^/?#]+)$/.exec(location);
  return match ? decodeURIComponent(match[1]) : null;
}

/**
 * Из текста выпуска (docs/releases/vX.Y.Z.md без заголовка) — только список под «Что нового», каждый пункт
 * одной строкой, без звёздочек Markdown. Нет такого раздела — весь текст без звёздочек.
 */
export function whatsNew(body: string): string {
  const lines = body.replace(/\r\n?/g, "\n").split("\n");
  const start = lines.findIndex((l) => /^\s*\**\s*что нового\s*\**\s*:?\s*$/i.test(l));
  let section: string[];
  if (start < 0) {
    section = lines;
  } else {
    section = [];
    for (const line of lines.slice(start + 1)) {
      if (!line.trim()) {
        if (section.length) break;
        continue;
      }
      section.push(line);
    }
  }

  // Строки-продолжения (с отступом) приклеиваются к своему пункту.
  const items: string[] = [];
  for (const line of section) {
    if (/^\s+\S/.test(line) && items.length && !/^\s*[-*]\s/.test(line)) {
      items[items.length - 1] += " " + line.trim();
    } else {
      items.push(line.trim());
    }
  }

  return items
    .map((l) => l.replace(/\*\*/g, "").replace(/^[-*]\s+/, "• "))
    .join("\n")
    .trim();
}
