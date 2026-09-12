using System.Runtime.Serialization;

namespace Strassio.Core.Stones
{
    /// <summary>Цвет камня — название и RGB (docs/SPEC.md, раздел 3.2).</summary>
    [DataContract]
    public sealed class StoneColor
    {
        [DataMember(Name = "name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>RGB в виде "#RRGGBB".</summary>
        [DataMember(Name = "rgb")]
        public string Rgb { get; set; } = "#000000";
    }
}
