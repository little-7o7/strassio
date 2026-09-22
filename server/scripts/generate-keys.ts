// Пара ключей подписи лицензий. Запуск: npm run keys
// Закрытый ключ — ТОЛЬКО в переменную окружения LICENSE_PRIVATE_KEY на сервере (vercel env add),
// никуда не сохранять и не коммитить. Открытый — в плагин (Strassio.Licensing, LicenseKeys.cs).
import { generateKeys } from "../core/crypto.js";

const { privateKey, publicKey } = generateKeys();
console.log("LICENSE_PRIVATE_KEY (секрет, только на сервер):");
console.log(privateKey);
console.log("");
console.log("Открытый ключ (в плагин):");
console.log(publicKey);
