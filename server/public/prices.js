// Цены с сайта (тарифы из хаба с галочкой «на сайте») и их подписи — для главной и страницы «Купить».
// Нет связи или цен нет — пустой список: страница просто не показывает цены.
function strassioPrices() {
  return fetch("/api/prices")
    .then((r) => (r.ok ? r.json() : { prices: [] }))
    .then((d) => (Array.isArray(d.prices) ? d.prices : []))
    .catch(() => []);
}

/** «$115.20», «1 200 000 сум». */
function priceText(price, currency) {
  const n = Number(price) || 0;
  const whole = Math.floor(n).toString().replace(/\B(?=(\d{3})+(?!\d))/g, currency === "USD" ? "," : " ");
  const cents = Math.round((n - Math.floor(n)) * 100);
  const frac = cents ? (currency === "USD" ? "." : ",") + String(cents).padStart(2, "0") : "";
  return currency === "USD" ? "$" + whole + frac : whole + frac + " сум";
}

/** Срок тарифа словами: «навсегда», «на месяц», «на 3 месяца», «на год», «на 10 дней». */
function periodText(days) {
  if (!days) return "навсегда";
  const named = { 30: "на месяц", 90: "на 3 месяца", 180: "на 6 месяцев", 365: "на год" };
  if (named[days]) return named[days];
  const n = days % 100 >= 11 && days % 100 <= 14 ? "дней" : { 1: "день", 2: "дня", 3: "дня", 4: "дня" }[days % 10] || "дней";
  return "на " + days + " " + n;
}

/** Для срока дольше месяца — сколько выходит в месяц: «$9.60 в месяц». */
function perMonth(t) {
  if (!t.days || t.days <= 31) return "";
  const months = Math.round(t.days / 30);
  return priceText(Math.round((t.price / months) * (t.currency === "USD" ? 100 : 1)) / (t.currency === "USD" ? 100 : 1), t.currency) + " в месяц";
}
