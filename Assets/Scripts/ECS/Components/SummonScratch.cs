using System.Collections.Generic;

namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Скрэтч-буфер сущностей, призванных в ТЕКУЩЕМ резолве способности. Заполняет SummonEffect
    /// (актив), читает RunResolveAbilityQueueSystem сразу после применения эффектов, чтобы положить
    /// призванных в AbilityResolvedNetEvent.SummonedEntities → снапшот → пассив применит к ним
    /// модификаторы призыва. Лежит в Components, т.к. и Ability, и Systems ссылаются на Components.
    ///
    /// Безопасность: ECS однопоточный, резолв одной способности за тик, эффекты синхронны →
    /// нет реентерабельности. RunResolveAbilityQueueSystem чистит буфер перед каждым резолвом.
    /// </summary>
    public static class SummonScratch
    {
        public static readonly List<int> Summoned = new List<int>();

        // Занятые клетки фронта в ТЕКУЩЕМ резолве: размещение идёт через MoveCardToBoardEvent/CreateCardEvent
        // (позиция выставится позже в кадре), поэтому несколько спавнов за один резолв (в т.ч. RepeatEffect+
        // одиночный спавн) считали бы клетки свободными повторно → коллизия. Резервируем тут. (ownerId,col) упак.
        static readonly HashSet<long> _claimedCells = new HashSet<long>();

        public static void Clear() { Summoned.Clear(); _claimedCells.Clear(); }
        public static void Add(int entity) => Summoned.Add(entity);

        static long CellKey(int ownerId, int col) => ((long)ownerId << 8) | (uint)(col & 0xFF);
        public static bool IsCellClaimed(int ownerId, int col) => _claimedCells.Contains(CellKey(ownerId, col));
        public static void ClaimCell(int ownerId, int col) => _claimedCells.Add(CellKey(ownerId, col));
    }
}
