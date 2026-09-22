#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Strassio.Core.Localization;
using Strassio.Corel.Themes;
using CorelApplication = Corel.Interop.VGCore.Application;

namespace Strassio.Corel
{
    /// <summary>
    /// Маленькое окно «введите название» — для пресета и набора таблицы камней. Строится кодом (без
    /// XAML): одно поле и две кнопки, цвета — тема CorelDRAW, тексты — из файлов языков.
    /// </summary>
    internal sealed class NameDialog : Window
    {
        private readonly TextBox box;

        private NameDialog(CorelApplication? app, Window? owner, string title, string label, string initial)
        {
            Localizer loc = PluginContext.Instance.Localizer;
            Title = title;
            Width = 340;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
            Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/Strassio.Corel;component/Themes/Styles.xaml", UriKind.Relative),
            });
            SetResourceReference(BackgroundProperty, "Strassio.Background");
            SetResourceReference(ForegroundProperty, "Strassio.Foreground");

            if (owner != null)
            {
                Owner = owner;
            }
            else
            {
                try
                {
                    if (app != null)
                    {
                        new WindowInteropHelper(this).Owner = new IntPtr(app.AppWindow.Handle);
                    }
                }
                catch (Exception)
                {
                    WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }
            }

            var caption = new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 6) };
            box = new TextBox { Text = initial, Margin = new Thickness(0, 0, 0, 12) };

            var ok = new Button { Content = loc["settings.ok"], IsDefault = true, MinWidth = 80 };
            ok.SetResourceReference(StyleProperty, "Strassio.PrimaryButton");
            ok.Click += (s, e) =>
            {
                if (box.Text.Trim().Length > 0)
                {
                    DialogResult = true;
                }
            };
            var cancel = new Button { Content = loc["settings.cancel"], IsCancel = true, MinWidth = 80, Margin = new Thickness(6, 0, 0, 0) };

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var root = new StackPanel { Margin = new Thickness(14) };
            root.Children.Add(caption);
            root.Children.Add(box);
            root.Children.Add(buttons);
            Content = root;

            ThemeManager.Attach(this, app);
            Closed += (s, e) => ThemeManager.Detach(this);
            Loaded += (s, e) =>
            {
                box.Focus();
                box.SelectAll();
            };
        }

        /// <summary>Спрашивает название; null — нажали «Отмена».</summary>
        public static string? Ask(CorelApplication? app, Window? owner, string title, string label, string initial = "")
        {
            var dialog = new NameDialog(app, owner, title, label, initial);
            return dialog.ShowDialog() == true ? dialog.box.Text.Trim() : null;
        }
    }
}
