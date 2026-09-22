// Код компьютера (SPEC 13.1). Плагин присылает 4 коротких хэша через точку (HardwareCode.ToStorage в
// Strassio.Licensing); "-" — признак не прочитан. Сравнение нечёткое — ровно как HardwareCode.Matches:
// среди признаков, известных в обоих кодах, различаться может один, совпасть должны хотя бы два.
import { createHash } from "node:crypto";


export const UNKNOWN = "-";
export const COMPONENTS = 4;

export function parseHwid(text: unknown): string[] | null {
  if (typeof text !== "string") return null;
  const parts = text.trim().toLowerCase().split(".");
  if (parts.length !== COMPONENTS) return null;
  if (!parts.every((p) => p === UNKNOWN || /^[0-9a-f]{16}$/.test(p))) return null;
  if (parts.filter((p) => p !== UNKNOWN).length < 2) return null; // с одним признаком сравнивать не с чем
  return parts;
}

export function hwidMatches(a: string[], b: string[], allowedDifferences = 1): boolean {
  let same = 0;
  let different = 0;
  for (let i = 0; i < COMPONENTS; i++) {
    if (a[i] === UNKNOWN || b[i] === UNKNOWN) continue;
    if (a[i] === b[i]) same++;
    else different++;
  }
  return different <= allowedDifferences && same >= 2;
}

/**
 * Короткий код компьютера XXXX-XXXX-XXXX-XXXX — ровно как HardwareCode.Display в плагине (первые
 * 16 знаков SHA-256 от признаков через «|»): человек сверяет его с окном «Лицензия».
 */
export function hwidDisplay(parts: string[]): string {
  const hex = createHash("sha256").update(parts.join("|"), "utf8").digest("hex").substring(0, 16).toUpperCase();
  return [0, 4, 8, 12].map((i) => hex.substring(i, i + 4)).join("-");
}
