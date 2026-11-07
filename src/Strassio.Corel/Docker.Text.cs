#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Corel.Interop.VGCore;
using Strassio.Core.Geometry;
using Strassio.Core.Methods;
using Strassio.Core.Placement;
using Strassio.Core.Vector;
using CoreCurve = Strassio.Core.Geometry.Curve;

namespace Strassio.Corel
{
    /// <summary>
    /// Вкладка «Текст» (docs/SPEC.md, раздел 10.1; пункт 10 прокачки): надпись стразами одной кнопкой.
    /// Выделяете текст (или буквы в кривых), выбираете способ — обвести, один ряд по середине или залить —
    /// и нажимаете. Заодно плагин проверяет, не тонкие ли буквы для камня и не стоят ли они слишком тесно,
    /// и помечает такие места красными кружками.
    /// </summary>
    public partial class Docker
    {
        private TextMode SelectedTextMode
        {
            get
            {
                switch ((TextModeCombo.SelectedItem as ChoiceOption)?.Value)
                {
                    case "outline":
                        return TextMode.Outline;
                    case "fill":
                        return TextMode.Fill;
                    default:
                        return TextMode.Middle;
                }
            }
        }

        /// <summary>Список способов и поле «Самый мелкий камень» — на текущем языке и с текущей таблицей камней.</summary>
        private void RebuildTextModes()
        {
            var modes = new List<ChoiceOption>
            {
                new ChoiceOption("middle", Loc["text.mode.middle"]),
                new ChoiceOption("outline", Loc["text.mode.outline"]),
                new ChoiceOption("fill", Loc["text.mode.fill"]),
            };
            string? current = (TextModeCombo.SelectedItem as ChoiceOption)?.Value;
            TextModeCombo.ItemsSource = modes;
            TextModeCombo.SelectedItem = modes.FirstOrDefault(m => m.Value == current) ?? modes[0];
            TextThinHost.Content = CreateSizeChoice(MethodCatalog.ThinSize);
            UpdateTextModeHint();
        }

        private void TextModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateTextModeHint();

        private void UpdateTextModeHint()
        {
            TextMode mode = SelectedTextMode;
            TextModeHint.Text = Loc[mode == TextMode.Outline ? "text.mode.outline.hint" : mode == TextMode.Fill ? "text.mode.fill.hint" : "text.mode.middle.hint"];
            TextThinRow.Visibility = mode == TextMode.Middle ? Visibility.Visible : Visibility.Collapsed;
        }

        private void TextCheck_Click(object sender, RoutedEventArgs e) => RunText(make: false);

        private void TextMake_Click(object sender, RoutedEventArgs e) => RunText(make: true);

        private void RunText(bool make)
        {
            if (!(SizeCombo.SelectedItem is SizeOption size) || !(ColorList.SelectedItem is ColorOption color))
            {
                SetStatus("status.noStone");
                return;
            }

            TextMode mode = SelectedTextMode;
            MethodParameters p = TextCheck.ParametersFor(mode, Params);
            double smallest = ThinDiameter(size.Size.DiameterMm);
            string caption = Loc.Format("undo.text", size.Size.Name, color.Name);
            RunInDocument(null, doc =>
            {
                List<SourceShape> sources = SelectedCurves(doc);
                if (sources.Count == 0)
                {
                    SetStatus("text.select");
                    return;
                }

                List<CoreCurve> contours = sources.SelectMany(s => s.Contours).ToList();
                TextReport report = TextCheck.Analyze(contours, mode, size.Size.DiameterMm, smallest, p.GapMm, p.EdgeMarginMm);
                List<Point2D> marks = report.ThinSpots.Concat(report.CloseSpots).ToList();
                string advice = Advice(report, size.Size.Name);

                if (!make)
                {
                    doc.BeginCommandGroup(Loc["undo.marks"]);
                    try
                    {
                        doc.ClearSelection();
                        CreateMarkers(doc, marks, "text.markProblem");
                    }
                    finally
                    {
                        doc.EndCommandGroup();
                    }

                    if (advice.Length == 0)
                    {
                        SetStatus("text.checkOk", report.LetterCount, Format(report.StrokeMm));
                    }
                    else
                    {
                        SetStatus("text.checkFound", report.LetterCount, Format(report.StrokeMm), advice);
                    }

                    return;
                }

                MethodKind kind = TextCheck.MethodFor(mode);
                MethodResult result = MethodRunner.Run(kind, contours, size.Size.DiameterMm, p, SizeTable);
                IReadOnlyList<PlacedStone> stones = result.Stones;
                if (stones.Count == 0)
                {
                    SetStatus("status.noStones");
                    return;
                }

                color.Color.TryGetRgb(out byte red, out byte green, out byte blue);
                doc.BeginCommandGroup(caption);
                try
                {
                    Shape group = CreateStoneGroup(doc, stones, size.Size, color.Name, red, green, blue, result.ConflictIndices, caption);
                    LiveStones.Save(group, new LiveRecipe
                    {
                        Method = kind.ToString().ToLowerInvariant(),
                        Size = size.Size.Name,
                        Color = color.Name,
                        Sources = sources.Select(s => s.Shape.StaticID).ToList(),
                        Parameters = p,
                    });
                    CreateMarkers(doc, marks, "text.markProblem");
                }
                finally
                {
                    doc.EndCommandGroup();
                }

                if (advice.Length == 0)
                {
                    SetStatus("text.done", stones.Count);
                }
                else
                {
                    SetStatus("text.doneWarn", stones.Count, advice);
                }
            });
        }

        /// <summary>Самый мелкий камень для способа «по середине»: из поля, а если пусто — самый мелкий в таблице.</summary>
        private double ThinDiameter(double main)
        {
            Dictionary<string, double> table = SizeTable;
            if (!string.IsNullOrWhiteSpace(Params.ThinSize) && table.TryGetValue(Params.ThinSize, out double chosen))
            {
                return chosen;
            }

            return table.Count > 0 ? System.Math.Min(main, table.Values.Min()) : main;
        }

        /// <summary>Советы по надписи одной строкой: увеличить, раздвинуть, объединить. Пусто — всё хорошо.</summary>
        private string Advice(TextReport report, string sizeName)
        {
            var parts = new List<string>();
            if (report.IsThin)
            {
                parts.Add(Loc.Format(
                    "text.thin", sizeName, Format(report.StrokeMm), Format(report.NeededMm),
                    report.Scale.ToString("0.0#", CultureInfo.CurrentCulture)));
            }

            if (report.ThinSpots.Count > 0)
            {
                parts.Add(Loc.Format("text.thinSpots", report.ThinSpots.Count));
            }

            if (report.Overlaps > 0)
            {
                parts.Add(Loc.Format("text.overlap", report.Overlaps));
            }
            else if (report.IsTight)
            {
                parts.Add(Loc.Format("text.tight", Format(report.ClosestMm), Format(report.ExtraSpacingMm)));
            }

            return string.Join(" ", parts);
        }
    }
}
