using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Mono;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Обрабатывает существ с DeadTag:
    ///   1. Добавляет DieEvent (→ RunAbilityDieSystem запускает предсмертные способности).
    ///   2. Снимает BoardTag, добавляет GraveTag.
    ///   3. Запускает анимацию смерти.
    ///   4. Публикует CreatureDiedEvent и DeathTrackedEvent.
    /// </summary>
    public sealed class DieSystem : IEcsRunSystem
    {
        readonly EcsFilterInject<Inc<DeadTag, BoardTag, CreatureTag>> _filter = default;

        readonly EcsPoolInject<BoardTag> _boardPool = default;
        readonly EcsPoolInject<GraveTag> _gravePool = default;
        readonly EcsPoolInject<DieEvent> _diePool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<SelectTag> _selectPool = default;
        readonly EcsPoolInject<ViewRefComponent> _viewPool = default;
        readonly EcsPoolInject<CardModelComponent> _modelPool = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var entity in _filter.Value)
            {
                if (_selectPool.Value.Has(entity))
                    _selectPool.Value.Del(entity);

                _boardPool.Value.Del(entity);
                _gravePool.Value.Add(entity);

                if (!_diePool.Value.Has(entity))
                    _diePool.Value.Add(entity);

                if (_viewPool.Value.Has(entity))
                {
                    ref var vr = ref _viewPool.Value.Get(entity);
                    vr.View?.GetComponent<CreatureView>()?.PlayDeath();
                }

                int ownerId = _ownerPool.Value.Has(entity) ? _ownerPool.Value.Get(entity).OwnerId : -1;

                GameEventBus.Publish(new CreatureDiedEvent { CreatureEntity = entity, PlayerId = ownerId });

                // Уведомляем MatchTracker
                string cardName = string.Empty;
                int modelId = -1;
                if (_modelPool.Value.Has(entity))
                {
                    ref var m = ref _modelPool.Value.Get(entity);
                    cardName = m.CardName;
                    modelId  = m.ModelId;
                }
                GameEventBus.Publish(new DeathTrackedEvent
                {
                    CreatureEntity = entity,
                    OwnerId        = ownerId,
                    KillerPlayerId = -1,   // TODO: передавать через AttackHitEvent если нужно
                    CardName       = cardName,
                    ModelId        = modelId,
                });

                GameEventBus.Publish(new InputBlockedEvent());
            }
        }
    }
}
