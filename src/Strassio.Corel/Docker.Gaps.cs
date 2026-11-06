#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using Corel.Interop.VGCore;
using Strassio.Core.Editing;
using Strassio.Core.Settings;

namespace Strassio.Corel
{
    /// <summary>
    /// «Выровнять просветы» (docs/SPEC.md, 7.2): после удаления наложений и ручной правки просветы между
    /// стразами в ряду бывают разными. «Показать» выделяет такие стразы, «Выровнять» чуть сдвигает их вдоль
    /// ряда — не дальше заданного предела. Концы линий, углы и закреплённые стразы стоят на месте.
    /// </summary>
    public partial class Docker
    {
        /// <summary>Разница просветов по разные стороны от стразы, которую глаз уже замечает, мм.</summary>
        private const double UnevenToleranceMm = 0.2;

        /// <summary>Насколько можно сдвинуть одну стразу, мм (поле в докере; по умолчанию 0,5 мм).</summary>
        private double evenShiftMm = 0.5;

        private double EvenShiftMm
        {
            get
            {
                if (LengthUnits.TryParse(EvenShiftBox.Text, context.Settings.Units, out double mm) && mm > 0 && mm <= 5)
                {
                    evenShiftMm = mm;
                }

                return evenShiftMm;
            }
        }

        private void EvenShiftBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => _ = EvenShiftMm;

        /// <summary>Поле предела сдвига — в текущих единицах (при смене мм/дюймов пересчитывается).</summary>
        private void RefreshEvenShiftBox() =>
            EvenShiftBox.Text = LengthUnits.Format(evenShiftMm, context.Settings.Units, Math.Max(2, context.Settings.Decimals), CultureInfo.CurrentCulture);

        /// <summary>Выделяет стразы, у которых просвет с одной стороны в ряду заметно больше, чем с другой.</summary>
        private void ShowUneven_Click(object sender, RoutedEventArgs e)
        {
            RunInDocument(null, doc =>
            {
                List<StoneShape> stones = StonesToEdit(doc);
                if (stones.Count == 0)
                {
                    SetStatus("edit.noStones");
                    return;
                }

                IReadOnlyList<int> uneven = GapEvener.FindUneven(stones.Select(s => s.Stone).ToList(), UnevenToleranceMm, out double worst);
                doc.ClearSelection();
                if (uneven.Count == 0)
                {
                    SetStatus("edit.gapsEven", Format(UnevenToleranceMm));
                    return;
                }

                SelectStones(doc, stones, uneven);
                SetStatus("edit.unevenFound", uneven.Count, Format(worst));
            });
        }

        /// <summary>Разводит стразы поровну. Что не удалось выровнять до конца — остаётся выделенным.</summary>
        private void EvenGaps_Click(object sender, RoutedEventArgs e)
        {
            double maxShift = EvenShiftMm;
            double minGap = Params.GapMm;
            RunInDocument("undo.evenGaps", doc =>
            {
                List<StoneShape> stones = StonesToEdit(doc);
                if (stones.Count == 0)
                {
                    SetStatus("edit.noStones");
                    return;
                }

                GapEvenResult result = GapEvener.Even(stones.Select(s => s.Stone).ToList(), maxShift, minGap, UnevenToleranceMm);
                if (result.UnevenBefore.Count == 0)
                {
                    SetStatus("edit.gapsEven", Format(UnevenToleranceMm));
                    return;
                }

                Apply(stones, result.Edit);
                doc.ClearSelection();
                if (result.UnevenAfter.Count == 0)
                {
                    SetStatus("edit.gapsEvened", result.Edit.Moved.Count);
                    return;
                }

                SelectStones(doc, stones, result.UnevenAfter);
                SetStatus("edit.gapsEvenedSome", result.Edit.Moved.Count, result.UnevenAfter.Count);
            });
        }

        /// <summary>Выделенные стразы, а если их нет — все стразы страницы.</summary>
        private List<StoneShape> StonesToEdit(Document doc)
        {
            StoneSelection selection = StoneShapes.FromSelection(doc, KnownSizes);
            return selection.Stones.Count > 0 ? selection.Stones : StoneShapes.FromPage(doc.ActivePage, KnownSizes);
        }

        private static void SelectStones(Document doc, List<StoneShape> stones, IReadOnlyList<int> indices)
        {
            object[] shapes = indices.Select(i => (object)stones[i].Shape).ToArray();
            doc.CreateShapeRangeFromArray(ref shapes).CreateSelection();
        }
    }
}
