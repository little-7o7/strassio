// Копия всей базы Strassio для компьютеров владельца (Little Hub; владелец 2026-11-08, пункт 52: «копия всей базы
// Strassio каждую ночь, под паролем копий»). Хаб спрашивает GET /api/platform/pc/backup?live=1&have=<время>, шифрует
// копию паролем копий на лету и отдаёт программе — на компьютере она лежит как копия хаба и Sarpo
// («Strassio/Копии базы/<месяц>/strassio-<день>.l7copy»). Файл дня заменяется, пока в Strassio что-то меняется, в
// 00:00 по Ташкенту замирает.
//
// Формат — как у Sarpo (sarpobusiness/src/lib/backup.ts): SQL, таблицы в порядке связей, строки — json_agg самого
// Postgres, обратно — json_populate_recordset: суммы, даты и файлы договоров переносятся без потерь. Восстановление
// ЗАМЕНЯЕТ все данные базы. platform_nonces (одноразовые отметки подписи) не берём — они нужны только 10 минут.
import { randomBytes } from "node:crypto";
import { gzipSync } from "node:zlib";

export type Query = (text: string, params?: unknown[]) => Promise<Record<string, any>[]>;

export type Backup = {
  /** Время последней записи журнала (мс): сменилось — в Strassio что-то делали. 0 — журнал пуст. */
  made(): Promise<number>;
  /** Вся база в SQL (gzip). */
  dump(at: Date): Promise<Buffer>;
};

const NAME = /^[a-z_][a-z0-9_]*$/;
const SKIP = new Set(["platform_nonces"]);
/** Строк за один запрос: файлы договоров (до 3 МБ каждый) не собираются в один огромный ответ базы. */
const PAGE = 50;
/** Таблицы с файлами (до 4 МБ в base64 на строку): по 2 строки, чтобы ответ базы не вырос за её предел. */
const BIG = new Set(["contract_files"]);

type Col = { t: string; c: string; serial: boolean };

export async function dumpSql(sql: Query, at: Date): Promise<string> {
  const cols = (await sql(`SELECT c.table_name AS t, c.column_name AS c, coalesce(c.column_default LIKE 'nextval(%', false) AS serial
    FROM information_schema.columns c JOIN information_schema.tables t ON t.table_schema = c.table_schema AND t.table_name = c.table_name
    WHERE c.table_schema = 'public' AND t.table_type = 'BASE TABLE'
    ORDER BY c.table_name, c.ordinal_position`)) as Col[];
  const fks = (await sql(`SELECT conrelid::regclass::text AS t, confrelid::regclass::text AS ref FROM pg_constraint
    WHERE contype = 'f' AND connamespace = 'public'::regnamespace`)) as { t: string; ref: string }[];

  const tables = new Map<string, Col[]>();
  for (const col of cols) {
    if (!NAME.test(col.t) || !NAME.test(col.c)) throw new Error(`Неожиданное имя в базе: ${col.t}.${col.c}`);
    if (SKIP.has(col.t)) continue;
    tables.set(col.t, [...(tables.get(col.t) ?? []), col]);
  }
  const order: string[] = [];
  const seen = new Set<string>();
  const visit = (t: string) => {
    if (seen.has(t)) return;
    seen.add(t);
    for (const f of fks) if (f.t === t && f.ref !== t && tables.has(f.ref)) visit(f.ref);
    order.push(t);
  };
  for (const t of tables.keys()) visit(t);

  // Таблица с id — пачками по id (файлы договоров большие — по 2), без id — целиком. Читаем с конца порядка: сначала
  // строки, что ссылаются, потом те, на что ссылаются. Ключ и его активация, созданные посреди выгрузки, тогда не
  // дадут строку без родителя (снимка у запросов по HTTP нет). В файл — в прямом порядке.
  const got = new Map<string, string[]>();
  const push = (t: string, json: string) => got.set(t, [...(got.get(t) ?? []), json]);
  for (const t of [...order].reverse()) {
    const byId = tables.get(t)!.some((c) => c.c === "id");
    if (!byId) {
      const [row] = await sql(`SELECT count(*)::int AS n, coalesce(json_agg(x), '[]')::text AS json FROM "${t}" x`);
      if (row.n) push(t, row.json);
      continue;
    }
    const page = BIG.has(t) ? 2 : PAGE;
    let after = -1;
    for (;;) {
      const [row] = await sql(
        `SELECT count(*)::int AS n, max(id)::text AS last, coalesce(json_agg(x ORDER BY x.id), '[]')::text AS json
         FROM (SELECT * FROM "${t}" WHERE id > $1 ORDER BY id LIMIT ${page}) x`,
        [after],
      );
      if (!row.n) break;
      push(t, row.json);
      after = Number(row.last);
      if (row.n < page || !Number.isFinite(after)) break;
    }
  }
  const data = order.flatMap((t) => (got.get(t) ?? []).map((json) => ({ t, json })));
  let tag = "";
  do tag = `$st${randomBytes(6).toString("hex")}$`;
  while (data.some((d) => d.json.includes(tag)));

  const names = (t: string) => tables.get(t)!.map((c) => `"${c.c}"`).join(", ");
  return [
    `-- Strassio: копия базы данных, ${at.toISOString()}.`,
    "-- Восстановление ЗАМЕНЯЕТ ВСЕ ДАННЫЕ базы, в которую его загружают.",
    "BEGIN;",
    ...(order.length ? [`TRUNCATE ${order.map((t) => `"${t}"`).join(", ")} RESTART IDENTITY CASCADE;`] : []),
    ...data.map(({ t, json }) => `INSERT INTO "${t}" (${names(t)}) SELECT ${names(t)} FROM json_populate_recordset(NULL::"${t}", ${tag}${json}${tag});`),
    // Счётчики id — дальше последней строки, чтобы новые записи не столкнулись со старыми.
    ...cols.filter((c) => c.serial && tables.has(c.t)).map((c) => `SELECT setval(pg_get_serial_sequence('"${c.t}"', '${c.c}'), coalesce(max("${c.c}"), 0) + 1, false) FROM "${c.t}";`),
    "COMMIT;",
    "",
  ].join("\n");
}

/** Копия по базе Postgres. */
export function pgBackup(sql: Query): Backup {
  return {
    async made() {
      const [r] = await sql(`SELECT coalesce(floor(extract(epoch FROM max(at)) * 1000), 0)::bigint::text AS t FROM audit_log`);
      return Number(r?.t ?? 0) || 0;
    },
    async dump(at) {
      return gzipSync(await dumpSql(sql, at));
    },
  };
}

/** Имя копии дня по Ташкенту: strassio-ГГГГ-ММ-ДД.sql.gz. */
export function backupName(now: number): string {
  return `strassio-${new Date(now + 5 * 60 * 60 * 1000).toISOString().slice(0, 10)}.sql.gz`;
}
