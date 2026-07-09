using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Тикает ActiveState.TimeRemaining у ЛОКАЛЬНОГО активного игрока. При выходе времени —
    /// добавляет EndTurnRequestEvent (конец хода). Наличие ActiveState = «мой ход», фаз больше нет.
    ///
    /// ПАУЗА НА АНИМАЦИЯХ/РЕЗОЛВЕ: пока крутится способность/движение/атака/анимация каста — таймер НЕ
    /// тикает (иначе игрок терял бы время хода за визуальный отклик игры, который сам не контролирует —
    /// снаряд летит, существо бежит/атакует/умирает, способность резолвится). Тот же список гейтов, что и
    /// в RunActivateSystem/EndTurnRequestSystem ("осел ли пайплайн"), БЕЗ AbilityTargetPendingState — это
    /// ожидание ИГРОКА (клик по цели), тут время идёт как обычно.
    /// </summary>
    public sealed class TurnTimerSystem : IEcsRunSystem
    {
        readonly EcsPoolInject<ActiveState> _activePool = default;
        readonly EcsPoolInject<EndTurnRequestEvent> _endTurnPool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsFilterInject<Inc<ActiveState, PlayerComponent>, Exc<EndTurnState>> _activeFilter = default;

        readonly EcsFilterInject<Inc<AbilityCastEvent>>            _abilityCast      = default;
        readonly EcsFilterInject<Inc<AbilityTargetingState>>       _abilityTargeting = default;
        readonly EcsFilterInject<Inc<AbilityQueuedState>>          _abilityQueued    = default;
        readonly EcsFilterInject<Inc<AbilityCastPendingComponent>> _projectileFlight = default;
        readonly EcsFilterInject<Inc<AbilityAnimPendingComponent>> _abilityAnim      = default;
        readonly EcsFilterInject<Inc<MovingTag>>                   _moving           = default;
        readonly EcsFilterInject<Inc<AttackAnimPendingTag>>        _attackAnim       = default;
        readonly EcsFilterInject<Inc<PendingOnCastComponent>>      _pendingOnCast    = default;

        bool _wasBusy;

        public void Run(IEcsSystems systems)
        {
            if (MatchState.IsOver) return;   // матч окончен — таймер хода не тикает

            bool busy = _abilityCast.Value.GetEntitiesCount()      > 0
                     || _abilityTargeting.Value.GetEntitiesCount() > 0
                     || _abilityQueued.Value.GetEntitiesCount()    > 0
                     || _projectileFlight.Value.GetEntitiesCount() > 0
                     || _abilityAnim.Value.GetEntitiesCount()      > 0
                     || _moving.Value.GetEntitiesCount()           > 0
                     || _attackAnim.Value.GetEntitiesCount()       > 0
                     || _pendingOnCast.Value.GetEntitiesCount()    > 0;

            // Едино на КАЖДОЕ изменение гейта (не на каждый кадр) — UI прячет Root таймера, пока busy=true,
            // чтобы пауза была видна глазами (см. TurnTimerBusyUIEvent).
            if (busy != _wasBusy)
            {
                _wasBusy = busy;
                GameEventBus.Publish(new TurnTimerBusyUIEvent { IsBusy = busy });
            }

            if (busy) return;

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
