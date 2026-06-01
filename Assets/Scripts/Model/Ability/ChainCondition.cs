using Leopotam.EcsLite;
using Game.Core.Shared.Interface;

namespace Game.Core.Model.Ability
{
    /// <summary>
    /// Условие, проверяемое перед запуском эффектов шага цепочки.
    /// Если возвращает false — шаг пропускается (но цепочка движется дальше).
    /// Реализует IChainCondition — ECS-слой держит ссылку только на интерфейс.
    /// </summary>
    [System.Serializable]
    public abstract class ChainCondition : IChainCondition
    {
        /// <summary>
        /// abilityEntity = entity способности, sourceCard = карта-источник,
        /// ownerPlayerId = id владельца, ownerPlayerEntity = entity игрока (или -1).
        /// </summary>
        public abstract bool Evaluate(EcsWorld world,
                                      int abilityEntity,
                                      int sourceCard,
                                      int ownerPlayerId,
                                      int ownerPlayerEntity);
    }
}
