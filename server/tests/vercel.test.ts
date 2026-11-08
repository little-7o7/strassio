// Адаптер Vercel: какой адрес на самом деле приходит в функцию. vercel.json переписывает /api/:path* в
// /api?__path=api/:path*, а Vercel (@vercel/routing-utils) сам дописывает ещё и «&path=…» — 2026-11-08 из-за этого
// подпись хаба не сходилась и /api/platform отвечал 404 на верные ключи.
import { test } from "node:test";
import assert from "node:assert/strict";
import { createHash, createHmac, randomBytes } from "node:crypto";
import { requestTarget } from "../core/requestTarget.js";
import { MemoryStore } from "../core/memoryStore.js";
import { generateKeys, Signer } from "../core/crypto.js";
import { Platform } from "../core/platform.js";
import { platformKeys } from "../core/platformAuth.js";
import { LicenseService } from "../core/service.js";

// Так Vercel собирает наш rewrite: «/api?__path=api%2F$1&path=$1», запрос клиента — следом.
const vercel = (rest: string, query = "") => `/api?__path=api%2F${encodeURIComponent(rest)}&path=${encodeURIComponent(rest)}${query ? "&" + query : ""}`;

test("путь и запрос — как их отправил клиент, без __path и без копии path от Vercel", () => {
  const a = requestTarget(vercel("platform/ping"));
  assert.equal(a.path, "/api/platform/ping");
  assert.equal(a.query.toString(), "");

  const b = requestTarget(vercel("platform/audit", "limit=200&who=hub&serial=STR-AB12"));
  assert.equal(b.path, "/api/platform/audit");
  assert.equal(b.query.toString(), "limit=200&who=hub&serial=STR-AB12");

  const c = requestTarget("/api?__path=adminpanel");
  assert.equal(c.path, "/adminpanel");
  assert.equal(c.query.toString(), "");
});

test("свой параметр path клиента не теряется; без rewrite (локальный сервер) адрес не меняется", () => {
  const a = requestTarget(vercel("platform/x", "path=other"));
  assert.equal(a.query.toString(), "path=other");
  const b = requestTarget("/api/platform/ping?a=1");
  assert.equal(b.path, "/api/platform/ping");
  assert.equal(b.query.toString(), "a=1");
  assert.equal(requestTarget(undefined).path, "/");
});

test("подпись хаба сходится на адресе, который присылает Vercel", async () => {
  const KEY = "k".repeat(40);
  const SECRET = "s".repeat(48);
  const store = new MemoryStore();
  const now = Date.parse("2026-11-08T10:00:00Z");
  const platform = new Platform({
    service: new LicenseService(store, new Signer(generateKeys().privateKey), () => new Date(now), "hub"),
    keys: platformKeys(KEY, SECRET),
    adminPassword: "admin-pass-123",
    spendNonce: (n, until, s) => store.spendNonce(n, until, s),
    now: () => now,
  });
  const call = async (signed: string, url: string) => {
    const time = String(Math.floor(now / 1000));
    const nonce = randomBytes(18).toString("base64url");
    const text = ["GET", signed, time, nonce, createHash("sha256").update("").digest("hex")].join("\n");
    const headers = { "x-hub-key": KEY, "x-hub-time": time, "x-hub-nonce": nonce, "x-hub-signature": createHmac("sha256", SECRET).update(text).digest("hex") };
    const { path, query } = requestTarget(url);
    return platform.handle({ method: "GET", path, query, headers, body: null, rawBody: "", ip: "1.1.1.1" });
  };
  const ping = await call("/api/platform/ping", vercel("platform/ping"));
  assert.equal(ping.status, 200);
  assert.equal((ping.json as { project?: string }).project, "strassio");
  const audit = await call("/api/platform/audit?limit=5&who=hub", vercel("platform/audit", "limit=5&who=hub"));
  assert.equal(audit.status, 200);
});
