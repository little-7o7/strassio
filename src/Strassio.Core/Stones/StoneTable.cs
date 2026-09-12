using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Strassio.Core.Stones
{
    /// <summary>
    /// Таблица камней целиком — один или несколько наборов размеров и цветов (docs/SPEC.md, раздел 3.2).
    /// Хранится в %APPDATA%\Strassio\stones.json. Сериализация через встроенный DataContractJsonSerializer
    /// (CLAUDE.md, правило 7 — минимум сторонних библиотек в аддоне).
    /// </summary>
    [DataContract]
    public sealed class StoneTable
    {
        [DataMember(Name = "sets")]
        public List<StoneSet> Sets { get; set; } = new List<StoneSet>();

        public string ToJson()
        {
            var serializer = new DataContractJsonSerializer(typeof(StoneTable));
            using var stream = new MemoryStream();
            serializer.WriteObject(stream, this);
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        public static StoneTable FromJson(string json)
        {
            var serializer = new DataContractJsonSerializer(typeof(StoneTable));
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            return (StoneTable)serializer.ReadObject(stream)!;
        }

        public void Save(string filePath)
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(filePath, ToJson(), Encoding.UTF8);
        }

        public static StoneTable Load(string filePath) => FromJson(File.ReadAllText(filePath, Encoding.UTF8));
    }
}
