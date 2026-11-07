using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Strassio.Core.Stones
{
    /// <summary>Размер камня — название (например, "ss6") и диаметр в мм, со своим списком цветов.</summary>
    [DataContract]
    public sealed class StoneSize
    {
        [DataMember(Name = "name")]
        public string Name { get; set; } = string.Empty;

        [DataMember(Name = "diameterMm")]
        public double DiameterMm { get; set; }

        [DataMember(Name = "colors")]
        public List<StoneColor> Colors { get; set; } = new List<StoneColor>();

        /// <summary>
        /// Сверло для этого размера из каталога свёрл, мм; 0 — «авто» (ближайшее из каталога). Если сверло
        /// меньше отверстия, станок дофрезерует отверстие по кругу (docs/SPEC.md, раздел 11.1).
        /// </summary>
        [DataMember(Name = "drillMm")]
        public double DrillMm { get; set; }
    }
}
