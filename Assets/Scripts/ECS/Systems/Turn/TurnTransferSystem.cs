using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Принимает TurnTransferEvent на сущности следующего игрока.
    /// Добавляет TurnState + TurnPhaseState, бросает TurnStartEvent на все карты на столе.
    /// На сервере дополнительно публикует TurnTransferRpcEvent для синхронизации клиентов.
    /// </summary>
    public sealed class TurnTransferSystem : IEcsRunSystem
    {
        readonly EcsPoolInject<TurnState> _turnStatePool = default;
        readonly EcsPoolInject<TurnPhaseState> _phasePool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<TurnTransferEvent> _transferPool = default;
        readonly EcsPoolInject<TurnStartEvent> _turnStartEventPool = default;
        readonly EcsFilterInject<Inc<TurnTransferEvent, PlayerComponent>> _filter = default;
        readonly EcsFilterInject<Inc<PlayerComponent>> _playersFilter = default;
        readonly EcsFilterInject<Inc<AbilityContainerComponent, BoardTag>, Exc<HandTag, DeckTag>> _boardCardsFilter = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var entity in _filter.Value)
            {
                ref var transfer = ref _transferPool.Value.Get(entity);
                ref var player = ref _playerPool.Value.Get(entity);

                int personalTurnNumber = CalculatePersonalTurnNumber(entity, transfer.ToPlayerId);
                int globalTurnNumber = GetCurrentGlobalTurn() + 1;

                ref var turnState = ref _turnStatePool.Value.Add(entity);
                turnState.TurnNumber = globalTurnNumber;
                turnState.PersonalTurnNumber = personalTurnNumber;
                turnState.TimeRemaining = InitTurnSystem.TurnDuration;

                ref var phase = ref _phasePool.Value.Add(entity);
                phase.Phase = TurnPhase.TurnStartAbilities;

                _transferPool.Value.Del(entity);

                // Бросаем TurnStartEvent на все карты на столе
                foreach (var cardEntity in _boardCardsFilter.Value)
                {
                    if (!_turnStartEventPool.Value.Has(cardEntity))
                        _turnStartEventPool.Value.Add(cardEntity);
                }

                GameEventBus.Publish(new TurnStartedEvent
                {
                    ActivePlayerId = player.PlayerId,
                    TurnNumber = globalTurnNumber
                });
            }
        }

        private int GetCurrentGlobalTurn()
        {
            foreach (var e in _playersFilter.Value)
            {
                if (_turnStatePool.Value.Has(e))
                    return _turnStatePool.Value.Get(e).TurnNumber;
            }
            return 0;
        }

        private int CalculatePersonalTurnNumber(int playerEntity, int playerId)
        {
            int playerCount = 0;
            foreach (var _ in _playersFilter.Value) playerCount++;
            if (playerCount == 0) playerCount = 1;

            int globalTurn = GetCurrentGlobalTurn() + 1;
            return (globalTurn / playerCount) + 1;
        }
    }
}
