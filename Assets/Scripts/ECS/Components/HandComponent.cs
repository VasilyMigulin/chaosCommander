using System.Collections.Generic;

namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Хранит entity-идентификаторы карт в руке игрока.
    /// </summary>
    public struct HandComponent
    {
        /// <summary>Полный размер руки: 5 обычных карт + командир.</summary>
        public const int MaxHandSize = 6;

        /// <summary>Лимит обычных (не-командир) карт в руке. Командир держится в своём слоте отдельно.</summary>
        public const int MaxNonCommanderCards = 5;

        public List<int> CardEntities;
        public int Count;
    }
}
