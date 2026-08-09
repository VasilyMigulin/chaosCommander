using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// Смерть существа по HP ≤ 0, наступившему НЕ уроном (свою смерть от урона разруливает TakeDamageSystem).
    /// Раньше, если HP падал до 0 через дебафф статов (BuffStatsEffect{-hp}) или снятие HP-ауры — где
    /// RecalculateValue зажимает Current к упавшему Max — смерти не было, существо оставалось живым (баг).
    ///
    /// СОБЫТИЙНАЯ (по просьбе пользователя, а не покадровый скан): подписана на CreatureHealthChangedEvent,
    /// который публикуют точки, меняющие HealthComponent модификаторами (BuffStatsEffect, BuffPerCharmSystem…).
    /// На событии проверяет существо: Current ≤ 0 → DeadTag → штатный DieSystem (кладбище/лимбо/командир +
    /// CreatureDiedEvent для OnDie). Стоит ПЕРЕД DieSystem. СИНК даром: HP-модификаторы едут ActionAbilityData
    /// (ре-ран на обоих) → событие и смерть зеркальны. НОВЫЙ путь, меняющий HP существа модификатором, —
    /// не забыть Publish(CreatureHealthChangedEvent).
    /// </summary>
    public sealed class LethalHealthSystem : IEcsRunSystem, IEcsInitSystem, IEcsDestroySystem, System.IDisposable
    {
        readonly EcsPoolInject<HealthComponent> _hpPool = default;
        readonly EcsPoolInject<CreatureTag> _creaturePool = default;
        readonly EcsPoolInject<BoardTag> _boardPool = default;
        readonly EcsPoolInject<DeadTag> _deadPool = default;

        readonly Queue<int> _pending = new Queue<int>();   // копим сущности с события, обрабатываем в Run
        bool _subscribed;

        public void Init(IEcsSystems systems)
        {
            if (_subscribed) return;
            _subscribed = true;
            GameEventBus.Subscribe<CreatureHealthChangedEvent>(OnHealthChanged);
        }

        // EcsSystems.Destroy() зовёт IEcsDestroySystem (не System.IDisposable) — мост.
        public void Destroy(IEcsSystems systems) => Dispose();

        public void Dispose()
        {
            if (!_subscribed) return;
            GameEventBus.Unsubscribe<CreatureHealthChangedEvent>(OnHealthChanged);
            _subscribed = false;
        }

        void OnHealthChanged(CreatureHealthChangedEvent e) => _pending.Enqueue(e.CreatureEntity);

        public void Run(IEcsSystems systems)
        {
            while (_pending.Count > 0)
            {
                int e = _pending.Dequeue();
                if (e < 0 || _deadPool.Value.Has(e)) continue;
                if (!_creaturePool.Value.Has(e) || !_boardPool.Value.Has(e)) continue;   // только живое существо на борде
                if (_hpPool.Value.Has(e) && _hpPool.Value.Get(e).Current <= 0)
                    _deadPool.Value.Add(e);
            }
        }
    }
}
