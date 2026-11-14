#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Strassio.Corel
{
    /// <summary>
    /// Порядок в докере (2026-11-14, автор: «плагин слишком перемешанный»). Вкладки стоят сеткой 3×3 по шагам
    /// работы, а внутри длинных вкладок — сворачиваемые группы, как блоки в докерах самого CorelDRAW.
    /// Докер помнит, какие группы открыты и на какой вкладке с ним работали в прошлый раз.
    /// </summary>
    public partial class Docker
    {
        private bool restoringGroups;

        /// <summary>Все группы всех вкладок, включая скрытые (Docker.xaml, стиль Strassio.Group).</summary>
        private IEnumerable<Expander> Groups => Descendants(LayoutGrid).OfType<Expander>();

        /// <summary>
        /// Открывает группы, как их оставили в прошлый раз. Пока группы ни разу не открывали и не закрывали,
        /// открыты те, что заданы в Docker.xaml (IsExpanded="True").
        /// </summary>
        private void RestoreGroups()
        {
            List<string>? open = context.Settings.OpenGroups;
            restoringGroups = true;
            try
            {
                foreach (Expander group in Groups)
                {
                    if (open != null)
                    {
                        group.IsExpanded = open.Contains(group.Name);
                    }

                    group.Expanded -= Group_Toggled;
                    group.Expanded += Group_Toggled;
                    group.Collapsed -= Group_Toggled;
                    group.Collapsed += Group_Toggled;
                }
            }
            finally
            {
                restoringGroups = false;
            }
        }

        private void Group_Toggled(object sender, RoutedEventArgs e)
        {
            if (restoringGroups || !ReferenceEquals(sender, e.OriginalSource))
            {
                return;
            }

            context.Settings.OpenGroups = Groups.Where(g => g.IsExpanded).Select(g => g.Name).ToList();
            context.SaveSettingsQuietly();

            // Открытая внизу группа не должна остаться за краем докера: прокрутка сама показывает её.
            if (sender is Expander group && group.IsExpanded)
            {
                Dispatcher.BeginInvoke(
                    new System.Action(() => group.BringIntoView()),
                    System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        /// <summary>Открывает вкладку, на которой работали в прошлый раз (Линию и Заливку уже выбрал последний метод).</summary>
        private void RestoreLastTab()
        {
            TabItem? last = MethodTabs.Items.OfType<TabItem>().FirstOrDefault(t => t.Name == context.Settings.LastTab);
            if (last != null && !ReferenceEquals(last, MethodTabs.SelectedItem))
            {
                MethodTabs.SelectedItem = last;
            }
        }

        private void RememberTab()
        {
            string name = (MethodTabs.SelectedItem as TabItem)?.Name ?? string.Empty;
            if (name != context.Settings.LastTab)
            {
                context.Settings.LastTab = name;
                context.SaveSettingsQuietly();
            }
        }

        /// <summary>Подсказка внизу докера, когда открыли вкладку.</summary>
        private string ReadyKey(TabItem? tab)
        {
            if (tab == VectorTab)
            {
                return "vec.ready";
            }

            if (tab == TextTab)
            {
                return "text.ready";
            }

            if (tab == MotifTab)
            {
                return "motif.ready";
            }

            if (tab == FabricTab)
            {
                return "docker.fabric.ready";
            }

            if (tab == EditTab)
            {
                return "edit.ready";
            }

            if (tab == ColorTab)
            {
                return "color.ready";
            }

            if (tab == OutputTab)
            {
                return "docker.output.ready";
            }

            return "status.ready";
        }
    }
}
