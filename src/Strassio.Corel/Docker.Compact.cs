#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Strassio.Corel
{
    /// <summary>
    /// Компактный режим докера (пункт TRW-4 прокачки): кнопка в шапке прячет подсказки под полями и сжимает
    /// поля — в докер помещается больше, меньше прокрутки на ноутбуке. Всплывающие подсказки остаются.
    /// Режим запоминается.
    /// </summary>
    public partial class Docker
    {
        private const double NormalMargin = 10;
        private const double CompactMargin = 6;

        private void Compact_Click(object sender, RoutedEventArgs e)
        {
            context.Settings.Compact = !context.Settings.Compact;
            context.SaveSettingsQuietly();
            ApplyCompact();
        }

        private void ApplyCompact()
        {
            bool compact = context.Settings.Compact;
            object muted = FindResource("Strassio.Muted");
            var keep = new HashSet<TextBlock> { LicenseLine, UpdateLine, StatusText };
            foreach (TextBlock hint in Descendants(LayoutGrid).OfType<TextBlock>().Where(t => t.Style == muted && !keep.Contains(t)))
            {
                hint.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            }

            double margin = compact ? CompactMargin : NormalMargin;
            LayoutGrid.Margin = new Thickness(margin);
            CompactButton.Content = compact ? "" : "";
            // Привязка, а не готовый текст: подсказка сама меняется при смене языка.
            CompactButton.SetBinding(ToolTipProperty, new Binding(compact ? "[docker.compact.off]" : "[docker.compact.on]"));
        }

        /// <summary>Все элементы внутри, включая скрытые вкладки (логическое дерево не зависит от видимости).</summary>
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
    }
}
