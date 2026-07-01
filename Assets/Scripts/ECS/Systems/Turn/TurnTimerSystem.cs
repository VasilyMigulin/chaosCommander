using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Тикает ActiveState.TimeRemaining у ЛОКАЛЬНОГО активного игрока. При выходе времени —
    /// добавляет EndTurnRequestEvent (конец хода). Наличие ActiveState = «мой ход», фаз больше нет.
    /// </summary>
    public sealed class TurnTimerSystem : IEcsRunSystem
    {
        readonly EcsPoolInject<ActiveState> _activePool = default;
        readonly EcsPoolInject<EndTurnRequestEvent> _endTurnPool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsFilterInject<Inc<ActiveState, PlayerComponent>, Exc<EndTurnState>> _activeFilter = default;

        public void Run(IEcsSystems systems)
        {
            if (MatchState.IsOver) return;   // матч окончен — таймер хода не тикает

            float delta = Time.deltaTime;

            foreach (var entity in _activeFilter.Value)
            {
                ref var player = ref _playerPool.Value.Get(entity);
                if (!player.IsLocalPlayer) continue;   // таймер тикает только у локального

                ref var active = ref _activePool.Value.Get(entity);
                active.TimeRemaining -= delta;

                if (active.TimeRemaining <= 0f)
                {
                    active.TimeRemaining = 0f;
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
