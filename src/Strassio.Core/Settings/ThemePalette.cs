namespace Strassio.Core.Settings
{
    /// <summary>
    /// Цвета интерфейса докера ("#RRGGBB"). Четыре палитры повторяют четыре цветовые схемы
    /// CorelDRAW («Параметры → Внешний вид»): самая светлая, средне-светлая, тёмная, чёрная — чтобы
    /// докер Strassio выглядел как родные докеры CorelDRAW.
    /// </summary>
    public sealed class ThemePalette
    {
        public static readonly ThemePalette LightestGrey = new ThemePalette(
            "LightestGrey", isDark: false,
            background: "#F4F4F4", foreground: "#000000", mutedForeground: "#5A5A5A",
            controlBackground: "#EAEAEA", controlBorder: "#B2B2B2", inputBackground: "#FFFFFF",
            hoverBackground: "#E0F0FF", selectedBackground: "#CEE3FF", accent: "#0078D7", accentForeground: "#FFFFFF");

        public static readonly ThemePalette MediumGrey = new ThemePalette(
            "MediumGrey", isDark: false,
            background: "#ECECEC", foreground: "#000000", mutedForeground: "#505050",
            controlBackground: "#E2E2E2", controlBorder: "#B2B2B2", inputBackground: "#FFFFFF",
            hoverBackground: "#E0F0FF", selectedBackground: "#C2D6F0", accent: "#0078D7", accentForeground: "#FFFFFF");

        public static readonly ThemePalette DarkGrey = new ThemePalette(
            "DarkGrey", isDark: true,
            background: "#383838", foreground: "#CCCCCC", mutedForeground: "#9A9A9A",
            controlBackground: "#323232", controlBorder: "#202020", inputBackground: "#2D2D2D",
            hoverBackground: "#003C78", selectedBackground: "#0064CA", accent: "#3399FF", accentForeground: "#FFFFFF");

        public static readonly ThemePalette Black = new ThemePalette(
            "Black", isDark: true,
            background: "#232323", foreground: "#CCCCCC", mutedForeground: "#8C8C8C",
            controlBackground: "#323232", controlBorder: "#5A5A5A", inputBackground: "#0F0F0F",
            hoverBackground: "#003C78", selectedBackground: "#00468C", accent: "#3399FF", accentForeground: "#FFFFFF");

        private ThemePalette(
            string name, bool isDark, string background, string foreground, string mutedForeground,
            string controlBackground, string controlBorder, string inputBackground,
            string hoverBackground, string selectedBackground, string accent, string accentForeground)
        {
            Name = name;
            IsDark = isDark;
            Background = background;
            Foreground = foreground;
            MutedForeground = mutedForeground;
            ControlBackground = controlBackground;
            ControlBorder = controlBorder;
            InputBackground = inputBackground;
            HoverBackground = hoverBackground;
            SelectedBackground = selectedBackground;
            Accent = accent;
            AccentForeground = accentForeground;
        }

        public string Name { get; }

        public bool IsDark { get; }

        public string Background { get; }

        public string Foreground { get; }

        /// <summary>Подсказки, подписи второго плана.</summary>
        public string MutedForeground { get; }

        public string ControlBackground { get; }

        public string ControlBorder { get; }

        /// <summary>Поля ввода и выпадающие списки.</summary>
        public string InputBackground { get; }

        public string HoverBackground { get; }

        public string SelectedBackground { get; }

        public string Accent { get; }

        public string AccentForeground { get; }

        /// <summary>
        /// Какую палитру показать. «Авто» — как схема CorelDRAW (значение настройки CorelDRAW
        /// WindowScheme/Colors, например "…_DarkGrey"); если CorelDRAW её не отдаёт (X7) —
        /// как тема Windows. «Светлая» и «Тёмная» — выбор пользователя в настройках Strassio.
        /// </summary>
        public static ThemePalette Resolve(ThemeMode mode, string? corelScheme, bool windowsIsDark)
        {
            switch (mode)
            {
                case ThemeMode.Light:
                    return LightestGrey;
                case ThemeMode.Dark:
                    return DarkGrey;
            }

            ThemePalette? fromCorel = FromCorelScheme(corelScheme);
            if (fromCorel != null)
            {
                return fromCorel;
            }

            return windowsIsDark ? DarkGrey : LightestGrey;
        }

        /// <summary>Имя схемы CorelDRAW → палитра; null — схема неизвестна.</summary>
        public static ThemePalette? FromCorelScheme(string? corelScheme)
        {
            if (string.IsNullOrWhiteSpace(corelScheme))
            {
                return null;
            }

            string name = corelScheme!.Trim();
            int underscore = name.LastIndexOf('_');
            if (underscore >= 0)
            {
                name = name.Substring(underscore + 1);
            }

            foreach (ThemePalette palette in new[] { LightestGrey, MediumGrey, DarkGrey, Black })
            {
                if (string.Equals(palette.Name, name, System.StringComparison.OrdinalIgnoreCase))
                {
                    return palette;
                }
            }

            return null;
        }
    }
}
