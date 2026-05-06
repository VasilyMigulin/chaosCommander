using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Переводит фазу хода из TurnEndAbilities → TurnTransfer
    /// когда очередь способностей пуста и нет активных эффектов.
    /// Снимает TurnState с текущего игрока и выдаёт TurnTransferEvent.
    /// </summary>
    public sealed class TurnEndReadySystem : IEcsRunSystem
    {
        readonly EcsPoolInject<TurnState> _turnStatePool = default;
        readonly EcsPoolInject<TurnPhaseState> _phasePool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<TurnTransferEvent> _transferPool = default;
        readonly EcsFilterInject<Inc<TurnState, TurnPhaseState, PlayerComponent>> _activePlayerFilter = default;
        readonly EcsFilterInject<Inc<PlayerComponent>> _playersFilter = default;
        readonly EcsFilterInject<Inc<AbilityQueueTag, AbilityQueueComponent>, Exc<LockState>> _queueFilter = default;
        readonly EcsFilterInject<Inc<EffectComponent, ActiveState>> _activeEffectsFilter = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var entity in _activePlayerFilter.Value)
            {
                ref var phase = ref _phasePool.Value.Get(entity);
                if (phase.Phase != TurnPhase.TurnEndAbilities)
                    continue;

                bool effectsActive = _activeEffectsFilter.Value.GetEntitiesCount() > 0;
                bool queueBusy = IsQueueBusy();

                if (queueBusy || effectsActive)
                    continue;

                phase.Phase = TurnPhase.TurnTransfer;

                ref var currentPlayer = ref _playerPool.Value.Get(entity);
                ref var turnState = ref _turnStatePool.Value.Get(entity);
                int finishedTurnNumber = turnState.TurnNumber;
                int fromPlayerId = currentPlayer.PlayerId;
                int toPlayerId = GetNextPlayerId(fromPlayerId);

                GameEventBus.Publish(new TurnEndedEvent { ActivePlayerId = fromPlayerId });

                // Снимаем TurnState и фазу с текущего игрока
                _turnStatePool.Value.Del(entity);
                _phasePool.Value.Del(entity);

                // Выдаём TurnTransferEvent следующему игроку
                int nextEntity = GetPlayerEntity(toPlayerId);
                if (nextEntity == -1)
                    continue;

                ref var transfer = ref _transferPool.Value.Add(nextEntity);
                transfer.FromPlayerId = fromPlayerId;
                transfer.ToPlayerId = toPlayerId;
            }
        }

        private bool IsQueueBusy()
        {
            foreach (var _ in _queueFilter.Value)
                return false; // фильтр Exc<LockState> — если в фильтр попал, значит не залочен и стек может быть пустым
            // Если фильтр пустой — синглтон залочен (идёт резолв)
            return true;
        }

        private int GetNextPlayerId(int currentPlayerId)
        {
            int minId = int.MaxValue;
            int nextId = int.MaxValue;

            foreach (var e in _playersFilter.Value)
            {
                int pid = _playerPool.Value.Get(e).PlayerId;
                if (pid < minId) minId = pid;
                if (pid > currentPlayerId && pid < nextId) nextId = pid;
            }

            return nextId == int.MaxValue ? minId : nextId;
        }

        private int GetPlayerEntity(int playerId)
        {
            foreach (var e in _playersFilter.Value)
            {
                if (_playerPool.Value.Get(e).PlayerId == playerId)
                    return e;
            }
            return -1;
        }
    }
}
