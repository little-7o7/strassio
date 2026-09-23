#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Microsoft.Win32;
using Strassio.Core.Localization;
using Strassio.Core.Settings;
using Strassio.Corel.Themes;
using Strassio.Licensing;
using CorelApplication = Corel.Interop.VGCore.Application;

namespace Strassio.Corel
{
    /// <summary>
    /// Настройки плагина (docs/SPEC.md, раздел 12). Изменения применяются по «ОК» — язык, тема и
    /// единицы меняются сразу, без перезапуска CorelDRAW.
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly PluginContext context = PluginContext.Instance;
        private readonly CorelApplication? app;

        public SettingsWindow(CorelApplication? app)
        {
            this.app = app;
            InitializeComponent();
            DataContext = context.Localizer;
            SetOwner(app);
            ThemeManager.Attach(this, app);
            Closed += (s, e) => ThemeManager.Detach(this);

            Fill();
        }

        private Localizer Loc => context.Localizer;

        /// <summary>Окно поверх CorelDRAW и по центру его окна, а не где-то сбоку на панели задач.</summary>
        private void SetOwner(CorelApplication? app)
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

        private void Fill()
        {
            PluginSettings s = context.Settings;
            FillAbout();

            IReadOnlyList<LanguageInfo> languages = Loc.AvailableLanguages;
            LanguageCombo.ItemsSource = languages;
            LanguageCombo.SelectedItem = languages.FirstOrDefault(l => l.Code == s.Language) ?? languages.FirstOrDefault();

            Choose(ThemeCombo, s.Theme,
                (ThemeMode.Auto, "settings.theme.auto"), (ThemeMode.Light, "settings.theme.light"), (ThemeMode.Dark, "settings.theme.dark"));
            Choose(UnitsCombo, s.Units, (LengthUnit.Millimeter, "unit.mm.long"), (LengthUnit.Inch, "unit.in.long"));
            Choose(LayoutCombo, s.Layout,
                (DockerLayout.Vertical, "settings.layout.vertical"), (DockerLayout.Horizontal, "settings.layout.horizontal"));
            Choose(OutlineCombo, s.Outline,
                (OutlineStyle.None, "settings.outline.none"), (OutlineStyle.Hairline, "settings.outline.hairline"),
                (OutlineStyle.Width, "settings.outline.width"));

            DecimalsCombo.ItemsSource = Enumerable.Range(0, 5).ToList();
            DecimalsCombo.SelectedItem = Math.Max(0, Math.Min(4, s.Decimals));

            UpdateOutlineWidth();
        }

        private void Choose<T>(ComboBox combo, T current, params (T Value, string Key)[] options)
        {
            List<Choice<T>> items = options.Select(o => new Choice<T>(o.Value, Loc[o.Key])).ToList();
            combo.ItemsSource = items;
            combo.SelectedItem = items.FirstOrDefault(i => Equals(i.Value, current)) ?? items[0];
        }

        private static T Chosen<T>(ComboBox combo, T fallback) => combo.SelectedItem is Choice<T> c ? c.Value : fallback;

        /// <summary>Толщина обводки — в единицах, выбранных в этом же окне.</summary>
        private void UpdateOutlineWidth()
        {
            if (OutlineWidthBox == null)
            {
                return;
            }

            bool byWidth = Chosen(OutlineCombo, OutlineStyle.None) == OutlineStyle.Width;
            OutlineWidthCaption.Visibility = byWidth ? Visibility.Visible : Visibility.Collapsed;
            OutlineWidthBox.Visibility = OutlineWidthCaption.Visibility;

            LengthUnit unit = Chosen(UnitsCombo, context.Settings.Units);
            OutlineWidthCaption.Text = Loc.Format("settings.outlineWidth", Loc[unit == LengthUnit.Inch ? "unit.in" : "unit.mm"]);
            OutlineWidthBox.Text = LengthUnits.Format(context.Settings.OutlineWidthMm, unit, 4, CultureInfo.CurrentCulture);
        }

        private void OutlineCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateOutlineWidth();

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            PluginSettings s = context.Settings;
            LengthUnit units = Chosen(UnitsCombo, s.Units);
            OutlineStyle outline = Chosen(OutlineCombo, s.Outline);

            double outlineWidthMm = s.OutlineWidthMm;
            if (outline == OutlineStyle.Width &&
                (!LengthUnits.TryParse(OutlineWidthBox.Text, units, out outlineWidthMm) || outlineWidthMm <= 0 || outlineWidthMm > 10))
            {
                ShowError(Loc["settings.outlineWidth.invalid"]);
                return;
            }

            s.Language = (LanguageCombo.SelectedItem as LanguageInfo)?.Code ?? s.Language;
            s.Theme = Chosen(ThemeCombo, s.Theme);
            s.Units = units;
            s.Layout = Chosen(LayoutCombo, s.Layout);
            s.Decimals = DecimalsCombo.SelectedItem is int d ? d : s.Decimals;
            s.Outline = outline;
            s.OutlineWidthMm = outlineWidthMm;

            context.SaveSettings();
            DialogResult = true;
        }

        /// <summary>Сохраняет в файл уже сохранённые настройки и таблицу камней (не то, что сейчас не нажато «ОК»).</summary>
        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                FileName = Loc["transfer.fileName"] + SettingsTransfer.Extension,
                DefaultExt = SettingsTransfer.Extension,
                Filter = Loc.Format("transfer.filter", SettingsTransfer.Extension),
                AddExtension = true,
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                SettingsTransfer.Export(dialog.FileName, context.Settings, context.Stones);
                ShowInfo(Loc.Format("transfer.exported", Path.GetFileName(dialog.FileName)));
            }
            catch (Exception ex)
            {
                ShowError(Loc.Format("status.error", ex.Message));
            }
        }

        /// <summary>Загружает файл переноса и сразу применяет его; поля окна показывают новые значения.</summary>
        private void Import_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                DefaultExt = SettingsTransfer.Extension,
                Filter = Loc.Format("transfer.filter", SettingsTransfer.Extension),
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            if (!SettingsTransfer.TryImport(dialog.FileName, out SettingsBundle? bundle, out string errorKey))
            {
                ShowError(Loc[errorKey]);
                return;
            }

            context.ApplyImport(bundle!);
            Fill();
            ShowInfo(Loc.Format("transfer.imported", Path.GetFileName(dialog.FileName)));
        }

        private void ShowInfo(string text)
        {
            ErrorText.Text = text;
            ErrorText.SetResourceReference(TextBlock.ForegroundProperty, "Strassio.MutedForeground");
            ErrorText.Visibility = Visibility.Visible;
        }

        private void ShowError(string text)
        {
            ErrorText.Text = text;
            ErrorText.SetResourceReference(TextBlock.ForegroundProperty, "Strassio.Error");
            ErrorText.Visibility = Visibility.Visible;
        }

        /// <summary>Окно «Лицензия» (активация через сайт) — живёт в настройках, а не в шапке докера.</summary>
        private void License_Click(object sender, RoutedEventArgs e)
        {
            new LicenseWindow(app) { Owner = this }.ShowDialog();
            FillAbout();
        }

        /// <summary>
        /// «О программе»: версии Strassio, CorelDRAW, Windows и .NET, папки, лицензия и клиент (имя,
        /// телефон, почта, день рождения, Telegram — приходят с сервера при сверке, см. LicenseClient).
        /// </summary>
        private void FillAbout()
        {
            var lines = new List<string>();
            void Line(string key, string? value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    lines.Add(Loc.Format(key, value!.Trim()));
                }
            }

            string addonDir = Path.GetDirectoryName(typeof(SettingsWindow).Assembly.Location) ?? string.Empty;
            Line("about.version", typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3));
            Line("about.corel", (context.License.CorelVersion ?? "—") + " (" + (Environment.Is64BitProcess ? "64" : "32") + " bit)");
            Line("about.windows", Environment.OSVersion.VersionString + (Environment.Is64BitOperatingSystem ? ", 64 bit" : ", 32 bit"));
            Line("about.net", System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
            Line("about.addonDir", addonDir);
            Line("about.dataDir", context.Store.Directory);
            Line("about.site", LicenseKeys.Server);

            lines.Add(string.Empty);
            LicenseStatus status = context.License.Status;
            Line("about.license", LicenseWindow.DescribeState(Loc, status, context.License.LastProblem));
            LicenseData? license = status.License;
            if (license != null && !license.IsTrial)
            {
                Line("about.serial", license.Serial);
                Line("about.until", license.ExpiresUtc.HasValue ? license.ExpiresUtc.Value.ToLocalTime().ToString("d", CultureInfo.CurrentCulture) : Loc["license.forever"]);
            }

            if (license?.IssuedUtc != null)
            {
                Line("about.lastCheck", license.IssuedUtc.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
            }

            Line("about.computer", context.License.IsComputerKnown ? context.License.Computer.Display : "…");

            lines.Add(string.Empty);
            lines.Add(Loc["about.client"]);
            LicenseClient? client = license != null && !license.IsTrial ? context.License.Client : null;
            if (client == null)
            {
                lines.Add(Loc["about.noClient"]);
            }
            else
            {
                Line("about.firstName", client.FirstName);
                Line("about.lastName", client.LastName);
                Line("about.phone", client.Phone);
                Line("about.email", client.Email);
                Line("about.birthday", DateTime.TryParseExact(client.Birthday, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime birthday)
                    ? birthday.ToString("d", CultureInfo.CurrentCulture) : client.Birthday);
                Line("about.telegram", string.IsNullOrWhiteSpace(client.Telegram) ? null : "@" + client.Telegram);
            }

            AboutBox.Text = string.Join(Environment.NewLine, lines);
        }

        private void CopyAbout_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(AboutBox.Text);
                ErrorText.Text = Loc["about.copied"];
                ErrorText.Foreground = System.Windows.Media.Brushes.SeaGreen;
                ErrorText.Visibility = Visibility.Visible;
            }
            catch (Exception)
            {
                // Буфер обмена занят другой программой — можно нажать ещё раз.
            }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(context.Store.Directory);
                Process.Start("explorer.exe", "\"" + context.Store.Directory + "\"");
            }
            catch (Exception ex)
            {
                ShowError(Loc.Format("status.error", ex.Message));
            }
        }

        /// <summary>Вариант в выпадающем списке: значение и текст на языке интерфейса.</summary>
        public sealed class Choice<T>
        {
            internal Choice(T value, string text)
            {
                Value = value;
                Text = text;
            }

            public T Value { get; }

            public string Text { get; }
        }
    }
}
