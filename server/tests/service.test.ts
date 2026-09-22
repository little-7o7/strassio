// Правила лицензий из SPEC 13 — по пунктам.
import { test } from "node:test";
import assert from "node:assert/strict";
import { generateKeys, normalizeSerial, newSerial, Signer, verifyDocument } from "../core/crypto.js";
import { hwidDisplay, hwidMatches, parseHwid } from "../core/hwid.js";
import { MemoryStore } from "../core/memoryStore.js";
import { LicenseService } from "../core/service.js";

const DAY = 24 * 60 * 60 * 1000;
const keys = generateKeys();

function hw(a: string, b: string, c: string, d: string): string {
  const h = (x: string) => (x === "-" ? "-" : x.padEnd(16, "0").slice(0, 16));
  return [a, b, c, d].map(h).join(".");
}

const PC1 = hw("a1", "b1", "c1", "d1");
const PC1_NEW_DISK = hw("a1", "b1", "c1", "d9");
const PC2 = hw("a2", "b2", "c2", "d2");
const PC3 = hw("a3", "b3", "c3", "d3");

function setup() {
  const store = new MemoryStore();
  let now = new Date("2026-09-22T10:00:00Z");
  const service = new LicenseService(store, new Signer(keys.privateKey), () => now);
  return { store, service, advance: (days: number) => (now = new Date(now.getTime() + days * DAY)) };
}

function payload(result: { ok: boolean; license?: { payload: string; signature: string } }) {
  assert.ok(result.ok, JSON.stringify(result));
  assert.ok(verifyDocument(result.license!, keys.publicKey), "подпись должна сходиться");
  return JSON.parse(result.license!.payload);
}

test("серийный ключ: формат и ввод как попало", () => {
  const s = newSerial();
  assert.match(s, /^STRS-[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}$/);
  assert.equal(normalizeSerial(" strs abcd-efgh jkmn "), "STRS-ABCD-EFGH-JKMN");
  assert.equal(normalizeSerial("ABCDEFGHJKMN"), "STRS-ABCD-EFGH-JKMN");
  assert.equal(normalizeSerial("STRS-ABC"), null);
});

test("код компьютера: меняется один признак — тот же ПК, два — другой", () => {
  assert.ok(hwidMatches(parseHwid(PC1)!, parseHwid(PC1_NEW_DISK)!));
  assert.ok(!hwidMatches(parseHwid(PC1)!, parseHwid(hw("a1", "b1", "c9", "d9"))!));
  assert.equal(parseHwid("zz"), null);
  assert.equal(parseHwid(hw("a1", "-", "-", "-")), null, "одного признака мало");
});

test("активация выдаёт подписанную лицензию с полями из SPEC 13.2", async () => {
  const { service } = setup();
  const [{ serial }] = await service.createKeys({});
  const p = payload(await service.activate({ serial, hwid: PC1, pluginVersion: "0.6.0", corelVersion: "27" }));
  assert.deepEqual(Object.keys(p), ["serial", "hwid", "plan", "issuedAt", "nextCheckAt"]);
  assert.equal(p.serial, serial);
  assert.equal(p.hwid, PC1);
  assert.equal(p.plan, "full");
  assert.equal(new Date(p.nextCheckAt).getTime() - new Date(p.issuedAt).getTime(), 1 * DAY);
});

test("переустановка Windows / сменили диск — тот же ключ восстанавливается, место не занимается", async () => {
  const { service, store } = setup();
  const [{ serial }] = await service.createKeys({});
  await service.activate({ serial, hwid: PC1 });
  const p = payload(await service.activate({ serial, hwid: PC1_NEW_DISK }));
  assert.equal(p.hwid, PC1_NEW_DISK);
  assert.equal(store.activationRows.filter((a) => a.status === "active").length, 1);
});

test("новый компьютер: место занято → перенос; старый отключается при проверке", async () => {
  const { service } = setup();
  const [{ serial }] = await service.createKeys({});
  await service.activate({ serial, hwid: PC1 });

  const busy = await service.activate({ serial, hwid: PC2 });
  assert.deepEqual(busy, { ok: false, error: "occupied", transfersLeft: 1, nextTransferAt: null });

  payload(await service.transfer({ serial, hwid: PC2 }));
  assert.deepEqual(await service.check({ serial, hwid: PC1 }), { ok: false, error: "revoked" });
  payload(await service.check({ serial, hwid: PC2 }));
});

test("переносов сколько угодно, но не чаще раза в 7 дней; админ может снять ожидание", async () => {
  const { service, advance } = setup();
  const [{ serial }] = await service.createKeys({});
  await service.activate({ serial, hwid: PC1 });
  payload(await service.transfer({ serial, hwid: PC2 }));

  advance(6);
  const early = await service.transfer({ serial, hwid: PC3 });
  assert.equal(early.ok, false);
  if (!early.ok) {
    assert.equal(early.error, "transfer_limit");
    assert.equal(early.transfersLeft, 0);
    assert.ok(early.nextTransferAt, "сообщается, с какого дня можно снова");
  }

  advance(1);
  payload(await service.transfer({ serial, hwid: PC3 }));
  for (let week = 0; week < 10; week++) {
    advance(7);
    payload(await service.transfer({ serial, hwid: week % 2 ? PC1 : PC2 }));
  }

  assert.equal((await service.transfer({ serial, hwid: PC3 })).ok, false);
  await service.adminLicense(serial, "reset_transfers", undefined);
  payload(await service.transfer({ serial, hwid: PC3 }));
});

test("один ключ — один компьютер: даже старый ключ с max_pcs = 2 в базе второй компьютер не пускает", async () => {
  const { service, store } = setup();
  const [{ serial }] = await service.createKeys({});
  store.licenses[0].maxPcs = 2;
  payload(await service.activate({ serial, hwid: PC1 }));
  assert.equal((await service.activate({ serial, hwid: PC2 })).ok, false);
  assert.equal((await service.adminLicense(serial, "max_pcs", 5)), null, "в админке больше нельзя поменять число компьютеров");
});

test("освободить компьютер — место свободно, не считается переносом", async () => {
  const { service, store } = setup();
  const [{ serial }] = await service.createKeys({});
  await service.activate({ serial, hwid: PC1 });
  assert.deepEqual(await service.deactivate({ serial, hwid: PC1 }), { ok: true });
  payload(await service.activate({ serial, hwid: PC2 }));
  assert.equal(store.licenses[0].transfersCount, 0);
});

test("неизвестный, заблокированный и просроченный ключ", async () => {
  const { service, advance } = setup();
  assert.deepEqual(await service.activate({ serial: "STRS-AAAA-BBBB-CCCC", hwid: PC1 }), { ok: false, error: "not_found" });
  assert.deepEqual(await service.activate({ serial: "мусор", hwid: PC1 }), { ok: false, error: "bad_request" });

  const [{ serial: blocked }] = await service.createKeys({});
  await service.activate({ serial: blocked, hwid: PC1 });
  await service.adminLicense(blocked, "block", undefined);
  assert.deepEqual(await service.check({ serial: blocked, hwid: PC1 }), { ok: false, error: "blocked" });

  const [{ serial: timed }] = await service.createKeys({ days: 30 });
  const p = payload(await service.activate({ serial: timed, hwid: PC1 }));
  assert.ok(p.expiresAt);
  advance(31);
  assert.deepEqual(await service.check({ serial: timed, hwid: PC1 }), { ok: false, error: "expired" });
  await service.adminLicense(timed, "extend", 30);
  payload(await service.check({ serial: timed, hwid: PC1 }));
});

test("пробный период: 14 дней, переустановка не сбрасывает", async () => {
  const { service, advance } = setup();
  const first = payload(await service.trial({ hwid: PC1 }));
  assert.equal(first.plan, "trial");
  assert.equal(new Date(first.expiresAt).getTime() - new Date(first.issuedAt).getTime(), 14 * DAY);

  advance(20);
  const again = payload(await service.trial({ hwid: PC1_NEW_DISK }));
  assert.equal(again.expiresAt, first.expiresAt, "срок считается от первого запуска");

  const other = payload(await service.trial({ hwid: PC2 }));
  assert.notEqual(other.expiresAt, first.expiresAt);
});

test("офлайн-активация и журнал", async () => {
  const { service } = setup();
  const [{ serial }] = await service.createKeys({ note: "Мастер Аня" });
  const doc = await service.offline(serial, PC1);
  assert.ok(doc && verifyDocument(doc, keys.publicKey));
  // Клиент без интернета не может сверяться каждый день — офлайн-лицензия помечена.
  assert.equal(JSON.parse(doc.payload).offline, true);
  assert.equal(payload(await service.check({ serial, hwid: PC1 })).offline, undefined);
  const log = await service.auditLog();
  assert.deepEqual(log.map((r) => r.action).slice(0, 2), ["offline", "create_keys"]);
  assert.equal((await service.search("аня")).length, 1);
});

test("обновление: подписанные сведения, канал beta отдельно", async () => {
  const { service } = setup();
  assert.equal(await service.latestUpdate("stable"), null);
  await service.publishUpdate({ channel: "stable", version: "0.6.0", url: "https://x/y.exe", sha256: "a".repeat(64), notesRu: "Лицензии", notesEn: "Licensing", minVersion: "" });
  const doc = await service.latestUpdate("stable");
  assert.ok(doc && verifyDocument(doc, keys.publicKey));
  assert.equal(JSON.parse(doc!.payload).version, "0.6.0");
  assert.equal(await service.latestUpdate("beta"), null);
});

test("короткий код компьютера на сайте — тот же, что в окне «Лицензия» плагина (HardwareCode.Display)", () => {
  // То же значение проверяет C#-тест HardwareCode_Display_MatchesServer.
  assert.equal(hwidDisplay(parseHwid(PC1)!), "7289-FDB0-F904-5FFC");
});

test("«Моя лицензия»: по одному ключу видны компьютеры; неизвестный ключ — not_found", async () => {
  const { service } = setup();
  const [{ serial }] = await service.createKeys({});
  await service.activate({ serial, hwid: PC1 });

  assert.deepEqual(await service.siteLookup("STRS-AAAA-BBBB-CCCC"), { ok: false, error: "not_found" });
  assert.deepEqual(await service.siteLookup("мусор"), { ok: false, error: "not_found" });
  assert.deepEqual(await service.siteRelease("мусор", 1), { ok: false, error: "not_found" });

  const view = await service.siteLookup(serial.toLowerCase().replace(/-/g, " "));
  assert.ok(view.ok);
  if (view.ok) {
    assert.equal(view.license.computers.length, 1);
    assert.equal(view.license.computers[0].code, hwidDisplay(parseHwid(PC1)!));
    assert.equal(view.license.transfersLeft, 1);
    assert.equal(view.license.nextTransferAt, null);
    assert.ok(!JSON.stringify(view).includes(PC1), "полный код компьютера наружу не отдаётся");
  }
});

test("«Моя лицензия»: освободить старый компьютер с сайта — это перенос (раз в 7 дней), потом ключ встаёт на новый", async () => {
  const { service } = setup();
  const [{ serial }] = await service.createKeys({});
  await service.activate({ serial, hwid: PC1 });
  assert.equal((await service.activate({ serial, hwid: PC2 })).ok, false);

  const view = await service.siteLookup(serial);
  const id = view.ok ? view.license.computers[0].id : -1;
  const released = await service.siteRelease(serial, id);
  assert.ok(released.ok);
  if (released.ok) {
    assert.equal(released.license.transfersLeft, 0);
    assert.ok(released.license.nextTransferAt);
  }
  assert.deepEqual(await service.siteRelease(serial, id), { ok: false, error: "not_found" }, "дважды не освобождается");

  payload(await service.activate({ serial, hwid: PC2 }));
  assert.deepEqual(await service.check({ serial, hwid: PC1 }), { ok: false, error: "revoked" });

  // Чужую активацию (другого ключа) освободить нельзя.
  const [{ serial: other }] = await service.createKeys({});
  assert.deepEqual(await service.siteRelease(other, id), { ok: false, error: "not_found" });
});

test("«Моя лицензия»: ожидание 7 дней действует и на сайте", async () => {
  const { service, advance } = setup();
  const [{ serial }] = await service.createKeys({});
  const releaseActive = async () => {
    const v = await service.siteLookup(serial);
    const active = v.ok ? v.license.computers.find((c) => c.active) : undefined;
    return service.siteRelease(serial, active!.id);
  };

  payload(await service.activate({ serial, hwid: PC1 }));
  assert.ok((await releaseActive()).ok);
  payload(await service.activate({ serial, hwid: PC2 }));
  assert.deepEqual(await releaseActive(), { ok: false, error: "transfer_limit" });
  advance(7);
  assert.ok((await releaseActive()).ok);
});

test("«Моя лицензия»: файл лицензии для компьютера без интернета — только если есть свободное место", async () => {
  const { service } = setup();
  const [{ serial }] = await service.createKeys({});
  const file = await service.siteOffline(serial, PC1);
  assert.ok(file.ok);
  if (file.ok) {
    assert.ok(verifyDocument(file.file, keys.publicKey));
    assert.equal(JSON.parse(file.file.payload).offline, true);
  }

  assert.ok((await service.siteOffline(serial, PC1_NEW_DISK)).ok, "тот же компьютер — можно ещё раз");
  assert.deepEqual(await service.siteOffline(serial, PC2), { ok: false, error: "occupied" });
  assert.deepEqual(await service.siteOffline(serial, "zz"), { ok: false, error: "bad_request" });
});

test("админка: пробные периоды видны (код компьютера, дни); удалённый можно взять заново", async () => {
  const { service, advance } = setup();
  payload(await service.trial({ hwid: PC1 }));
  advance(3);
  payload(await service.trial({ hwid: PC2 }));

  const trials = await service.listTrials();
  assert.equal(trials.length, 2);
  assert.equal(trials[0].code, hwidDisplay(parseHwid(PC2)!), "новые сверху");
  assert.equal(trials[0].daysLeft, 14);
  assert.equal(trials[1].daysLeft, 11);
  assert.ok(trials.every((t) => t.active));

  advance(12);
  const later = await service.listTrials();
  assert.equal(later[1].active, false);
  assert.equal(later[1].daysLeft, 0);

  assert.ok(await service.deleteTrial(later[1].id));
  assert.equal(await service.deleteTrial(later[1].id), false);
  assert.equal((await service.listTrials()).length, 1);
  payload(await service.trial({ hwid: PC1 }));
  assert.equal((await service.listTrials())[0].daysLeft, 14, "после удаления — снова 14 дней");
});

test("админка: действия new_recovery больше нет", async () => {
  const { service } = setup();
  const [{ serial }] = await service.createKeys({});
  assert.equal(await service.adminLicense(serial, "new_recovery", undefined), null);
});

test("сайт: активация по коду компьютера — код активации SA1 с подписанной лицензией, занято → перенос раз в 7 дней", async () => {
  const { service, advance } = setup();
  const [{ serial }] = await service.createKeys({});

  const first = await service.siteActivate(serial, "  " + PC1 + "\n", false);
  assert.ok(first.ok);
  if (first.ok) {
    const [prefix, payloadB64, sigB64] = first.code.split(".");
    assert.equal(prefix, "SA1");
    const payload = JSON.parse(Buffer.from(payloadB64, "base64url").toString("utf8"));
    assert.equal(payload.serial, serial);
    assert.equal(payload.hwid, PC1);
    assert.equal(payload.offline, undefined, "обычная лицензия — плагин сверяет её каждый день");
    assert.equal(Buffer.from(sigB64, "base64url").length, 64);
  }

  assert.ok((await service.siteActivate(serial, PC1_NEW_DISK, false)).ok, "тот же компьютер после смены диска");
  const busy = await service.siteActivate(serial, PC2, false);
  assert.equal(busy.ok, false);
  if (!busy.ok) assert.equal(busy.error, "occupied");

  assert.ok((await service.siteActivate(serial, PC2, true)).ok);
  assert.deepEqual(await service.check({ serial, hwid: PC1 }), { ok: false, error: "revoked" });
  const early = await service.siteActivate(serial, PC3, true);
  assert.equal(early.ok ? "" : early.error, "transfer_limit");
  advance(7);
  assert.ok((await service.siteActivate(serial, PC3, true)).ok);

  assert.deepEqual(await service.siteActivate(serial, "мусор", false), { ok: false, error: "bad_request" });
  assert.deepEqual(await service.siteActivate("STRS-AAAA-BBBB-CCCC", PC1, false), { ok: false, error: "not_found" });
});

test("админка: данные клиента (имя, фамилия, телефон, почта, день рождения), поиск по ним и проверка", async () => {
  const { service } = setup();
  const client = { firstName: "Мадина", lastName: "Каримова", phone: "+998 90 123 45 67", email: "madina@example.com", birthday: "1995-03-08", telegram: "madina_k" };
  const [{ serial }] = await service.createKeys({ client });
  const [{ serial: other }] = await service.createKeys({});

  for (const q of ["мадина", "Каримова", "123 45", "example.com", "madina_k"]) {
    const found = await service.search(q);
    assert.deepEqual(found.map((l) => l.serial), [serial], q);
    assert.deepEqual(found[0].client, client);
  }

  assert.ok(await service.adminLicense(other, "client", { ...client, firstName: "Азиз", email: "" }));
  assert.equal((await service.search("Азиз"))[0].serial, other);
  assert.equal(await service.adminLicense(other, "client", { email: "не почта" }), null);
  assert.equal(await service.adminLicense(other, "client", { birthday: "1995-02-30" }), null);
  assert.equal(await service.adminLicense(other, "client", { birthday: "08.03.1995" }), null);
  const tg = await service.adminLicense(other, "client", { telegram: "https://t.me/Aziz_2000" });
  assert.equal(tg?.client.telegram, "Aziz_2000", "ссылка t.me превращается в имя");
  assert.equal((await service.adminLicense(other, "client", { telegram: "@aziz" }))?.client.telegram, "aziz");
  assert.equal(await service.adminLicense(other, "client", { telegram: "не телеграм" }), null);
});

test("админка: удалить ключ полностью — вместе с активациями; плагин с ним отключается", async () => {
  const { service, store } = setup();
  const [{ serial }] = await service.createKeys({ client: { firstName: "Тест" } });
  payload(await service.activate({ serial, hwid: PC1 }));

  assert.equal(await service.deleteLicense(serial), true);
  assert.equal(store.licenses.length, 0);
  assert.equal(store.activationRows.length, 0);
  assert.deepEqual(await service.check({ serial, hwid: PC1 }), { ok: false, error: "not_found" });
  assert.equal(await service.deleteLicense(serial), false, "второй раз — нечего удалять");
  assert.ok((await service.auditLog(10)).some((r) => r.action === "delete" && r.serial === serial), "в журнале остаётся запись");
});

test("сайт: заявка на покупку → админка → ключ по заявке (данные клиента переходят в ключ)", async () => {
  const { service } = setup();
  assert.deepEqual(await service.submitRequest({ firstName: "Мадина" }, ""), { ok: false, error: "missing" }, "нужны имя, фамилия и телефон");
  assert.deepEqual(await service.submitRequest({ firstName: "А", lastName: "Б", phone: "+998901234567", email: "плохо" }, ""), { ok: false, error: "bad_client" });

  const client = { firstName: "Мадина", lastName: "Каримова", phone: "+998 90 123 45 67", email: "m@example.com", birthday: "1995-03-08", telegram: "@madina_k" };
  assert.deepEqual(await service.submitRequest(client, "  Хочу купить на 1 год  "), { ok: true });
  const [request] = await service.listRequests();
  assert.equal(request.status, "new");
  assert.equal(request.client.telegram, "madina_k");
  assert.equal(request.message, "Хочу купить на 1 год");

  assert.ok(await service.adminRequest(request.id, "working"));
  assert.equal((await service.listRequests())[0].status, "working");
  assert.equal(await service.adminRequest(request.id, "взломать"), false);

  const key = await service.requestKey(request.id, 365);
  assert.ok(key);
  const [license] = await service.search(key!.serial);
  assert.deepEqual(license.client, { ...client, telegram: "madina_k" });
  assert.ok(license.expiresAt);
  assert.match(license.note, /^Заявка #\d+: Хочу купить/);
  const done = (await service.listRequests())[0];
  assert.equal(done.status, "done");
  assert.equal(done.serial, key!.serial);
  assert.equal(await service.requestKey(request.id, null), null, "второй ключ по той же заявке не создаётся");

  assert.ok(await service.adminRequest(request.id, "delete"));
  assert.equal((await service.listRequests()).length, 0);
});
