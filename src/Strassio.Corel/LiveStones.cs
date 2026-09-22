#nullable enable
using System;
using Corel.Interop.VGCore;
using Strassio.Core.Methods;

namespace Strassio.Corel
{
    /// <summary>
    /// «Живые» стразы (docs/SPEC.md, раздел 8): рецепт группы (метод, камень, параметры, номера
    /// исходных фигур) хранится невидимой меткой на самой группе — Shape.Properties, как и метки
    /// страз, поэтому он сохраняется вместе с файлом .cdr.
    /// </summary>
    internal static class LiveStones
    {
        private const string Tag = "Strassio";
        private const int RecipeId = 10;

        public static void Save(Shape group, LiveRecipe recipe)
        {
            Properties props = group.Properties;
            props[Tag, RecipeId] = recipe.ToText();
        }

        /// <summary>Рецепт группы; null — это не группа Strassio или рецепт испорчен.</summary>
        public static LiveRecipe? Read(Shape shape)
        {
            try
            {
                Properties props = shape.Properties;
                if (!props.Exists(Tag, RecipeId))
                {
                    return null;
                }

                object value = props[Tag, RecipeId];
                return LiveRecipe.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), out LiveRecipe? recipe)
                    ? recipe
                    : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Исходная фигура по её номеру — на любой странице документа; null — её удалили.</summary>
        public static Shape? FindSource(Document doc, int staticId)
        {
            Pages pages = doc.Pages;
            for (int i = 1; i <= pages.Count; i++)
            {
                try
                {
                    Shape found = pages[i].Shapes.FindShape(StaticID: staticId, Recursive: true);
                    if (found != null)
                    {
                        return found;
                    }
                }
                catch (Exception)
                {
                    // На этой странице нет — ищем дальше.
                }
            }

            return null;
        }
    }
}
