#nullable enable
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Strassio.Core.Settings;
using CorelApplication = Corel.Interop.VGCore.Application;

namespace Strassio.Corel.Themes
{
    /// <summary>
    /// Красит докер и окна Strassio в цвета текущей схемы CorelDRAW и перекрашивает их сразу,
    /// как только пользователь сменит схему в CorelDRAW. Способ (настройка WindowScheme/Colors и
    /// событие OnColorSchemeChanged) сверен с рабочими аддонами bonus630 (DockerTemplateX7) — см.
    /// docs/SPEC.md, раздел 18. В X7 такой настройки нет — там берётся тема Windows.
    /// </summary>
    internal static class ThemeManager
    {
        private static readonly List<WeakReference<FrameworkElement>> Targets = new List<WeakReference<FrameworkElement>>();
        private static CorelApplication? app;
        private static Dispatcher? dispatcher;

        public static ThemePalette Current { get; private set; } = ThemePalette.LightestGrey;

        /// <summary>Подключает элемент (докер, окно настроек): красит сейчас и при каждой смене темы.</summary>
        public static void Attach(FrameworkElement element, CorelApplication? corelApp)
        {
            if (app == null && corelApp != null)
            {
                app = corelApp;
                dispatcher = element.Dispatcher;
                try
                {
                    app.OnApplicationEvent += OnApplicationEvent;
                }
                catch (Exception)
                {
                    // Старая версия без события — тема обновится при следующем открытии докера.
                }
            }

            Targets.Add(new WeakReference<FrameworkElement>(element));
            Refresh();
        }

        public static void Detach(FrameworkElement element)
        {
            Targets.RemoveAll(w => !w.TryGetTarget(out FrameworkElement? e) || ReferenceEquals(e, element));
        }

        /// <summary>Заново определяет тему (после смены схемы CorelDRAW или настройки темы Strassio) и красит всё.</summary>
        public static void Refresh()
        {
            Current = ThemePalette.Resolve(PluginContext.Instance.Settings.Theme, ReadCorelScheme(), WindowsIsDark());

            Targets.RemoveAll(w => !w.TryGetTarget(out _));
            foreach (WeakReference<FrameworkElement> weak in Targets)
            {
                if (weak.TryGetTarget(out FrameworkElement? element))
                {
                    Apply(element.Resources, Current);
                }
            }
        }

        private static void OnApplicationEvent(string eventName, ref object[] parameters)
        {
            if (eventName == "OnColorSchemeChanged" || eventName == "WorkspaceChanged")
            {
                // Событие приходит из COM — перекрашиваем в потоке интерфейса.
                dispatcher?.BeginInvoke(new Action(Refresh));
            }
        }

        private static void Apply(ResourceDictionary resources, ThemePalette palette)
        {
            resources["Strassio.Background"] = Brush(palette.Background);
            resources["Strassio.Foreground"] = Brush(palette.Foreground);
            resources["Strassio.MutedForeground"] = Brush(palette.MutedForeground);
            resources["Strassio.ControlBackground"] = Brush(palette.ControlBackground);
            resources["Strassio.ControlBorder"] = Brush(palette.ControlBorder);
            resources["Strassio.InputBackground"] = Brush(palette.InputBackground);
            resources["Strassio.HoverBackground"] = Brush(palette.HoverBackground);
            resources["Strassio.SelectedBackground"] = Brush(palette.SelectedBackground);
            resources["Strassio.Accent"] = Brush(palette.Accent);
            resources["Strassio.AccentForeground"] = Brush(palette.AccentForeground);
        }

        private static SolidColorBrush Brush(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        private static string? ReadCorelScheme()
        {
            try
            {
                object? value = app == null ? null : (object)app.GetApplicationPreferenceValue("WindowScheme", "Colors");
                return value?.ToString();
            }
            catch (Exception)
            {
                return null; // X7: настройки нет
            }
        }

        private static bool WindowsIsDark()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
