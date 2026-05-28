using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Обрабатывает BurnEvent: перевешивает карту DeckTag → GraveTag, удаляет событие.
    /// Карты с TokenTag не попадают на кладбище — entity удаляется.
    /// </summary>
    public sealed class BurnCardSystem : IEcsRunSystem
    {
        readonly EcsPoolInject<BurnEvent> _burnPool = default;
        readonly EcsPoolInject<DeckTag> _deckTagPool = default;
        readonly EcsPoolInject<HandTag> _handTagPool = default;
        readonly EcsPoolInject<GraveTag> _graveTagPool = default;
        readonly EcsPoolInject<TokenTag> _tokenTagPool = default;
        readonly EcsPoolInject<AbilityContainerComponent> _abilityContainerPool = default;
        readonly EcsPoolInject<AbilityConditionContainerComponent> _conditionContainerPool = default;
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<BurnEvent>> _filter = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var entity in _filter.Value)
            {
                CleanupAbilities(entity);

                if (_tokenTagPool.Value.Has(entity))
                {
                    // Токены исчезают — удаляем entity полностью
                    _world.Value.DelEntity(entity);
                    continue;
                }

                if (_deckTagPool.Value.Has(entity))
                    _deckTagPool.Value.Del(entity);

                if (_handTagPool.Value.Has(entity))
                    _handTagPool.Value.Del(entity);

                if (!_graveTagPool.Value.Has(entity))
                    _graveTagPool.Value.Add(entity);

                _burnPool.Value.Del(entity);
            }
        }

        void CleanupAbilities(int entity)
        {
            if (!_abilityContainerPool.Value.Has(entity)) return;

            ref var abilityContainer = ref _abilityContainerPool.Value.Get(entity);
            if (abilityContainer.AbilityEntities == null) return;

            foreach (var abilityEntity in abilityContainer.AbilityEntities)
            {
                if (_conditionContainerPool.Value.Has(abilityEntity))
                {
                    ref var condContainer = ref _conditionContainerPool.Value.Get(abilityEntity);
                    if (condContainer.AbilityConditions != null)
                    {
                        foreach (var condition in condContainer.AbilityConditions)
                            condition.Dispose();
                    }
                }
            }
        }
    }
}
