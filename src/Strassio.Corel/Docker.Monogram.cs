#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Corel.Interop.VGCore;
using Strassio.Core.Geometry;
using Strassio.Core.Methods;
using Strassio.Core.Motifs;
using Strassio.Core.Vector;
using CoreCurve = Strassio.Core.Geometry.Curve;

namespace Strassio.Corel
{
    /// <summary>
    /// Монограмма на вкладке «Текст»: 1–3 буквы любым шрифтом из системы, при трёх буквах средняя крупнее,
    /// и рамка вокруг — круг, овал, ромб, скруглённая или щит. Буквы заполняются выбранным выше способом,
    /// рамка — одним рядом. Буквы и рамка остаются обычными кривыми: их можно править, стразы обновятся
    /// сами («Живые стразы»).
    /// </summary>
    public partial class Docker
    {
        private const int MaxMonogramLetters = 3;

        private double monogramHeightMm = 40;

        private MonogramFrame SelectedMonogramFrame =>
            Enum.TryParse((MonogramFrameCombo.SelectedItem as ChoiceOption)?.Value, out MonogramFrame frame) ? frame : MonogramFrame.Circle;

        /// <summary>Рамки — на текущем языке; шрифты — один раз (список шрифтов от языка не зависит).</summary>
        private void RebuildMonogram()
        {
            var frames = new List<ChoiceOption>
            {
                new ChoiceOption(nameof(MonogramFrame.Circle), Loc["text.monogram.frame.circle"]),
                new ChoiceOption(nameof(MonogramFrame.Oval), Loc["text.monogram.frame.oval"]),
                new ChoiceOption(nameof(MonogramFrame.Diamond), Loc["text.monogram.frame.diamond"]),
                new ChoiceOption(nameof(MonogramFrame.Rounded), Loc["text.monogram.frame.rounded"]),
                new ChoiceOption(nameof(MonogramFrame.Shield), Loc["text.monogram.frame.shield"]),
                new ChoiceOption(nameof(MonogramFrame.None), Loc["text.monogram.frame.none"]),
            };
            string? current = (MonogramFrameCombo.SelectedItem as ChoiceOption)?.Value;
            MonogramFrameCombo.ItemsSource = frames;
            MonogramFrameCombo.SelectedItem = frames.FirstOrDefault(f => f.Value == current) ?? frames[0];

            if (MonogramFontCombo.ItemsSource == null)
            {
                List<string> fonts;
                try
                {
                    fonts = Fonts.SystemFontFamilies.Select(f => f.Source).Distinct().OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
                }
                catch (Exception)
                {
                    fonts = new List<string>();
                }

                MonogramFontCombo.ItemsSource = fonts;
                MonogramFontCombo.SelectedItem = fonts.FirstOrDefault(f => f == "Times New Roman") ?? fonts.FirstOrDefault();
            }

            MonogramHeightBox.Text = FormatLength(monogramHeightMm);
        }

        private void MonogramHeightBox_TextChanged(object sender, TextChangedEventArgs e) =>
            monogramHeightMm = ReadLength(MonogramHeightBox, 5, 2000, monogramHeightMm);

        private void Monogram_Click(object sender, RoutedEventArgs e)
        {
            if (!(SizeCombo.SelectedItem is SizeOption size) || !(ColorList.SelectedItem is ColorOption color))
            {
                SetStatus("status.noStone");
                return;
            }

            string letters = new string(MonogramLettersBox.Text.Where(c => !char.IsWhiteSpace(c)).ToArray());
            if (letters.Length == 0 || letters.Length > MaxMonogramLetters)
            {
                SetStatus("text.monogram.letters");
                return;
            }

            string font = MonogramFontCombo.SelectedItem as string ?? MonogramFontCombo.Text;
            MonogramFrame frame = SelectedMonogramFrame;
            TextMode mode = SelectedTextMode;
            MethodParameters letterParams = TextCheck.ParametersFor(mode, Params);
            MethodParameters frameParams = Params;
            double d = size.Size.DiameterMm;
            string caption = Loc.Format("undo.monogram", letters);
            color.Color.TryGetRgb(out byte red, out byte green, out byte blue);

            RunInDocument(null, doc =>
            {
                doc.BeginCommandGroup(caption);
                try
                {
                    // Буквы шрифта — через временный текст: CorelDRAW сам знает все шрифты системы.
                    var glyphs = new List<IReadOnlyList<CoreCurve>>();
                    foreach (char ch in letters)
                    {
                        List<CoreCurve> contours = LetterContours(doc, ch.ToString(), font);
                        if (contours.Count == 0)
                        {
                            SetStatus("text.monogram.noGlyph", ch.ToString(), font);
                            return;
                        }

                        glyphs.Add(contours);
                    }

                    var options = new MonogramOptions
                    {
                        Frame = frame,
                        LetterHeightMm = monogramHeightMm,
                        // Между буквами и до рамки — место для камня и зазор, чтобы ряды не слиплись.
                        LetterGapMm = d + Params.GapMm,
                        FrameGapMm = 1.5 * d + Params.GapMm,
                    };
                    MonogramLayout layout = Monogram.Arrange(glyphs, options, new Point2D(doc.ActivePage.CenterX, doc.ActivePage.CenterY));

                    var made = new List<object>();
                    var letterShapes = new List<Shape>();
                    for (int i = 0; i < layout.Letters.Count; i++)
                    {
                        Shape letter = CreateCurveShape(doc, layout.Letters[i], null);
                        letter.Name = Loc.Format("text.monogram.letterName", letters[i].ToString());
                        letterShapes.Add(letter);
                        made.Add(letter);
                    }

                    List<CoreCurve> letterContours = layout.Letters.SelectMany(l => l).ToList();
                    MethodKind kind = TextCheck.MethodFor(mode);
                    MethodResult letterResult = MethodRunner.Run(kind, letterContours, d, letterParams, SizeTable);
                    int count = 0;
                    if (letterResult.Stones.Count > 0)
                    {
                        Shape group = CreateStoneGroup(doc, letterResult.Stones, size.Size, color.Name, red, green, blue, letterResult.ConflictIndices, caption);
                        LiveStones.Save(group, new LiveRecipe
                        {
                            Method = kind.ToString().ToLowerInvariant(),
                            Size = size.Size.Name,
                            Color = color.Name,
                            Sources = letterShapes.Select(s => s.StaticID).ToList(),
                            Parameters = letterParams,
                        });
                        made.Add(group);
                        count += letterResult.Stones.Count;
                    }

                    if (layout.Frame != null)
                    {
                        Shape frameShape = CreateCurveShape(doc, new[] { layout.Frame }, null);
                        frameShape.Name = Loc["text.monogram.frameName"];
                        made.Add(frameShape);
                        MethodResult frameResult = MethodRunner.Run(MethodKind.L1, new[] { layout.Frame }, d, frameParams, SizeTable);
                        if (frameResult.Stones.Count > 0)
                        {
                            Shape group = CreateStoneGroup(doc, frameResult.Stones, size.Size, color.Name, red, green, blue, frameResult.ConflictIndices, caption);
                            LiveStones.Save(group, new LiveRecipe
                            {
                                Method = nameof(MethodKind.L1).ToLowerInvariant(),
                                Size = size.Size.Name,
                                Color = color.Name,
                                Sources = new List<int> { frameShape.StaticID },
                                Parameters = frameParams,
                            });
                            made.Add(group);
                            count += frameResult.Stones.Count;
                        }
                    }

                    // Всё созданное выделено — монограмму можно сразу перетащить на место.
                    object[] all = made.ToArray();
                    doc.CreateShapeRangeFromArray(ref all).CreateSelection();
                    SetStatus("text.monogram.done", count, Format(layout.WidthMm), Format(layout.HeightMm));
                }
                finally
                {
                    doc.EndCommandGroup();
                }
            });
        }

        /// <summary>Контуры одной буквы: временный художественный текст → кривая → сразу удаляем.</summary>
        private static List<CoreCurve> LetterContours(Document doc, string letter, string font)
        {
            Shape text = doc.ActiveLayer.CreateArtisticText(0, 0, letter, Font: font, Size: 200f);
            try
            {
                global::Corel.Interop.VGCore.Curve? curve = GetCurveOf(text);
                List<CoreCurve> contours = curve == null ? new List<CoreCurve>() : ReadAllSubPaths(curve);
                if (contours.Count == 0)
                {
                    // Запасной путь: у некоторых версий у текста нет DisplayCurve — переводим копию в кривые.
                    text.ConvertToCurves();
                    curve = GetCurveOf(text);
                    contours = curve == null ? new List<CoreCurve>() : ReadAllSubPaths(curve);
                }

                return contours;
            }
            finally
            {
                text.Delete();
            }
        }
    }
}
