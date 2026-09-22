using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Strassio.Core.Methods
{
    /// <summary>
    /// «Рецепт» живых страз (docs/SPEC.md, раздел 8, [NEW]): чем и по какой линии была сделана группа
    /// страз. Хранится невидимой меткой на самой группе в документе CorelDRAW. Пользователь правит
    /// исходную линию → «Обновить живые стразы» → группа строится заново тем же методом, камнем и
    /// параметрами. Одна строка JSON — так рецепт переживает сохранение и открытие файла .cdr.
    /// </summary>
    [DataContract]
    public sealed class LiveRecipe
    {
        /// <summary>Метод: "l1", "f3"… (как в настройках).</summary>
        [DataMember(Name = "method", Order = 0)]
        public string Method { get; set; } = string.Empty;

        [DataMember(Name = "size", Order = 1)]
        public string Size { get; set; } = string.Empty;

        [DataMember(Name = "color", Order = 2)]
        public string Color { get; set; } = string.Empty;

        /// <summary>Номера исходных фигур в документе (StaticID): линия или форма; для F7/F8 — и вторая линия.</summary>
        [DataMember(Name = "sources", Order = 3)]
        public List<int> Sources { get; set; } = new List<int>();

        [DataMember(Name = "params", Order = 4)]
        public MethodParameters Parameters { get; set; } = new MethodParameters();

        public string ToText()
        {
            using var stream = new MemoryStream();
            new DataContractJsonSerializer(typeof(LiveRecipe)).WriteObject(stream, this);
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        /// <summary>Разбирает рецепт; false — строка не рецепт (испорчена или от чужого плагина).</summary>
        public static bool TryParse(string? text, out LiveRecipe? recipe)
        {
            recipe = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            try
            {
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
                recipe = (LiveRecipe?)new DataContractJsonSerializer(typeof(LiveRecipe)).ReadObject(stream);
                return recipe != null && recipe.Method.Length > 0 && recipe.Sources != null && recipe.Sources.Count > 0;
            }
            catch (Exception)
            {
                recipe = null;
                return false;
            }
        }

        /// <summary>Вид метода по записи "l1"… ; false — такого метода (уже) нет.</summary>
        public bool TryGetKind(out MethodKind kind) =>
            Enum.TryParse(Method, ignoreCase: true, out kind) && Enum.IsDefined(typeof(MethodKind), kind);
    }
}
