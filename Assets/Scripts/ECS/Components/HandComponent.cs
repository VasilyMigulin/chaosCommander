using System.Collections.Generic;

namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Хранит entity-идентификаторы карт в руке игрока.
    /// </summary>
    public struct HandComponent
    {
        /// <summary>Лимит обычных (не-командир) карт в руке. Командир держится в своём слоте отдельно.</summary>
        public const int MaxNonCommanderCards = 6;

        /// <summary>Полный размер руки: MaxNonCommanderCards + командирский слот. Вычисляется от лимита
        /// выше, а не задаётся отдельным числом — иначе оба лимита снова можно рассинхронить (см. класс
        /// HandSpace: та же причина, по которой обычные карты считаются ОДНИМ местом, а не россыпью).</summary>
        public const int MaxHandSize = MaxNonCommanderCards + 1;

        public List<int> CardEntities;
        public int Count;
    }
}
