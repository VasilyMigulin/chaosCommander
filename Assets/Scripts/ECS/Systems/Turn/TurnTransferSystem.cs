using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Выполняет команду хоста RPC_TurnStartPhaseBegin(nextPlayerId, turnNumber).
    /// Команда приходит через TurnTransferEvent на entity нового активного игрока.
    /// Оба клиента одновременно:
    ///   - Обновляют GlobalTurnState
    ///   - Ставят TurnState на нового активного игрока
    ///   - Вешают фазу TurnStartAbilities на всех игроков
    ///   - Бросают TurnStartEvent на карты нового активного игрока
    ///   - Публикуют TurnStartedEvent для UI
    /// </summary>
    public sealed class TurnTransferSystem : IEcsRunSystem
    {
        readonly EcsPoolInject<TurnState> _turnStatePool = default;
        readonly EcsPoolInject<TurnPhaseState> _phasePool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<TurnTransferEvent> _transferPool = default;
        readonly EcsPoolInject<TurnStartEvent> _turnStartEventPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<GlobalTurnState> _globalTurnPool = default;
        readonly EcsFilterInject<Inc<TurnTransferEvent, PlayerComponent>> _filter = default;
        readonly EcsFilterInject<Inc<PlayerComponent, TurnPhaseState>> _allPlayersFilter = default;
        readonly EcsFilterInject<Inc<GlobalTurnState>> _globalFilter = default;
        readonly EcsFilterInject<Inc<AbilityContainerComponent, BoardTag, OwnerComponent>, Exc<HandTag, DeckTag>> _boardCardsFilter = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var entity in _filter.Value)
            {
                ref var transfer = ref _transferPool.Value.Get(entity);
                _transferPool.Value.Del(entity);

                int activePlayerId = transfer.ToPlayerId;
                int turnNumber = transfer.TurnNumber;
                int personalTurnNumber = transfer.PersonalTurnNumber;

                // Обновляем GlobalTurnState
                foreach (var ge in _globalFilter.Value)
                {
                    ref var g = ref _globalTurnPool.Value.Get(ge);
                    g.ActivePlayerId = activePlayerId;
                    g.TurnNumber = turnNumber;
                    g.PersonalTurnNumber = personalTurnNumber;
                    break;
                }

                // Ставим TurnState на нового активного игрока
                if (!_turnStatePool.Value.Has(entity))
                {
                    ref var ts = ref _turnStatePool.Value.Add(entity);
                    ts.TurnNumber = turnNumber;
                    ts.PersonalTurnNumber = personalTurnNumber;
                    ts.TimeRemaining = InitTurnSystem.TurnDuration;
                }

                // Переводим ВСЕХ игроков в фазу TurnStartAbilities
                foreach (var pe in _allPlayersFilter.Value)
                {
                    ref var phase = ref _phasePool.Value.Get(pe);
                    phase.Phase = TurnPhase.TurnStartAbilities;
                }

                // TurnStartEvent только на карты нового активного игрока
                foreach (var cardEntity in _boardCardsFilter.Value)
                {
                    if (_ownerPool.Value.Get(cardEntity).OwnerId != activePlayerId) continue;
                    if (!_turnStartEventPool.Value.Has(cardEntity))
                        _turnStartEventPool.Value.Add(cardEntity);
                }

                GameEventBus.Publish(new TurnStartedEvent
                {
                    ActivePlayerId = activePlayerId,
                    TurnNumber = turnNumber
                });
            }
        }
    }
}
