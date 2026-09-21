#nullable enable
using System.Windows;
using System.Windows.Controls;
using Strassio.Core.Settings;

namespace Strassio.Corel
{
    /// <summary>
    /// Раскладка докера (docs/SPEC.md, разделы 2.2 и 12): вертикальная — блоки друг под другом, как у
    /// обычных докеров справа; горизонтальная — три блока рядом, для докера внизу или сверху окна.
    /// </summary>
    public partial class Docker
    {
        /// <summary>Промежуток между блоками в горизонтальной раскладке.</summary>
        private const double SectionGap = 14;

        private void ApplyLayout()
        {
            bool horizontal = context.Settings.Layout == DockerLayout.Horizontal;
            FrameworkElement[] sections = { StoneSection, MethodSection, ActionSection };

            LayoutGrid.RowDefinitions.Clear();
            LayoutGrid.ColumnDefinitions.Clear();

            for (int i = 0; i < sections.Length; i++)
            {
                if (horizontal)
                {
                    LayoutGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 210 });
                    Grid.SetColumn(sections[i], i);
                    Grid.SetRow(sections[i], 0);
                    sections[i].Margin = new Thickness(0, 0, i < sections.Length - 1 ? SectionGap : 0, 0);
                }
                else
                {
                    LayoutGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    Grid.SetRow(sections[i], i);
                    Grid.SetColumn(sections[i], 0);
                    sections[i].Margin = new Thickness(0);
                }
            }

            // В ряд докер может не поместиться по ширине — тогда прокрутка вбок, а не обрезанные поля.
            // Но при прокрутке вбок ширина «бесконечная», и длинные подсказки перестают переноситься —
            // поэтому ширину блоков задаём сами: по ширине докера, но не уже HorizontalMinWidth.
            Scroller.HorizontalScrollBarVisibility = horizontal ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
            MinWidth = horizontal ? 0 : 220;
            Scroller.SizeChanged -= Scroller_SizeChanged;
            if (horizontal)
            {
                Scroller.SizeChanged += Scroller_SizeChanged;
                FitHorizontalWidth();
            }
            else
            {
                LayoutGrid.ClearValue(WidthProperty);
            }
        }

        /// <summary>Самая узкая ширина трёх блоков в ряд; уже — прокрутка вбок.</summary>
        private const double HorizontalMinWidth = 660;

        private void Scroller_SizeChanged(object sender, SizeChangedEventArgs e) => FitHorizontalWidth();

        private void FitHorizontalWidth()
        {
            double available = Scroller.ActualWidth - LayoutGrid.Margin.Left - LayoutGrid.Margin.Right;
            if (Scroller.ComputedVerticalScrollBarVisibility == Visibility.Visible)
            {
                available -= SystemParameters.VerticalScrollBarWidth;
            }

            LayoutGrid.Width = System.Math.Max(HorizontalMinWidth, available);
        }
    }
}
