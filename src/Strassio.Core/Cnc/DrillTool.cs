using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using Strassio.Core.Stones;

namespace Strassio.Core.Cnc
{
    /// <summary>
    /// Сверло из каталога пользователя (docs/SPEC.md, раздел 11.1, «Каталог страз и свёрл»): диаметр,
    /// номер в станке, название и свои подача и обороты. Нули — «как в профиле станка».
    /// </summary>
    [DataContract]
    public sealed class DrillTool
    {
        /// <summary>Диаметр сверла (фрезы), мм.</summary>
        [DataMember(Name = "diameterMm")]
        public double DiameterMm { get; set; }

        /// <summary>Номер инструмента T в станке; 0 — место в списке (с 1).</summary>
        [DataMember(Name = "tool")]
        public int Tool { get; set; }

        /// <summary>Своё название или пометка, например «фреза 2,2 твердосплав».</summary>
        [DataMember(Name = "name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>Своя подача опускания, мм/мин; 0 — как в профиле станка.</summary>
        [DataMember(Name = "feed")]
        public double Feed { get; set; }

        /// <summary>Свои обороты шпинделя; 0 — как в профиле станка.</summary>
        [DataMember(Name = "rpm")]
        public int Rpm { get; set; }

        public DrillTool Clone() => new DrillTool { DiameterMm = DiameterMm, Tool = Tool, Name = Name ?? string.Empty, Feed = Feed, Rpm = Rpm };
    }

    /// <summary>
    /// Свёрла для одного расчёта: каталог свёрл пользователя и сверло, выбранное у размера страз в таблице
    /// камней. Пустой набор — сверло под каждый размер равно диаметру отверстия.
    /// </summary>
    public sealed class DrillRack
    {
        /// <summary>Диаметры свёрл сравниваются с такой точностью: 2,2 и 2,2004 — одно сверло.</summary>
        public const double SameDrillMm = 0.0005;

        private readonly Dictionary<string, double> sizeDrills;

        public DrillRack(IEnumerable<DrillTool>? tools = null, IEnumerable<KeyValuePair<string, double>>? sizeDrills = null)
        {
            Tools = (tools ?? Array.Empty<DrillTool>()).Where(t => t != null && t.DiameterMm > 0 && !double.IsNaN(t.DiameterMm)).ToList();
            this.sizeDrills = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, double> pair in sizeDrills ?? Array.Empty<KeyValuePair<string, double>>())
            {
                string key = (pair.Key ?? string.Empty).Trim();
                if (key.Length > 0 && pair.Value > 0 && !this.sizeDrills.ContainsKey(key))
                {
                    this.sizeDrills[key] = pair.Value;
                }
            }
        }

        public static DrillRack Empty { get; } = new DrillRack();

        /// <summary>Свёрла пользователя по порядку (только с диаметром больше нуля).</summary>
        public IReadOnlyList<DrillTool> Tools { get; }

        /// <summary>Только диаметры — для простых наборов в тестах и превью.</summary>
        public static DrillRack Of(params double[] drills) => new DrillRack(drills.Select(d => new DrillTool { DiameterMm = d }));

        /// <summary>Каталог: свёрла таблицы камней и сверло у каждого размера набора <paramref name="setName"/>.</summary>
        public static DrillRack Of(StoneTable? table, string? setName)
        {
            if (table == null)
            {
                return Empty;
            }

            StoneSet? set = StoneTableRules.FindSet(table, setName);
            IEnumerable<KeyValuePair<string, double>> sizes = set == null
                ? Array.Empty<KeyValuePair<string, double>>()
                : set.Sizes.Where(s => s != null && s.DrillMm > 0).Select(s => new KeyValuePair<string, double>(s.Name ?? string.Empty, s.DrillMm));
            return new DrillRack(table.Drills, sizes);
        }

        /// <summary>Сверло, выбранное для размера в таблице камней, или null («авто»).</summary>
        public double? DrillForSize(string size) =>
            sizeDrills.TryGetValue((size ?? string.Empty).Trim(), out double d) ? d : (double?)null;

        /// <summary>Сверло каталога с этим диаметром или null.</summary>
        public DrillTool? Find(double drillMm) => Tools.FirstOrDefault(t => Math.Abs(t.DiameterMm - drillMm) <= SameDrillMm);

        /// <summary>Номер T: свой номер сверла, иначе место в списке (с 1); сверла нет в каталоге — 0.</summary>
        public int ToolNumber(double drillMm)
        {
            for (int i = 0; i < Tools.Count; i++)
            {
                if (Math.Abs(Tools[i].DiameterMm - drillMm) <= SameDrillMm)
                {
                    return Tools[i].Tool > 0 ? Tools[i].Tool : i + 1;
                }
            }

            return 0;
        }
    }

    /// <summary>Правила каталога свёрл: проверка, порядок, перенос старых списков из окна сверления.</summary>
    public static class DrillCatalog
    {
        /// <summary>Самое большое сверло, которое имеет смысл для трафарета, мм.</summary>
        public const double MaxDrillMm = 50;

        /// <summary>
        /// Ошибки каталога свёрл — ключи языка "catalog.error.*" и значения для них. Пусто — можно сохранять.
        /// Свёрла одного диаметра не повторяются (по диаметру выбирается сверло для размера), номера T тоже.
        /// </summary>
        public static IReadOnlyList<StoneProblem> Validate(IReadOnlyList<DrillTool>? drills)
        {
            var problems = new List<StoneProblem>();
            if (drills == null)
            {
                return problems;
            }

            var diameters = new List<double>();
            var numbers = new HashSet<int>();
            for (int i = 0; i < drills.Count; i++)
            {
                DrillTool t = drills[i];
                string name = t.DiameterMm > 0 ? t.DiameterMm.ToString("0.###", CultureInfo.CurrentCulture) : (i + 1).ToString(CultureInfo.CurrentCulture);
                if (!(t.DiameterMm > 0) || t.DiameterMm > MaxDrillMm)
                {
                    problems.Add(new StoneProblem("catalog.error.diameter", name, MaxDrillMm));
                    continue;
                }

                if (diameters.Any(d => Math.Abs(d - t.DiameterMm) <= DrillRack.SameDrillMm))
                {
                    problems.Add(new StoneProblem("catalog.error.duplicate", name));
                }

                diameters.Add(t.DiameterMm);
                int number = t.Tool > 0 ? t.Tool : i + 1;
                if (t.Tool < 0 || t.Tool > 999)
                {
                    problems.Add(new StoneProblem("catalog.error.tool", name));
                }
                else if (!numbers.Add(number))
                {
                    problems.Add(new StoneProblem("catalog.error.toolDuplicate", name, number));
                }

                if (t.Feed < 0 || t.Feed > 20000 || double.IsNaN(t.Feed))
                {
                    problems.Add(new StoneProblem("catalog.error.feed", name));
                }

                if (t.Rpm < 0 || t.Rpm > 100000)
                {
                    problems.Add(new StoneProblem("catalog.error.rpm", name));
                }
            }

            return problems;
        }

        /// <summary>Свёрла по возрастанию диаметра (свои номера T не меняются).</summary>
        public static void SortByDiameter(List<DrillTool> drills) =>
            drills.Sort((a, b) => a.DiameterMm.CompareTo(b.DiameterMm));

        /// <summary>
        /// Какие размеры страз набора сверлятся этим сверлом: выбранные у размера и «авто», для которых
        /// оно ближайшее (с теми же правилами, что при сверлении).
        /// </summary>
        public static IReadOnlyList<string> SizesOf(DrillTool drill, StoneSet? set, IReadOnlyList<DrillTool> drills, bool millSmaller, double maxMismatchMm)
        {
            if (set == null)
            {
                return Array.Empty<string>();
            }

            var rack = new DrillRack(drills, set.Sizes.Where(s => s.DrillMm > 0).Select(s => new KeyValuePair<string, double>(s.Name, s.DrillMm)));
            var settings = new DrillSettings { MillSmaller = millSmaller, MaxMismatchMm = maxMismatchMm };
            return set.Sizes
                .Where(s => s.DiameterMm > 0 && Math.Abs(DrillPlanner.ChooseDrill(s.Name, s.DiameterMm, settings, rack) - drill.DiameterMm) <= DrillRack.SameDrillMm)
                .Select(s => s.Name)
                .ToList();
        }

        /// <summary>
        /// Переносит свёрла и «свои свёрла для размеров» из старого окна сверления (до каталога) в
        /// каталог, если в каталоге свёрл ещё нет. true — каталог изменился, его нужно сохранить.
        /// </summary>
        public static bool ImportLegacy(StoneTable table, DrillSettings legacy, string? setName)
        {
            List<double> drills = (legacy.DrillsMm ?? new List<double>()).Where(d => d > 0 && d <= MaxDrillMm).ToList();
            List<DrillRule> rules = (legacy.Rules ?? new List<DrillRule>()).Where(r => r != null && r.DrillMm > 0 && r.DrillMm <= MaxDrillMm).ToList();
            if (table.Drills.Count > 0 || (drills.Count == 0 && rules.Count == 0))
            {
                return false;
            }

            foreach (double d in drills)
            {
                if (!table.Drills.Any(t => Math.Abs(t.DiameterMm - d) <= DrillRack.SameDrillMm))
                {
                    table.Drills.Add(new DrillTool { DiameterMm = d });
                }
            }

            StoneSet? set = StoneTableRules.FindSet(table, setName);
            if (set != null)
            {
                foreach (DrillRule rule in rules)
                {
                    StoneSize? size = set.Sizes.FirstOrDefault(s => string.Equals((s.Name ?? string.Empty).Trim(), (rule.Size ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
                    if (size != null && !(size.DrillMm > 0))
                    {
                        size.DrillMm = rule.DrillMm;
                    }

                    if (!table.Drills.Any(t => Math.Abs(t.DiameterMm - rule.DrillMm) <= DrillRack.SameDrillMm))
                    {
                        table.Drills.Add(new DrillTool { DiameterMm = rule.DrillMm });
                    }
                }
            }

            legacy.DrillsMm = new List<double>();
            legacy.Rules = new List<DrillRule>();
            return true;
        }
    }
}
