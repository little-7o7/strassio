// Правила лицензий из SPEC 13 — по пунктам.
import { test } from "node:test";
import assert from "node:assert/strict";
import { generateKeys, normalizeSerial, newSerial, Signer, verifyDocument } from "../core/crypto.js";
import { hwidDisplay, hwidMatches, parseHwid } from "../core/hwid.js";
import { MemoryStore } from "../core/memoryStore.js";
import { LicenseService } from "../core/service.js";

const DAY = 24 * 60 * 60 * 1000;
const keys = generateKeys();
/** Почта клиента: без неё сайт не выдаёт код активации и не открывает «Моя лицензия». */
const EMAIL = "client@example.com";

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
  // Место осталось «без интернета»: если связь появится, сверка вернёт такую же лицензию, а не обычную на 14 дней.
  assert.equal(payload(await service.check({ serial, hwid: PC1 })).offline, true);
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

  assert.deepEqual(await service.siteLookup("STRS-AAAA-BBBB-CCCC", EMAIL), { ok: false, error: "not_found" });
  assert.deepEqual(await service.siteLookup("мусор", EMAIL), { ok: false, error: "not_found" });
  assert.deepEqual(await service.siteRelease("мусор", 1, EMAIL), { ok: false, error: "not_found" });

  const view = await service.siteLookup(serial.toLowerCase().replace(/-/g, " "), EMAIL);
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

  const view = await service.siteLookup(serial, EMAIL);
  const id = view.ok ? view.license.computers[0].id : -1;
  const released = await service.siteRelease(serial, id, EMAIL);
  assert.ok(released.ok);
  if (released.ok) {
    assert.equal(released.license.transfersLeft, 0);
    assert.ok(released.license.nextTransferAt);
  }
  assert.deepEqual(await service.siteRelease(serial, id, EMAIL), { ok: false, error: "not_found" }, "дважды не освобождается");

  payload(await service.activate({ serial, hwid: PC2 }));
  assert.deepEqual(await service.check({ serial, hwid: PC1 }), { ok: false, error: "revoked" });

  // Чужую активацию (другого ключа) освободить нельзя.
  const [{ serial: other }] = await service.createKeys({});
  assert.deepEqual(await service.siteRelease(other, id, EMAIL), { ok: false, error: "not_found" });
});

test("«Моя лицензия»: ожидание 7 дней действует и на сайте", async () => {
  const { service, advance } = setup();
  const [{ serial }] = await service.createKeys({});
  const releaseActive = async () => {
    const v = await service.siteLookup(serial, EMAIL);
    const active = v.ok ? v.license.computers.find((c) => c.active) : undefined;
    return service.siteRelease(serial, active!.id, EMAIL);
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
  const file = await service.siteOffline(serial, PC1, EMAIL);
  assert.ok(file.ok);
  if (file.ok) {
    assert.ok(verifyDocument(file.file, keys.publicKey));
    assert.equal(JSON.parse(file.file.payload).offline, true);
  }

  assert.ok((await service.siteOffline(serial, PC1_NEW_DISK, EMAIL)).ok, "тот же компьютер — можно ещё раз");
  assert.deepEqual(await service.siteOffline(serial, PC2, EMAIL), { ok: false, error: "offline_seat" }, "место занято компьютером без интернета");
  assert.deepEqual(await service.siteOffline(serial, "zz", EMAIL), { ok: false, error: "bad_request" });
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

  const first = await service.siteActivate(serial, "  " + PC1 + "\n", false, EMAIL);
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

  assert.ok((await service.siteActivate(serial, PC1_NEW_DISK, false, EMAIL)).ok, "тот же компьютер после смены диска");
  const busy = await service.siteActivate(serial, PC2, false, EMAIL);
  assert.equal(busy.ok, false);
  if (!busy.ok) assert.equal(busy.error, "occupied");

  assert.ok((await service.siteActivate(serial, PC2, true, EMAIL)).ok);
  assert.deepEqual(await service.check({ serial, hwid: PC1 }), { ok: false, error: "revoked" });
  const early = await service.siteActivate(serial, PC3, true, EMAIL);
  assert.equal(early.ok ? "" : early.error, "transfer_limit");
  advance(7);
  assert.ok((await service.siteActivate(serial, PC3, true, EMAIL)).ok);

  assert.deepEqual(await service.siteActivate(serial, "мусор", false, EMAIL), { ok: false, error: "bad_request" });
  assert.deepEqual(await service.siteActivate("STRS-AAAA-BBBB-CCCC", PC1, false, EMAIL), { ok: false, error: "not_found" });
});

test("админка: данные клиента (имя, фамилия, телефон, почта, день рождения), поиск по ним и проверка", async () => {
  const { service } = setup();
  const client = { firstName: "Мадина", lastName: "Каримова", phone: "+998 90 123 45 67", email: "madina@example.com", birthday: "1995-03-08", telegram: "madina_k" };
  const [{ serial }] = await service.createKeys({ client });
  const [{ serial: other }] = await service.createKeys({});

  // Полный телефон — 12 цифр, как ключ: точного ключа нет, значит ищем как обычно.
  for (const q of ["мадина", "Каримова", "123 45", "example.com", "madina_k", "+998 90 123 45 67"]) {
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

test("почта — второй секрет к ключу: без неё и с чужой почтой сайт ничего не выдаёт", async () => {
  const { service } = setup();
  const [{ serial }] = await service.createKeys({ client: { email: "Madina@Example.com" } });

  assert.deepEqual(await service.siteActivate(serial, PC1, false), { ok: false, error: "email_required" });
  assert.deepEqual(await service.siteActivate(serial, PC1, false, "other@example.com"), { ok: false, error: "wrong_email" });
  assert.deepEqual(await service.siteActivate(serial, PC1, false, "не почта"), { ok: false, error: "wrong_email" });
  assert.deepEqual(await service.siteLookup(serial, "other@example.com"), { ok: false, error: "wrong_email" });
  assert.deepEqual(await service.siteRelease(serial, 1, ""), { ok: false, error: "email_required" });
  assert.deepEqual(await service.siteOffline(serial, PC1, "x@y.z"), { ok: false, error: "wrong_email" });
  assert.ok((await service.siteActivate(serial, PC1, false, "  madina@example.COM ")).ok, "регистр и пробелы не важны");
  assert.ok((await service.siteLookup(serial, "madina@example.com")).ok);

  // У ключа почты нет — первая введённая привязывается, дальше нужна именно она.
  const [{ serial: bare }] = await service.createKeys({});
  assert.ok((await service.siteLookup(bare, "first@example.com")).ok);
  assert.equal((await service.search(bare))[0].client.email, "first@example.com");
  assert.deepEqual(await service.siteActivate(bare, PC2, false, "second@example.com"), { ok: false, error: "wrong_email" });
  assert.ok((await service.auditLog(20)).some((r) => r.action === "bind_email" && r.serial === bare));

  // Несуществующий ключ — обычное not_found (почту не проверяем, раз ключа нет).
  assert.deepEqual(await service.siteLookup("STRS-AAAA-BBBB-CCCC", "a@b.cd"), { ok: false, error: "not_found" });
});

test("сверка отдаёт плагину данные клиента (для «О программе»), только активированному компьютеру", async () => {
  const { service } = setup();
  const client = { firstName: "Мадина", lastName: "Каримова", phone: "+998 90 123 45 67", email: "m@example.com", birthday: "1995-03-08", telegram: "madina_k" };
  const [{ serial }] = await service.createKeys({ client });
  payload(await service.activate({ serial, hwid: PC1 }));
  const checked = await service.check({ serial, hwid: PC1 });
  assert.ok(checked.ok);
  // Почта — второй секрет для переноса: в сверке её видно только скрытой.
  if (checked.ok) assert.deepEqual(checked.client, { ...client, email: "m***@example.com" });
  const again = await service.activate({ serial, hwid: PC1 });
  if (again.ok) assert.deepEqual(again.client, client, "повторная активация (с почтой) — полные данные");
  assert.deepEqual(await service.check({ serial, hwid: PC2 }), { ok: false, error: "not_activated" }, "чужой компьютер ничего не получает");
});

test("два запроса одновременно не ставят ключ на два компьютера", async () => {
  const { service } = setup();
  const [{ serial }] = await service.createKeys({});
  const both = await Promise.all([service.activate({ serial, hwid: PC1 }), service.activate({ serial, hwid: PC2 })]);
  assert.equal(both.filter((r) => r.ok).length, 1, JSON.stringify(both));
  const checks = await Promise.all([service.check({ serial, hwid: PC1 }), service.check({ serial, hwid: PC2 })]);
  assert.equal(checks.filter((r) => r.ok).length, 1, "работает только один компьютер");
});

test("файл без интернета: место не освобождается с сайта и не уходит при переносе — иначе файлов было бы сколько угодно", async () => {
  const { service, store, advance } = setup();
  const [{ serial }] = await service.createKeys({ client: { firstName: "", lastName: "", phone: "", email: EMAIL, birthday: "", telegram: "" } });
  assert.ok((await service.siteOffline(serial, PC1, EMAIL)).ok);
  const license = await store.findLicense(serial);
  const [seat] = await store.activations(license!.id);
  assert.deepEqual(await service.siteRelease(serial, seat.id, EMAIL), { ok: false, error: "offline_seat" });
  advance(8);
  const moved = await service.transfer({ serial, hwid: PC2 });
  assert.equal(moved.ok, false);
  assert.equal((await store.activations(license!.id)).filter((a) => a.status === "active").length, 1);
});

// ---------- Компьютер без интернета (SPEC 13.3b) ----------

/** Ключ с почтой клиента — сайт без неё ничего не выдаёт. */
async function keyWithEmail(service: LicenseService, days?: number) {
  const [{ serial }] = await service.createKeys({ days, client: { firstName: "", lastName: "", phone: "", email: EMAIL, birthday: "", telegram: "" } });
  return serial;
}

function codePayload(code: string) {
  const [prefix, body] = code.split(".");
  assert.equal(prefix, "SA1");
  return JSON.parse(Buffer.from(body, "base64url").toString("utf8"));
}

test("«Активация» с галочкой «нет интернета»: код без сверки, бессрочный ключ — без срока", async () => {
  const { service, store } = setup();
  const serial = await keyWithEmail(service);
  const r = await service.siteActivate(serial, PC1, false, EMAIL, true);
  assert.ok(r.ok);
  if (!r.ok) return;
  assert.equal(r.offline, true);
  const p = codePayload(r.code);
  assert.equal(p.offline, true);
  assert.equal(p.expiresAt, undefined, "куплен навсегда — работает всегда");
  assert.ok(verifyDocument(r.file, keys.publicKey), "тот же код — файлом для флешки");
  assert.equal(r.file.payload, Buffer.from(r.code.split(".")[1], "base64url").toString("utf8"));

  const [seat] = await store.activations((await store.findLicense(serial))!.id);
  assert.equal(seat.offline, true);
  const audit = (await service.auditLog()).map((e) => e.action);
  assert.ok(audit.includes("activate_offline"));
});

test("годовой ключ без интернета: код до конца срока; продлили — новый срок при первой же связи или новым кодом", async () => {
  const { service, advance } = setup();
  const serial = await keyWithEmail(service, 365);
  const r = await service.siteActivate(serial, PC1, false, EMAIL, true);
  assert.ok(r.ok);
  if (!r.ok) return;
  const first = codePayload(r.code);
  assert.equal(first.offline, true);
  assert.equal(new Date(first.expiresAt).getTime(), new Date("2026-09-22T10:00:00Z").getTime() + 365 * DAY);

  advance(300);
  await service.adminLicense(serial, "extend", 365);
  // Компьютер на минуту вышел в интернет: плагин тихо сверился — пришёл новый срок, лицензия по-прежнему без сверки.
  const renewed = payload(await service.check({ serial, hwid: PC1, offline: true }));
  assert.equal(renewed.offline, true);
  assert.equal(new Date(renewed.expiresAt).getTime(), new Date(first.expiresAt).getTime() + 365 * DAY);
  // Интернета нет совсем: тот же компьютер берёт новый код на сайте — это не перенос.
  const again = await service.siteActivate(serial, PC1_NEW_DISK, false, EMAIL, true);
  assert.ok(again.ok);
  if (again.ok) assert.equal(codePayload(again.code).expiresAt, renewed.expiresAt);
  const view = await service.siteLookup(serial, EMAIL);
  assert.ok(view.ok);
  if (view.ok) assert.equal(view.license.transfersLeft, 1, "перенос не потрачен");
});

test("дыра закрыта: обычная активация, потом файл без интернета на тот же ПК — место уже не освободить и не перенести", async () => {
  const { service, store, advance } = setup();
  const serial = await keyWithEmail(service);
  const online = await service.siteActivate(serial, PC1, false, EMAIL);
  assert.ok(online.ok);
  if (online.ok) assert.equal(codePayload(online.code).offline, undefined, "без галочки — обычный код");

  assert.ok((await service.siteOffline(serial, PC1, EMAIL)).ok);
  const licenseId = (await store.findLicense(serial))!.id;
  const [seat] = await store.activations(licenseId);
  assert.equal(seat.offline, true, "файл без сверки — место стало «без интернета»");

  assert.deepEqual(await service.siteRelease(serial, seat.id, EMAIL), { ok: false, error: "offline_seat" });
  assert.deepEqual(await service.deactivate({ serial, hwid: PC1 }), { ok: false, error: "offline_seat" }, "и из плагина тоже");
  advance(8);
  const moved = await service.siteActivate(serial, PC2, true, EMAIL);
  assert.deepEqual(moved, { ok: false, error: "offline_seat" }, "сайт честно говорит, что перенос — только через автора");
  assert.deepEqual(await service.siteActivate(serial, PC2, false, EMAIL), { ok: false, error: "offline_seat" });
  assert.equal((await store.activations(licenseId)).filter((a) => a.status === "active").length, 1);

  // Пришла сверка с этого компьютера (плагин обновился, лицензия — обычная) — всё равно лицензия без сверки.
  const back = payload(await service.check({ serial, hwid: PC1 }));
  assert.equal(back.offline, true);
  const v = await service.siteLookup(serial, EMAIL);
  assert.ok(v.ok);
  if (v.ok) assert.deepEqual(v.license.computers.map((c) => [c.active, c.offline]), [[true, true]]);
});

test("обычный код, потом галочка «нет интернета» для того же ПК — место становится «без интернета»", async () => {
  const { service, store } = setup();
  const serial = await keyWithEmail(service);
  assert.ok((await service.siteActivate(serial, PC1, false, EMAIL)).ok);
  const r = await service.siteActivate(serial, PC1_NEW_DISK, false, EMAIL, true);
  assert.ok(r.ok);
  if (r.ok) assert.equal(codePayload(r.code).offline, true);
  const seats = await store.activations((await store.findLicense(serial))!.id);
  assert.deepEqual(seats.map((a) => [a.status, a.offline]), [["active", true]], "тот же компьютер — то же место");
});

test("плагин с файлом без интернета сверяется (связь появилась) — место помечается, даже если файл выдан раньше метки", async () => {
  const { service, store } = setup();
  const serial = await keyWithEmail(service);
  payload(await service.activate({ serial, hwid: PC1 }));
  const plain = payload(await service.check({ serial, hwid: PC1 }));
  assert.equal(plain.offline, undefined);

  const marked = payload(await service.check({ serial, hwid: PC1, offline: true }));
  assert.equal(marked.offline, true);
  const [seat] = await store.activations((await store.findLicense(serial))!.id);
  assert.equal(seat.offline, true);
  assert.equal(payload(await service.check({ serial, hwid: PC1 })).offline, true, "метка не снимается");
  assert.deepEqual(await service.check({ serial, hwid: PC2, offline: true }), { ok: false, error: "not_activated" }, "чужой компьютер метку не ставит");
});

test("перенос на компьютер без интернета: старый (с интернетом) отключается, новый — без сверки", async () => {
  const { service, store } = setup();
  const serial = await keyWithEmail(service);
  assert.ok((await service.siteActivate(serial, PC1, false, EMAIL)).ok);
  const moved = await service.siteActivate(serial, PC2, true, EMAIL, true);
  assert.ok(moved.ok);
  if (moved.ok) assert.equal(codePayload(moved.code).offline, true);
  assert.deepEqual(await service.check({ serial, hwid: PC1 }), { ok: false, error: "revoked" });
  const seats = await store.activations((await store.findLicense(serial))!.id);
  assert.deepEqual(seats.map((a) => [a.status, a.offline]), [["revoked", false], ["active", true]]);
});

test("автор (хаб) освобождает компьютер без интернета; этот компьютер при связи теряет лицензию", async () => {
  const { service, store } = setup();
  const serial = await keyWithEmail(service);
  assert.ok((await service.siteOffline(serial, PC1, EMAIL)).ok);
  const [seat] = await store.activations((await store.findLicense(serial))!.id);
  assert.ok(await service.revokeActivation(seat.id));
  assert.deepEqual(await service.check({ serial, hwid: PC1, offline: true }), { ok: false, error: "revoked" });
  assert.ok((await service.siteActivate(serial, PC2, false, EMAIL)).ok, "место свободно");
});

test("хаб и «Моя лицензия» видят, какой компьютер без интернета", async () => {
  const { service } = setup();
  const serial = await keyWithEmail(service);
  const file = await service.siteOffline(serial, PC1, EMAIL);
  assert.ok(file.ok);
  if (file.ok) assert.equal(codePayload(file.code).offline, true, "«Моя лицензия» даёт и код, не только файл");
  const view = await service.licenseView(serial);
  assert.deepEqual(view!.computers.map((c) => c.offline), [true]);
});
