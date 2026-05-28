using Game.Core.Shared;
using Game.Core.Shared.Interface;
using System.Collections.Generic;

namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Применяется эффектом замешивания карты — хранит модель карты для инициализации
    /// и целевого игрока. CardModel хранится как object, чтобы не создавать зависимость
    /// сборки компонентов от сборки моделей.
    /// </summary>
    public struct ShuffleCardEffectComponent
    {
        public List<ShuffleCardData> Cards;
    }
}
