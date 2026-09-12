using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Strassio.Core.Stones
{
    /// <summary>Один набор таблицы размеров, например «Основной» или «Поставщик Х» (docs/SPEC.md, раздел 3.1).</summary>
    [DataContract]
    public sealed class StoneSet
    {
        [DataMember(Name = "name")]
        public string Name { get; set; } = string.Empty;

        [DataMember(Name = "sizes")]
        public List<StoneSize> Sizes { get; set; } = new List<StoneSize>();
    }
}
