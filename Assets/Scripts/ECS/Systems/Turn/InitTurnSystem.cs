using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Инициализирует TurnState, TurnPhaseState на первом игроке
    /// и бросает TurnStartEvent на все карты на столе.
    /// </summary>
    public sealed class InitTurnSystem : IEcsInitSystem
    {
        public const float TurnDuration = 60f;

        readonly EcsPoolInject<TurnState> _turnStatePool = default;
        readonly EcsPoolInject<TurnPhaseState> _phasePool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<TurnStartEvent> _turnStartEventPool = default;
        readonly EcsFilterInject<Inc<PlayerComponent>> _playersFilter = default;
        readonly EcsFilterInject<Inc<AbilityContainerComponent, BoardTag>, Exc<HandTag, DeckTag>> _boardCardsFilter = default;

        public void Init(IEcsSystems systems)
        {
            int firstEntity = -1;
            int firstPlayerId = int.MaxValue;

            foreach (var entity in _playersFilter.Value)
            {
                int pid = _playerPool.Value.Get(entity).PlayerId;
                if (pid < firstPlayerId)
                {
                    firstPlayerId = pid;
                    firstEntity = entity;
                }
            }

            if (firstEntity == -1)
                return;

            ref var turnState = ref _turnStatePool.Value.Add(firstEntity);
            turnState.TurnNumber = 1;
            turnState.PersonalTurnNumber = 1;
            turnState.TimeRemaining = TurnDuration;

            ref var phase = ref _phasePool.Value.Add(firstEntity);
            phase.Phase = TurnPhase.TurnStartAbilities;

            foreach (var cardEntity in _boardCardsFilter.Value)
            {
                if (!_turnStartEventPool.Value.Has(cardEntity))
                    _turnStartEventPool.Value.Add(cardEntity);
            }

            GameEventBus.Publish(new TurnStartedEvent
            {
                ActivePlayerId = firstPlayerId,
                TurnNumber = 1
            });
        }
    }
}
