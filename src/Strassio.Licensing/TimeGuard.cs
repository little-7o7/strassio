using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Strassio.Licensing
{
    /// <summary>Где хранить метки времени (файл, реестр). Ошибка чтения/записи — не исключение, а null/ничего.</summary>
    public interface IMarkStore
    {
        string? Read();

        void Write(string value);
    }

    /// <summary>Метка в файле.</summary>
    public sealed class FileMarkStore : IMarkStore
    {
        private readonly string path;

        public FileMarkStore(string path)
        {
            this.path = path;
        }

        public string? Read()
        {
            try
            {
                return File.Exists(path) ? File.ReadAllText(path, Encoding.ASCII) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public void Write(string value)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, value, Encoding.ASCII);
            }
            catch (Exception)
            {
                // Нет доступа — остаётся метка в других местах.
            }
        }
    }

    /// <summary>
    /// Защита от перевода часов назад и от подмены файла лицензии старой копией.
    ///
    /// Помнит три момента (UTC) и хранит их в нескольких местах сразу (файл + реестр), каждую копию —
    /// с подписью HMAC от кода компьютера: поправить число руками или перенести метку с другого
    /// компьютера нельзя, а удалить метки — значит потерять право работать без сверки с сервером.
    /// <list type="bullet">
    /// <item><b>LastSeen</b> — самое позднее время на часах этого компьютера, которое плагин видел;</item>
    /// <item><b>ServerTime</b> — самое позднее время сервера (issuedAt последней полученной лицензии) —
    ///   ему можно верить, часы компьютера тут ни при чём;</item>
    /// <item><b>Forbidden</b> — лицензии, выданные не позже этого момента, больше не принимаются
    ///   (компьютер освободили, лицензию перенесли или отозвали — старая копия файла не оживёт).</item>
    /// </list>
    /// </summary>
    public sealed class TimeGuard
    {
        /// <summary>Сколько часы могут «отставать» без подозрений (перевод времени, синхронизация).</summary>
        public static readonly TimeSpan Tolerance = TimeSpan.FromHours(2);

        /// <summary>Чаще этого LastSeen на диск не пишется.</summary>
        private static readonly TimeSpan WriteEvery = TimeSpan.FromMinutes(10);

        private readonly IReadOnlyList<IMarkStore> stores;
        private readonly Lazy<byte[]> key;
        private readonly object sync = new object();
        private bool loaded;
        private DateTime? lastSeen;
        private DateTime? serverTime;
        private DateTime? forbidden;
        private DateTime? lastSeenOnDisk;

        /// <param name="secret">Отличает компьютеры: метка с другого ПК не подойдёт (код компьютера).</param>
        public TimeGuard(IReadOnlyList<IMarkStore> stores, Func<string> secret)
        {
            this.stores = stores;
            key = new Lazy<byte[]>(() =>
            {
                using SHA256 sha = SHA256.Create();
                return sha.ComputeHash(Encoding.UTF8.GetBytes("Strassio.TimeGuard.v1|" + secret()));
            });
        }

        /// <summary>Есть хотя бы одна целая метка (иначе — первый запуск или метки удалили).</summary>
        public bool HasMarks
        {
            get
            {
                Load();
                return lastSeen.HasValue || serverTime.HasValue;
            }
        }

        public DateTime? LastSeenUtc
        {
            get
            {
                Load();
                return lastSeen;
            }
        }

        public DateTime? ServerTimeUtc
        {
            get
            {
                Load();
                return serverTime;
            }
        }

        public DateTime? ForbiddenUtc
        {
            get
            {
                Load();
                return forbidden;
            }
        }

        /// <summary>Часы компьютера показывают <paramref name="nowUtc"/>: если это позже всего виденного — запомнить.</summary>
        public void Observe(DateTime nowUtc)
        {
            lock (sync)
            {
                Load();
                if (lastSeen.HasValue && nowUtc <= lastSeen.Value)
                {
                    return;
                }

                lastSeen = nowUtc;
                if (!lastSeenOnDisk.HasValue || nowUtc - lastSeenOnDisk.Value >= WriteEvery)
                {
                    Save();
                }
            }
        }

        /// <summary>
        /// Свежая лицензия от сервера: его время — надёжное. LastSeen ставится на него, даже если
        /// раньше часы компьютера убегали вперёд (иначе человек, случайно поставивший 2030 год,
        /// остался бы без работы навсегда).
        /// </summary>
        public void AcceptServerTime(DateTime serverUtc)
        {
            lock (sync)
            {
                Load();
                serverTime = Max(serverTime, serverUtc);
                lastSeen = serverUtc;
                Save();
            }
        }

        /// <summary>Лицензии, выданные не позже <paramref name="issuedUtc"/>, больше не принимать.</summary>
        public void Forbid(DateTime issuedUtc)
        {
            lock (sync)
            {
                Load();
                forbidden = Max(forbidden, issuedUtc);
                Save();
            }
        }

        private static DateTime? Max(DateTime? a, DateTime b) => a.HasValue && a.Value > b ? a : b;

        private void Load()
        {
            lock (sync)
            {
                if (loaded)
                {
                    return;
                }

                loaded = true;
                foreach (IMarkStore store in stores)
                {
                    if (!TryParse(store.Read(), out DateTime? seen, out DateTime? server, out DateTime? forbid))
                    {
                        continue;
                    }

                    // Из всех целых копий берётся самое строгое: удалить или откатить одну — мало.
                    if (seen.HasValue) lastSeen = Max(lastSeen, seen.Value);
                    if (server.HasValue) serverTime = Max(serverTime, server.Value);
                    if (forbid.HasValue) forbidden = Max(forbidden, forbid.Value);
                }

                lastSeenOnDisk = lastSeen;
            }
        }

        private void Save()
        {
            string body = "v1|" + Ticks(lastSeen) + "|" + Ticks(serverTime) + "|" + Ticks(forbidden);
            string value = body + "|" + Sign(body);
            foreach (IMarkStore store in stores)
            {
                store.Write(value);
            }

            // Запись запретили (файл «только чтение», права на ключ реестра) — метка застряла бы в
            // прошлом, и часы можно было бы откатывать до неё. Такое считается подделкой.
            Broken = !stores.Any(s => s.Read()?.Trim() == value);
            lastSeenOnDisk = lastSeen;
        }

        /// <summary>Метку не удалось записать ни в одно место.</summary>
        public bool Broken { get; private set; }

        private bool TryParse(string? value, out DateTime? seen, out DateTime? server, out DateTime? forbid)
        {
            seen = server = forbid = null;
            string[] parts = (value ?? string.Empty).Trim().Split('|');
            if (parts.Length != 5 || parts[0] != "v1")
            {
                return false;
            }

            string body = string.Join("|", parts, 0, 4);
            byte[] expected = Encoding.ASCII.GetBytes(Sign(body));
            byte[] given = Encoding.ASCII.GetBytes(parts[4]);
            if (!FixedTimeEquals(expected, given))
            {
                return false;
            }

            return TryTicks(parts[1], out seen) && TryTicks(parts[2], out server) && TryTicks(parts[3], out forbid);
        }

        private string Sign(string body)
        {
            using var hmac = new HMACSHA256(key.Value);
            return string.Concat(hmac.ComputeHash(Encoding.ASCII.GetBytes(body)).Select(b => b.ToString("x2")));
        }

        private static string Ticks(DateTime? value) => value.HasValue ? value.Value.Ticks.ToString(CultureInfo.InvariantCulture) : "-";

        private static bool TryTicks(string text, out DateTime? value)
        {
            value = null;
            if (text == "-")
            {
                return true;
            }

            if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long ticks) || ticks < 0 || ticks > DateTime.MaxValue.Ticks)
            {
                return false;
            }

            value = new DateTime(ticks, DateTimeKind.Utc);
            return true;
        }

        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            int diff = 0;
            for (int i = 0; i < a.Length; i++)
            {
                diff |= a[i] ^ b[i];
            }

            return diff == 0;
        }
    }
}
