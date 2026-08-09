using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Свойство «Ядовитый»: в конце хода ВЛАДЕЛЬЦА поражённой сущности (не источника, наложившего яд) бьёт
    /// Stacks урона обычным TakeDamageEvent (уважает Shield/Invulnerable, как любой урон). БЕЗ фильтра
    /// BoardTag — работает и на игроках (PlayerComponent), и на существах (OwnerComponent), в отличие от
    /// RecurringDamageTickSystem (тот бьёт ОБЕИХ сторон без учёта владельца — саранча).
    /// </summary>
    public sealed class PoisonTickSystem : IEcsInitSystem, IEcsRunSystem, IEcsDestroySystem, System.IDisposable
    {
        readonly EcsFilterInject<Inc<PoisonComponent>, Exc<DeadTag>> _filter = default;
        readonly EcsPoolInject<PoisonComponent> _poisonPool = default;
        readonly EcsPoolInject<TakeDamageEvent> _takeDamagePool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;

        readonly Queue<int> _pending = new Queue<int>();
        bool _subscribed;

        public void Init(IEcsSystems systems) => Subscribe();
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

        void OnTurnEnded(TurnEndedEvent e) => _pending.Enqueue(e.ActivePlayerId);

        public void Run(IEcsSystems systems)
        {
            while (_pending.Count > 0) Tick(_pending.Dequeue());
        }

        void Tick(int playerId)
        {
            foreach (var entity in _filter.Value)
            {
                if (OwnerPlayerId(entity) != playerId) continue;
                int stacks = _poisonPool.Value.Get(entity).Stacks;
                if (stacks <= 0) continue;

                if (!_takeDamagePool.Value.Has(entity)) _takeDamagePool.Value.Add(entity);
                ref var d = ref _takeDamagePool.Value.Get(entity);
                d.Amount += stacks;
                d.Attacker = -1;   // амбиентный урон — как у таймера смерти/саранчи, атрибуция киллов не нужна
            }
        }

        // Владелец сущности: игрок сам себе владелец, у существа — OwnerComponent.
        int OwnerPlayerId(int entity)
        {
            if (_playerPool.Value.Has(entity)) return _playerPool.Value.Get(entity).PlayerId;
            if (_ownerPool.Value.Has(entity))  return _ownerPool.Value.Get(entity).OwnerId;
            return -1;
        }
    }
}
