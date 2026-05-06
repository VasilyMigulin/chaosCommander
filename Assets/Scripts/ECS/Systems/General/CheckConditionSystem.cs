using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Каждый фрейм проверяет все способности с AbilityConditionContainerComponent:
    ///   - Если все условия выполнены и ReadyTag ещё не стоит  → добавляет ReadyTag,
    ///     публикует AbilityReadyEvent.
    ///   - Если хотя бы одно условие не выполнено, но ReadyTag стоит → снимает ReadyTag,
    ///     публикует AbilityNotReadyEvent.
    ///
    /// Условия реализуют IAbilityCondition.Check() и могут обращаться к MatchTracker
    /// или любому другому источнику данных — система об этом не знает.
    /// </summary>
    public sealed class CheckConditionSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<AbilityConditionContainerComponent>> _allFilter = default; 

        readonly EcsPoolInject<AbilityConditionContainerComponent> _condPool = default;
        readonly EcsPoolInject<ReadyTag> _readyPool = default;
        readonly EcsPoolInject<OwnerCardComponent> _ownerCardPool = default;

        int _abilityEntity;

        public void Run(IEcsSystems systems)
        {
            foreach (var abilityEntity in _allFilter.Value)
            {
                _abilityEntity = abilityEntity;

                ref var container = ref _condPool.Value.Get(abilityEntity);

                bool allMet = AllConditionsMet(ref container);
                bool hasReady = _readyPool.Value.Has(abilityEntity);

                int cardEntity = _ownerCardPool.Value.Has(abilityEntity)
                    ? _ownerCardPool.Value.Get(abilityEntity).CardEntity
                    : -1;

                if (allMet && !hasReady)
                {
                    _readyPool.Value.Add(abilityEntity);
                    GameEventBus.Publish(new AbilityReadyEvent
                    {
                        AbilityEntity = abilityEntity,
                        CardEntity    = cardEntity,
                    });
                }
                else if (!allMet && hasReady)
                {
                    _readyPool.Value.Del(abilityEntity);
                    GameEventBus.Publish(new AbilityNotReadyEvent
                    {
                        AbilityEntity = abilityEntity,
                        CardEntity    = cardEntity,
                    });
                }
            }
        }

        bool AllConditionsMet(ref AbilityConditionContainerComponent container)
        {
            if (container.AbilityConditions == null || container.AbilityConditions.Count == 0)
                return true;

            foreach (var condition in container.AbilityConditions)
                if (!condition.CheckCondition(_world.Value, _abilityEntity))
                    return false;

            return true;
        }
    }
}
