using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// Декрементит TurnsRemaining у баффов с длительностью (AddBuffEffect{Duration>0}) — счётчик стоит НЕ
    /// на карте целиком, а на КАЖДОЙ выданной записи TrackedBuffsComponent.Items (одна запись = одна пара
    /// цель+бафф). Момент тика свой у каждой записи (ExpireAt: TurnStart/TurnEnd — тот же CharmTickMoment,
    /// что у чар, паттерн CharmTimerTickSystem). На 0 — Buff.Revert(target) и запись убирается из списка.
    /// Записи с TurnsRemaining=0 (умолчание, обычные Tracked-ауры без Duration) не трогаем — авто-списывания
    /// не было и не будет, снимаются как раньше (вручную/по смерти источника).
    ///
    /// БЕЗ BoardTag в фильтре (в отличие от CharmTimerTickSystem): источник может быть уже разыгранным
    /// спеллом, ушедшим в кладбище — карта-сущность не удаляется при смене зоны, только перевешивает тег,
    /// поэтому длительность тикает и там же, где и раньше тикали чары.
    /// </summary>
    public sealed class BuffDurationTickSystem : IEcsInitSystem, IEcsRunSystem, IEcsDestroySystem, System.IDisposable
    {
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<TrackedBuffsComponent, OwnerComponent>> _filter = default;
        readonly EcsPoolInject<TrackedBuffsComponent> _trackedPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;

        readonly Queue<(int playerId, CharmTickMoment moment)> _pending = new();
        bool _subscribed;

        public void Init(IEcsSystems systems) => Subscribe();

        // EcsSystems.Destroy() ищет IEcsDestroySystem, не System.IDisposable — без этого моста Dispose()
        // фреймворк никогда не вызывал бы (см. EcsRunHandler/TutorialEcsHandler.Dispose → _allSystems.Destroy()).
        public void Destroy(IEcsSystems systems) => Dispose();

        public void Dispose()
        {
            if (!_subscribed) return;
            GameEventBus.Unsubscribe<TurnEndedEvent>(OnTurnEnded);
            GameEventBus.Unsubscribe<TurnStartedEvent>(OnTurnStarted);
            _subscribed = false;
        }

        void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            GameEventBus.Subscribe<TurnEndedEvent>(OnTurnEnded);
            GameEventBus.Subscribe<TurnStartedEvent>(OnTurnStarted);
        }

        void OnTurnEnded(TurnEndedEvent e)     => _pending.Enqueue((e.ActivePlayerId, CharmTickMoment.TurnEnd));
        void OnTurnStarted(TurnStartedEvent e) => _pending.Enqueue((e.ActivePlayerId, CharmTickMoment.TurnStart));

        public void Run(IEcsSystems systems)
        {
            while (_pending.Count > 0)
            {
                var (playerId, moment) = _pending.Dequeue();
                Tick(playerId, moment);
            }
        }

        void Tick(int playerId, CharmTickMoment moment)
        {
            var world = _world.Value;
            foreach (var source in _filter.Value)
            {
                if (_ownerPool.Value.Get(source).OwnerId != playerId) continue;
                ref var t = ref _trackedPool.Value.Get(source);
                if (t.Items == null || t.Items.Count == 0) continue;

                for (int i = t.Items.Count - 1; i >= 0; i--)
                {
                    var it = t.Items[i];
                    if (it.TurnsRemaining <= 0 || it.ExpireAt != moment) continue;   // без длительности / не свой момент

                    it.TurnsRemaining--;
                    if (it.TurnsRemaining <= 0)
                    {
                        it.Buff?.Revert(world, source, it.Target);
                        t.Items.RemoveAt(i);
                    }
                    else
                    {
                        t.Items[i] = it;   // struct — записать декремент обратно в список
                    }
                }
            }
        }
    }
}
