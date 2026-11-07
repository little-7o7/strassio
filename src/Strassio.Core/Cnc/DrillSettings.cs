using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace Strassio.Core.Cnc
{
    /// <summary>Где ноль станка (X0 Y0) относительно рисунка.</summary>
    public enum DrillOrigin
    {
        /// <summary>Левый нижний угол рисунка — по краю крайних отверстий (как модель в ArtCAM).</summary>
        DesignLowerLeft,

        /// <summary>Центр рисунка.</summary>
        DesignCenter,

        /// <summary>Ноль документа CorelDRAW (обычно левый нижний угол страницы).</summary>
        Document,
    }

    /// <summary>Как раскладывать свёрла по файлам.</summary>
    public enum DrillFileMode
    {
        /// <summary>Отдельный файл на каждое сверло: сверло меняют руками и заново ставят ноль Z.</summary>
        FilePerDrill,

        /// <summary>Один файл, смена сверла командой T… M6 (станок со сменой инструмента).</summary>
        OneFileToolChange,
    }

    /// <summary>Своё сверло для размера камня: «ss10 → 3,0 мм» (иначе берётся ближайшее из списка).</summary>
    [DataContract]
    public sealed class DrillRule
    {
        [DataMember(Name = "size")]
        public string Size { get; set; } = string.Empty;

        [DataMember(Name = "drillMm")]
        public double DrillMm { get; set; }
    }

    /// <summary>
    /// Настройки станка и свёрл для экспорта отверстий в G-code (docs/SPEC.md, раздел 11.1). Все длины —
    /// в мм, скорости — в мм/мин. Z0 — верх материала, глубина отсчитывается вниз. В файле перечисления
    /// хранятся словами, как в <see cref="Settings.PluginSettings"/>; поля, которых нет в старом файле,
    /// получают значения по умолчанию.
    /// </summary>
    [DataContract]
    public sealed class DrillSettings
    {
        public DrillSettings()
        {
            SetDefaults();
        }

        /// <summary>
        /// Свёрла, которые есть у пользователя, в мм — по порядку гнёзд: номер инструмента T = место в
        /// списке (с 1). Пустой список — сверло под каждый размер равно диаметру отверстия.
        /// </summary>
        [DataMember(Name = "drills")]
        public List<double> DrillsMm { get; set; } = new List<double>();

        /// <summary>Свои свёрла для размеров камней (важнее ближайшего из списка).</summary>
        [DataMember(Name = "rules")]
        public List<DrillRule> Rules { get; set; } = new List<DrillRule>();

        /// <summary>Глубина сверления ниже верха материала (положительное число).</summary>
        [DataMember(Name = "depthMm")]
        public double DepthMm { get; set; }

        /// <summary>Высота над материалом, на которую сверло поднимается между отверстиями.</summary>
        [DataMember(Name = "retractMm")]
        public double RetractMm { get; set; }

        /// <summary>Безопасная высота в начале, в конце и при смене сверла (выше прижимов).</summary>
        [DataMember(Name = "clearanceMm")]
        public double ClearanceMm { get; set; }

        /// <summary>Скорость опускания сверла (подача по Z).</summary>
        [DataMember(Name = "plungeFeed")]
        public double PlungeFeed { get; set; }

        /// <summary>Шаг сверления с выводом сверла (0 — за один проход).</summary>
        [DataMember(Name = "peckMm")]
        public double PeckMm { get; set; }

        /// <summary>Обороты шпинделя; 0 — шпиндель включают руками, команд M3/M5 в файле нет.</summary>
        [DataMember(Name = "spindleRpm")]
        public int SpindleRpm { get; set; }

        [IgnoreDataMember]
        public DrillOrigin Origin { get; set; }

        [IgnoreDataMember]
        public DrillFileMode FileMode { get; set; }

        /// <summary>Циклы G81/G83 вместо простых перемещений: файл короче, но понимает не каждый станок.</summary>
        [DataMember(Name = "cannedCycle")]
        public bool CannedCycle { get; set; }

        /// <summary>Номера строк N10, N20… (нужны некоторым старым станкам).</summary>
        [DataMember(Name = "lineNumbers")]
        public bool LineNumbers { get; set; }

        /// <summary>Пояснения в скобках (латиницей): какое сверло, сколько отверстий, где ноль.</summary>
        [DataMember(Name = "comments")]
        public bool Comments { get; set; }

        /// <summary>Строка «%» в начале и в конце файла.</summary>
        [DataMember(Name = "percent")]
        public bool Percent { get; set; }

        /// <summary>Отдельный шаблон на каждый цвет камня (у многоцветного дизайна свой трафарет на цвет).</summary>
        [DataMember(Name = "splitByColor")]
        public bool SplitByColor { get; set; }

        /// <summary>Скорость холостого хода — только для оценки времени работы.</summary>
        [DataMember(Name = "rapidFeed")]
        public double RapidFeed { get; set; }

        /// <summary>Если сверло отличается от отверстия больше этого — предупредить.</summary>
        [DataMember(Name = "maxMismatchMm")]
        public double MaxMismatchMm { get; set; }

        /// <summary>Расширение файлов (".tap", ".nc"…).</summary>
        [DataMember(Name = "extension")]
        public string Extension { get; set; } = ".tap";

        [DataMember(Name = "origin")]
        private string OriginText
        {
            get => Origin == DrillOrigin.DesignCenter ? "center" : Origin == DrillOrigin.Document ? "document" : "lowerLeft";
            set => Origin = value == "center" ? DrillOrigin.DesignCenter : value == "document" ? DrillOrigin.Document : DrillOrigin.DesignLowerLeft;
        }

        [DataMember(Name = "fileMode")]
        private string FileModeText
        {
            get => FileMode == DrillFileMode.OneFileToolChange ? "toolChange" : "perDrill";
            set => FileMode = value == "toolChange" ? DrillFileMode.OneFileToolChange : DrillFileMode.FilePerDrill;
        }

        /// <summary>Своё сверло для размера или null.</summary>
        public double? RuleFor(string size)
        {
            DrillRule? rule = Rules?.FirstOrDefault(r => r != null && r.DrillMm > 0 && string.Equals(r.Size, size, StringComparison.OrdinalIgnoreCase));
            return rule?.DrillMm;
        }

        /// <summary>
        /// Что не так с настройками — ключи языка "drill.problem.*" (пусто — всё в порядке). С такими
        /// настройками файл не пишется: сверло ушло бы в стол или волоклось по материалу.
        /// </summary>
        public IReadOnlyList<string> Problems()
        {
            var problems = new List<string>();
            if (!(DepthMm > 0) || DepthMm > 50)
            {
                problems.Add("drill.problem.depth");
            }

            if (!(RetractMm > 0) || RetractMm > 100)
            {
                problems.Add("drill.problem.retract");
            }

            if (!(ClearanceMm >= RetractMm) || ClearanceMm > 200)
            {
                problems.Add("drill.problem.clearance");
            }

            if (!(PlungeFeed > 0) || PlungeFeed > 20000)
            {
                problems.Add("drill.problem.feed");
            }

            if (PeckMm < 0 || double.IsNaN(PeckMm))
            {
                problems.Add("drill.problem.peck");
            }

            if (SpindleRpm < 0 || SpindleRpm > 100000)
            {
                problems.Add("drill.problem.rpm");
            }

            if ((DrillsMm ?? new List<double>()).Any(d => !(d > 0) || d > 50) || (Rules ?? new List<DrillRule>()).Any(r => r != null && (r.DrillMm < 0 || r.DrillMm > 50)))
            {
                problems.Add("drill.problem.drills");
            }

            return problems;
        }

        public DrillSettings Clone()
        {
            DrillSettings copy = (DrillSettings)MemberwiseClone();
            copy.DrillsMm = new List<double>(DrillsMm ?? new List<double>());
            copy.Rules = (Rules ?? new List<DrillRule>()).Where(r => r != null).Select(r => new DrillRule { Size = r.Size, DrillMm = r.DrillMm }).ToList();
            return copy;
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) => SetDefaults();

        /// <summary>
        /// Осторожные значения для мягкого материала шаблона (резина, флок, ПВХ ~1 мм). Автор поправит
        /// их под свой станок в окне «Сверление».
        /// </summary>
        private void SetDefaults()
        {
            DrillsMm = new List<double>();
            Rules = new List<DrillRule>();
            DepthMm = 1.5;
            RetractMm = 1.0;
            ClearanceMm = 10;
            PlungeFeed = 500;
            PeckMm = 0;
            SpindleRpm = 18000;
            Origin = DrillOrigin.DesignLowerLeft;
            FileMode = DrillFileMode.FilePerDrill;
            CannedCycle = false;
            LineNumbers = false;
            Comments = true;
            Percent = false;
            SplitByColor = false;
            RapidFeed = 5000;
            MaxMismatchMm = 0.2;
            Extension = ".tap";
        }
    }
}
