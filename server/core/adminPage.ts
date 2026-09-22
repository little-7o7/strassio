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
  main { max-width:1800px; margin:0 auto; padding:16px 24px; }
  a { color:var(--accent); }
  a:visited { color:var(--accent); }
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
  .key { white-space:nowrap; font-family:Consolas, monospace; font-size:14px; font-weight:600; }
  .acts { display:flex; flex-wrap:wrap; gap:4px; min-width:260px; }
  .acts button { margin:0; }
  td.client { min-width:200px; line-height:1.6; }
  td.client a { word-break:break-all; }
  @media (max-width: 800px) {
    main { padding:10px; }
    label { display:flex; width:100%; margin-right:0; }
    label input, label select { width:100%; }
    .tabs button { margin-bottom:4px; }
    table, thead, tbody, tr, th, td { display:block; width:100%; }
    tr:first-child { display:none; }
    tr { border-top:2px solid var(--line); padding:6px 0; }
    td { border:0; padding:3px 0; }
    .acts { min-width:0; }
  }
  #msg { min-height:20px; margin-bottom:8px; }
  dialog { background:var(--card); color:var(--text); border:1px solid var(--line); border-radius:10px; padding:16px; }
  dialog::backdrop { background:rgba(0,0,0,.4); }
  button.danger { color:var(--bad); border-color:var(--bad); }
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
      <button data-tab="requests">Заявки <b id="reqCount"></b></button>
      <button data-tab="trials">Пробные</button>
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
          <label style="flex:1">Заметка<input id="kNote"></label>
        </div>
        <div class="row">
          <label>Имя<input id="kFirst"></label>
          <label>Фамилия<input id="kLast"></label>
          <label>Телефон<input id="kPhone" type="tel" placeholder="+998 ..."></label>
          <label>Почта<input id="kEmail" type="email"></label>
          <label>День рождения<input id="kBirthday" type="date"></label>
          <label>Telegram<input id="kTelegram" placeholder="@username"></label>
          <button class="primary" onclick="createKeys()">Создать</button>
        </div>
        <textarea id="kOut" readonly hidden class="mono"></textarea>
      </section>
      <section>
        <h2>Поиск</h2>
        <div class="row">
          <label style="flex:1">Ключ, имя, фамилия, телефон, почта или заметка<input id="q" onkeydown="if(event.key==='Enter')search()"></label>
          <button onclick="search()">Найти</button>
        </div>
        <div id="results" class="scroll"></div>
      </section>
    </div>

    <dialog id="clientDlg">
      <h2>Клиент <span id="cSerial" class="mono muted"></span></h2>
      <div class="row">
        <label>Имя<input id="cFirst"></label>
        <label>Фамилия<input id="cLast"></label>
      </div>
      <div class="row">
        <label>Телефон<input id="cPhone" type="tel"></label>
        <label>Почта<input id="cEmail" type="email"></label>
        <label>День рождения<input id="cBirthday" type="date"></label>
        <label>Telegram<input id="cTelegram" placeholder="@username"></label>
      </div>
      <div class="row" style="justify-content:flex-end">
        <button onclick="$('clientDlg').close()">Отмена</button>
        <button class="primary" onclick="saveClient()">Сохранить</button>
      </div>
    </dialog>

    <section data-page="trials" hidden>
      <h2>Пробные периоды (14 дней)</h2>
      <p class="muted">Каждый компьютер, где нажали «Пробный период». Код компьютера — как в окне «Лицензия» у клиента.
        «удалить» — компьютер сможет взять пробный период заново.</p>
      <div id="trials" class="scroll"></div>
    </section>

    <section data-page="requests" hidden>
      <h2>Заявки на покупку</h2>
      <p class="muted">Приходят с сайта (страница «Купить»). Напишите клиенту, договоритесь — и нажмите «создать ключ»:
        данные клиента перейдут в ключ, а заявка будет отмечена выполненной.</p>
      <div id="requests" class="scroll"></div>
    </section>

    <dialog id="sendDlg" style="width:min(640px, 95vw)">
      <h2>Отправить ключ клиенту</h2>
      <textarea id="sendText" style="min-height:220px"></textarea>
      <div class="row" style="margin-top:8px">
        <a id="sendMail" target="_blank"><button>Письмо</button></a>
        <a id="sendTg" target="_blank"><button>Telegram</button></a>
        <a id="sendSms"><button>SMS</button></a>
        <button onclick="copySend()">Копировать текст</button>
        <button onclick="$('sendDlg').close()" style="margin-left:auto">Закрыть</button>
      </div>
      <p class="muted" id="sendHint">Telegram откроет чат с клиентом — текст уже скопирован, вставьте его (Ctrl+V).</p>
    </dialog>

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
  if (!data.ok) throw new Error({ unauthorized: "Неверный пароль", too_many_requests: "Слишком много попыток, подождите", admin_disabled: "Админка выключена: не задан ADMIN_PASSWORD (8+ знаков)", not_found: "Не найдено", bad_request: "Проверьте поля", bad_client: "Проверьте почту, день рождения и Telegram (4–32 латинских букв, цифр или _)" }[data.error] || data.error);
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
function show() { $("login").hidden = true; $("app").hidden = false; search(); loadRequests(); }

document.querySelectorAll("[data-tab]").forEach((b) => b.onclick = () => {
  document.querySelectorAll("[data-tab]").forEach((x) => x.classList.toggle("on", x === b));
  document.querySelectorAll("[data-page]").forEach((p) => p.hidden = p.dataset.page !== b.dataset.tab);
  if (b.dataset.tab === "audit") loadAudit();
  if (b.dataset.tab === "requests") loadRequests();
  if (b.dataset.tab === "trials") loadTrials();
});

async function createKeys() {
  await run(async () => {
    const client = { firstName: $("kFirst").value, lastName: $("kLast").value, phone: $("kPhone").value, email: $("kEmail").value, birthday: $("kBirthday").value, telegram: $("kTelegram").value };
    const data = await api("POST", "keys", { count: +$("kCount").value, days: +$("kDays").value || null, note: $("kNote").value, client });
    ["kFirst", "kLast", "kPhone", "kEmail", "kBirthday", "kTelegram", "kNote"].forEach((id) => $(id).value = "");
    // Клиенту отдаются оба: ключ (вводит в плагине) и ключ восстановления (для сайта /license).
    $("kOut").hidden = false; $("kOut").value = data.keys.map((k) => k.serial).join("\\n");
    if (data.keys.length === 1) copyText(data.keys[0].serial);
    say("Создано ключей: " + data.keys.length + (data.keys.length === 1 ? ". Ключ скопирован в буфер обмена." : ".")); search();
  });
}

function button(label, handler) { return '<button class="small" data-h="' + esc(handler) + '">' + esc(label) + "</button> "; }

async function search() {
  await run(async () => {
    const data = await api("GET", "licenses?q=" + encodeURIComponent($("q").value));
    if (!data.licenses.length) { $("results").innerHTML = '<p class="muted">Ничего нет</p>'; return; }
    licenses = {};
    data.licenses.forEach((l) => licenses[l.serial] = l);
    $("results").innerHTML = "<table><tr><th>Ключ</th><th>Клиент</th><th>Статус</th><th>Срок</th><th>Переносы</th><th>Заметка</th><th>Активации</th><th></th></tr>" +
      data.licenses.map((l) => {
        const acts = l.activations.map((a) => '<div class="mono' + (a.status === "active" ? "" : " muted") + '">' + esc(a.hwid.slice(0, 22)) + "… " +
          (a.status === "active" ? "" : "(отозвана) ") + '<span class="muted">' + date(a.lastCheckAt) + " · " + esc(a.pluginVersion) + " · Corel " + esc(a.corelVersion) + "</span> " +
          (a.status === "active" ? button("отозвать", JSON.stringify(["revoke", a.id])) : "") + "</div>").join("") || '<span class="muted">нет</span>';
        const s = l.serial;
        const client = clientCell(l.client);
        return '<tr><td><span class="key">' + esc(s) + "</span><br>" + button("копировать", JSON.stringify(["copy", s])) + '</td><td class="client">' + client + '</td><td class="' + (l.status === "active" ? "ok" : "bad") + '">' + (l.status === "active" ? "активен" : "заблокирован") +
          "</td><td>" + (l.expiresAt ? date(l.expiresAt) : "бессрочно") + "</td><td>" + l.transfersCount + "</td><td>" + esc(l.note) + "</td><td>" + acts + '</td><td><div class="acts">' +
          button(l.status === "active" ? "заблокировать" : "разблокировать", JSON.stringify(["act", s, l.status === "active" ? "block" : "unblock"])) +
          button("продлить", JSON.stringify(["extend", s])) +
          button("разрешить перенос сейчас", JSON.stringify(["act", s, "reset_transfers"])) +
          button("заметка", JSON.stringify(["note", s])) +
          button("клиент", JSON.stringify(["client", s])) +
          button("отправить ключ", JSON.stringify(["send", s])) +
          '<button class="small danger" data-h="' + esc(JSON.stringify(["delete", s])) + '">удалить</button>' + "</div></td></tr>";
      }).join("") + "</table>";
  });
}

// Кнопки таблицы: действие и данные лежат в data-h (JSON) — без склейки кода в onclick.
$("results").addEventListener("click", (e) => {
  const b = e.target.closest("button[data-h]"); if (!b) return;
  const [kind, x, y] = JSON.parse(b.dataset.h);
  ({ act: () => act(x, y), extend: () => extend(x), note: () => note(x), revoke: () => revoke(x), copy: () => copyText(x), client: () => editClient(x), delete: () => removeLicense(x), send: () => sendLicense(x) })[kind]();
});

async function act(serial, action, value) { await run(async () => { await api("POST", "license", { serial, action, value }); say("Готово: " + serial); search(); }); }
function extend(serial) { const d = prompt("На сколько дней продлить? (0 — сделать бессрочным)", "365"); if (d !== null) act(serial, "extend", +d); }
let licenses = {};
let clientSerial = "";
function editClient(serial) {
  const c = (licenses[serial] && licenses[serial].client) || {};
  clientSerial = serial; $("cSerial").textContent = serial;
  $("cFirst").value = c.firstName || ""; $("cLast").value = c.lastName || ""; $("cPhone").value = c.phone || "";
  $("cEmail").value = c.email || ""; $("cBirthday").value = c.birthday || ""; $("cTelegram").value = c.telegram ? "@" + c.telegram : "";
  $("clientDlg").showModal();
}
async function saveClient() {
  const value = { firstName: $("cFirst").value, lastName: $("cLast").value, phone: $("cPhone").value, email: $("cEmail").value, birthday: $("cBirthday").value, telegram: $("cTelegram").value };
  await run(async () => { await api("POST", "license", { serial: clientSerial, action: "client", value }); $("clientDlg").close(); say("Сохранено: " + clientSerial); search(); });
}
async function removeLicense(serial) {
  const typed = prompt("Удалить ключ " + serial + " полностью? Вместе с ним удалятся все активации, плагин у клиента отключится. Вернуть нельзя.\\n\\nЧтобы подтвердить, введите последние 4 знака ключа:");
  if (typed === null) return;
  if (typed.trim().toUpperCase() !== serial.slice(-4)) { say("Не совпало — ключ не удалён.", true); return; }
  await run(async () => { await api("POST", "license", { serial, action: "delete" }); say("Ключ удалён: " + serial); search(); });
}
// Клиент в таблицах: имя, телефон, почта, Telegram — ссылками (позвонить, написать).
function clientCell(c) {
  c = c || {};
  const phone = (c.phone || "").replace(/[^\\d+]/g, "");
  return [
    esc([c.firstName, c.lastName].filter(Boolean).join(" ")),
    c.phone ? '<a href="tel:' + esc(phone) + '">' + esc(c.phone) + "</a>" : "",
    c.email ? '<a href="mailto:' + esc(c.email) + '">' + esc(c.email) + "</a>" : "",
    c.telegram ? '<a href="https://t.me/' + esc(c.telegram) + '" target="_blank">@' + esc(c.telegram) + "</a>" : "",
    c.birthday ? '<span title="День рождения">' + esc(c.birthday.split("-").reverse().join(".")) + "</span>" : "",
  ].filter(Boolean).join("<br>") || '<span class="muted">—</span>';
}

// ---------- Заявки ----------
let requests = {};
const REQ_STATUS = { new: '<b class="bad">новая</b>', working: "в работе", done: '<span class="ok">ключ создан</span>', rejected: '<span class="muted">отказ</span>' };

async function loadRequests() {
  await run(async () => {
    const data = await api("GET", "requests");
    requests = {};
    data.requests.forEach((r) => requests[r.id] = r);
    const fresh = data.requests.filter((r) => r.status === "new").length;
    $("reqCount").textContent = fresh ? "(" + fresh + ")" : "";
    if (!data.requests.length) { $("requests").innerHTML = '<p class="muted">Заявок пока нет</p>'; return; }
    $("requests").innerHTML = "<table><tr><th>№</th><th>Когда</th><th>Клиент</th><th>Комментарий</th><th>Состояние</th><th></th></tr>" +
      data.requests.map((r) => "<tr><td>" + r.id + "</td><td>" + date(r.createdAt) + '</td><td class="client">' + clientCell(r.client) + "</td><td>" + esc(r.message) +
        "</td><td>" + (REQ_STATUS[r.status] || esc(r.status)) + (r.serial ? '<br><span class="key">' + esc(r.serial) + "</span> " + button("копировать", JSON.stringify(["copy", r.serial])) : "") + "</td><td>" +
        (r.serial
          ? button("отправить ключ", JSON.stringify(["send", r.serial]))
          : button("создать ключ", JSON.stringify(["key", r.id])) +
            (r.status === "working" ? "" : button("в работе", JSON.stringify(["status", r.id, "working"]))) +
            (r.status === "rejected" ? button("вернуть", JSON.stringify(["status", r.id, "new"])) : button("отказ", JSON.stringify(["status", r.id, "rejected"])))) +
        '<button class="small danger" data-h="' + esc(JSON.stringify(["status", r.id, "delete"])) + '">удалить</button>' +
        "</td></tr>").join("") + "</table>";
  });
}

$("requests").addEventListener("click", (e) => {
  const b = e.target.closest("button[data-h]"); if (!b) return;
  const [kind, x, y] = JSON.parse(b.dataset.h);
  ({ key: () => keyFromRequest(x), status: () => requestStatus(x, y), send: () => sendLicense(x), copy: () => copyText(x) })[kind]();
});

async function requestStatus(id, action) {
  if (action === "delete" && !confirm("Удалить заявку №" + id + "?")) return;
  await run(async () => { await api("POST", "request", { id, action }); loadRequests(); });
}

async function keyFromRequest(id) {
  const r = requests[id];
  const days = prompt("Создать ключ для " + [r.client.firstName, r.client.lastName].join(" ") + ".\\nСрок в днях (пусто — бессрочно):", "");
  if (days === null) return;
  await run(async () => {
    const data = await api("POST", "request_key", { id, days: +days || null });
    say("Ключ создан: " + data.key.serial);
    openSend(r.client, data.key.serial);
    loadRequests(); search();
  });
}

// ---------- Отправка ключа клиенту ----------
async function sendLicense(serial) {
  await run(async () => {
    const data = await api("GET", "licenses?q=" + encodeURIComponent(serial));
    const l = data.licenses.find((x) => x.serial === serial);
    if (!l) throw new Error("Ключ не найден");
    openSend(l.client, l.serial);
  });
}

function keyText(c, serial) {
  return "Здравствуйте" + (c.firstName ? ", " + c.firstName : "") + "!\\n\\n" +
    "Ваш ключ Strassio: " + serial + "\\n" +
    "Сохраните этот ключ — он нужен для активации и на странице «Моя лицензия»" + (c.email ? " (вместе с почтой " + c.email + ")" : " (вместе с вашей почтой)") + ".\\n\\n" +
    "1. Скачайте и установите плагин: https://github.com/little-7o7/strassio/releases\\n" +
    "2. В CorelDRAW: панель Strassio → Настройки (шестерёнка) → «Лицензия…» → «Открыть сайт активации».\\n" +
    "3. На сайте введите ключ и почту, скопируйте код активации и вставьте его в окно «Лицензия» → «Активировать».\\n\\n" +
    "Сайт: https://strassio.vercel.app";
}

function openSend(c, serial) {
  c = c || {};
  const text = keyText(c, serial);
  $("sendText").value = text;
  const mail = $("sendMail"), tg = $("sendTg"), sms = $("sendSms");
  mail.hidden = !c.email; tg.hidden = !c.telegram; sms.hidden = !c.phone;
  mail.href = "mailto:" + (c.email || "") + "?subject=" + encodeURIComponent("Ваш ключ Strassio") + "&body=" + encodeURIComponent(text);
  tg.href = "https://t.me/" + (c.telegram || "");
  tg.onclick = () => copySend();
  sms.href = "sms:" + (c.phone || "").replace(/[^\\d+]/g, "") + "?body=" + encodeURIComponent(text);
  $("sendHint").hidden = !c.telegram;
  $("sendDlg").showModal();
}

// Буфер обмена: ключ одним нажатием.
async function copyText(text) {
  try { await navigator.clipboard.writeText(text); }
  catch (e) {
    const t = document.createElement("textarea"); t.value = text; document.body.appendChild(t); t.select();
    document.execCommand("copy"); t.remove();
  }
  say("Скопировано: " + text);
}

// ---------- Пробные периоды ----------
async function loadTrials() {
  await run(async () => {
    const data = await api("GET", "trials");
    if (!data.trials.length) { $("trials").innerHTML = '<p class="muted">Пробных периодов пока нет</p>'; return; }
    const active = data.trials.filter((t) => t.active).length;
    $("trials").innerHTML = '<p class="muted">Всего: ' + data.trials.length + ", идут сейчас: " + active + "</p>" +
      "<table><tr><th>Код компьютера</th><th>Начало</th><th>Конец</th><th>Осталось</th><th></th></tr>" +
      data.trials.map((t) => '<tr><td class="key">' + esc(t.code) + "</td><td>" + date(t.startedAt) + "</td><td>" + date(t.endsAt) + "</td><td>" +
        (t.active ? '<span class="ok">' + t.daysLeft + " дн.</span>" : '<span class="muted">закончился</span>') + "</td><td>" +
        '<button class="small danger" data-h="' + esc(JSON.stringify(["trialDelete", t.id])) + '">удалить</button></td></tr>').join("") + "</table>";
  });
}

$("trials").addEventListener("click", async (e) => {
  const b = e.target.closest("button[data-h]"); if (!b) return;
  const [, id] = JSON.parse(b.dataset.h);
  if (!confirm("Удалить пробный период? Этот компьютер сможет взять 14 дней заново.")) return;
  await run(async () => { await api("POST", "trial_delete", { id }); say("Пробный период удалён"); loadTrials(); });
});

async function copySend() {
  const text = $("sendText").value;
  try { await navigator.clipboard.writeText(text); } catch (e) { $("sendText").select(); document.execCommand("copy"); }
  say("Текст скопирован");
}

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
