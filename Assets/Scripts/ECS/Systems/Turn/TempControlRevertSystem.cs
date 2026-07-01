using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Срок временного контроля. На TurnEndedEvent КОНТРОЛЁРА (его id в ExpiresOnPlayerId) тикает
    /// TurnsRemaining; при 0 откатывает контроль (RevertControl) и публикует ControlRevertedNetEvent →
    /// CollectActionSystem шлёт ActionControlRevertData, пассив повторяет откат по ключу. Считает ТОЛЬКО
    /// актив (TurnEndedEvent публикует EndTurnRequestSystem у активного) — пассив сам НЕ тикает (как
    /// таймер-смерти), иначе несимметричные границы хода рассинхронили бы владельца. Контроль-на-месте →
    /// откатываем только OwnerComponent + Own/EnemyCardTag (позицию/визуал TakeControl не менял).
    /// </summary>
    public sealed class TempControlRevertSystem : IEcsInitSystem, IEcsRunSystem, System.IDisposable
    {
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<TempControlledComponent>> _filter = default;
        readonly EcsPoolInject<TempControlledComponent> _tempPool = default;

        readonly Queue<int> _pending = new Queue<int>();
        readonly List<int> _expired = new List<int>();
        bool _subscribed;

        public void Init(IEcsSystems systems) => Subscribe();
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

        // Конец хода контролёра playerId: дотикиваем срок его временных контролей; истёкшие откатываем + шлём.
        void Tick(int playerId)
        {
            _expired.Clear();
            foreach (var entity in _filter.Value)
            {
                ref var t = ref _tempPool.Value.Get(entity);
                if (t.ExpiresOnPlayerId != playerId) continue;
                t.TurnsRemaining--;
                if (t.TurnsRemaining <= 0) _expired.Add(entity);
            }

            foreach (var entity in _expired)
            {
                RevertControl(_world.Value, entity);
                GameEventBus.Publish(new ControlRevertedNetEvent { CreatureEntity = entity });   // → ActionControlRevertData
            }
        }

        /// <summary>Откат контроля-на-месте по локальной TempControlledComponent: исходный OwnerId (абсолютный,
        /// одинаков у обоих) + Own/EnemyCardTag (OriginalWasOwn клиент-относителен, захвачен на каждом клиенте).
        /// Зовут TempControlRevertSystem (актив) и ReplayActionSystem (пассив по ActionControlRevertData).</summary>
        public static void RevertControl(EcsWorld world, int entity)
        {
            var tempPool = world.GetPool<TempControlledComponent>();
            if (!tempPool.Has(entity)) return;
            ref var t = ref tempPool.Get(entity);

            var ownerPool = world.GetPool<OwnerComponent>();
            if (ownerPool.Has(entity) && t.OriginalOwnerId >= 0)
                ownerPool.Get(entity).OwnerId = t.OriginalOwnerId;

            var ownTag = world.GetPool<OwnCardTag>();
            var enemyTag = world.GetPool<EnemyCardTag>();
            if (t.OriginalWasOwn)
            {
                if (enemyTag.Has(entity)) enemyTag.Del(entity);
                if (!ownTag.Has(entity))  ownTag.Add(entity);
            }
            else
            {
                if (ownTag.Has(entity))   ownTag.Del(entity);
                if (!enemyTag.Has(entity)) enemyTag.Add(entity);
            }

            tempPool.Del(entity);
        }
    }
}
