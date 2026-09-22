// Правила лицензий из SPEC 13 — по пунктам.
import { test } from "node:test";
import assert from "node:assert/strict";
import { generateKeys, normalizeSerial, newSerial, Signer, verifyDocument } from "../core/crypto.js";
import { hwidMatches, parseHwid } from "../core/hwid.js";
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
  const [serial] = await service.createKeys({});
  const p = payload(await service.activate({ serial, hwid: PC1, pluginVersion: "0.6.0", corelVersion: "27" }));
  assert.deepEqual(Object.keys(p), ["serial", "hwid", "plan", "issuedAt", "nextCheckAt"]);
  assert.equal(p.serial, serial);
  assert.equal(p.hwid, PC1);
  assert.equal(p.plan, "full");
  assert.equal(new Date(p.nextCheckAt).getTime() - new Date(p.issuedAt).getTime(), 14 * DAY);
});

test("переустановка Windows / сменили диск — тот же ключ восстанавливается, место не занимается", async () => {
  const { service, store } = setup();
  const [serial] = await service.createKeys({});
  await service.activate({ serial, hwid: PC1 });
  const p = payload(await service.activate({ serial, hwid: PC1_NEW_DISK }));
  assert.equal(p.hwid, PC1_NEW_DISK);
  assert.equal(store.activationRows.filter((a) => a.status === "active").length, 1);
});

test("новый компьютер: место занято → перенос; старый отключается при проверке", async () => {
  const { service } = setup();
  const [serial] = await service.createKeys({});
  await service.activate({ serial, hwid: PC1 });

  const busy = await service.activate({ serial, hwid: PC2 });
  assert.deepEqual(busy, { ok: false, error: "occupied", transfersLeft: 3 });

  payload(await service.transfer({ serial, hwid: PC2 }));
  assert.deepEqual(await service.check({ serial, hwid: PC1 }), { ok: false, error: "revoked" });
  payload(await service.check({ serial, hwid: PC2 }));
});

test("переносов не больше 3 за 365 дней; через год счётчик обнуляется; админ может сбросить", async () => {
  const { service, advance } = setup();
  const [serial] = await service.createKeys({});
  await service.activate({ serial, hwid: PC1 });
  for (const pc of [PC2, PC3, PC1]) payload(await service.transfer({ serial, hwid: pc }));
  assert.deepEqual(await service.transfer({ serial, hwid: PC2 }), { ok: false, error: "transfer_limit", transfersLeft: 0 });

  await service.adminLicense(serial, "reset_transfers", undefined);
  payload(await service.transfer({ serial, hwid: PC2 }));

  for (const pc of [PC3, PC1]) payload(await service.transfer({ serial, hwid: pc }));
  assert.equal((await service.transfer({ serial, hwid: PC2 })).ok, false);
  advance(366);
  payload(await service.transfer({ serial, hwid: PC2 }));
});

test("ключ на 2 компьютера: второй активируется без переноса", async () => {
  const { service } = setup();
  const [serial] = await service.createKeys({ maxPcs: 2 });
  payload(await service.activate({ serial, hwid: PC1 }));
  payload(await service.activate({ serial, hwid: PC2 }));
  assert.equal((await service.activate({ serial, hwid: PC3 })).ok, false);
});

test("освободить компьютер — место свободно, не считается переносом", async () => {
  const { service, store } = setup();
  const [serial] = await service.createKeys({});
  await service.activate({ serial, hwid: PC1 });
  assert.deepEqual(await service.deactivate({ serial, hwid: PC1 }), { ok: true });
  payload(await service.activate({ serial, hwid: PC2 }));
  assert.equal(store.licenses[0].transfersCount, 0);
});

test("неизвестный, заблокированный и просроченный ключ", async () => {
  const { service, advance } = setup();
  assert.deepEqual(await service.activate({ serial: "STRS-AAAA-BBBB-CCCC", hwid: PC1 }), { ok: false, error: "not_found" });
  assert.deepEqual(await service.activate({ serial: "мусор", hwid: PC1 }), { ok: false, error: "bad_request" });

  const [blocked] = await service.createKeys({});
  await service.activate({ serial: blocked, hwid: PC1 });
  await service.adminLicense(blocked, "block", undefined);
  assert.deepEqual(await service.check({ serial: blocked, hwid: PC1 }), { ok: false, error: "blocked" });

  const [timed] = await service.createKeys({ days: 30 });
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
  const [serial] = await service.createKeys({ note: "Мастер Аня" });
  const doc = await service.offline(serial, PC1);
  assert.ok(doc && verifyDocument(doc, keys.publicKey));
  // Клиент без интернета не может сверяться раз в 14 дней — офлайн-лицензия помечена.
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
