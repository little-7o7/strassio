using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Strassio.Core.Cnc;
using Strassio.Core.Geometry;
using Strassio.Core.Settings;
using Strassio.Core.Stones;

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
                // Код компьютера (WMI) — заранее, чтобы окно «Лицензия» снялось уже готовым, а не «Связь с сервером…».
                object context = Addon.GetType("Strassio.Corel.PluginContext").GetProperty("Instance")!.GetValue(null)!;
                var license = (Strassio.Licensing.LicenseManager)context.GetType().GetProperty("License")!.GetValue(context)!;
                _ = license.Computer;

                foreach (bool dark in new[] { false, true })
                {
                    string theme = dark ? "-dark" : string.Empty;
                    SetTheme(dark);

                    // Каждая вкладка, как её видят при первом открытии (группы — по умолчанию), и она же со всеми
                    // открытыми группами — чтобы было видно всё, что в ней есть.
                    for (int i = 0; i < DockerTabs.Length; i++)
                    {
                        string tab = DockerTabs[i];
                        string shot = "docker-" + (i + 1) + "-" + tab.Replace("Tab", string.Empty).ToLowerInvariant();
                        SnapDocker(outDir, shot + theme, DockerLayout.Vertical, 300, 900, tab);
                        SnapDocker(outDir, shot + "-open" + theme, DockerLayout.Vertical, 300, 900, tab, allOpen: true);
                    }

                    SnapDocker(outDir, "docker-horizontal" + theme, DockerLayout.Horizontal, 960, 420, "FillTab");
                    SnapWindow(outDir, "stones" + theme, Make("Strassio.Corel.StonesWindow", null, "ss6", false, SampleCatalog(), null));
                    SnapWindow(outDir, "catalog-drills" + theme, Make("Strassio.Corel.StonesWindow", null, "ss6", true, SampleCatalog(), null));
                    SnapWindow(outDir, "settings" + theme, Make("Strassio.Corel.SettingsWindow", new object?[] { null }));
                    SnapWindow(outDir, "license" + theme, Make("Strassio.Corel.LicenseWindow", new object?[] { null }));
                    SnapWindow(outDir, "drill" + theme, Make("Strassio.Corel.DrillWindow", null, DrillSample(), DrillProfiles(), SampleCatalog(), "Main", false, "rose", string.Empty, new DrillBoard(Point2D.Zero, 600, 400), null));
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

        /// <summary>Отверстия для окна «Сверление»: три размера камней в два цвета.</summary>
        private static List<DrillHole> DrillSample()
        {
            var holes = new List<DrillHole>();
            for (int i = 0; i < 160; i++)
            {
                holes.Add(new DrillHole(new Point2D(10 + (i % 20) * 3, 10 + (i / 20) * 3), 2.4, "ss6", i % 2 == 0 ? "Crystal" : "Rose"));
            }

            for (int i = 0; i < 40; i++)
            {
                holes.Add(new DrillHole(new Point2D(10 + i * 3.4, 40), 2.9, "ss10", "Crystal"));
            }

            for (int i = 0; i < 12; i++)
            {
                holes.Add(new DrillHole(new Point2D(10 + i * 5, 46), 4.0, "ss16", "Crystal"));
            }

            return holes;
        }

        /// <summary>Каталог для снимков: ss6 сверлится фрезой 2,2 с дофрезеровкой, у сверла 4 — свой номер и подача.</summary>
        private static StoneTable SampleCatalog()
        {
            var table = new StoneTable();
            table.Sets.Add(new StoneSet
            {
                Name = "Main",
                Sizes =
                {
                    new StoneSize
                    {
                        Name = "ss6", DiameterMm = 2.4, DrillMm = 2.2,
                        Colors = { new StoneColor { Name = "Crystal", Rgb = "#E8EEF5" }, new StoneColor { Name = "Rose", Rgb = "#F48FB1" } },
                    },
                    new StoneSize { Name = "ss10", DiameterMm = 2.9, Colors = { new StoneColor { Name = "Crystal", Rgb = "#E8EEF5" } } },
                },
            });
            table.Drills.Add(new DrillTool { DiameterMm = 2.2, Name = "cutter", Rpm = 20000 });
            table.Drills.Add(new DrillTool { DiameterMm = 3.0 });
            table.Drills.Add(new DrillTool { DiameterMm = 4.0, Tool = 7, Feed = 300 });
            return table;
        }

        /// <summary>Профиль для снимка: две копии рисунка на доске и метки совмещения.</summary>
        private static DrillConfig DrillProfiles()
        {
            var config = new DrillConfig();
            DrillSettings p = config.Profile();
            p.Name = "Workshop";
            p.Marks = true;
            p.RepeatX = 2;
            return config;
        }

        /// <summary>Вкладки докера по порядку (сетка 3×3, Docker.xaml).</summary>
        private static readonly string[] DockerTabs =
        {
            "VectorTab", "LineTab", "FillTab", "TextTab", "MotifTab", "FabricTab", "EditTab", "ColorTab", "OutputTab",
        };

        /// <summary>Снимки — вдвое крупнее экрана, чтобы их было удобно смотреть на телефоне.</summary>
        private const double Scale = 2;

        private static Window Make(string typeName, params object?[] args) =>
            (Window)Activator.CreateInstance(Addon.GetType(typeName), args);

        /// <summary>
        /// Докер на вкладке <paramref name="tab"/>; <paramref name="allOpen"/> — все группы открыты и снимается
        /// всё содержимое докера, а не только видимая часть.
        /// </summary>
        private static void SnapDocker(string outDir, string name, DockerLayout layout, int width, int height, string tab, bool allOpen = false)
        {
            Settings.Layout = layout;
            Settings.LastMethod = string.Empty; // иначе докер сам откроет метод и вкладку с прошлого снимка
            Settings.LastTab = string.Empty;
            Settings.OpenGroups = null;         // группы — как в Docker.xaml по умолчанию
            var docker = new Strassio.Corel.Docker(null);
            ((TabControl)docker.FindName("MethodTabs")).SelectedItem = docker.FindName(tab);
            if (tab == "FillTab")
            {
                ((ComboBox)docker.FindName("MethodCombo")).SelectedIndex = 3;
            }

            if (allOpen)
            {
                foreach (Expander group in Descendants(docker).OfType<Expander>())
                {
                    group.IsExpanded = true;
                }
            }

            var window = new Window
            {
                Content = docker, Width = width, Height = height, Left = -5000, Top = 0,
                ShowInTaskbar = false, WindowStyle = WindowStyle.None,
            };

            window.Show();
            if (allOpen)
            {
                // Всё содержимое докера целиком, без прокрутки: окно выше экрана Windows не сделать,
                // поэтому снимается сама сетка докера (она всегда во всю высоту содержимого) на фоне докера.
                var grid = (FrameworkElement)docker.FindName("LayoutGrid");
                docker.UpdateLayout();
                Save(outDir, name, grid, grid.ActualWidth + grid.Margin.Left + grid.Margin.Right,
                    grid.ActualHeight + grid.Margin.Top + grid.Margin.Bottom, docker.Background);
            }
            else
            {
                Save(outDir, name, docker, docker.ActualWidth, docker.ActualHeight, null);
            }

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

            var bitmap = new RenderTargetBitmap(
                (int)Math.Ceiling(width * Scale), (int)Math.Ceiling(height * Scale), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
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

        /// <summary>Все элементы внутри, включая скрытые (логическое дерево не зависит от видимости).</summary>
        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is DependencyObject node)
                {
                    yield return node;
                    foreach (DependencyObject inner in Descendants(node))
                    {
                        yield return inner;
                    }
                }
            }
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
