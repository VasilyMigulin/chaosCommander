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
    /// ожидание ИГРОКА (клик по цели), тут время идёт как обычно. ChainStateComponent (RunChainSystem —
    /// AbilityChain/RepeatAbility) — та же логика: пока цепочка резолвится (снаряд летит между стадиями
    /// ИЛИ мир оседает между ними), таймер стоит.
    ///
    /// НЕТ ДЕЙСТВИЙ → ШОРТКАТ (2026-08-11): если у активного локального игрока не осталось ни одной
    /// играбельной карты в руке, ни одного существа с остатком скорости — ждать полные TurnDuration
    /// секунд бессмысленно (ход всё равно пассивный). Остаток таймера форсится до NoActionsTimeCap (3с),
    /// НЕ мгновенно — чтобы игрок успел это увидеть, а не потерял ход стелс-скипом. Проверка НЕ каждый
    /// кадр (поле меняется только действиями игрока) — на двух точках: (1) пайплайн только что осел
    /// (busy→idle edge — «действие только что применилось и доигралось»), (2) начало хода
    /// (LocalTurnStartedEvent — «действий не было ни одного»).
    /// </summary>
    public sealed class TurnTimerSystem : IEcsInitSystem, IEcsRunSystem, IEcsDestroySystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsPoolInject<ActiveState> _activePool = default;
        readonly EcsPoolInject<EndTurnRequestEvent> _endTurnPool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<SpeedComponent> _speedPool = default;
        readonly EcsFilterInject<Inc<ActiveState, PlayerComponent>, Exc<EndTurnState>> _activeFilter = default;

        readonly EcsFilterInject<Inc<HandTag, OwnerComponent>> _handCards = default;
        readonly EcsFilterInject<Inc<CreatureTag, BoardTag, SpeedComponent, OwnerComponent>, Exc<DeadTag>> _boardCreatures = default;

        readonly EcsFilterInject<Inc<AbilityCastEvent>>            _abilityCast      = default;
        readonly EcsFilterInject<Inc<AbilityTargetingState>>       _abilityTargeting = default;
        readonly EcsFilterInject<Inc<AbilityQueuedState>>          _abilityQueued    = default;
        readonly EcsFilterInject<Inc<AbilityCastPendingComponent>> _projectileFlight = default;
        readonly EcsFilterInject<Inc<AbilityAnimPendingComponent>> _abilityAnim      = default;
        readonly EcsFilterInject<Inc<MovingTag>>                   _moving           = default;
        readonly EcsFilterInject<Inc<AttackAnimPendingTag>>        _attackAnim       = default;
        readonly EcsFilterInject<Inc<PendingOnCastComponent>>      _pendingOnCast    = default;
        readonly EcsFilterInject<Inc<ChainStateComponent>>         _chainResolving   = default;   // цепочка (RunChainSystem) в процессе — снаряд/оседание между стадиями
        readonly EcsFilterInject<Inc<DeathAnimPendingTag>>         _deathAnim        = default;   // существо ещё доигрывает анимацию смерти

        const float NoActionsTimeCap = 3f;

        bool _wasBusy;

        public void Init(IEcsSystems systems) => GameEventBus.Subscribe<LocalTurnStartedEvent>(OnLocalTurnStarted);
        public void Destroy(IEcsSystems systems) => GameEventBus.Unsubscribe<LocalTurnStartedEvent>(OnLocalTurnStarted);

        void OnLocalTurnStarted(LocalTurnStartedEvent e) => CheckNoActions();

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
                     || _pendingOnCast.Value.GetEntitiesCount()    > 0
                     || _chainResolving.Value.GetEntitiesCount()   > 0
                     || _deathAnim.Value.GetEntitiesCount()        > 0;

            // Едино на КАЖДОЕ изменение гейта (не на каждый кадр) — UI прячет Root таймера, пока busy=true,
            // чтобы пауза была видна глазами (см. TurnTimerBusyUIEvent).
            if (busy != _wasBusy)
            {
                _wasBusy = busy;
                GameEventBus.Publish(new TurnTimerBusyUIEvent { IsBusy = busy });
                if (!busy) CheckNoActions();   // пайплайн только что осел — действие игрока применилось и доигралось
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

        // Активному локальному игроку нечем ходить (см. докстринг класса) → форсируем остаток таймера
        // до NoActionsTimeCap. Только вниз — уже идущий более короткий отсчёт не продлеваем назад.
        void CheckNoActions()
        {
            foreach (var entity in _activeFilter.Value)
            {
                ref var player = ref _playerPool.Value.Get(entity);
                if (!player.IsLocalPlayer) continue;

                ref var active = ref _activePool.Value.Get(entity);
                if (active.TimeRemaining <= NoActionsTimeCap) continue;
                if (HasAnyAction(player.PlayerId)) continue;

                active.TimeRemaining = NoActionsTimeCap;
            }
        }

        // Упрощённо (не полный BFS легальности хода/атаки, как у RunSelectCellSystem/RunAiTurnSystem):
        // играбельная карта в руке ЛИБО существо с остатком скорости (двигаться/бить). Redundant edge-case
        // «скорость есть, но существо намертво заблокировано Защитником» намеренно не считаем — крайне
        // редко и самоисправляется следующим действием/сменой борда.
        bool HasAnyAction(int playerId)
        {
            foreach (var card in _handCards.Value)
            {
                if (_ownerPool.Value.Get(card).OwnerId != playerId) continue;
                if (CardAffordabilityUtil.IsAffordable(_world.Value, card)) return true;
            }

            foreach (var creature in _boardCreatures.Value)
            {
                if (_ownerPool.Value.Get(creature).OwnerId != playerId) continue;
                if (_speedPool.Value.Get(creature).Remaining > 0) return true;
            }

            return false;
        }
    }
}
