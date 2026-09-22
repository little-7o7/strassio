// Маршруты, пароль админки, ограничение частоты.
import { test } from "node:test";
import assert from "node:assert/strict";
import { generateKeys, Signer } from "../core/crypto.js";
import { App, HttpRequest, RateLimiter } from "../core/http.js";
import { MemoryStore } from "../core/memoryStore.js";
import { LicenseService } from "../core/service.js";

const keys = generateKeys();
const PASSWORD = "correct-horse-battery";

function makeApp(password: string | undefined = PASSWORD) {
  return new App(new LicenseService(new MemoryStore(), new Signer(keys.privateKey)), password);
}

function req(method: string, path: string, body?: unknown, auth?: string, ip = "1.1.1.1"): HttpRequest {
  const [p, q] = path.split("?");
  return { method, path: p, query: new URLSearchParams(q ?? ""), headers: auth ? { authorization: "Bearer " + auth } : {}, body, ip };
}

test("админка: без пароля 401, с паролем работает, без ADMIN_PASSWORD выключена", async () => {
  const app = makeApp();
  assert.equal((await app.handle(req("POST", "/api/admin/keys", { count: 2 }))).status, 401);
  assert.equal((await app.handle(req("POST", "/api/admin/keys", { count: 2 }, "wrong"))).status, 401);
  const created = await app.handle(req("POST", "/api/admin/keys", { count: 2, note: "тест" }, PASSWORD));
  assert.equal(created.status, 200);
  assert.equal((created.json as { serials: string[] }).serials.length, 2);

  assert.equal((await makeApp("").handle(req("POST", "/api/admin/login", {}, "x"))).status, 503);
  assert.equal((await makeApp("short").handle(req("POST", "/api/admin/login", {}, "short"))).status, 503);
});

test("полный путь: ключ из админки → активация плагином → проверка", async () => {
  const app = makeApp();
  const created = await app.handle(req("POST", "/api/admin/keys", {}, PASSWORD));
  const serial = (created.json as { serials: string[] }).serials[0];
  const hwid = ["a", "b", "c", "d"].map((x) => x.repeat(16)).join(".");

  const activated = await app.handle(req("POST", "/api/activate", { serial, hwid }));
  assert.equal(activated.status, 200);
  assert.equal((await app.handle(req("POST", "/api/check", { serial, hwid }))).status, 200);
  assert.equal((await app.handle(req("POST", "/api/activate", { serial: "плохо", hwid }))).status, 400);
  assert.equal((await app.handle(req("GET", "/api/activate"))).status, 405);

  const list = await app.handle(req("GET", "/api/admin/licenses?q=" + serial, undefined, PASSWORD));
  const licenses = (list.json as { licenses: Array<{ activations: unknown[] }> }).licenses;
  assert.equal(licenses[0].activations.length, 1);
});

test("страница админки и неизвестные пути", async () => {
  const app = makeApp();
  const page = await app.handle(req("GET", "/admin"));
  assert.equal(page.status, 200);
  assert.match(page.html!, /Strassio/);
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

test("перебор паролей админки упирается в лимит", async () => {
  const app = makeApp();
  const statuses = [];
  for (let i = 0; i < 12; i++) statuses.push((await app.handle(req("POST", "/api/admin/login", {}, "guess" + i))).status);
  assert.equal(statuses[0], 401);
  assert.equal(statuses[11], 429);
});

test("админка: пробелы и перенос строки по краям пароля (так вставили в Vercel) не мешают войти", async () => {
  const app = makeApp("  " + PASSWORD + "\n");
  assert.equal((await app.handle(req("POST", "/api/admin/login", {}, PASSWORD))).status, 200);
  assert.equal((await app.handle(req("POST", "/api/admin/login", {}, " " + PASSWORD + " "))).status, 200);
  assert.equal((await app.handle(req("POST", "/api/admin/login", {}, "wrong-password"))).status, 401);
});

test("сайт: восстановление — маршруты работают, после 10 неверных ключей восстановления адрес ждёт", async () => {
  const app = makeApp();
  const created = await app.handle(req("POST", "/api/admin/keys", { count: 1 }, PASSWORD));
  const key = (created.json as { keys: Array<{ serial: string; recoveryCode: string }> }).keys[0];
  const good = await app.handle(req("POST", "/api/recovery/lookup", { serial: key.serial, recovery: key.recoveryCode }, undefined, "7.7.7.7"));
  assert.equal(good.status, 200);

  for (let i = 0; i < 10; i++) {
    assert.equal((await app.handle(req("POST", "/api/recovery/lookup", { serial: key.serial, recovery: "RCV-AAAA-BBBB-CCCC-DDDD" }, undefined, "8.8.8.8"))).status, 403);
  }
  // Даже с верным кодом — подождать (перебор бессмысленен).
  assert.equal((await app.handle(req("POST", "/api/recovery/lookup", { serial: key.serial, recovery: key.recoveryCode }, undefined, "8.8.8.8"))).status, 429);
  assert.equal((await app.handle(req("GET", "/api/recovery/lookup"))).status, 405);
});

test("админка открывается по /adminpanel", async () => {
  const res = await makeApp().handle(req("GET", "/adminpanel"));
  assert.equal(res.status, 200);
  assert.ok(res.html?.includes("Strassio"));
});

test("админка: пароль с русскими буквами и знаками — браузер шлёт его закодированным (encodeURIComponent)", async () => {
  const password = "Стразы2026!@#%";
  const app = makeApp(password);
  assert.equal((await app.handle(req("POST", "/api/admin/login", {}, encodeURIComponent(password)))).status, 200);
  assert.equal((await app.handle(req("POST", "/api/admin/login", {}, encodeURIComponent("Стразы2026")))).status, 401);
  assert.equal((await app.handle(req("POST", "/api/admin/login", {}, "%E0%A4%A"))).status, 401, "битая кодировка — просто неверный пароль");
});

test("админка: скрипт страницы без синтаксических ошибок (иначе кнопка «Войти» не работает)", async () => {
  const page = (await makeApp().handle(req("GET", "/adminpanel"))).html ?? "";
  const script = page.substring(page.indexOf("<script>") + 8, page.lastIndexOf("</script>"));
  assert.ok(script.includes("function login"));
  assert.doesNotThrow(() => new Function(script));
});

test("сайт «Моя лицензия»: скрипт без синтаксических ошибок", async () => {
  const { readFileSync } = await import("node:fs");
  const page = readFileSync(new URL("../../public/license.html", import.meta.url), "utf8");
  const script = page.substring(page.indexOf("<script>") + 8, page.lastIndexOf("</script>"));
  assert.doesNotThrow(() => new Function(script));
});
