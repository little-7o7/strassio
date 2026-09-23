// Локальный сервер для проверки на своём компьютере — без Vercel и без базы (данные в памяти,
// пропадают при остановке). Запуск: npm run local
//   PORT                — порт (по умолчанию 8787)
//   LICENSE_PRIVATE_KEY — ключ подписи; не задан — создаётся одноразовый, открытый ключ печатается
//   ADMIN_PASSWORD      — пароль админки (по умолчанию local-admin-pass; только для этого компьютера)
import { existsSync, readFileSync, statSync } from "node:fs";
import { createServer } from "node:http";
import { extname, join, normalize } from "node:path";
import { generateKeys, Signer } from "../core/crypto.js";
import { App } from "../core/http.js";
import { MemoryStore } from "../core/memoryStore.js";
import { LicenseService } from "../core/service.js";

const port = Number(process.env.PORT) || 8787;
const privateKey = process.env.LICENSE_PRIVATE_KEY || generateKeys().privateKey;
const password = process.env.ADMIN_PASSWORD || "local-admin-pass";
const signer = new Signer(privateKey);
const app = new App(new LicenseService(new MemoryStore(), signer), password);

// Сайт из public/ — как на Vercel: «/» → index.html, «/license» → license.html (cleanUrls).
const PUBLIC = join(process.cwd(), "public");
const TYPES: Record<string, string> = {
  ".html": "text/html; charset=utf-8", ".css": "text/css; charset=utf-8", ".svg": "image/svg+xml",
  ".png": "image/png", ".txt": "text/plain; charset=utf-8",
};

function staticFile(pathname: string): string | null {
  const clean = normalize(decodeURIComponent(pathname)).replace(/^[\\/]+/, "");
  if (clean.startsWith("..")) return null;
  for (const candidate of [clean || "index.html", clean + ".html"]) {
    const file = join(PUBLIC, candidate);
    if (existsSync(file) && statSync(file).isFile()) return file;
  }
  return null;
}

createServer(async (req, res) => {
  const url = new URL(req.url ?? "/", "http://localhost");
  const file = req.method === "GET" && !url.pathname.startsWith("/api/") ? staticFile(url.pathname) : null;
  if (file) {
    res.setHeader("Content-Type", TYPES[extname(file)] ?? "application/octet-stream");
    res.end(readFileSync(file));
    return;
  }

  const chunks: Buffer[] = [];
  for await (const chunk of req) chunks.push(chunk as Buffer);
  let body: unknown = null;
  try {
    body = chunks.length ? JSON.parse(Buffer.concat(chunks).toString("utf8")) : null;
  } catch {
    body = null;
  }

  const headers: Record<string, string | undefined> = {};
  for (const [key, value] of Object.entries(req.headers)) headers[key] = Array.isArray(value) ? value[0] : value;
  const response = await app.handle({ method: req.method ?? "GET", path: url.pathname, query: url.searchParams, headers, body, ip: "local" });

  res.statusCode = response.status;
  if (response.html !== undefined) {
    res.setHeader("Content-Type", "text/html; charset=utf-8");
    res.end(response.html);
  } else {
    res.setHeader("Content-Type", "application/json; charset=utf-8");
    res.end(JSON.stringify(response.json ?? {}));
  }
}).listen(port, "127.0.0.1", () => {
  console.log(`Strassio: локальный сервер http://localhost:${port} (сайт: /, админка: /adminpanel, пароль: ${password})`);
  console.log(`PUBLIC_KEY=${signer.publicKeyXY()}`);
});
