/*
  Переключатель темы сайта: «Авто» (как в системе), «Светлая», «Тёмная».
  Выбор хранится у посетителя в браузере (localStorage, ключ strassio-theme) и сразу ставится
  на <html data-theme="...">, до отрисовки страницы — поэтому подключать файл нужно в <head>
  обычным <script src="/theme.js"></script>, без defer: иначе при тёмной теме мелькнёт белый экран.
  Сам переключатель добавляется в конец меню, когда страница загрузится.
*/
(function () {
  var KEY = "strassio-theme";
  var root = document.documentElement;

  function saved() {
    try {
      var value = localStorage.getItem(KEY);
      return value === "light" || value === "dark" ? value : "auto";
    } catch (e) {
      return "auto"; // приватный режим или запрет хранилища — просто «Авто»
    }
  }

  function apply(mode) {
    if (mode === "auto") {
      root.removeAttribute("data-theme");
    } else {
      root.setAttribute("data-theme", mode);
    }
  }

  apply(saved());

  var ICONS = {
    auto:
      '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round">' +
      '<circle cx="12" cy="12" r="8"/><path d="M12 4a8 8 0 0 1 0 16z" fill="currentColor" stroke="none"/></svg>',
    light:
      '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round">' +
      '<circle cx="12" cy="12" r="4.5"/><path d="M12 2.5v2M12 19.5v2M2.5 12h2M19.5 12h2M5.2 5.2l1.4 1.4M17.4 17.4l1.4 1.4M18.8 5.2l-1.4 1.4M6.6 17.4l-1.4 1.4"/></svg>',
    dark:
      '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">' +
      '<path d="M20 14.5A8.5 8.5 0 0 1 9.5 4a8.5 8.5 0 1 0 10.5 10.5z"/></svg>',
  };

  var TITLES = { auto: "Как в системе", light: "Светлая тема", dark: "Тёмная тема" };

  function build() {
    // Сайт — в меню шапки; админка — в место, помеченное data-theme-switch.
    var nav = document.querySelector("[data-theme-switch]") || document.querySelector("header.top nav");
    if (!nav || nav.querySelector(".theme-switch")) {
      return;
    }

    var box = document.createElement("div");
    box.className = "theme-switch";
    box.setAttribute("role", "group");
    box.setAttribute("aria-label", "Тема оформления");

    var current = saved();
    var buttons = {};

    ["auto", "light", "dark"].forEach(function (mode) {
      var b = document.createElement("button");
      b.type = "button";
      b.innerHTML = ICONS[mode];
      b.title = TITLES[mode];
      b.setAttribute("aria-label", TITLES[mode]);
      b.setAttribute("aria-pressed", String(mode === current));
      b.addEventListener("click", function () {
        try {
          if (mode === "auto") {
            localStorage.removeItem(KEY);
          } else {
            localStorage.setItem(KEY, mode);
          }
        } catch (e) {
          // не сохранилось — тема всё равно сменится до конца этой страницы
        }

        apply(mode);
        Object.keys(buttons).forEach(function (name) {
          buttons[name].setAttribute("aria-pressed", String(name === mode));
        });
      });

      buttons[mode] = b;
      box.appendChild(b);
    });

    nav.appendChild(box);
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", build);
  } else {
    build();
  }
})();
