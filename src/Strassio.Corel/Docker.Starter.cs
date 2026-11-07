#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Corel.Interop.VGCore;
using Strassio.Core.Geometry;
using Strassio.Core.Motifs;

namespace Strassio.Corel
{
    /// <summary>
    /// Готовые мотивы и рамки в комплекте (вкладка «Мотивы»): при первом открытии плагин кладёт в библиотеку
    /// набор из 27 мотивов под выбранный камень. Кнопка «Готовые под этот камень» добавляет такой же набор
    /// под другой размер или цвет, «Поставить на страницу» — одна копия мотива в центр страницы.
    /// </summary>
    public partial class Docker
    {
        /// <summary>Мелкий, основной и крупный камень из текущего набора и два цвета; null — камень не выбран.</summary>
        private StarterKit? CurrentStarterKit()
        {
            if (!(SizeCombo.SelectedItem is SizeOption size) || !(ColorList.SelectedItem is ColorOption color))
            {
                return null;
            }

            List<StarterSize> sizes = (SizeCombo.ItemsSource as IEnumerable<SizeOption>)?
                .Select(o => new StarterSize(o.Size.Name, o.Size.DiameterMm)).ToList() ?? new List<StarterSize>();
            List<StarterColor> colors = size.Size.Colors.Select(c => new StarterColor(c.Name, c.Rgb)).ToList();
            return StarterKit.From(
                sizes, new StarterSize(size.Size.Name, size.Size.DiameterMm), colors, new StarterColor(color.Name, color.Color.Rgb), Params.GapMm);
        }

        private List<Motif> BuildStarter(StarterKit kit) => StarterMotifs.Build(kit, key => Loc["motif.starter." + key]);

        /// <summary>Один раз на версию набора: кладёт готовые мотивы в библиотеку (удалённые потом не возвращаются).</summary>
        private void EnsureStarterMotifs()
        {
            try
            {
                MotifLibrary store = MotifStore;
                if (store.StarterInstalled(StarterMotifs.Version))
                {
                    return;
                }

                StarterKit? kit = CurrentStarterKit();
                if (kit != null)
                {
                    store.InstallStarter(BuildStarter(kit), StarterMotifs.Version);
                }
            }
            catch (Exception)
            {
                // Нет доступа к папке — без готовых мотивов плагин всё равно работает.
            }
        }

        private void StarterAdd_Click(object sender, RoutedEventArgs e)
        {
            StarterKit? kit = CurrentStarterKit();
            if (kit == null)
            {
                SetStatus("status.noStone");
                return;
            }

            try
            {
                List<Motif> set = BuildStarter(kit);
                MotifLibrary store = MotifStore;
                foreach (Motif motif in set)
                {
                    store.Save(motif); // тот же размер — обновляются цвета
                }

                RebuildMotifs();
                SetStatus("motif.starter.added", set.Count, kit.Main.Name);
            }
            catch (Exception ex)
            {
                SetStatus("status.error", ex.Message);
            }
        }

        private void MotifDrop_Click(object sender, RoutedEventArgs e)
        {
            Motif? motif = SelectedMotif(MotifCombo);
            if (motif == null)
            {
                SetStatus("motif.noMotif");
                return;
            }

            string caption = Loc.Format("undo.motif", motif.Name);
            RunInDocument(null, doc =>
            {
                var center = new Point2D(doc.ActivePage.CenterX, doc.ActivePage.CenterY);
                List<MotifPlacement> placed = motif.Stones.Select(s => new MotifPlacement(center + s.Offset, s, 0)).ToList();
                doc.BeginCommandGroup(caption);
                try
                {
                    Shape group = CreateMotifStones(doc, placed, caption);
                    doc.ClearSelection();
                    group.CreateSelection();
                }
                finally
                {
                    doc.EndCommandGroup();
                }

                SetStatus("motif.dropped", motif.Name, placed.Count);
            });
        }
    }
}
