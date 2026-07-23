using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// Завершает каскад начала хода: когда одноразовая часть выполнена (StartTurnState.Resolved)
    /// И каскад «осел» (нет способностей в обработке и нет анимаций), вешает ActiveState и снимает
    /// StartTurnState. До этого момента у игрока нет ActiveState → он не может действовать (тот же
    /// гейт, что и везде). Публикует LocalTurnStartedEvent для UI, если игрок локальный.
    /// </summary>
    public sealed class RunActivateSystem : IEcsRunSystem
    {
        readonly EcsPoolInject<StartTurnState> _startPool  = default;
        readonly EcsPoolInject<ActiveState>    _activePool = default;
        readonly EcsPoolInject<LocalComponent> _localPool  = default;
        readonly EcsFilterInject<Inc<StartTurnState>> _filter = default;

        // «Каскад в работе»: способности резолвятся/целятся или идут анимации.
        readonly EcsFilterInject<Inc<AbilityCastEvent>>       _abilityCast    = default;
        readonly EcsFilterInject<Inc<AbilityTargetingState>>  _abilityTarget  = default;
        readonly EcsFilterInject<Inc<AbilityQueuedState>>     _abilityQueued  = default;
        readonly EcsFilterInject<Inc<MovingTag>>              _moving         = default;
        readonly EcsFilterInject<Inc<AttackAnimPendingTag>>   _attackAnim     = default;
        readonly EcsFilterInject<Inc<PendingOnCastComponent>> _pendingOnCast  = default;   // #2: призыв → OnCast

        public void Run(IEcsSystems systems)
        {
            if (MatchState.IsOver) return;   // матч окончен — новый ход не выдаём

            bool busy = _abilityCast.Value.GetEntitiesCount()   > 0
                     || _abilityTarget.Value.GetEntitiesCount() > 0
                     || _abilityQueued.Value.GetEntitiesCount() > 0
                     || _moving.Value.GetEntitiesCount()        > 0
                     || _attackAnim.Value.GetEntitiesCount()    > 0
                     || _pendingOnCast.Value.GetEntitiesCount() > 0;   // #2: ждём призыв → OnCast

            if (busy) return;

            foreach (var entity in _filter.Value)
            {
                ref var start = ref _startPool.Value.Get(entity);
                if (!start.Resolved) continue;   // одноразовая часть ещё не отработала

                if (!_activePool.Value.Has(entity))
                {
                    ref var active = ref _activePool.Value.Add(entity);
                    active.TurnNumber         = start.TurnNumber;
                    active.PersonalTurnNumber = start.PersonalTurnNumber;
                    active.TimeRemaining      = TurnConfig.TurnDuration;
                }

                bool isLocal = _localPool.Value.Has(entity);
                int turnNumber = start.TurnNumber;
                _startPool.Value.Del(entity);

                if (isLocal)
                {
                    GameEventBus.Publish(new LocalTurnStartedEvent
                    {
                        TurnNumber = turnNumber,
                        TurnDurationSeconds = TurnConfig.TurnDuration
                    });
                    GameEventBus.Publish(new InputRestoredEvent());
                }

                UnityEngine.Debug.Log($"[Activate] ActiveState set, turn={turnNumber} local={isLocal}");
                break;   // один за кадр
            }
        }
    }
}
