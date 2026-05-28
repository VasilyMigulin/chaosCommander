using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Mono;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Обрабатывает существ с DeadTag:
    ///   1. Добавляет DieEvent (→ RunAbilityDieSystem запускает предсмертные способности).
    ///   2. Обычные существа: снимает BoardTag, добавляет GraveTag, играет анимацию смерти.
    ///   3. Командир: НЕ уходит на кладбище — возвращается в руку с кулдауном 1 ход
    ///      (визуал деспавнится, чтобы корректно развернуться повторно).
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

        // Командир
        readonly EcsPoolInject<CommanderTag> _commanderPool = default;
        readonly EcsPoolInject<CommanderCooldownComponent> _commanderCdPool = default;
        readonly EcsPoolInject<HandTag> _handTagPool = default;
        readonly EcsPoolInject<HandComponent> _handPool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<DeadTag> _deadPool = default;
        readonly EcsPoolInject<BoardPositionComponent> _posPool = default;
        readonly EcsPoolInject<ViewSpawnedTag> _spawnedPool = default;
        readonly EcsPoolInject<HealthComponent> _hpPool = default;
        readonly EcsPoolInject<SpeedComponent> _speedPool = default;

        readonly EcsFilterInject<Inc<PlayerComponent, HandComponent>> _playerFilter = default;

        const int CommanderDeathCooldown = 1;

        public void Run(IEcsSystems systems)
        {
            foreach (var entity in _filter.Value)
            {
                if (_selectPool.Value.Has(entity))
                    _selectPool.Value.Del(entity);

                _boardPool.Value.Del(entity);

                // Предсмертные способности срабатывают в обоих случаях
                if (!_diePool.Value.Has(entity))
                    _diePool.Value.Add(entity);

                bool isCommander = _commanderPool.Value.Has(entity);

                int ownerId = _ownerPool.Value.Has(entity) ? _ownerPool.Value.Get(entity).OwnerId : -1;

                if (isCommander)
                    ReturnCommanderToHand(entity, ownerId);
                else
                    SendToGrave(entity);

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

        private void SendToGrave(int entity)
        {
            _gravePool.Value.Add(entity);

            if (_viewPool.Value.Has(entity))
            {
                ref var vr = ref _viewPool.Value.Get(entity);
                vr.View?.GetComponent<CreatureView>()?.PlayDeath();
            }
        }

        private void ReturnCommanderToHand(int entity, int ownerId)
        {
            // Деспавним визуал, чтобы повторное развёртывание создало новый инстанс
            if (_viewPool.Value.Has(entity))
            {
                ref var vr = ref _viewPool.Value.Get(entity);
                if (vr.View != null)
                    Object.Destroy(vr.View);
                vr.View = null;
            }
            if (_spawnedPool.Value.Has(entity))
                _spawnedPool.Value.Del(entity);

            if (_posPool.Value.Has(entity))
                _posPool.Value.Del(entity);

            // Снимаем «мёртвость» — командир жив, но недоступен (кулдаун)
            if (_deadPool.Value.Has(entity))
                _deadPool.Value.Del(entity);

            // Восстанавливаем статы к базовым
            if (_hpPool.Value.Has(entity))
            {
                ref var hp = ref _hpPool.Value.Get(entity);
                hp.Max     = hp.BaseMax;
                hp.Current = hp.BaseMax;
            }
            if (_speedPool.Value.Has(entity))
            {
                ref var sp = ref _speedPool.Value.Get(entity);
                sp.Remaining = sp.Max;
            }

            // Возвращаем в руку (для повторного розыгрыша) и ставим кулдаун
            if (!_handTagPool.Value.Has(entity))
                _handTagPool.Value.Add(entity);

            int playerEntity = FindPlayerEntity(ownerId);
            if (playerEntity >= 0)
            {
                ref var hand = ref _handPool.Value.Get(playerEntity);
                if (hand.CardEntities != null && !hand.CardEntities.Contains(entity))
                {
                    hand.CardEntities.Insert(0, entity);
                    hand.Count = hand.CardEntities.Count;
                }
            }

            if (!_commanderCdPool.Value.Has(entity))
                _commanderCdPool.Value.Add(entity);
            _commanderCdPool.Value.Get(entity).TurnsRemaining = CommanderDeathCooldown;

            GameEventBus.Publish(new CommanderOnCooldownUIEvent
            {
                CardEntity    = entity,
                CooldownTurns = CommanderDeathCooldown,
            });
        }

        private int FindPlayerEntity(int playerId)
        {
            foreach (var pe in _playerFilter.Value)
                if (_playerPool.Value.Get(pe).PlayerId == playerId) return pe;
            return -1;
        }
    }
}
