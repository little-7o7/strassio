#nullable enable
using System;
using System.Collections.Generic;
using Corel.Interop.VGCore;
using Strassio.Core.Editing;
using Strassio.Core.Geometry;

namespace Strassio.Corel
{
    /// <summary>Страза в документе: фигура CorelDRAW и то, что о ней знает Strassio.Core.</summary>
    internal sealed class StoneShape
    {
        public StoneShape(Shape shape, DocStone stone, string size, string color)
        {
            Shape = shape;
            Stone = stone;
            Size = size;
            Color = color;
        }

        public Shape Shape { get; }

        public DocStone Stone { get; }

        public string Size { get; }

        public string Color { get; }
    }

    /// <summary>Что выделено: стразы и «инструменты» (линии и формы для режимов «по линии», «внутри формы»).</summary>
    internal sealed class StoneSelection
    {
        public List<StoneShape> Stones { get; } = new List<StoneShape>();

        /// <summary>Выделенные фигуры верхнего уровня, которые не стразы и не группы.</summary>
        public List<Shape> Tools { get; } = new List<Shape>();
    }

    /// <summary>
    /// Поиск и разметка страз в документе (docs/SPEC.md, разделы 3.3, 7). Страза — круг (эллипс с
    /// равными сторонами). У страз, созданных Strassio, есть невидимые метки CorelDRAW
    /// (Shape.Properties, имя "Strassio"): размер, цвет, «закреплена». У старых или скопированных
    /// из других файлов меток может не быть — тогда размер и цвет берутся из имени «ss6 Красный».
    ///
    /// Порядок по высоте: обход идёт сверху вниз (в CorelDRAW Shapes[1] — самый верхний), и чем
    /// раньше страза встретилась, тем она «выше».
    /// Вызывать при единицах документа — миллиметрах.
    /// </summary>
    internal static class StoneShapes
    {
        private const string Tag = "Strassio";
        private const int SizeId = 1;
        private const int ColorId = 2;
        private const int LockId = 3;

        /// <summary>Разница сторон, при которой эллипс ещё считается кругом-стразой.</summary>
        private const double CircleTolerance = 0.02;

        /// <summary>Помечает новую стразу: размер и цвет — чтобы потом узнавать её, даже если имя поменяют.</summary>
        public static void Mark(Shape shape, string size, string color)
        {
            Properties props = shape.Properties;
            props[Tag, SizeId] = size;
            props[Tag, ColorId] = color;
        }

        /// <summary>Новый цвет у стразы: имя «размер цвет» и метка цвета.</summary>
        public static void Rename(Shape shape, string size, string color)
        {
            shape.Name = StoneNames.Compose(size, color);
            Properties props = shape.Properties;
            props[Tag, SizeId] = size;
            props[Tag, ColorId] = color;
        }

        public static void SetLocked(Shape shape, bool locked)
        {
            Properties props = shape.Properties;
            if (locked)
            {
                props[Tag, LockId] = 1;
            }
            else if (props.Exists(Tag, LockId))
            {
                props.Delete(Tag, LockId);
            }
        }

        /// <summary>
        /// Стразы и инструменты из выделения. Выделенная группа (например, результат Strassio)
        /// просматривается целиком. Если выделено что-то внутри группы (Ctrl+щелчок), берётся как есть.
        /// </summary>
        public static StoneSelection FromSelection(Document doc, IEnumerable<string> knownSizes)
        {
            var result = new StoneSelection();
            ShapeRange selection = doc.SelectionRange;
            int order = 0;
            for (int i = 1; i <= selection.Count; i++)
            {
                Shape shape = selection[i];
                if (shape.Type == cdrShapeType.cdrGroupShape)
                {
                    CollectStones(shape.Shapes, knownSizes, result.Stones, ref order);
                }
                else if (!TryAddStone(shape, knownSizes, result.Stones, ref order))
                {
                    result.Tools.Add(shape);
                }
            }

            return result;
        }

        /// <summary>Все стразы на странице (на всех слоях, внутри групп тоже).</summary>
        public static List<StoneShape> FromPage(Page page, IEnumerable<string> knownSizes)
        {
            var stones = new List<StoneShape>();
            int order = 0;
            CollectStones(page.Shapes, knownSizes, stones, ref order);
            return stones;
        }

        private static void CollectStones(Shapes shapes, IEnumerable<string> knownSizes, List<StoneShape> into, ref int order)
        {
            int count = shapes.Count;
            for (int i = 1; i <= count; i++)
            {
                Shape shape = shapes[i];
                if (shape.Type == cdrShapeType.cdrGroupShape)
                {
                    CollectStones(shape.Shapes, knownSizes, into, ref order);
                }
                else
                {
                    TryAddStone(shape, knownSizes, into, ref order);
                }
            }
        }

        private static bool TryAddStone(Shape shape, IEnumerable<string> knownSizes, List<StoneShape> into, ref int order)
        {
            if (shape.Type != cdrShapeType.cdrEllipseShape)
            {
                return false;
            }

            shape.GetBoundingBox(out double x, out double y, out double w, out double h, false);
            if (w <= 0 || h <= 0 || Math.Abs(w - h) > CircleTolerance * Math.Max(w, h))
            {
                return false;
            }

            Properties props = shape.Properties;
            string size = ReadTag(props, SizeId);
            string color = ReadTag(props, ColorId);
            if (size.Length == 0 && StoneNames.TryParse(shape.Name, knownSizes, out string parsedSize, out string parsedColor))
            {
                size = parsedSize;
                color = parsedColor;
            }

            // Заблокированный средствами CorelDRAW объект тоже не трогаем — его и нельзя удалить.
            bool locked = props.Exists(Tag, LockId) || shape.Locked;

            // Выше — раньше в обходе: порядок идёт по убыванию.
            var stone = new DocStone(new Point2D(x + w / 2, y + h / 2), (w + h) / 2, -order, locked);
            order++;
            into.Add(new StoneShape(shape, stone, size, color));
            return true;
        }

        private static string ReadTag(Properties props, int id)
        {
            try
            {
                if (!props.Exists(Tag, id))
                {
                    return string.Empty;
                }

                // Значение приходит как COM VARIANT (dynamic); через object — без Microsoft.CSharp (правило 7).
                object value = props[Tag, id];
                return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}
