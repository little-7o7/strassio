// Код компьютера (SPEC 13.1). Плагин присылает 4 коротких хэша через точку (HardwareCode.ToStorage в
// Strassio.Licensing); "-" — признак не прочитан. Сравнение нечёткое — ровно как HardwareCode.Matches:
// среди признаков, известных в обоих кодах, различаться может один, совпасть должны хотя бы два.

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
