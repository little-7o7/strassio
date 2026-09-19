using System.Collections.Generic;

namespace Strassio.Core.Placement
{
    /// <summary>Что делать со стразой, которая мешает более важной (docs/SPEC.md, раздел 6.4).</summary>
    public enum IntersectionAction
    {
        /// <summary>Удалить стразу ниже приоритетом.</summary>
        Remove,

        /// <summary>Сдвинуть вдоль своего ряда в пределах лимита; не получилось — удалить.</summary>
        Shift,

        /// <summary>Ничего не менять, только сообщить, какие стразы конфликтуют.</summary>
        ShowOnly,
    }

    /// <summary>Параметры исправления пересечений (docs/SPEC.md, раздел 6).</summary>
    public sealed class IntersectionFixOptions
    {
        public IntersectionAction Action { get; set; } = IntersectionAction.Remove;

        /// <summary>Минимальный зазор между стразами (край в край), мм.</summary>
        public double MinGapMm { get; set; } = 0.1;

        /// <summary>Допуск: такое маленькое «наложение» конфликтом не считается, мм.</summary>
        public double ToleranceMm { get; set; } = 0.01;

        /// <summary>Для «Сдвинуть»: насколько самое большее можно отодвинуть стразу вдоль её ряда, мм.</summary>
        public double MaxShiftMm { get; set; } = 1.0;
    }

    /// <summary>Итог исправления пересечений. Все номера — индексы в ИСХОДНОМ списке страз, по возрастанию.</summary>
    public sealed class IntersectionFixResult
    {
        public IntersectionFixResult(
            IReadOnlyList<PlacedStone> stones,
            IReadOnlyList<int> removedIndices,
            IReadOnlyList<int> shiftedIndices,
            IReadOnlyList<int> conflictIndices)
        {
            Stones = stones;
            RemovedIndices = removedIndices;
            ShiftedIndices = shiftedIndices;
            ConflictIndices = conflictIndices;
        }

        /// <summary>Итоговые стразы в исходном порядке (убранные пропущены, сдвинутые — на новом месте).</summary>
        public IReadOnlyList<PlacedStone> Stones { get; }

        public IReadOnlyList<int> RemovedIndices { get; }

        public IReadOnlyList<int> ShiftedIndices { get; }

        /// <summary>Все стразы, которые до исправления с кем-то накладывались (обе из каждой пары) — их подсвечивает «Только показать».</summary>
        public IReadOnlyList<int> ConflictIndices { get; }
    }
}
