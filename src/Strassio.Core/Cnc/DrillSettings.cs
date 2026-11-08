using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace Strassio.Core.Cnc
{
    /// <summary>Где ноль станка (X0 Y0): угол доски или место на рисунке.</summary>
    public enum DrillOrigin
    {
        /// <summary>
        /// Угол (или центр) доски — как модель в ArtCAM: размер доски и где на ней ноль. Доска — это
        /// страница CorelDRAW (её левый нижний угол), рисунок стоит на доске там же, где на странице.
        /// </summary>
        Board,

        /// <summary>Левый нижний угол рисунка — по краю крайних отверстий (как модель в ArtCAM).</summary>
        DesignLowerLeft,

        /// <summary>Центр рисунка.</summary>
        DesignCenter,

        /// <summary>Ноль документа CorelDRAW (обычно левый нижний угол страницы).</summary>
        Document,
    }

    /// <summary>Где на доске ноль станка (home). Оси обычные: X вправо, Y вверх (от себя).</summary>
    public enum BoardCorner
    {
        LowerLeft,
        UpperLeft,
        LowerRight,
        UpperRight,
        Center,
    }

    /// <summary>Как раскладывать свёрла по файлам.</summary>
    public enum DrillFileMode
    {
        /// <summary>Отдельный файл на каждое сверло: сверло меняют руками и заново ставят ноль Z.</summary>
        FilePerDrill,

        /// <summary>Один файл, смена сверла командой T… M6 (станок со сменой инструмента).</summary>
        OneFileToolChange,
    }

    /// <summary>Старый формат: своё сверло для размера камня «ss10 → 3,0 мм» (теперь — в таблице камней).</summary>
    [DataContract]
    public sealed class DrillRule
    {
        [DataMember(Name = "size")]
        public string Size { get; set; } = string.Empty;

        [DataMember(Name = "drillMm")]
        public double DrillMm { get; set; }
    }

    /// <summary>
    /// Профиль станка для экспорта отверстий в G-code (docs/SPEC.md, раздел 11.1): глубина, подачи, ноль,
    /// доска, метки, повтор, формат файла. Свёрла живут в каталоге (<see cref="Stones.StoneTable.Drills"/>).
    /// Все длины — в мм, скорости — в мм/мин. Z0 — верх материала, глубина отсчитывается вниз. В файле
    /// перечисления хранятся словами, как в <see cref="Settings.PluginSettings"/>; поля, которых нет в
    /// старом файле, получают значения по умолчанию.
    /// </summary>
    [DataContract]
    public sealed class DrillSettings
    {
        public DrillSettings()
        {
            SetDefaults();
        }

        /// <summary>Название профиля станка, например «Мой станок» или «Станок в цеху».</summary>
        [DataMember(Name = "name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Старый формат (до каталога свёрл): свёрла пользователя, мм. Читается только для переноса в
        /// каталог (<see cref="DrillCatalog.ImportLegacy"/>), в расчёте не участвует.
        /// </summary>
        [DataMember(Name = "drills")]
        public List<double> DrillsMm { get; set; } = new List<double>();

        /// <summary>Старый формат: свои свёрла для размеров. Только для переноса в каталог.</summary>
        [DataMember(Name = "rules")]
        public List<DrillRule> Rules { get; set; } = new List<DrillRule>();

        /// <summary>
        /// Сверло меньше отверстия — дофрезеровать отверстие по кругу до нужного диаметра (например, ss6
        /// 2,4 мм сверлом 2,2). Выключено — отверстие будет по сверлу.
        /// </summary>
        [DataMember(Name = "millSmaller")]
        public bool MillSmaller { get; set; }

        /// <summary>Подача по кругу при дофрезеровке, мм/мин.</summary>
        [DataMember(Name = "millFeed")]
        public double MillFeed { get; set; }

        /// <summary>Метки совмещения: по отверстию у каждого угла рисунка, снаружи.</summary>
        [DataMember(Name = "marks")]
        public bool Marks { get; set; }

        /// <summary>Насколько метки отстоят от края рисунка (по X и по Y), мм.</summary>
        [DataMember(Name = "marksOffsetMm")]
        public double MarksOffsetMm { get; set; }

        /// <summary>Сколько копий рисунка на доске по X (вправо).</summary>
        [DataMember(Name = "repeatX")]
        public int RepeatX { get; set; }

        /// <summary>Сколько копий рисунка на доске по Y (вниз).</summary>
        [DataMember(Name = "repeatY")]
        public int RepeatY { get; set; }

        /// <summary>Промежуток между копиями (между метками, если они есть), мм.</summary>
        [DataMember(Name = "repeatGapMm")]
        public double RepeatGapMm { get; set; }

        /// <summary>Сколько материала должно оставаться между отверстиями, мм (проверка при сохранении).</summary>
        [DataMember(Name = "minBridgeMm")]
        public double MinBridgeMm { get; set; }

        /// <summary>В каком порядке сверлить отверстия одного сверла.</summary>
        [IgnoreDataMember]
        public DrillPathStyle PathStyle { get; set; }

        /// <summary>Готовый формат файла (<see cref="GCodeFormats"/>) или "custom".</summary>
        [DataMember(Name = "format")]
        public string Format { get; set; } = GCodeFormats.ArtCam;

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

        /// <summary>Где на доске ноль станка (при <see cref="DrillOrigin.Board"/>).</summary>
        [IgnoreDataMember]
        public BoardCorner Home { get; set; }

        /// <summary>Ширина доски (по X), мм; 0 — как страница CorelDRAW.</summary>
        [DataMember(Name = "boardWidthMm")]
        public double BoardWidthMm { get; set; }

        /// <summary>Высота доски (по Y), мм; 0 — как страница CorelDRAW.</summary>
        [DataMember(Name = "boardHeightMm")]
        public double BoardHeightMm { get; set; }

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

        /// <summary>Куда сохраняли в прошлый раз (пусто — папка документа).</summary>
        [DataMember(Name = "lastFolder")]
        public string LastFolder { get; set; } = string.Empty;

        [DataMember(Name = "origin")]
        private string OriginText
        {
            get => Origin == DrillOrigin.Board ? "board" : Origin == DrillOrigin.DesignCenter ? "center" : Origin == DrillOrigin.Document ? "document" : "lowerLeft";
            set => Origin = value == "board" ? DrillOrigin.Board : value == "center" ? DrillOrigin.DesignCenter : value == "document" ? DrillOrigin.Document : DrillOrigin.DesignLowerLeft;
        }

        [DataMember(Name = "home")]
        private string HomeText
        {
            get => Home switch
            {
                BoardCorner.UpperLeft => "upperLeft",
                BoardCorner.LowerRight => "lowerRight",
                BoardCorner.UpperRight => "upperRight",
                BoardCorner.Center => "center",
                _ => "lowerLeft",
            };
            set => Home = value switch
            {
                "upperLeft" => BoardCorner.UpperLeft,
                "lowerRight" => BoardCorner.LowerRight,
                "upperRight" => BoardCorner.UpperRight,
                "center" => BoardCorner.Center,
                _ => BoardCorner.LowerLeft,
            };
        }

        [DataMember(Name = "path")]
        private string PathText
        {
            get => PathStyle switch
            {
                DrillPathStyle.Rows => "rows",
                DrillPathStyle.Snake => "snake",
                DrillPathStyle.Columns => "columns",
                DrillPathStyle.Groups => "groups",
                DrillPathStyle.Spread => "spread",
                _ => "fast",
            };
            set => PathStyle = value switch
            {
                "rows" => DrillPathStyle.Rows,
                "snake" => DrillPathStyle.Snake,
                "columns" => DrillPathStyle.Columns,
                "groups" => DrillPathStyle.Groups,
                "spread" => DrillPathStyle.Spread,
                _ => DrillPathStyle.Fast,
            };
        }

        [DataMember(Name = "fileMode")]
        private string FileModeText
        {
            get => FileMode == DrillFileMode.OneFileToolChange ? "toolChange" : "perDrill";
            set => FileMode = value == "toolChange" ? DrillFileMode.OneFileToolChange : DrillFileMode.FilePerDrill;
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

            // Меньше 1 мм/мин в файле стало бы F0 — станок стоит или берёт свою подачу.
            if (!(PlungeFeed >= 1) || PlungeFeed > 20000)
            {
                problems.Add("drill.problem.feed");
            }

            // Слишком мелкий шаг — тысячи строк на отверстие: файл в гигабайты и нехватка памяти в CorelDRAW.
            if (PeckMm < 0 || double.IsNaN(PeckMm) || (PeckMm > 0 && (PeckMm < 0.05 || DepthMm / PeckMm > 200)))
            {
                problems.Add("drill.problem.peck");
            }

            if (BoardWidthMm < 0 || BoardHeightMm < 0 || BoardWidthMm > 10000 || BoardHeightMm > 10000 || double.IsNaN(BoardWidthMm) || double.IsNaN(BoardHeightMm))
            {
                problems.Add("drill.problem.board");
            }

            if (SpindleRpm < 0 || SpindleRpm > 100000)
            {
                problems.Add("drill.problem.rpm");
            }

            if (!(MillFeed >= 1) || MillFeed > 20000)
            {
                problems.Add("drill.problem.millFeed");
            }

            if (double.IsNaN(MarksOffsetMm) || MarksOffsetMm < 0 || MarksOffsetMm > 200)
            {
                problems.Add("drill.problem.marks");
            }

            if (RepeatX < 1 || RepeatY < 1 || RepeatX > MaxRepeat || RepeatY > MaxRepeat || double.IsNaN(RepeatGapMm) || RepeatGapMm < 0 || RepeatGapMm > 1000)
            {
                problems.Add("drill.problem.repeat");
            }

            if (double.IsNaN(MinBridgeMm) || MinBridgeMm < 0 || MinBridgeMm > 10)
            {
                problems.Add("drill.problem.bridge");
            }

            return problems;
        }

        /// <summary>Больше копий рисунка по одной оси не бывает.</summary>
        public const int MaxRepeat = 50;

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
            Origin = DrillOrigin.Board;
            Home = BoardCorner.LowerLeft;
            BoardWidthMm = 0;
            BoardHeightMm = 0;
            FileMode = DrillFileMode.FilePerDrill;
            CannedCycle = false;
            LineNumbers = false;
            Comments = true;
            Percent = false;
            SplitByColor = false;
            RapidFeed = 5000;
            MaxMismatchMm = 0.2;
            Extension = ".tap";
            LastFolder = string.Empty;
            Name = string.Empty;
            MillSmaller = true;
            MillFeed = 300;
            Marks = false;
            MarksOffsetMm = 10;
            RepeatX = 1;
            RepeatY = 1;
            RepeatGapMm = 10;
            MinBridgeMm = 0.3;
            Format = GCodeFormats.ArtCam;
            PathStyle = DrillPathStyle.Fast;
        }
    }
}
