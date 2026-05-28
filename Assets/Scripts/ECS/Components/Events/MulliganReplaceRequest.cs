using System.Collections.Generic;

namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Запрос на замену карты во время мулигана.
    /// Вешается на entity карты которую игрок хочет заменить.
    /// </summary>
    public struct MulliganReplaceRequest
    { 
        public int[] CardEntities;
    }
}
