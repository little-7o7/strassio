// Локальный сервер для проверки на своём компьютере — без Vercel и без базы (данные в памяти,
// пропадают при остановке). Запуск: npm run local
//   PORT                — порт (по умолчанию 8787)
//   LICENSE_PRIVATE_KEY — ключ подписи; не задан — создаётся одноразовый, открытый ключ печатается
//   ADMIN_PASSWORD      — пароль админки (по умолчанию local-admin-pass; только для этого компьютера)
import { createServer } from "node:http";
import { generateKeys, Signer } from "../core/crypto.js";
import { App } from "../core/http.js";
import { MemoryStore } from "../core/memoryStore.js";
import { LicenseService } from "../core/service.js";

const port = Number(process.env.PORT) || 8787;
const privateKey = process.env.LICENSE_PRIVATE_KEY || generateKeys().privateKey;
const password = process.env.ADMIN_PASSWORD || "local-admin-pass";
const signer = new Signer(privateKey);
const app = new App(new LicenseService(new MemoryStore(), signer), password);

createServer(async (req, res) => {
  const url = new URL(req.url ?? "/", "http://localhost");
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
  console.log(`Strassio: локальный сервер http://localhost:${port} (админка: /admin, пароль: ${password})`);
  console.log(`PUBLIC_KEY=${signer.publicKeyXY()}`);
});
