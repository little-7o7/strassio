using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Strassio.Core.Settings;

namespace Strassio.UiShots
{
    /// <summary>
    /// Снимки окон аддона без CorelDRAW: dotnet run --project tools/Strassio.UiShots → out/ui/*.png.
    ///
    /// Окна создаются без объекта CorelDRAW (null) — так же, как их видит дизайнер WPF. Кнопки,
    /// которым нужен документ, здесь не нажимаются. Настройки берутся из временной папки
    /// (STRASSIO_SETTINGS_DIR), поэтому настоящие настройки пользователя не трогаются.
    /// Конструкторы окон принимают тип CorelDRAW, встроенный в Strassio.Corel.dll, поэтому они
    /// вызываются через отражение.
    /// </summary>
    internal static class Program
    {
        private static readonly Assembly Addon = typeof(Strassio.Corel.Docker).Assembly;

        [STAThread]
        private static int Main()
        {
            string root = FindRepoRoot();
            string outDir = Path.Combine(root, "out", "ui");
            Directory.CreateDirectory(outDir);

            string settingsDir = Path.Combine(Path.GetTempPath(), "strassio-uishots-" + Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable(SettingsStore.DirectoryVariable, settingsDir);

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            try
            {
                foreach (bool dark in new[] { false, true })
                {
                    string theme = dark ? "-dark" : string.Empty;
                    SetTheme(dark);

                    SnapDocker(outDir, "docker-line" + theme, DockerLayout.Vertical, fillTab: false, 300, 900);
                    SnapDocker(outDir, "docker-fill" + theme, DockerLayout.Vertical, fillTab: true, 300, 900);
                    SnapDocker(outDir, "docker-horizontal" + theme, DockerLayout.Horizontal, fillTab: true, 960, 420);
                    SnapDocker(outDir, "docker-edit" + theme, DockerLayout.Vertical, fillTab: false, 300, 900, tab: 2);
                    SnapDocker(outDir, "docker-color" + theme, DockerLayout.Vertical, fillTab: false, 300, 900, tab: 3);
                    SnapWindow(outDir, "stones" + theme, Make("Strassio.Corel.StonesWindow", null, "ss6"));
                    SnapWindow(outDir, "settings" + theme, Make("Strassio.Corel.SettingsWindow", new object?[] { null }));
                }

                Console.WriteLine("Снимки: " + outDir);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
            finally
            {
                app.Shutdown();
                try
                {
                    Directory.Delete(settingsDir, recursive: true);
                }
                catch (Exception)
                {
                    // Временная папка — не страшно, если осталась.
                }
            }
        }

        private static PluginSettings Settings
        {
            get
            {
                Type context = Addon.GetType("Strassio.Corel.PluginContext");
                object instance = context.GetProperty("Instance").GetValue(null);
                return (PluginSettings)context.GetProperty("Settings").GetValue(instance);
            }
        }

        private static void SetTheme(bool dark)
        {
            Settings.Theme = dark ? ThemeMode.Dark : ThemeMode.Light;
            Addon.GetType("Strassio.Corel.Themes.ThemeManager").GetMethod("Refresh").Invoke(null, null);
        }

        private static Window Make(string typeName, params object?[] args) =>
            (Window)Activator.CreateInstance(Addon.GetType(typeName), args);

        private static void SnapDocker(string outDir, string name, DockerLayout layout, bool fillTab, int width, int height, int tab = -1)
        {
            Settings.Layout = layout;
            Settings.LastMethod = string.Empty; // иначе докер сам откроет метод с прошлого снимка
            var docker = new Strassio.Corel.Docker(null);
            if (fillTab)
            {
                ((TabControl)docker.FindName("MethodTabs")).SelectedIndex = 1;
                ((ComboBox)docker.FindName("MethodCombo")).SelectedIndex = 3;
            }

            if (tab >= 0)
            {
                ((TabControl)docker.FindName("MethodTabs")).SelectedIndex = tab;
            }

            var window = new Window
            {
                Content = docker, Width = width, Height = height, Left = -5000, Top = 0,
                ShowInTaskbar = false, WindowStyle = WindowStyle.None,
            };
            window.Show();
            Save(outDir, name, docker, docker.ActualWidth, docker.ActualHeight, null);
            window.Close();
            Settings.Layout = DockerLayout.Vertical;
        }

        private static void SnapWindow(string outDir, string name, Window window)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -5000;
            window.Top = 0;
            window.Show();
            var content = (FrameworkElement)window.Content;
            Save(outDir, name, content, content.ActualWidth + content.Margin.Left + content.Margin.Right,
                content.ActualHeight + content.Margin.Top + content.Margin.Bottom, window.Background);
            window.Close();
        }

        /// <summary>Дожидается отрисовки и сохраняет элемент в PNG (с фоном окна, если он задан).</summary>
        private static void Save(string outDir, string name, FrameworkElement element, double width, double height, Brush? background)
        {
            element.UpdateLayout();
            element.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                if (background != null)
                {
                    dc.DrawRectangle(background, null, new Rect(0, 0, width, height));
                }

                dc.DrawRectangle(
                    new VisualBrush(element), null,
                    new Rect(element.Margin.Left, element.Margin.Top, element.ActualWidth, element.ActualHeight));
            }

            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using FileStream file = File.Create(Path.Combine(outDir, name + ".png"));
            encoder.Save(file);
        }

        private static string FindRepoRoot()
        {
            string? current = AppContext.BaseDirectory;
            while (current != null && !File.Exists(Path.Combine(current, "Strassio.sln")))
            {
                current = Path.GetDirectoryName(current);
            }

            return current ?? throw new InvalidOperationException("Не найден Strassio.sln");
        }
    }
}
