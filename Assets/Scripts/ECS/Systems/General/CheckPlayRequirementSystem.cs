using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Каждый фрейм проверяет все карты с AbilityPlayRequirementContainerComponent:
    ///   - Если все требования к полю выполнены и PlayableTag ещё не стоит → добавляет PlayableTag,
    ///     публикует CardPlayableChangedEvent { IsPlayable = true }.
    ///   - Если хотя бы одно требование не выполнено, но PlayableTag стоит → снимает PlayableTag,
    ///     публикует CardPlayableChangedEvent { IsPlayable = false }.
    ///
    /// Карты без AbilityPlayRequirementContainerComponent считаются безусловно разыгрываемыми
    /// (ограничений к полю нет) — PlayableTag на них не трогается этой системой.
    /// </summary>
    public sealed class CheckPlayRequirementSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;

        readonly EcsFilterInject<Inc<AbilityPlayRequirementContainerComponent>> _filter = default;

        readonly EcsPoolInject<AbilityPlayRequirementContainerComponent> _reqPool     = default;
        readonly EcsPoolInject<PlayableTag>                              _playablePool = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var cardEntity in _filter.Value)
            {
                ref var container = ref _reqPool.Value.Get(cardEntity);

                bool allMet   = AllRequirementsMet(ref container, cardEntity);
                bool hasTag   = _playablePool.Value.Has(cardEntity);

                if (allMet && !hasTag)
                {
                    _playablePool.Value.Add(cardEntity);
                    GameEventBus.Publish(new CardPlayableChangedEvent
                    {
                        CardEntity  = cardEntity,
                        IsPlayable  = true,
                    });
                }
                else if (!allMet && hasTag)
                {
                    _playablePool.Value.Del(cardEntity);
                    GameEventBus.Publish(new CardPlayableChangedEvent
                    {
                        CardEntity  = cardEntity,
                        IsPlayable  = false,
                    });
                }
            }
        }

        bool AllRequirementsMet(ref AbilityPlayRequirementContainerComponent container, int cardEntity)
        {
            if (container.PlayRequirements == null || container.PlayRequirements.Count == 0)
                return true;

            foreach (var req in container.PlayRequirements)
                if (!req.IsSatisfied(_world.Value, cardEntity))
                    return false;

            return true;
        }
    }
}
