using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// Каскад НАЧАЛА хода (одноразовая часть). На игроке со StartTurnState (Resolved=false):
    ///   1) восстановить скорость существ владельца;
    ///   2) начислить золото (доход по PersonalTurnNumber);
    ///   3) добор (DrawCardEvent);
    ///   4) сигнал начала хода (bus TurnStartedEvent) — его ловят OnTurnStartTrigger способностей;
    /// затем Resolved=true. ActiveState вешает RunActivateSystem, когда каскад осядет.
    /// Крутится только у активного (локального) клиента — на пассиве StartTurnState не вешается.
    /// </summary>
    public sealed class RunTurnStartSystem : IEcsRunSystem
    {
        readonly EcsPoolInject<StartTurnState>   _startPool  = default;
        readonly EcsPoolInject<PlayerComponent>  _playerPool = default;
        readonly EcsPoolInject<GoldComponent>    _goldPool   = default;
        readonly EcsPoolInject<ManaComponent>    _manaPool   = default;
        readonly EcsPoolInject<DrawCardEvent>    _drawPool   = default;
        readonly EcsPoolInject<LocalComponent>   _localPool  = default;
        readonly EcsFilterInject<Inc<StartTurnState, PlayerComponent>> _filter = default;

        readonly EcsFilterInject<Inc<CreatureTag, BoardTag, SpeedComponent, OwnerComponent>, Exc<DeadTag>> _creatures = default;
        readonly EcsPoolInject<SpeedComponent>   _speedPool  = default;
        readonly EcsPoolInject<OwnerComponent>   _ownerPool  = default;
        readonly EcsPoolInject<AttacksUsedComponent> _attacksUsedPool = default;

        readonly EcsFilterInject<Inc<AbilityContainerComponent, BoardTag, OwnerComponent>, Exc<HandTag, DeckTag>> _boardCards = default;
        readonly EcsPoolInject<TurnStartEvent>   _turnStartEventPool = default;

        readonly EcsFilterInject<Inc<CommanderTag, CommanderCooldownComponent, OwnerComponent>> _commanderCd = default;
        readonly EcsPoolInject<CommanderCooldownComponent> _commanderCdPool = default;

        // Наступающий кризис: игрок с GoldBlockComponent не получает доход золота в начале хода.
        readonly EcsPoolInject<GoldBlockComponent> _goldBlockPool = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var entity in _filter.Value)
            {
                ref var start = ref _startPool.Value.Get(entity);
                if (start.Resolved) continue;
                start.Resolved = true;

                int playerId = _playerPool.Value.Get(entity).PlayerId;

                // 1) Скорость существ владельца + сброс счётчика атак за ход (#4).
                foreach (var ce in _creatures.Value)
                {
                    if (_ownerPool.Value.Get(ce).OwnerId != playerId) continue;
                    ref var sp = ref _speedPool.Value.Get(ce);
                    sp.Remaining = sp.Max;
                    if (_attacksUsedPool.Value.Has(ce)) _attacksUsedPool.Value.Get(ce).Value = 0;
                }

                // 2) Золото — кроме игрока с маркером GoldBlockComponent (Наступающий кризис): доход не начисляем.
                bool isLocal = _localPool.Value.Has(entity);
                if (!_goldBlockPool.Value.Has(entity))
                {
                    ref var gold = ref _goldPool.Value.Get(entity);
                    gold.Max = Mathf.Min(gold.Max + GoldIncome(start.PersonalTurnNumber), 10);
                    gold.Current = gold.Max;
                    GameEventBus.Publish(new ResourceChangedEvent
                    {
                        isLocalPlayer = isLocal,
                        Type = Service.EnumService.ResourceType.Gold,
                        NewValue = gold.Current, MaxValue = gold.Max
                    });
                }
                if (_manaPool.Value.Has(entity))
                {
                    ref var mana = ref _manaPool.Value.Get(entity);
                    GameEventBus.Publish(new ResourceChangedEvent
                    {
                        isLocalPlayer = isLocal,
                        Type = Service.EnumService.ResourceType.Mana,
                        NewValue = mana.Current, MaxValue = mana.Max
                    });
                }

                // 2b) Кулдаун командира владельца
                foreach (var ce in _commanderCd.Value)
                {
                    if (_ownerPool.Value.Get(ce).OwnerId != playerId) continue;
                    ref var cd = ref _commanderCdPool.Value.Get(ce);
                    if (cd.TurnsRemaining > 0) cd.TurnsRemaining--;
                    else
                    {
                        _commanderCdPool.Value.Del(ce);
                        GameEventBus.Publish(new CommanderCooldownExpiredUIEvent { CardEntity = ce });
                    }
                }

                // 3) Добор (turn-start → Sync=true: каскад идёт только у активного, пассив повторит по ActionDrawData)
                if (!_drawPool.Value.Has(entity)) _drawPool.Value.Add(entity);
                _drawPool.Value.Get(entity).Sync = true;

                // 4) Сигнал начала хода (bus): сброс индекса действий у коллектора +
                //    OnTurnStartTrigger способностей ловят TurnStartedEvent и поднимают каст.
                //    (Per-card TurnStartEvent ниже — легаси-маркер, потребителя больше нет,
                //     DelHere его чистит; оставлен инертным, не мешает.)
                foreach (var ce in _boardCards.Value)
                {
                    if (_ownerPool.Value.Get(ce).OwnerId != playerId) continue;
                    if (!_turnStartEventPool.Value.Has(ce)) _turnStartEventPool.Value.Add(ce);
                }

                GameEventBus.Publish(new TurnStartedEvent { ActivePlayerId = playerId, TurnNumber = start.TurnNumber });

                UnityEngine.Debug.Log($"[TurnStart] cascade for player={playerId} turn={start.TurnNumber} personal={start.PersonalTurnNumber}");
            }
        }

        static int GoldIncome(int personalTurnNumber)
        {
            if (personalTurnNumber <= 3) return 1;
            if (personalTurnNumber <= 6) return 2;
            return 3;
        }
    }
}
