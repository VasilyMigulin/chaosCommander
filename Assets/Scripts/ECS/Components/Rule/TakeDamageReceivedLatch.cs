using Game.Core.Service;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using System.Collections.Generic;

namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Latch-правило: накопительный счётчик урона игрока-владельца способности.
    /// При суммарном >= Threshold защёлкивается до конца матча (PassedTag не снимается).
    /// Чекер подписывается на ECS-event TakeDamageEvent (не GameEventBus).
    /// </summary>
    [System.Serializable]
    public struct TakeDamageReceivedLatch : IAbilityRule
    {
        public int Threshold;
        public int Accumulated;

        public void AddComponent(EcsWorld world, Dictionary<string, int> entities)
        {
            if (entities.TryGetValue(EntityService.RULE_ENTITY, out int ruleEntity))
                world.GetPool<TakeDamageReceivedLatch>().Add(ruleEntity) = this;
        }
    }
}
