// Админка (SPEC 13.6) — одна страница без сборки и библиотек. Пароль хранится только во вкладке
// (sessionStorage) и уходит в заголовке Authorization каждого запроса.
export const ADMIN_PAGE = `<!doctype html>
<html lang="ru">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="robots" content="noindex">
<title>Strassio — админка</title>
<style>
  :root { --bg:#f6f7f9; --card:#fff; --text:#1d2330; --muted:#6b7280; --line:#e3e6eb; --accent:#2f6fde; --bad:#c62828; --ok:#2e7d32; }
  @media (prefers-color-scheme: dark) { :root { --bg:#15181e; --card:#1e232b; --text:#e6e9ef; --muted:#9aa3b2; --line:#2e3440; --accent:#6ea0ff; --bad:#ef6c6c; --ok:#6fcf73; } }
  * { box-sizing: border-box; }
  body { margin:0; font:14px/1.45 system-ui, "Segoe UI", sans-serif; background:var(--bg); color:var(--text); }
  main { max-width:1100px; margin:0 auto; padding:16px; }
  h1 { font-size:20px; margin:8px 0 16px; }
  h2 { font-size:15px; margin:0 0 10px; }
  section { background:var(--card); border:1px solid var(--line); border-radius:10px; padding:14px; margin-bottom:14px; }
  label { display:inline-flex; flex-direction:column; gap:3px; margin:0 10px 8px 0; color:var(--muted); font-size:12px; }
  input, textarea, select { font:inherit; color:var(--text); background:var(--bg); border:1px solid var(--line); border-radius:6px; padding:6px 8px; }
  textarea { width:100%; min-height:70px; }
  button { font:inherit; border:1px solid var(--line); background:var(--bg); color:var(--text); border-radius:6px; padding:6px 12px; cursor:pointer; }
  button.primary { background:var(--accent); border-color:var(--accent); color:#fff; }
  button.small { padding:2px 8px; font-size:12px; }
  .row { display:flex; flex-wrap:wrap; align-items:flex-end; gap:0 6px; }
  .muted { color:var(--muted); }
  .bad { color:var(--bad); } .ok { color:var(--ok); }
  table { width:100%; border-collapse:collapse; font-size:13px; }
  th, td { text-align:left; padding:6px; border-top:1px solid var(--line); vertical-align:top; }
  code, .mono { font-family:Consolas, monospace; }
  .tabs button { margin-right:4px; } .tabs button.on { background:var(--accent); color:#fff; border-color:var(--accent); }
  .scroll { overflow-x:auto; }
  #msg { min-height:20px; margin-bottom:8px; }
</style>
</head>
<body>
<main>
  <h1>Strassio — лицензии</h1>
  <!-- Сообщения — вне блока app: ошибка входа («Неверный пароль») должна быть видна и до входа. -->
  <div id="msg"></div>

  <section id="login">
    <h2>Вход</h2>
    <div class="row">
      <label>Пароль<input id="pwd" type="password" autocomplete="current-password" onkeydown="if(event.key==='Enter')login()"></label>
      <button class="primary" onclick="login()">Войти</button>
    </div>
  </section>

  <div id="app" hidden>
    <div class="tabs" style="margin-bottom:12px">
      <button data-tab="keys" class="on">Ключи</button>
      <button data-tab="offline">Офлайн-активация</button>
      <button data-tab="updates">Обновления</button>
      <button data-tab="audit">Журнал</button>
      <button onclick="logout()" style="float:right">Выйти</button>
    </div>

    <div data-page="keys">
      <section>
        <h2>Создать ключи</h2>
        <div class="row">
          <label>Сколько<input id="kCount" type="number" value="1" min="1" max="500" style="width:80px"></label>
          <label>Срок, дней (пусто — бессрочно)<input id="kDays" type="number" min="0" style="width:120px"></label>
          <label>Компьютеров<input id="kPcs" type="number" value="1" min="1" style="width:90px"></label>
          <label style="flex:1">Заметка (имя клиента, Telegram)<input id="kNote"></label>
          <button class="primary" onclick="createKeys()">Создать</button>
        </div>
        <textarea id="kOut" readonly hidden class="mono"></textarea>
      </section>
      <section>
        <h2>Поиск</h2>
        <div class="row">
          <label style="flex:1">Ключ или заметка<input id="q" onkeydown="if(event.key==='Enter')search()"></label>
          <button onclick="search()">Найти</button>
        </div>
        <div id="results" class="scroll"></div>
      </section>
    </div>

    <section data-page="offline" hidden>
      <h2>Офлайн-активация</h2>
      <p class="muted">Для клиента без интернета: вставьте его ключ и код компьютера из окна «Лицензия» в плагине — получите файл лицензии, который он загрузит в плагин.</p>
      <div class="row">
        <label>Ключ<input id="oSerial" class="mono" placeholder="STRS-XXXX-XXXX-XXXX"></label>
        <label style="flex:1">Код компьютера (полный)<input id="oHwid" class="mono"></label>
        <button class="primary" onclick="offline()">Получить файл</button>
      </div>
    </section>

    <section data-page="updates" hidden>
      <h2>Опубликовать обновление</h2>
      <p class="muted">Установщик сначала выложите в GitHub Releases, затем вставьте сюда ссылку и SHA-256 файла.</p>
      <div class="row">
        <label>Канал<select id="uChannel"><option>stable</option><option>beta</option></select></label>
        <label>Версия<input id="uVersion" placeholder="0.6.0" style="width:90px"></label>
        <label>Мин. версия<input id="uMin" style="width:90px"></label>
        <label style="flex:1">Ссылка на установщик<input id="uUrl" placeholder="https://github.com/..."></label>
      </div>
      <label style="display:flex">SHA-256<input id="uSha" class="mono"></label>
      <label style="display:flex">Что нового (русский)<textarea id="uRu"></textarea></label>
      <label style="display:flex">What's new (English)<textarea id="uEn"></textarea></label>
      <button class="primary" onclick="publishUpdate()">Опубликовать</button>
    </section>

    <section data-page="audit" hidden>
      <h2>Журнал действий</h2>
      <div id="audit" class="scroll"></div>
    </section>
  </div>
</main>
<script>
const $ = (id) => document.getElementById(id);
const esc = (s) => String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&":"&amp;", "<":"&lt;", ">":"&gt;", '"':"&quot;", "'":"&#39;" }[c]));
const date = (s) => s ? new Date(s).toLocaleString("ru-RU") : "—";
let password = sessionStorage.getItem("strassio-admin") || "";

async function api(method, path, body) {
  const res = await fetch("/api/admin/" + path, {
    method,
    headers: { "Content-Type": "application/json", "Authorization": "Bearer " + encodeURIComponent(password) },
    body: body ? JSON.stringify(body) : undefined,
  });
  const data = await res.json().catch(() => ({ ok: false, error: "bad_response" }));
  if (res.status === 401) { logout(); }
  if (!data.ok) throw new Error({ unauthorized: "Неверный пароль", too_many_requests: "Слишком много попыток, подождите", admin_disabled: "Админка выключена: не задан ADMIN_PASSWORD (8+ знаков)", not_found: "Не найдено", bad_request: "Проверьте поля" }[data.error] || data.error);
  return data;
}

function say(text, bad) { $("msg").innerHTML = '<span class="' + (bad ? "bad" : "ok") + '">' + esc(text) + "</span>"; }
async function run(fn) { try { await fn(); } catch (e) { say(e.message, true); } }

async function login() {
  password = $("pwd").value.trim();
  $("msg").innerHTML = "";
  await run(async () => { await api("POST", "login"); sessionStorage.setItem("strassio-admin", password); show(); });
}
function logout() { password = ""; sessionStorage.removeItem("strassio-admin"); $("app").hidden = true; $("login").hidden = false; }
function show() { $("login").hidden = true; $("app").hidden = false; search(); }

document.querySelectorAll("[data-tab]").forEach((b) => b.onclick = () => {
  document.querySelectorAll("[data-tab]").forEach((x) => x.classList.toggle("on", x === b));
  document.querySelectorAll("[data-page]").forEach((p) => p.hidden = p.dataset.page !== b.dataset.tab);
  if (b.dataset.tab === "audit") loadAudit();
});

async function createKeys() {
  await run(async () => {
    const data = await api("POST", "keys", { count: +$("kCount").value, days: +$("kDays").value || null, maxPcs: +$("kPcs").value, note: $("kNote").value });
    // Клиенту отдаются оба: ключ (вводит в плагине) и ключ восстановления (для сайта /license).
    $("kOut").hidden = false; $("kOut").value = data.keys.map((k) => k.serial + "   " + k.recoveryCode).join("\\n");
    say("Создано ключей: " + data.keys.length + ". Рядом с каждым — ключ восстановления, отдайте клиенту оба."); search();
  });
}

function button(label, handler) { return '<button class="small" data-h="' + esc(handler) + '">' + esc(label) + "</button> "; }

async function search() {
  await run(async () => {
    const data = await api("GET", "licenses?q=" + encodeURIComponent($("q").value));
    if (!data.licenses.length) { $("results").innerHTML = '<p class="muted">Ничего нет</p>'; return; }
    $("results").innerHTML = "<table><tr><th>Ключ</th><th>Статус</th><th>Срок</th><th>ПК</th><th>Переносы</th><th>Заметка</th><th>Активации</th><th></th></tr>" +
      data.licenses.map((l) => {
        const acts = l.activations.map((a) => '<div class="mono' + (a.status === "active" ? "" : " muted") + '">' + esc(a.hwid.slice(0, 22)) + "… " +
          (a.status === "active" ? "" : "(отозвана) ") + '<span class="muted">' + date(a.lastCheckAt) + " · " + esc(a.pluginVersion) + " · Corel " + esc(a.corelVersion) + "</span> " +
          (a.status === "active" ? button("отозвать", JSON.stringify(["revoke", a.id])) : "") + "</div>").join("") || '<span class="muted">нет</span>';
        const s = l.serial;
        return "<tr><td class=mono>" + esc(s) + '<br><span class="muted">' + esc(l.recoveryCode || "нет ключа восстановления") + "</span></td><td class="' + (l.status === "active" ? "ok" : "bad") + '">' + (l.status === "active" ? "активен" : "заблокирован") +
          "</td><td>" + (l.expiresAt ? date(l.expiresAt) : "бессрочно") + "</td><td>" + l.maxPcs + "</td><td>" + l.transfersCount + "</td><td>" + esc(l.note) + "</td><td>" + acts + "</td><td>" +
          button(l.status === "active" ? "заблокировать" : "разблокировать", JSON.stringify(["act", s, l.status === "active" ? "block" : "unblock"])) +
          button("продлить", JSON.stringify(["extend", s])) +
          button("разрешить перенос сейчас", JSON.stringify(["act", s, "reset_transfers"])) +
          button("число ПК", JSON.stringify(["pcs", s, l.maxPcs])) +
          button("заметка", JSON.stringify(["note", s])) +
          button("новый ключ восстановления", JSON.stringify(["recovery", s])) + "</td></tr>";
      }).join("") + "</table>";
  });
}

// Кнопки таблицы: действие и данные лежат в data-h (JSON) — без склейки кода в onclick.
$("results").addEventListener("click", (e) => {
  const b = e.target.closest("button[data-h]"); if (!b) return;
  const [kind, x, y] = JSON.parse(b.dataset.h);
  ({ act: () => act(x, y), extend: () => extend(x), pcs: () => pcs(x, y), note: () => note(x), revoke: () => revoke(x), recovery: () => newRecovery(x) })[kind]();
});

async function act(serial, action, value) { await run(async () => { await api("POST", "license", { serial, action, value }); say("Готово: " + serial); search(); }); }
function extend(serial) { const d = prompt("На сколько дней продлить? (0 — сделать бессрочным)", "365"); if (d !== null) act(serial, "extend", +d); }
function pcs(serial, current) { const n = prompt("Сколько компьютеров на ключ?", current); if (n !== null) act(serial, "max_pcs", +n); }
function newRecovery(serial) { if (confirm("Сделать новый ключ восстановления? Старый перестанет работать на сайте.")) act(serial, "new_recovery"); }
function note(serial) { const t = prompt("Заметка"); if (t !== null) act(serial, "note", t); }
async function revoke(id) { if (confirm("Отозвать активацию? Плагин на этом компьютере отключится при следующей проверке.")) await run(async () => { await api("POST", "revoke", { id }); search(); }); }

async function offline() {
  await run(async () => {
    const data = await api("POST", "offline", { serial: $("oSerial").value, hwid: $("oHwid").value });
    const blob = new Blob([JSON.stringify(data.license)], { type: "application/json" });
    const a = document.createElement("a"); a.href = URL.createObjectURL(blob); a.download = "strassio-license.json"; a.click();
    say("Файл лицензии скачан — отправьте его клиенту");
  });
}

async function publishUpdate() {
  await run(async () => {
    await api("POST", "update", { channel: $("uChannel").value, version: $("uVersion").value, minVersion: $("uMin").value, url: $("uUrl").value, sha256: $("uSha").value, notesRu: $("uRu").value, notesEn: $("uEn").value });
    say("Обновление опубликовано");
  });
}

async function loadAudit() {
  await run(async () => {
    const data = await api("GET", "audit");
    $("audit").innerHTML = "<table><tr><th>Когда</th><th>Кто</th><th>Что</th><th>Ключ</th><th>Подробности</th></tr>" +
      data.log.map((r) => "<tr><td>" + date(r.at) + "</td><td>" + esc(r.actor) + "</td><td>" + esc(r.action) + "</td><td class=mono>" + esc(r.serial) + "</td><td class=mono>" + esc(r.details) + "</td></tr>").join("") + "</table>";
  });
}

if (password) run(async () => { await api("POST", "login"); show(); });
</script>
</body>
</html>`;
