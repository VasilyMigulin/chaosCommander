using System.Collections.Generic;

namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Хранит entity-идентификаторы карт в колоде игрока (вершина = [0]).
    /// </summary>
    public struct DeckComponent
    {
        public List<int> CardEntities;
        public int Count;
    }
}
