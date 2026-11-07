using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Strassio.Core.Cnc
{
    /// <summary>
    /// Профили станков для сверления (docs/SPEC.md, раздел 11.1): «Мой станок», «Станок в цеху»… —
    /// у каждого свои глубина, подачи, ноль, доска и формат файла. Хранится в drill.json; старый файл
    /// (один профиль без списка) читается как единственный профиль.
    /// </summary>
    [DataContract]
    public sealed class DrillConfig
    {
        [DataMember(Name = "profiles")]
        public List<DrillSettings> Profiles { get; set; } = new List<DrillSettings>();

        /// <summary>Название профиля, с которым работали в прошлый раз.</summary>
        [DataMember(Name = "active")]
        public string Active { get; set; } = string.Empty;

        /// <summary>Профиль по названию; нет такого — первый; нет ни одного — создаётся новый.</summary>
        public DrillSettings Profile(string? name = null)
        {
            Tidy();
            string wanted = (name ?? Active ?? string.Empty).Trim();
            DrillSettings? found = Profiles.FirstOrDefault(p => string.Equals(p.Name.Trim(), wanted, StringComparison.OrdinalIgnoreCase)) ?? Profiles.FirstOrDefault();
            if (found == null)
            {
                found = new DrillSettings { Name = DefaultName };
                Profiles.Add(found);
            }

            Active = found.Name;
            return found;
        }

        /// <summary>Имя первого профиля, если пользователь его ещё не назвал (подставляется из языка в окне).</summary>
        public static string DefaultName { get; set; } = "1";

        /// <summary>Новый профиль — копия <paramref name="copyFrom"/> под уникальным именем; он же становится текущим.</summary>
        public DrillSettings Add(DrillSettings? copyFrom, string baseName)
        {
            Tidy();
            DrillSettings profile = copyFrom?.Clone() ?? new DrillSettings();
            profile.Name = Stones.StoneTableRules.UniqueName(Profiles.Select(p => p.Name), string.IsNullOrWhiteSpace(baseName) ? DefaultName : baseName.Trim());
            Profiles.Add(profile);
            Active = profile.Name;
            return profile;
        }

        /// <summary>Убирает профиль (последний не убирается). true — убран.</summary>
        public bool Remove(DrillSettings profile)
        {
            if (Profiles.Count <= 1 || !Profiles.Remove(profile))
            {
                return false;
            }

            Active = Profiles[0].Name;
            return true;
        }

        /// <summary>Переименовать; пустое или занятое имя — false.</summary>
        public bool Rename(DrillSettings profile, string name)
        {
            string n = (name ?? string.Empty).Trim();
            if (n.Length == 0 || Profiles.Any(p => !ReferenceEquals(p, profile) && string.Equals(p.Name.Trim(), n, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            bool active = string.Equals(Active, profile.Name, StringComparison.OrdinalIgnoreCase);
            profile.Name = n;
            if (active)
            {
                Active = n;
            }

            return true;
        }

        public DrillConfig Clone() => FromJson(ToJson());

        public string ToJson()
        {
            Tidy();
            var serializer = new DataContractJsonSerializer(typeof(DrillConfig));
            using var stream = new MemoryStream();
            serializer.WriteObject(stream, this);
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        /// <summary>Читает drill.json: новый формат (список профилей) или старый (один профиль).</summary>
        public static DrillConfig FromJson(string json)
        {
            DrillConfig? config = null;
            try
            {
                config = Read<DrillConfig>(json);
            }
            catch (SerializationException)
            {
                config = null;
            }

            if (config == null || config.Profiles == null || config.Profiles.Count == 0)
            {
                DrillSettings? single = null;
                try
                {
                    single = Read<DrillSettings>(json);
                }
                catch (SerializationException)
                {
                    single = null;
                }

                config = new DrillConfig();
                if (single != null)
                {
                    config.Profiles.Add(single);
                }
            }

            config.Tidy();
            return config;
        }

        /// <summary>Нет null, у каждого профиля непустое уникальное имя.</summary>
        private void Tidy()
        {
            Profiles = (Profiles ?? new List<DrillSettings>()).Where(p => p != null).ToList();
            Active ??= string.Empty;
            var names = new List<string>();
            foreach (DrillSettings p in Profiles)
            {
                string name = (p.Name ?? string.Empty).Trim();
                p.Name = Stones.StoneTableRules.UniqueName(names, name.Length == 0 ? DefaultName : name);
                names.Add(p.Name);
                p.DrillsMm ??= new List<double>();
                p.Rules ??= new List<DrillRule>();
                p.Extension ??= ".tap";
                p.LastFolder ??= string.Empty;
                p.Format ??= GCodeFormats.ArtCam;
            }
        }

        private static T? Read<T>(string json)
            where T : class
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json ?? string.Empty));
            return serializer.ReadObject(stream) as T;
        }
    }

    /// <summary>
    /// Готовые форматы файла (docs/SPEC.md, раздел 11.1): выбор формата ставит галочки «как надо этому
    /// станку». Любая галочка руками — формат становится «Свой».
    /// </summary>
    public static class GCodeFormats
    {
        /// <summary>Как файлы .tap из ArtCAM для Mach3 и похожих: пояснения, без «%», без номеров строк.</summary>
        public const string ArtCam = "artcam";

        /// <summary>GRBL (.nc): нет смены инструмента M6 и циклов G81 — файл на каждое сверло, простые перемещения.</summary>
        public const string Grbl = "grbl";

        /// <summary>Простой файл для DSP-пультов: только команды, без пояснений и номеров строк.</summary>
        public const string Plain = "plain";

        /// <summary>Свои галочки.</summary>
        public const string Custom = "custom";

        public static IReadOnlyList<string> All { get; } = new[] { ArtCam, Grbl, Plain, Custom };

        /// <summary>Ставит галочки формата. "custom" и неизвестный — ничего не меняет.</summary>
        public static void Apply(DrillSettings s, string format)
        {
            switch (format)
            {
                case ArtCam:
                    s.Extension = ".tap";
                    s.Comments = true;
                    s.Percent = false;
                    s.LineNumbers = false;
                    s.CannedCycle = false;
                    break;
                case Grbl:
                    s.Extension = ".nc";
                    s.Comments = true;
                    s.Percent = false;
                    s.LineNumbers = false;
                    s.CannedCycle = false;
                    s.FileMode = DrillFileMode.FilePerDrill;
                    break;
                case Plain:
                    s.Extension = ".tap";
                    s.Comments = false;
                    s.Percent = false;
                    s.LineNumbers = false;
                    s.CannedCycle = false;
                    break;
                default:
                    return;
            }

            s.Format = format;
        }

        /// <summary>Какой готовый формат совпадает с галочками (иначе "custom").</summary>
        public static string Detect(DrillSettings s)
        {
            foreach (string id in new[] { s.Format, ArtCam, Grbl, Plain })
            {
                if (id == null || id == Custom || !All.Contains(id))
                {
                    continue;
                }

                DrillSettings probe = s.Clone();
                Apply(probe, id);
                if (probe.Extension == (s.Extension ?? string.Empty).Trim() && probe.Comments == s.Comments && probe.Percent == s.Percent
                    && probe.LineNumbers == s.LineNumbers && probe.CannedCycle == s.CannedCycle && probe.FileMode == s.FileMode)
                {
                    return id;
                }
            }

            return Custom;
        }
    }
}
