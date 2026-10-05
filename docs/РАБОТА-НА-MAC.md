# Работа над Strassio с Mac

С 2026-10-05 автор работает на MacBook. На Mac идёт вся работа с кодом (вместе с Claude Code), а плагин
и установщик собирает GitHub: на Mac их не собрать — плагин сделан под Windows, установщик — Inno Setup.
Проверять в CorelDRAW — как раньше, на Windows. Что сделано и что дальше — [ПЛАН.md](ПЛАН.md).

## Что где делается

| Что | Где |
|---|---|
| Код, ядро, тесты, картинки Preview (SVG) | на Mac |
| Сборка плагина и установщика | GitHub, сам — после каждого push в master |
| Новая пробная версия на странице Releases | GitHub, сам — по тегу vX.Y.Z |
| Проверка в CorelDRAW | на Windows: скачать установщик с GitHub |
| Сайт лицензий (папка server) | с Mac, через Vercel |

## Новый Mac — один раз

1. Общие настройки компьютера и Claude Code (Homebrew, Git, GitHub CLI, Node, Claude Code, плагины,
   память Claude) — по README закрытого репозитория `little-7o7/claude-config`.
   Папка для проектов — `~/Developer`.
2. Скачать проект. Репозиторий публичный, поэтому коммиты в нём подписываются адресом noreply,
   а не gmail:
   ```bash
   cd ~/Developer
   gh repo clone little-7o7/strassio
   cd strassio
   git config user.email "97856162+little-7o7@users.noreply.github.com"
   ```
3. Плагин superpowers для Claude — на Windows он был включён только в этом проекте:
   ```bash
   claude plugin install superpowers@claude-plugins-official --scope local
   ```
4. .NET 8 SDK — для тестов ядра и картинок Preview:
   ```bash
   curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0
   echo 'export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"' >> ~/.zshrc
   ```
   Потом открыть новое окно терминала.
5. Сайт лицензий: `npm install -g vercel`, `vercel login`, затем `cd server && vercel link`
   (команда little7o7s-projects, проект strassio). Выкладывать сайт — только из папки server:
   `cd server && vercel deploy --prod --yes`. Пароли и ключи сайта хранятся в Vercel, на Mac их
   переносить не нужно. Резервную копию ключа подписи лицензий в GitHub не класть никогда.

Файл Corel `Corel.Interop.VGCore.dll` на Mac не нужен: GitHub берёт его из закрытого репозитория
`little-7o7/strassio-interop` сам.

## Проверка на Mac

```bash
dotnet test tests/Strassio.Core.Tests                     # тесты ядра
dotnet run --project tools/Strassio.Preview -- methods    # картинки → out/preview/
```

`dotnet build Strassio.sln` целиком на Mac не соберётся (плагин и UiShots — под Windows). Его
проверяет GitHub: после push смотреть вкладку **Actions** (или `gh run list --limit 1`).

## Установщик

**Промежуточная сборка.** После каждого push в master GitHub собирает установщик сам:
вкладка **Actions** → последний запуск «Сборка установщика» → внизу **Artifacts** →
`Strassio-Setup-<версия>` (zip, хранится 30 дней).

**Новая пробная версия на странице Releases:**
1. Поднять версию — одинаково в `src/Strassio.Corel/Strassio.Corel.csproj` (`<Version>`) и
   `installer/Strassio.iss` (`AppVersion`).
2. Записать изменения в `docs/CHANGELOG.md`, а текст для страницы выпуска — в
   `docs/releases/vX.Y.Z.md`: первая строка `# Strassio X.Y.Z — коротко о главном`, дальше — что
   нового простыми словами.
3. Коммит, тег и отправка: `git tag vX.Y.Z` и `git push origin master vX.Y.Z`.

Минуты через две на странице [Releases](https://github.com/little-7o7/strassio/releases) появится
пробный выпуск с установщиком. Если версия в двух файлах не совпадает или не совпадает с тегом,
выпуск не создастся — запуск в Actions будет красным и напишет, что не так.

**На Windows:** скачать `Strassio-Setup-X.Y.Z.exe` со страницы Releases, закрыть CorelDRAW,
запустить.

**Обновление у пользователей:** в админке сайта вставить ссылку на установщик со страницы Releases
и его SHA-256 — он написан на странице запуска в Actions (раздел Summary).
