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

        public void Run(IEcsSystems systems)
        {
            if (MatchState.IsOver) return;   // матч окончен — новый ход не выдаём

            // Гейт имеет смысл ТОЛЬКО если кто-то реально ждёт старта хода (StartTurnState есть) — иначе
            // «пайплайн не осел» может означать что угодно постороннее (игрок минуту разглядывает Discover-
            // попап посреди СВОЕГО уже идущего хода) и никак не связано со стартом хода. Без этой проверки
            // ReportIfStuck кричал «старт хода ЗАСТРЯЛ» на пустом месте (баг 2026-08-23: DiscoverRequestComponent
            // висел из-за обычного ожидания клика игрока, а не старта хода).
            if (_filter.Value.GetEntitiesCount() == 0) { _stuckSince = 0f; return; }

            // «Каскад в работе» — единая проверка (см. PipelineGate: раньше это был свой список тегов,
            // который разошёлся с соседними копиями в EndTurnRequestSystem/RunAiTurnSystem).
            var world = systems.GetWorld();
            if (!PipelineGate.IsSettled(world)) { ReportIfStuck(world); return; }
            _stuckSince = 0f;

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

        // [SyncWatch] см. тот же приём в EndTurnRequestSystem.ReportIfStuck/RunAiTurnSystem.ReportIfStuck.
        float _stuckSince;
        const float StuckReportAfter = 5f;

        void ReportIfStuck(EcsWorld world)
        {
            if (_stuckSince == 0f) { _stuckSince = UnityEngine.Time.time; return; }
            if (_stuckSince < 0f) return;
            if (UnityEngine.Time.time - _stuckSince < StuckReportAfter) return;

            UnityEngine.Debug.LogError("[SyncWatch] старт хода ЗАСТРЯЛ (PipelineGate не оседает):" + PipelineGate.DescribeBusy(world));
            _stuckSince = -1f;
        }
    }
}
