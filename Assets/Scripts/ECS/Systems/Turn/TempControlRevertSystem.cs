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
    public sealed class TempControlRevertSystem : IEcsInitSystem, IEcsRunSystem, IEcsDestroySystem, System.IDisposable
    {
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<TempControlledComponent>> _filter = default;
        readonly EcsPoolInject<TempControlledComponent> _tempPool = default;

        // ХС-порядок конца хода: откат ЖДЁТ оседания ability-пайплайна — «в конце хода» украденного
        // существа должен разрезолвиться В ПОЛЬЗУ контролёра ДО возврата хозяину (Rebind при откате
        // перепривяжет способности обратно). Набор гейтов — как у EndTurnRequestSystem.AbilitiesPending
        // + анимации; передача хода (Шаг B, _castSystems) в кадре идёт ПОЗЖЕ этой системы (_turnSystems),
        // поэтому при оседании порядок гарантирован: резолв → откат → передача хода.
        readonly EcsFilterInject<Inc<AbilityCastEvent>>       _abilityCast    = default;
        readonly EcsFilterInject<Inc<AbilityTargetingState>>  _abilityTarget  = default;
        readonly EcsFilterInject<Inc<AbilityQueuedState>>     _abilityQueued  = default;
        readonly EcsFilterInject<Inc<RequestCardCastEvent>>   _castRequest    = default;
        readonly EcsFilterInject<Inc<CastEvent>>              _castInProgress = default;
        readonly EcsFilterInject<Inc<PendingOnCastComponent>> _pendingOnCast  = default;
        readonly EcsFilterInject<Inc<MovingTag>>              _moving         = default;
        readonly EcsFilterInject<Inc<AttackAnimPendingTag>>   _attackAnim     = default;

        readonly Queue<int> _pending = new Queue<int>();
        readonly List<int> _expired = new List<int>();
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
        void OnTurnEnded(TurnEndedEvent e) => _pending.Enqueue(e.ActivePlayerId);

        public void Run(IEcsSystems systems)
        {
            if (_pending.Count == 0) return;
            if (PipelineBusy()) return;   // прощальный OnTurnEnd украденного резолвится В ПОЛЬЗУ контролёра до отката
            while (_pending.Count > 0) Tick(_pending.Dequeue());
        }

        bool PipelineBusy()
            => _abilityCast.Value.GetEntitiesCount()    > 0
            || _abilityTarget.Value.GetEntitiesCount()  > 0
            || _abilityQueued.Value.GetEntitiesCount()  > 0
            || _castRequest.Value.GetEntitiesCount()    > 0
            || _castInProgress.Value.GetEntitiesCount() > 0
            || _pendingOnCast.Value.GetEntitiesCount()  > 0
            || _moving.Value.GetEntitiesCount()         > 0
            || _attackAnim.Value.GetEntitiesCount()     > 0;

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

            int originalOwnerId = t.OriginalOwnerId;
            tempPool.Del(entity);

            // ХС-семантика: способности вернувшегося существа снова «служат» исходному хозяину (зеркало
            // пере-привязки при краже, см. AbilityOwnershipUtil в сборке Ability).
            AbilityRebindUtil.RebindToPlayerId(world, entity, originalOwnerId);
        }
    }
}
