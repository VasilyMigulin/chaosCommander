using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Тикает TurnState.TimeRemaining только в фазе PlayerTurn.
    /// При выходе времени — добавляет EndTurnRequestEvent.
    /// </summary>
    public sealed class TurnTimerSystem : IEcsRunSystem
    {
        readonly EcsPoolInject<TurnState> _turnStatePool = default;
        readonly EcsPoolInject<TurnPhaseState> _phasePool = default;
        readonly EcsPoolInject<EndTurnRequestEvent> _endTurnPool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsFilterInject<Inc<TurnState, TurnPhaseState, PlayerComponent>> _activeFilter = default;

        public void Run(IEcsSystems systems)
        {
            float delta = Time.deltaTime;

            foreach (var entity in _activeFilter.Value)
            {
                ref var player = ref _playerPool.Value.Get(entity);

                // Таймер тикает только у локального игрока — он авторитетен для своего хода
                if (!player.IsLocalPlayer)
                    continue;

                ref var phase = ref _phasePool.Value.Get(entity);
                if (phase.Phase != TurnPhase.PlayerTurn)
                    continue;

                ref var turnState = ref _turnStatePool.Value.Get(entity);
                turnState.TimeRemaining -= delta;

                if (turnState.TimeRemaining <= 0f)
                {
                    turnState.TimeRemaining = 0f;

                    if (!_endTurnPool.Value.Has(entity))
                    {
                        ref var req = ref _endTurnPool.Value.Add(entity);
                        req.RequestingPlayerId = player.PlayerId;
                    }
                }
            }
        }
    }
}
