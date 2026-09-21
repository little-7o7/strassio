<p align="center">
  <img src="assets/icon/strassio-stone-256.png" width="96" alt="Strassio">
</p>

<h1 align="center">Strassio</h1>

<p align="center">
  Плагин для CorelDRAW, который превращает линии и фигуры в шаблоны страз.<br>
  Каждая страза — обычный круг нужного диаметра, готовый для резки шаблона.
</p>

<p align="center">
  <a href="https://github.com/little-7o7/strassio/releases">Скачать</a> ·
  <a href="docs/CHANGELOG.md">Что нового</a> ·
  <a href="docs/SPEC.md">Техническое задание</a> ·
  <a href="#english">English</a>
</p>

---

> **Статус: бета, в разработке.** Готово ядро расстановки и докер с выбором камня и параметрами.
> Правка камней, лицензия и автообновление — впереди (см. [план](#план)).

## Что умеет

Нарисуйте линию или фигуру, выберите камень (размер и цвет) и метод, нажмите **«Создать»**.
Стразы появятся одной группой с понятным именем (`Strassio: линия ss6 Красный`),
у каждого круга имя вида `ss6 Красный`. Один **Ctrl+Z** отменяет всю операцию.

<table>
  <tr>
    <td align="center"><img src="docs/images/l2-5rows-stagger-star.svg" width="260"><br><sub>Вокруг линии — 5 рядов, шахматный сдвиг</sub></td>
    <td align="center"><img src="docs/images/f4-heart.svg" width="260"><br><sub>Комбинированная заливка</sub></td>
    <td align="center"><img src="docs/images/l2-zigzag.svg" width="260"><br><sub>Вокруг линии — острые углы зигзага</sub></td>
  </tr>
  <tr>
    <td align="center"><img src="docs/images/f2-star.svg" width="260"><br><sub>Соты</sub></td>
    <td align="center"><img src="docs/images/l3-inside-star.svg" width="260"><br><sub>По смещённой линии — внутрь</sub></td>
    <td align="center"><img src="docs/images/f5-square.svg" width="260"><br><sub>Кант — 2 ряда по краю</sub></td>
  </tr>
</table>

### Стразы по линии

| Метод | Что делает |
|---|---|
| **По линии** | Один ряд вдоль линии или контура. Шаг: подгонка без дырки в конце, точный шаг или точное количество. |
| **Вокруг линии** | Несколько рядов в обе стороны, только наружу или только внутрь; шахматный сдвиг; круглые или острые углы. |
| **По смещённой линии** | Один ряд на заданном расстоянии от линии — снаружи или внутри фигуры. |

### Заливка формы

| Метод | Что делает |
|---|---|
| **Сетка** | Квадратная сетка с поворотом на любой угол. |
| **Соты** | Плотная шахматная заливка. |
| **Контурная** | Ряды от края внутрь, повторяя форму; середина — соты или сетка. |
| **Комбинированная** | N рядов по краю + соты или сетка внутри. |
| **Кант** | Только N рядов по краю, внутри пусто. |

Отверстия в фигурах (буквы «О», «А», кольца) остаются пустыми.

### Острые углы и наложения

- В каждом остром углу страза ставится точно в вершину, отрезки между углами заполняются отдельно —
  угол остаётся углом. Замкнутые контуры заполняются равномерно, без «шва».
- Стразы не налезают друг на друга. Для нескольких рядов можно выбрать, что делать с наложениями:
  **удалить лишние** (соседи раздвигаются, дырки не остаётся), **сдвинуть** или **только показать** —
  такие стразы получат красную обводку.

### Докер

<img src="docs/images/docker.png" width="260" align="right" alt="Докер Strassio">

- Выбор камня: размер из вашей таблицы (`ss6 — 2,4 мм`) и цвет кружками-образцами.
- Вкладки «Линия» и «Заливка», у каждого метода — только нужные ему параметры и подсказки.
- Все параметры и последний камень запоминаются.
- **Русский и английский** интерфейс, переключается без перезапуска CorelDRAW.
- **Миллиметры или дюймы** — поля и таблица камней пересчитываются.
- **Тема как у CorelDRAW:** докер берёт цвета текущей схемы CorelDRAW и сам меняется вместе с ней.
- Обводка страз: нет, волосяная или заданной толщины.

Таблица камней хранится в `%APPDATA%\Strassio\stones.json`: у каждого размера свой список цветов.

<br clear="right">

## Установка

1. Скачайте `Strassio-Setup-<версия>.exe` со страницы [Releases](https://github.com/little-7o7/strassio/releases).
2. Закройте CorelDRAW и запустите установщик. Он сам найдёт установленные версии CorelDRAW
   и предложит выбрать, куда ставить.
3. Откройте CorelDRAW и нажмите кнопку-камень на панели **Strassio** —
   или меню **Окно → Докеры → Strassio**.

**Совместимость:** цель — все версии CorelDRAW начиная с X7 (32 и 64 бита). Сейчас проверяется на CorelDRAW 2025 и 2026.
Подробнее — [docs/УСТАНОВКА.md](docs/УСТАНОВКА.md).

## План

| Этап | Что | Статус |
|---|---|---|
| 0 | Каркас, кнопка, докер, одна DLL на все версии CorelDRAW | ✅ |
| 1 | Ядро: линии L1–L3, заливки F1–F5, углы, исправление пересечений | ✅ |
| 2 | Докер: камень, методы, параметры, настройки, языки, темы | 🔧 в работе |
| 3 | Правка и цвет: режимы удаления, перекраска, выделение по размеру/цвету | |
| 4 | Переменная ширина, переход размера, пунктир, автоподбор сетки, живой предпросмотр | |
| 5 | Векторные инструменты, проверки для производства, пресеты | |
| 6 | Лицензия, обновления, установщик | |

Полный список функций — в [техническом задании](docs/SPEC.md).

## Для разработчиков

```
src/Strassio.Core/        геометрия и алгоритмы — не знает о CorelDRAW (netstandard2.0)
src/Strassio.Corel/       аддон CorelDRAW: кнопка, докер WPF (net48, AnyCPU)
src/Strassio.Licensing/   клиент лицензии и обновлений
tests/Strassio.Core.Tests xUnit
tools/Strassio.Preview/   рисует результат алгоритмов в SVG — без CorelDRAW
installer/                установщик Inno Setup
lang/                     тексты интерфейса (ru.json, en.json)
```

Нужен [.NET 8 SDK](https://dotnet.microsoft.com/download); для установщика — [Inno Setup 6](https://jrsoftware.org/isdl.php).

```powershell
dotnet build Strassio.sln                                     # сборка
dotnet test tests/Strassio.Core.Tests                         # тесты ядра
dotnet run --project tools/Strassio.Preview -- methods        # все методы в SVG → out/preview/methods/
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1   # установщик → installer/out/
```

Вся геометрия живёт в `Strassio.Core` и сначала проверяется тестами и SVG-картинками,
и только потом подключается к CorelDRAW. Внутри всё считается в миллиметрах.
Правила проекта — в [CLAUDE.md](CLAUDE.md).

## Лицензия

© 2026 Munisxonov Maxmudxon. Все права защищены. Исходный код открыт для просмотра;
использование, копирование и распространение — только с письменного разрешения автора.

---

<a name="english"></a>
## English

**Strassio** is a CorelDRAW add-on that turns lines and shapes into rhinestone templates.
Every stone is a plain circle of the right diameter, ready for template cutting.

- **Along lines:** single row, several rows around a line (both sides / outside / inside, staggered),
  offset row. Exact step, exact count or fit-to-length. Stones land exactly on sharp corners.
- **Shape fills:** grid, honeycomb, contour, combined (edge rows + grid inside), border.
  Holes stay empty.
- **No overlaps:** remove extra stones (neighbours close the gap), shift them, or just highlight them.
- **Docker:** stone size and colour from your own stone table, per-method parameters,
  Russian and English UI, mm or inches, follows the CorelDRAW colour scheme.
- One operation = one group = one Ctrl+Z.

Targets CorelDRAW X7 and newer (32/64-bit); currently tested on CorelDRAW 2025 and 2026. Status: beta, under active development.
Download the installer from [Releases](https://github.com/little-7o7/strassio/releases).

© 2026 Munisxonov Maxmudxon. All rights reserved. Source is visible for reference only;
use, copying and redistribution require the author's written permission.
