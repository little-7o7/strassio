// Адаптер Vercel: единственная функция, все пути (/api/*, /adminpanel) приходят сюда через vercel.json.
// Вся логика — в server/core (SPEC 15: при переезде на Cloudflare меняется только этот файл).
import type { IncomingMessage, ServerResponse } from "node:http";
import { neon } from "@neondatabase/serverless";
import { Signer } from "../core/crypto.js";
import { App } from "../core/http.js";
import { PgStore } from "../core/pgStore.js";
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
  app = new App(new LicenseService(store, new Signer(privateKey)), process.env.ADMIN_PASSWORD);
  return app;
}

async function readBody(req: IncomingMessage & { body?: unknown }): Promise<unknown> {
  if (req.body !== undefined) {
    if (typeof req.body === "string") return tryJson(req.body);
    return req.body;
  }

  const chunks: Buffer[] = [];
  let size = 0;
  for await (const chunk of req) {
    size += (chunk as Buffer).length;
    if (size > 64 * 1024) return null; // запросы плагина крошечные
    chunks.push(chunk as Buffer);
  }

  return tryJson(Buffer.concat(chunks).toString("utf8"));
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
    response = await getApp().handle({ method: req.method ?? "GET", path, query: url.searchParams, headers, body: await readBody(req), ip });
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
