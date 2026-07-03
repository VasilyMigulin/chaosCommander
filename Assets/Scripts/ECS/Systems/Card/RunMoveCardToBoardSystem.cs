using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// Переносит карту на борд по MoveCardToBoardEvent: снимает зональные теги, убирает из руки
    /// владельца, ставит BoardTag и BoardPositionComponent (если Row != -1; чары — без позиции).
    /// Для существ дальше SpawnCreatureViewSystem создаёт визуал. Событие снимает _delSystems.
    /// </summary>
    public sealed class RunMoveCardToBoardSystem : IEcsRunSystem
    {
        readonly EcsFilterInject<Inc<MoveCardToBoardEvent>> _filter = default;
        readonly EcsPoolInject<MoveCardToBoardEvent>   _movePool     = default;
        readonly EcsPoolInject<HandTag>                _handTagPool  = default;
        readonly EcsPoolInject<DeckTag>                _deckTagPool  = default;
        readonly EcsPoolInject<GraveTag>               _graveTagPool = default;
        readonly EcsPoolInject<BoardTag>               _boardTagPool = default;
        readonly EcsPoolInject<BoardPositionComponent> _posPool      = default;
        readonly EcsPoolInject<OwnerComponent>         _ownerPool    = default;
        readonly EcsPoolInject<HandComponent>          _handPool     = default;
        readonly EcsPoolInject<PlayerComponent>        _playerPool   = default;
        readonly EcsPoolInject<DeadTag>                _deadTagPool  = default;
        readonly EcsPoolInject<HealthComponent>        _hpPool       = default;
        readonly EcsPoolInject<SpeedComponent>         _speedPool    = default;
        readonly EcsPoolInject<ViewRefComponent>       _viewPool     = default;
        readonly EcsPoolInject<ViewSpawnedTag>         _spawnedPool  = default;
        readonly EcsPoolInject<AttacksUsedComponent>   _attacksUsedPool = default;
        readonly EcsFilterInject<Inc<PlayerComponent, HandComponent>> _playerFilter = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var card in _filter.Value)
            {
                ref var move = ref _movePool.Value.Get(card);

                bool wasInHand = _handTagPool.Value.Has(card);

                if (_handTagPool.Value.Has(card))  _handTagPool.Value.Del(card);
                if (_deckTagPool.Value.Has(card))  _deckTagPool.Value.Del(card);
                if (_graveTagPool.Value.Has(card)) _graveTagPool.Value.Del(card);

                RemoveFromHand(card);

                // Освобождаем UI-слот руки (для чужих карт CardLayout не найдёт слот → no-op).
                if (wasInHand)
                    GameEventBus.Publish(new CardRemovedFromHandUIEvent { CardEntity = card });

                // Воскрешение: на стол кладут существо с DeadTag (призыв с кладбища) → оживляем — снимаем
                // «мёртвость» и восстанавливаем статы (умерло с Current=0). ДО BoardTag, чтобы DieSystem
                // (Inc<DeadTag,BoardTag>) не убил повторно. Работает на обоих (актив PlaceSummon / пассив ReplayCast).
                if (_deadTagPool.Value.Has(card))
                {
                    _deadTagPool.Value.Del(card);
                    if (_hpPool.Value.Has(card))    { ref var h = ref _hpPool.Value.Get(card);    h.Current   = h.Max; }
                    if (_speedPool.Value.Has(card)) { ref var s = ref _speedPool.Value.Get(card); s.Remaining = s.Max; }
                    // Счётчик атак с прошлой жизни — иначе воскрешённый не атакует в ход призыва.
                    if (_attacksUsedPool.Value.Has(card)) _attacksUsedPool.Value.Get(card).Value = 0;

                    // СБРОС ВИЗУАЛА для воскрешения (призыв с кладбища): старый GameObject уничтожен анимацией
                    // смерти (DieSystem.SendToGrave → PlayDeath), но ViewSpawnedTag остался → SpawnCreatureViewSystem
                    // (Exc<ViewSpawnedTag>) НЕ пересоздавал вид → поднятое существо стоит на борде, но НЕВИДИМО
                    // (на обоих клиентах). Чистим вид + флаг, чтобы спавн-система создала новый инстанс (Prefab цел).
                    if (_viewPool.Value.Has(card))
                    {
                        ref var vr = ref _viewPool.Value.Get(card);
                        if (vr.View != null) Object.Destroy(vr.View);
                        vr.View = null;
                    }
                    if (_spawnedPool.Value.Has(card)) _spawnedPool.Value.Del(card);
                }

                if (!_boardTagPool.Value.Has(card)) _boardTagPool.Value.Add(card);

                if (move.Row != -1)
                {
                    if (!_posPool.Value.Has(card)) _posPool.Value.Add(card);
                    ref var pos = ref _posPool.Value.Get(card);
                    pos.Row = move.Row; pos.Col = move.Col; pos.OwnerId = move.OwnerId;
                }
            }
        }

        void RemoveFromHand(int card)
        {
            if (!_ownerPool.Value.Has(card)) return;
            int ownerId = _ownerPool.Value.Get(card).OwnerId;
            foreach (var pe in _playerFilter.Value)
            {
                if (_playerPool.Value.Get(pe).PlayerId != ownerId) continue;
                ref var hand = ref _handPool.Value.Get(pe);
                if (hand.CardEntities != null && hand.CardEntities.Remove(card))
                    hand.Count = hand.CardEntities.Count;
                break;
            }
        }
    }
}
