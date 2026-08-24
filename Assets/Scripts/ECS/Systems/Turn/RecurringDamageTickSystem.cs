using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// В конце КАЖДОГО хода (обеих сторон) — бьёт Amount урона по всем существам с RecurringDamageComponent
    /// (Напустить саранчу — метка ставится сразу на ВСЕХ, без учёта владельца, поэтому и тикает без фильтра
    /// владельца, в отличие от CreatureTimerTickSystem). Обычный TakeDamageEvent — гибель/атрибуцию/хрипы
    /// отрабатывает штатный пайплайн урона, а не эта система напрямую.
    /// </summary>
    public sealed class RecurringDamageTickSystem : IEcsInitSystem, IEcsRunSystem, IEcsDestroySystem, System.IDisposable
    {
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<RecurringDamageComponent, BoardTag>, Exc<DeadTag>> _filter = default;
        readonly EcsPoolInject<RecurringDamageComponent> _dmgPool = default;
        readonly EcsPoolInject<TakeDamageEvent> _takeDamagePool = default;

        readonly Queue<int> _pending = new Queue<int>();
        bool _subscribed;

        public void Init(IEcsSystems systems) => Subscribe();

        // EcsSystems.Destroy() ищет IEcsDestroySystem, не System.IDisposable — без этого моста Dispose()
        // фреймворк никогда не вызывал бы (см. EcsRunHandler/TutorialEcsHandler.Dispose → _allSystems.Destroy()).
        public void Destroy(IEcsSystems systems) => Dispose();

        public void Dispose()
        {
            if (!_subscribed) return;
            GameEventBus.Unsubscribe<TurnEndedEvent>(OnTurnEnded);
            _subscribed = false;
        }

        void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            GameEventBus.Subscribe<TurnEndedEvent>(OnTurnEnded);
        }

        // ActivePlayerId не нужен — тикаем ВСЕХ помеченных на конец ЛЮБОГО хода (см. класс-комментарий).
        void OnTurnEnded(TurnEndedEvent e) => _pending.Enqueue(e.ActivePlayerId);

        public void Run(IEcsSystems systems)
        {
            while (_pending.Count > 0)
            {
                _pending.Dequeue();
                Tick();
            }
        }

        void Tick()
        {
            // [SyncWatch] см. тот же лог в PoisonTickSystem — сосед без доккомментария про актив/пассив,
            // а бьёт HP (входит в чексумму). Сравнить ticked/active между клиентами при подозрении на десинк.
            int ticked = 0;
            foreach (var entity in _filter.Value)
            {
                int amount = _dmgPool.Value.Get(entity).Amount;
                if (amount <= 0) continue;

                if (!_takeDamagePool.Value.Has(entity)) _takeDamagePool.Value.Add(entity);
                ref var d = ref _takeDamagePool.Value.Get(entity);
                d.Amount += amount;
                d.Attacker = -1;   // амбиентный урон, не от конкретной карты — атрибуция киллов не нужна (как у таймера смерти)
                ticked++;
            }
            if (ticked > 0)
                UnityEngine.Debug.Log($"[SyncWatch] RecurringDamageTick ticked={ticked} "
                    + $"active={Game.Core.Ecs.Components.TurnGate.IsLocalActive(_world.Value)}");
        }
    }
}
