// Маршруты сайта и плагина, ограничение частоты. Старой админки (/adminpanel, /api/admin/*) больше нет —
// владелец 2026-11-08: всё в хабе little_7o7 (тесты хаба — platform.test.ts).
import { test } from "node:test";
import assert from "node:assert/strict";
import { generateKeys, Signer } from "../core/crypto.js";
import { App, HttpRequest, RateLimiter } from "../core/http.js";
import { MemoryStore } from "../core/memoryStore.js";
import { LicenseService } from "../core/service.js";

const keys = generateKeys();

function makeApp() {
  const service = new LicenseService(new MemoryStore(), new Signer(keys.privateKey));
  return { app: new App(service), service };
}

function req(method: string, path: string, body?: unknown, auth?: string, ip = "1.1.1.1"): HttpRequest {
  const [p, q] = path.split("?");
  return { method, path: p, query: new URLSearchParams(q ?? ""), headers: auth ? { authorization: "Bearer " + auth } : {}, body, ip };
}

test("полный путь: ключ → активация плагином → проверка", async () => {
  const { app, service } = makeApp();
  const [{ serial }] = await service.createKeys({ count: 1 });
  const hwid = ["a", "b", "c", "d"].map((x) => x.repeat(16)).join(".");

  assert.equal((await app.handle(req("POST", "/api/activate", { serial, hwid }))).status, 403, "без почты ключ не активируется");
  const activated = await app.handle(req("POST", "/api/activate", { serial, hwid, email: "client@example.com" }));
  assert.equal(activated.status, 200);
  assert.equal((await app.handle(req("POST", "/api/check", { serial, hwid }))).status, 200);
  assert.equal((await app.handle(req("POST", "/api/activate", { serial: "плохо", hwid }))).status, 400);
  assert.equal((await app.handle(req("GET", "/api/activate"))).status, 405);

  const [license] = await service.search(serial);
  assert.equal(license.activations.length, 1);
});

test("старой админки нет: /adminpanel и /api/admin/* — как несуществующий адрес, даже с паролем", async () => {
  const { app } = makeApp();
  for (const r of [
    req("GET", "/adminpanel"),
    req("POST", "/api/admin/login", {}, "correct-horse-battery"),
    req("POST", "/api/admin/keys", { count: 2 }, "correct-horse-battery"),
    req("GET", "/api/admin/licenses?q=STRS", undefined, "correct-horse-battery"),
  ]) {
    const res = await app.handle(r);
    assert.equal(res.status, 404, r.path);
    assert.equal(res.html, undefined);
    assert.equal((res.json as { error: string }).error, "no_route", "не not_found: на него плагин удаляет лицензию");
  }
  assert.equal((await app.handle(req("GET", "/api/nope"))).status, 404);
  assert.equal((await app.handle(req("GET", "/api/update/latest"))).status, 404);
});

test("ограничение частоты: лимит на адрес, окно сдвигается", () => {
  let t = 0;
  const limiter = new RateLimiter(3, 1000, () => t);
  assert.ok(limiter.allow("a") && limiter.allow("a") && limiter.allow("a"));
  assert.ok(!limiter.allow("a"));
  assert.ok(limiter.allow("b"));
  t = 1001;
  assert.ok(limiter.allow("a"));
});

test("сайт: «Моя лицензия» по одному ключу; после 10 несуществующих ключей адрес ждёт", async () => {
  const { app, service } = makeApp();
  const [key] = await service.createKeys({ count: 1 });
  const good = await app.handle(req("POST", "/api/mylicense/lookup", { serial: key.serial, email: "client@example.com" }, undefined, "7.7.7.7"));
  assert.equal(good.status, 200);

  for (let i = 0; i < 10; i++) {
    assert.equal((await app.handle(req("POST", "/api/mylicense/lookup", { serial: "STRS-AAAA-BBBB-CCCC", email: "client@example.com" }, undefined, "8.8.8.8"))).status, 403);
  }
  // Перебор ключей бессмыслен: даже с верным ключом — подождать.
  assert.equal((await app.handle(req("POST", "/api/mylicense/lookup", { serial: key.serial, email: "client@example.com" }, undefined, "8.8.8.8"))).status, 429);
  assert.equal((await app.handle(req("GET", "/api/mylicense/lookup"))).status, 405);
  assert.equal((await app.handle(req("POST", "/api/recovery/lookup", { serial: key.serial }))).status, 404, "старого адреса нет");
});

test("сайт: скрипты страниц «Моя лицензия» и «Активация» без синтаксических ошибок", async () => {
  const { readFileSync } = await import("node:fs");
  for (const name of ["license.html", "activate.html", "buy.html"]) {
    const page = readFileSync(new URL("../../public/" + name, import.meta.url), "utf8");
    const script = page.substring(page.indexOf("<script>") + 8, page.lastIndexOf("</script>"));
    assert.doesNotThrow(() => new Function(script), name);
  }
});

test("сайт: заявка — не больше 5 в час с одного адреса; бот (поле website) ничего не сохраняет", async () => {
  const { app, service } = makeApp();
  const client = { firstName: "Иван", lastName: "Петров", phone: "+7 900 000 00 00" };
  const bot = await app.handle(req("POST", "/api/site/request", { client, website: "http://spam" }, undefined, "9.9.9.9"));
  assert.equal(bot.status, 200);
  assert.equal((await service.listRequests()).length, 0);

  const statuses = [];
  for (let i = 0; i < 6; i++) statuses.push((await app.handle(req("POST", "/api/site/request", { client }, undefined, "8.8.8.8"))).status);
  assert.deepEqual(statuses, [200, 200, 200, 200, 200, 429]);
  assert.equal((await app.handle(req("POST", "/api/site/request", { client: { firstName: "x" } }, undefined, "7.7.7.7"))).status, 400);

  const [first] = await service.listRequests();
  const made = await service.requestKey(first.id, null);
  assert.match(made!.serial, /^STRS-/);
});

test("сайт: подбор почты к чужому ключу упирается в лимит (10 неверных за 15 минут)", async () => {
  const { app, service } = makeApp();
  const [{ serial }] = await service.createKeys({ count: 1, client: { email: "owner@example.com" } });
  const hwid = "a100000000000000.b100000000000000.c100000000000000.d100000000000000";
  for (let i = 0; i < 10; i++) {
    const r = await app.handle(req("POST", "/api/site/activate", { serial, hwid, email: "guess" + i + "@example.com" }, undefined, "6.6.6.6"));
    assert.equal((r.json as any).error, "wrong_email");
  }
  const blocked = await app.handle(req("POST", "/api/site/activate", { serial, hwid, email: "owner@example.com" }, undefined, "6.6.6.6"));
  assert.equal(blocked.status, 429);
  const owner = await app.handle(req("POST", "/api/site/activate", { serial, hwid, email: "owner@example.com" }, undefined, "5.5.5.5"));
  assert.equal(owner.status, 200);
});

test("сайт: галочка «нет интернета» доходит до сервера; «Освободить» из плагина на таком ПК — отказ", async () => {
  const { app, service } = makeApp();
  const [{ serial }] = await service.createKeys({ count: 1, client: { email: "owner@example.com" } });
  const hwid = "a100000000000000.b100000000000000.c100000000000000.d100000000000000";
  const r = await app.handle(req("POST", "/api/site/activate", { serial, hwid, email: "owner@example.com", offline: true }));
  assert.equal(r.status, 200);
  const json = r.json as { offline: boolean; code: string; file: { payload: string } };
  assert.equal(json.offline, true);
  assert.equal(JSON.parse(json.file.payload).offline, true);
  assert.match(json.code, /^SA1\./);

  const release = await app.handle(req("POST", "/api/deactivate", { serial, hwid }));
  assert.equal(release.status, 403);
  assert.equal((release.json as { error: string }).error, "offline_seat");
  const offline = await app.handle(req("POST", "/api/mylicense/offline", { serial, hwid, email: "owner@example.com" }));
  assert.match((offline.json as { code: string }).code, /^SA1\./);
});
