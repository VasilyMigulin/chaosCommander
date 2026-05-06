using System.Collections.Generic;

namespace Game.Core.Ecs.Components
{
    public enum MulliganPhase
    {
        Offering,   // карты предложены, игрок выбирает замену
        Done        // игрок подтвердил
    }

    /// <summary>
    /// Вешается на entity игрока на время мулигана.
    /// </summary>
    public struct MulliganComponent
    {
        public MulliganPhase Phase;

        /// <summary>Entity карт предложенных игроку.</summary>
        public List<int> OfferedCards;

        /// <summary>Максимум карт которые можно заменить (1 для 1-го, 2 для 2-го).</summary>
        public int MaxReplacements;

        /// <summary>Сколько замен уже использовано.</summary>
        public int ReplacementsUsed;
    }
}
