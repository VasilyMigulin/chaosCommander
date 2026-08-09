using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Свойство «Скрытый»: в начале хода ВЛАДЕЛЬЦА декрементит StealthComponent.TurnsRemaining (паттерн
    /// CreatureTimerTickSystem). На 0 компонент просто СНИМАЕТСЯ (не смерть) — носитель снова виден
    /// вражескому таргетингу (RunSelectCellSystem/RunAiTurnSystem/TargetGather).
    /// </summary>
    public sealed class StealthTickSystem : IEcsInitSystem, IEcsRunSystem, IEcsDestroySystem, System.IDisposable
    {
        readonly EcsFilterInject<Inc<StealthComponent, BoardTag, OwnerComponent>> _filter = default;
        readonly EcsPoolInject<StealthComponent> _stealthPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;

        readonly Queue<int> _pending = new Queue<int>();
        bool _subscribed;

        public void Init(IEcsSystems systems) => Subscribe();
        public void Destroy(IEcsSystems systems) => Dispose();

        public void Dispose()
        {
            if (!_subscribed) return;
            GameEventBus.Unsubscribe<TurnStartedEvent>(OnTurnStarted);
            _subscribed = false;
        }

        void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            GameEventBus.Subscribe<TurnStartedEvent>(OnTurnStarted);
        }

        void OnTurnStarted(TurnStartedEvent e) => _pending.Enqueue(e.ActivePlayerId);

        public void Run(IEcsSystems systems)
        {
            while (_pending.Count > 0) Tick(_pending.Dequeue());
        }

        void Tick(int playerId)
        {
            // Буфер ПЕРЕД мутацией: Del(StealthComponent) меняет членство в ЭТОМ ЖЕ фильтре — своп-удаления
            // ecslite при мутации во время foreach могут пропустить/перепутать порядок (см. [[project_turn_system]]).
            var buf = new List<int>();
            foreach (var entity in _filter.Value)
                if (_ownerPool.Value.Get(entity).OwnerId == playerId) buf.Add(entity);

            foreach (var entity in buf)
            {
                ref var s = ref _stealthPool.Value.Get(entity);
                s.TurnsRemaining--;
                if (s.TurnsRemaining <= 0) _stealthPool.Value.Del(entity);
            }
        }
    }
}
