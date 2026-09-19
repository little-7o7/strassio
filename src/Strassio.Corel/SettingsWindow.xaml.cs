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
using Strassio.Core.Localization;
using Strassio.Core.Settings;
using Strassio.Corel.Themes;
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

        public SettingsWindow(CorelApplication? app)
        {
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

            IReadOnlyList<LanguageInfo> languages = Loc.AvailableLanguages;
            LanguageCombo.ItemsSource = languages;
            LanguageCombo.SelectedItem = languages.FirstOrDefault(l => l.Code == s.Language) ?? languages.FirstOrDefault();

            Choose(ThemeCombo, s.Theme,
                (ThemeMode.Auto, "settings.theme.auto"), (ThemeMode.Light, "settings.theme.light"), (ThemeMode.Dark, "settings.theme.dark"));
            Choose(UnitsCombo, s.Units, (LengthUnit.Millimeter, "unit.mm.long"), (LengthUnit.Inch, "unit.in.long"));
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
                ErrorText.Text = Loc["settings.outlineWidth.invalid"];
                ErrorText.Visibility = Visibility.Visible;
                return;
            }

            s.Language = (LanguageCombo.SelectedItem as LanguageInfo)?.Code ?? s.Language;
            s.Theme = Chosen(ThemeCombo, s.Theme);
            s.Units = units;
            s.Decimals = DecimalsCombo.SelectedItem is int d ? d : s.Decimals;
            s.Outline = outline;
            s.OutlineWidthMm = outlineWidthMm;

            context.SaveSettings();
            DialogResult = true;
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
                ErrorText.Text = Loc.Format("status.error", ex.Message);
                ErrorText.Visibility = Visibility.Visible;
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
