using Game.Core.Service;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using System.Collections.Generic;

namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Presence-правило: в мире должна быть хоть одна сущность, удовлетворяющая
    /// ВСЕМ полиморфным фильтрам (AND), в количестве >= MinCount.
    /// Композиция фильтров происходит на уровне списка — каждый фильтр проверяется
    /// на одном и том же кандидате, что даёт корректный AND-семантику
    /// (в отличие от независимых правил, которые дают ложный positive).
    /// </summary>
    [System.Serializable]
    public struct HasMatchingEntityRule : IAbilityRule
    {
        [UnityEngine.SerializeReference] public List<IEntityFilter> Filters;
        public int MinCount;

        public void AddComponent(EcsWorld world, Dictionary<string, int> entities)
        {
            if (entities.TryGetValue(EntityService.RULE_ENTITY, out int ruleEntity))
                world.GetPool<HasMatchingEntityRule>().Add(ruleEntity) = this;
        }
    }
}
