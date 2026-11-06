using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Strassio.Core.Geometry;

namespace Strassio.Core.Motifs
{
    /// <summary>Одна страза мотива: где она относительно середины мотива, какого размера и цвета.</summary>
    [DataContract]
    public sealed class MotifStone
    {
        public MotifStone()
        {
        }

        public MotifStone(double x, double y, double diameterMm, string size, string color, string rgb)
        {
            X = x;
            Y = y;
            DiameterMm = diameterMm;
            Size = size;
            Color = color;
            Rgb = rgb;
        }

        /// <summary>Сдвиг от середины мотива, мм (+X — вдоль линии, +Y — «верх» мотива).</summary>
        [DataMember(Name = "x")]
        public double X { get; set; }

        [DataMember(Name = "y")]
        public double Y { get; set; }

        [DataMember(Name = "d")]
        public double DiameterMm { get; set; }

        /// <summary>Имя размера («ss10»).</summary>
        [DataMember(Name = "size")]
        public string Size { get; set; } = string.Empty;

        /// <summary>Имя цвета («Кристалл»).</summary>
        [DataMember(Name = "color")]
        public string Color { get; set; } = string.Empty;

        /// <summary>Цвет заливки «#RRGGBB» — каким кругом рисовать, даже если цвета нет в таблице.</summary>
        [DataMember(Name = "rgb")]
        public string Rgb { get; set; } = "#C0C0C0";

        public Point2D Offset => new Point2D(X, Y);
    }

    /// <summary>
    /// Мотив (docs/SPEC.md, раздел 9): готовый элемент из страз — цветок, листик, завиток. Камни хранятся
    /// относительно середины мотива, поэтому мотив можно ставить куда угодно и поворачивать.
    /// </summary>
    [DataContract]
    public sealed class Motif
    {
        [DataMember(Name = "name")]
        public string Name { get; set; } = string.Empty;

        [DataMember(Name = "stones")]
        public List<MotifStone> Stones { get; set; } = new List<MotifStone>();

        /// <summary>Ширина мотива по краям камней, мм.</summary>
        public double Width => Stones.Count == 0 ? 0 : Stones.Max(s => s.X + s.DiameterMm / 2) - Stones.Min(s => s.X - s.DiameterMm / 2);

        /// <summary>Высота мотива по краям камней, мм.</summary>
        public double Height => Stones.Count == 0 ? 0 : Stones.Max(s => s.Y + s.DiameterMm / 2) - Stones.Min(s => s.Y - s.DiameterMm / 2);

        /// <summary>Самый крупный камень мотива, мм.</summary>
        public double LargestStoneMm => Stones.Count == 0 ? 0 : Stones.Max(s => s.DiameterMm);

        /// <summary>
        /// Мотив из страз, выделенных в документе (координаты листа, мм): середина габарита становится
        /// серединой мотива.
        /// </summary>
        public static Motif FromStones(string name, IEnumerable<MotifStone> stones)
        {
            List<MotifStone> list = stones.ToList();
            if (list.Count == 0)
            {
                return new Motif { Name = name };
            }

            double cx = (list.Min(s => s.X - s.DiameterMm / 2) + list.Max(s => s.X + s.DiameterMm / 2)) / 2;
            double cy = (list.Min(s => s.Y - s.DiameterMm / 2) + list.Max(s => s.Y + s.DiameterMm / 2)) / 2;
            return new Motif
            {
                Name = name,
                Stones = list.Select(s => new MotifStone(Round(s.X - cx), Round(s.Y - cy), s.DiameterMm, s.Size, s.Color, s.Rgb)).ToList(),
            };
        }

        public string ToJson()
        {
            var serializer = new DataContractJsonSerializer(typeof(Motif));
            using var stream = new MemoryStream();
            serializer.WriteObject(stream, this);
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        public static Motif FromJson(string json)
        {
            var serializer = new DataContractJsonSerializer(typeof(Motif));
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var motif = (Motif)serializer.ReadObject(stream)!;
            motif.Name ??= string.Empty;
            motif.Stones ??= new List<MotifStone>();
            return motif;
        }

        private static double Round(double v) => Math.Round(v, 4);
    }
}
