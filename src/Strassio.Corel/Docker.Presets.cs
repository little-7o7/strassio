#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Strassio.Core.Methods;
using Strassio.Core.Settings;
using Strassio.Core.Stones;

namespace Strassio.Corel
{
    /// <summary>
    /// Пресеты и «Недавние» (docs/SPEC.md, раздел 8, [P2]) и выбор набора таблицы камней (раздел 3.1).
    /// Пресет — метод, камень и все параметры под именем; применяется одним выбором в списке.
    /// </summary>
    public partial class Docker
    {
        private bool rebuildingPresets;

        private PresetStore Presets => new PresetStore(context.Store.PresetsDirectory);

        // ---- Наборы таблицы камней -------------------------------------------------------------

        private void RebuildSets()
        {
            List<StoneSet> sets = context.Stones.Sets;
            SetRow.Visibility = sets.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            refreshing = true;
            try
            {
                SetCombo.ItemsSource = null;
                SetCombo.ItemsSource = sets;
                SetCombo.SelectedItem = ActiveSet;
            }
            finally
            {
                refreshing = false;
            }
        }

        private void SetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (refreshing || !(SetCombo.SelectedItem is StoneSet chosen) || ReferenceEquals(chosen, ActiveSet))
            {
                return;
            }

            context.Settings.ActiveSet = chosen.Name;
            context.SaveSettingsQuietly();
            RebuildAll();
        }

        // ---- Пресеты ---------------------------------------------------------------------------

        private void RebuildPresets()
        {
            rebuildingPresets = true;
            try
            {
                var options = new List<PresetOption>();
                foreach (Preset preset in Presets.List())
                {
                    options.Add(new PresetOption(preset, preset.Name, isRecent: false));
                }

                foreach (Preset recent in context.Settings.Recent)
                {
                    string method = Loc["method." + recent.Method];
                    options.Add(new PresetOption(recent, Loc.Format("docker.preset.recent", method, recent.Size, recent.Color), isRecent: true));
                }

                PresetCombo.ItemsSource = options;
                PresetCombo.SelectedItem = null;
                DeletePresetButton.IsEnabled = false;
            }
            finally
            {
                rebuildingPresets = false;
            }
        }

        private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (rebuildingPresets || !(PresetCombo.SelectedItem is PresetOption option))
            {
                return;
            }

            DeletePresetButton.IsEnabled = !option.IsRecent;
            ApplyPreset(option.Preset);
        }

        /// <summary>Вкладка, метод, камень и параметры — как в пресете. Чего нет в таблице камней, остаётся как было.</summary>
        private void ApplyPreset(Preset preset)
        {
            MethodOption? method = methods.FirstOrDefault(m => m.Key == "method." + preset.Method);
            context.Settings.Method = preset.Parameters.Clone();
            context.SaveSettingsQuietly();

            refreshing = true;
            try
            {
                SizeOption? size = (SizeCombo.ItemsSource as IEnumerable<SizeOption>)?
                    .FirstOrDefault(o => string.Equals(o.Size.Name, preset.Size, StringComparison.OrdinalIgnoreCase));
                if (size != null)
                {
                    SizeCombo.SelectedItem = size;
                }

                if (method != null)
                {
                    MethodTabs.SelectedItem = method.Info.IsFill ? FillTab : LineTab;
                    RebuildMethods();
                    MethodCombo.SelectedItem = method;
                    context.Settings.LastMethod = preset.Method;
                }
            }
            finally
            {
                refreshing = false;
            }

            RebuildColors();
            ColorOption? color = (ColorList.ItemsSource as IEnumerable<ColorOption>)?
                .FirstOrDefault(o => string.Equals(o.Name, preset.Color, StringComparison.OrdinalIgnoreCase));
            if (color != null)
            {
                ColorList.SelectedItem = color;
            }

            ShowTabPanels();
            UpdateMethodHint();
            RebuildParams();
            ScheduleLivePreview();
            SetStatus("preset.applied", (PresetCombo.SelectedItem as PresetOption)?.Text ?? preset.Name);
        }

        private Preset? CurrentAsPreset(string name)
        {
            if (!(MethodCombo.SelectedItem is MethodOption method) || !(SizeCombo.SelectedItem is SizeOption size) ||
                !(ColorList.SelectedItem is ColorOption color))
            {
                return null;
            }

            return new Preset
            {
                Name = name,
                Method = method.Info.Kind.ToString().ToLowerInvariant(),
                Size = size.Size.Name,
                Color = color.Name,
                Parameters = Params.Clone(),
            };
        }

        private void SavePreset_Click(object sender, RoutedEventArgs e)
        {
            if (!CommitAllParams())
            {
                return;
            }

            string suggested = (PresetCombo.SelectedItem as PresetOption) is PresetOption { IsRecent: false } chosen ? chosen.Preset.Name : string.Empty;
            string? name = NameDialog.Ask(app, null, Loc["docker.preset.save"], Loc["docker.preset.name"], suggested);
            Preset? preset = name == null ? null : CurrentAsPreset(name);
            if (preset == null)
            {
                return;
            }

            try
            {
                Presets.Save(preset);
                RebuildPresets();
                SetStatus("preset.saved", preset.Name);
            }
            catch (Exception ex)
            {
                SetStatus("status.error", ex.Message);
            }
        }

        private void DeletePreset_Click(object sender, RoutedEventArgs e)
        {
            if (PresetCombo.SelectedItem is PresetOption option && !option.IsRecent)
            {
                Presets.Delete(option.Preset.Name);
                RebuildPresets();
                SetStatus("preset.deleted", option.Preset.Name);
            }
        }

        /// <summary>После «Создать»: настройки — в начало «Недавних».</summary>
        private void RememberRecent(MethodOption method, string size, string color)
        {
            PresetStore.Remember(context.Settings.Recent, new Preset
            {
                Method = method.Info.Kind.ToString().ToLowerInvariant(),
                Size = size,
                Color = color,
                Parameters = Params.Clone(),
            });
            context.SaveSettingsQuietly();
            Dispatcher.BeginInvoke(new Action(RebuildPresets));
        }

        /// <summary>Строка списка пресетов.</summary>
        public sealed class PresetOption
        {
            internal PresetOption(Preset preset, string text, bool isRecent)
            {
                Preset = preset;
                Text = text;
                IsRecent = isRecent;
            }

            internal Preset Preset { get; }

            public string Text { get; }

            internal bool IsRecent { get; }
        }
    }
}
