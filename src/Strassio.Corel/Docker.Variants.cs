#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Strassio.Core.Settings;

namespace Strassio.Corel
{
    /// <summary>
    /// Сравнение вариантов (пункт «ещё идеи» прокачки): в предпросмотре можно «запомнить» до трёх раскладок
    /// одной и той же фигуры — с другим методом, камнем, зазором. Они видны рядом маленькими картинками;
    /// щелчок по картинке возвращает все её настройки, остаётся нажать «Создать».
    /// </summary>
    public partial class Docker
    {
        private const int MaxVariants = 3;

        private readonly List<PreviewVariant> variants = new List<PreviewVariant>();

        /// <summary>Сколько страз в последнем предпросмотре (для подписи варианта).</summary>
        private int lastPreviewCount;

        private void PinVariant_Click(object sender, RoutedEventArgs e)
        {
            if (PreviewImage.Source == null || !CommitAllParams())
            {
                return;
            }

            Preset? preset = CurrentAsPreset(string.Empty);
            if (preset == null)
            {
                return;
            }

            variants.Add(new PreviewVariant(preset, PreviewImage.Source, lastPreviewCount));
            if (variants.Count > MaxVariants)
            {
                variants.RemoveAt(0);
            }

            RebuildVariantStrip();
            SetStatus("variant.pinned", variants.Count, MaxVariants);
        }

        private void ApplyVariant(int index)
        {
            if (index < 0 || index >= variants.Count)
            {
                return;
            }

            PreviewVariant variant = variants[index];
            ApplyPreset(variant.Preset);
            PreviewImage.Source = variant.Image;
            PreviewPanel.Visibility = Visibility.Visible;
            SetStatus("variant.applied", index + 1, variant.Count);
        }

        /// <summary>Новая фигура в предпросмотре — старые варианты к ней не относятся.</summary>
        private void ClearVariants()
        {
            variants.Clear();
            RebuildVariantStrip();
        }

        private void RebuildVariantStrip()
        {
            VariantStrip.Children.Clear();
            for (int i = 0; i < variants.Count; i++)
            {
                PreviewVariant variant = variants[i];
                string method = methods.FirstOrDefault(m => m.Key == "method." + variant.Preset.Method)?.Text ?? variant.Preset.Method;
                var button = new Button
                {
                    Content = new Image { Source = variant.Image, Height = 64, Stretch = Stretch.Uniform },
                    Margin = new Thickness(i == 0 ? 0 : 3, 0, i == variants.Count - 1 ? 0 : 3, 0),
                    Padding = new Thickness(2),
                    Background = Brushes.White,
                    ToolTip = Loc.Format("variant.tooltip", i + 1, method, variant.Preset.Size, variant.Preset.Color, variant.Count),
                };
                int index = i;
                button.Click += (s, e) => ApplyVariant(index);
                VariantStrip.Children.Add(button);
            }

            VariantStrip.Visibility = variants.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Запомненный вариант: настройки (как пресет), картинка и сколько страз.</summary>
        private sealed class PreviewVariant
        {
            public PreviewVariant(Preset preset, ImageSource image, int count)
            {
                Preset = preset;
                Image = image;
                Count = count;
            }

            public Preset Preset { get; }

            public ImageSource Image { get; }

            public int Count { get; }
        }
    }
}
