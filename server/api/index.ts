// Адаптер Vercel: единственная функция, все пути (/api/*, /adminpanel) приходят сюда через vercel.json.
// Вся логика — в server/core (SPEC 15: при переезде на Cloudflare меняется только этот файл).
import type { IncomingMessage, ServerResponse } from "node:http";
import { neon } from "@neondatabase/serverless";
import { Signer } from "../core/crypto.js";
import { App } from "../core/http.js";
import { PgStore } from "../core/pgStore.js";
import { Platform } from "../core/platform.js";
import { platformKeys } from "../core/platformAuth.js";
import { LicenseService } from "../core/service.js";

let app: App | null = null;

function getApp(): App {
  if (app) return app;
  const databaseUrl = process.env.DATABASE_URL;
  const privateKey = process.env.LICENSE_PRIVATE_KEY;
  if (!databaseUrl || !privateKey) {
    throw new Error("Не заданы переменные окружения DATABASE_URL и/или LICENSE_PRIVATE_KEY");
  }

  const sql = neon(databaseUrl);
  const store = new PgStore((text, params) => sql.query(text, params ?? []) as Promise<Record<string, any>[]>);
  const signer = new Signer(privateKey);
  // Хаб little_7o7 — закрытый API /api/platform: выключен (404), пока в Vercel нет PLATFORM_API_KEY и PLATFORM_API_SECRET.
  const platform = new Platform({
    service: new LicenseService(store, signer, undefined, "hub"),
    keys: platformKeys(process.env.PLATFORM_API_KEY, process.env.PLATFORM_API_SECRET),
    adminPassword: process.env.ADMIN_PASSWORD,
    spendNonce: (nonce, until, now) => store.spendNonce(nonce, until, now),
  });
  app = new App(new LicenseService(store, signer), process.env.ADMIN_PASSWORD).usePlatform(platform);
  return app;
}

/**
 * Тело запроса: разобранное (для логики) и текстом (по нему хаб считает подпись). Vercel может уже разобрать JSON
 * сам (req.body) — тогда текст восстанавливается JSON.stringify: хаб шлёт ровно JSON.stringify своего объекта, и
 * разбор с обратной записью даёт тот же текст.
 */
async function readBody(req: IncomingMessage & { body?: unknown }): Promise<{ body: unknown; raw: string }> {
  if (req.body !== undefined) {
    if (typeof req.body === "string") return { body: tryJson(req.body), raw: req.body };
    if (Buffer.isBuffer(req.body)) {
      const raw = req.body.toString("utf8");
      return { body: tryJson(raw), raw };
    }
    return { body: req.body, raw: req.body === null ? "" : JSON.stringify(req.body) };
  }

  const chunks: Buffer[] = [];
  let size = 0;
  for await (const chunk of req) {
    size += (chunk as Buffer).length;
    if (size > 64 * 1024) return { body: null, raw: "" }; // запросы плагина и хаба крошечные
    chunks.push(chunk as Buffer);
  }

  const raw = Buffer.concat(chunks).toString("utf8");
  return { body: tryJson(raw), raw };
}

function tryJson(text: string): unknown {
  try {
    return text ? JSON.parse(text) : null;
  } catch {
    return null;
  }
}

export default async function handler(req: IncomingMessage & { body?: unknown }, res: ServerResponse) {
  const url = new URL(req.url ?? "/", "http://localhost");
  // vercel.json переписывает путь в ?__path=…, настоящий путь восстанавливаем отсюда.
  const rewritten = url.searchParams.get("__path");
  const path = rewritten !== null ? "/" + rewritten.replace(/^\/+/, "") : url.pathname;
  url.searchParams.delete("__path");

  const headers: Record<string, string | undefined> = {};
  for (const [key, value] of Object.entries(req.headers)) headers[key] = Array.isArray(value) ? value[0] : value;
  const ip = (headers["x-forwarded-for"] ?? "").split(",")[0].trim() || req.socket.remoteAddress || "unknown";

  let response;
  try {
    const { body, raw } = await readBody(req);
    response = await getApp().handle({ method: req.method ?? "GET", path, query: url.searchParams, headers, body, rawBody: raw, ip });
  } catch (error) {
    console.error(error);
    response = { status: 500, json: { ok: false, error: "server_not_configured" } };
  }

  res.statusCode = response.status;
  res.setHeader("Cache-Control", "no-store");
  res.setHeader("X-Content-Type-Options", "nosniff");
  if (response.html !== undefined) {
    res.setHeader("Content-Type", "text/html; charset=utf-8");
    res.setHeader("X-Frame-Options", "DENY");
    res.end(response.html);
  } else {
    res.setHeader("Content-Type", "application/json; charset=utf-8");
    res.end(JSON.stringify(response.json ?? {}));
  }
}
