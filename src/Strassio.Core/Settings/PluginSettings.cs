using System.Runtime.Serialization;
using Strassio.Core.Methods;

namespace Strassio.Core.Settings
{
    public enum ThemeMode
    {
        Auto,
        Light,
        Dark,
    }

    public enum DockerLayout
    {
        Vertical,
        Horizontal,
    }

    /// <summary>Обводка круга-стразы в документе (docs/SPEC.md, раздел 3.3).</summary>
    public enum OutlineStyle
    {
        None,
        Hairline,
        Width,
    }

    /// <summary>
    /// Настройки плагина (docs/SPEC.md, раздел 12), файл %APPDATA%\Strassio\settings.json.
    /// В файле перечисления хранятся словами ("mm", "dark"…), а не числами — чтобы файл можно было
    /// прочитать глазами и чтобы новые варианты не сдвигали старые. Поля, которых нет в старом
    /// файле, получают значения по умолчанию (<see cref="SetDefaults"/> вызывается и при чтении).
    /// </summary>
    [DataContract]
    public sealed class PluginSettings
    {
        public PluginSettings()
        {
            SetDefaults();
        }

        [DataMember(Name = "language")]
        public string Language { get; set; } = "ru";

        [IgnoreDataMember]
        public LengthUnit Units { get; set; }

        [IgnoreDataMember]
        public ThemeMode Theme { get; set; }

        [IgnoreDataMember]
        public DockerLayout Layout { get; set; }

        /// <summary>Знаков после запятой в полях ввода.</summary>
        [DataMember(Name = "decimals")]
        public int Decimals { get; set; }

        /// <summary>Камень по умолчанию: название размера (например, "ss6") и цвета.</summary>
        [DataMember(Name = "defaultSize")]
        public string DefaultSize { get; set; } = string.Empty;

        [DataMember(Name = "defaultColor")]
        public string DefaultColor { get; set; } = string.Empty;

        [IgnoreDataMember]
        public OutlineStyle Outline { get; set; }

        /// <summary>Толщина обводки для <see cref="OutlineStyle.Width"/>, мм.</summary>
        [DataMember(Name = "outlineWidthMm")]
        public double OutlineWidthMm { get; set; }

        [DataMember(Name = "checkUpdates")]
        public bool CheckUpdates { get; set; }

        /// <summary>Последний выбранный метод ("l1", "f3"…) — выбирается сам при следующем запуске.</summary>
        [DataMember(Name = "lastMethod")]
        public string LastMethod { get; set; } = string.Empty;

        /// <summary>Параметры методов из докера (зазор, ряды…).</summary>
        [DataMember(Name = "method")]
        public MethodParameters Method { get; set; } = new MethodParameters();

        [DataMember(Name = "units")]
        private string UnitsText
        {
            get => Units == LengthUnit.Inch ? "in" : "mm";
            set => Units = value == "in" ? LengthUnit.Inch : LengthUnit.Millimeter;
        }

        [DataMember(Name = "theme")]
        private string ThemeText
        {
            get => Theme == ThemeMode.Light ? "light" : Theme == ThemeMode.Dark ? "dark" : "auto";
            set => Theme = value == "light" ? ThemeMode.Light : value == "dark" ? ThemeMode.Dark : ThemeMode.Auto;
        }

        [DataMember(Name = "layout")]
        private string LayoutText
        {
            get => Layout == DockerLayout.Horizontal ? "horizontal" : "vertical";
            set => Layout = value == "horizontal" ? DockerLayout.Horizontal : DockerLayout.Vertical;
        }

        [DataMember(Name = "outline")]
        private string OutlineText
        {
            get => Outline == OutlineStyle.Hairline ? "hairline" : Outline == OutlineStyle.Width ? "width" : "none";
            set => Outline = value == "hairline" ? OutlineStyle.Hairline : value == "width" ? OutlineStyle.Width : OutlineStyle.None;
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) => SetDefaults();

        private void SetDefaults()
        {
            Language = "ru";
            Units = LengthUnit.Millimeter;
            Theme = ThemeMode.Auto;
            Layout = DockerLayout.Vertical;
            Decimals = 2;
            DefaultSize = string.Empty;
            DefaultColor = string.Empty;
            Outline = OutlineStyle.None;
            OutlineWidthMm = 0.1;
            CheckUpdates = true;
            LastMethod = string.Empty;
            Method = new MethodParameters();
        }
    }
}
