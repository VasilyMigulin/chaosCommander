using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// СИНК начала ЧУЖОГО хода на пассиве: восстанавливает скорость и сбрасывает счётчик атак существ игрока,
    /// чей ход начался. RunTurnStartSystem крутит эту часть каскада ТОЛЬКО у активного клиента (гейт по
    /// StartTurnState, который на пассиве не вешается), поэтому у чужих существ Remaining/AttacksUsed на
    /// пассиве оставались стухшими — скорость визуально «не восстанавливалась» (CreatureStatsViewSystem пушит
    /// Remaining покадрово), а EnsureCanAct латал заряд лишь при реплее действия и лишь при Remaining≤0.
    ///
    /// TurnStartedEvent летит на ОБОИХ клиентах (реплей его тоже публикует) → на нём чиним. Гейтим на «ход НЕ
    /// локального игрока»: свой ход у активного восстанавливает сам RunTurnStartSystem (в PvE — оба, там пассива
    /// нет, наша ветка просто не срабатывает для локального). Восстановление детерминировано (Max) → синк даром.
    /// </summary>
    public sealed class SyncOpponentTurnStartSystem : IEcsRunSystem, IEcsInitSystem, IEcsDestroySystem, System.IDisposable
    {
        readonly EcsFilterInject<Inc<CreatureTag, BoardTag, SpeedComponent, OwnerComponent>, Exc<DeadTag>> _creatures = default;
        readonly EcsFilterInject<Inc<PlayerComponent, LocalComponent>> _localPlayer = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<SpeedComponent> _speedPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<AttacksUsedComponent> _attacksUsedPool = default;

        readonly Queue<int> _turns = new Queue<int>();   // ActivePlayerId с TurnStartedEvent
        bool _subscribed;

        public void Init(IEcsSystems systems)
        {
            if (_subscribed) return;
            _subscribed = true;
            GameEventBus.Subscribe<TurnStartedEvent>(OnTurnStarted);
        }

        public void Destroy(IEcsSystems systems) => Dispose();

        public void Dispose()
        {
            if (!_subscribed) return;
            GameEventBus.Unsubscribe<TurnStartedEvent>(OnTurnStarted);
            _subscribed = false;
        }

        void OnTurnStarted(TurnStartedEvent e) => _turns.Enqueue(e.ActivePlayerId);

        public void Run(IEcsSystems systems)
        {
            if (_turns.Count == 0) return;

            int localId = -1;
            foreach (var pe in _localPlayer.Value) { localId = _playerPool.Value.Get(pe).PlayerId; break; }

            while (_turns.Count > 0)
            {
                int playerId = _turns.Dequeue();
                if (playerId < 0 || playerId == localId) continue;   // свой ход — восстанавливает RunTurnStartSystem у активного

                foreach (var ce in _creatures.Value)
                {
                    if (_ownerPool.Value.Get(ce).OwnerId != playerId) continue;
                    ref var sp = ref _speedPool.Value.Get(ce);
                    sp.Remaining = sp.Max;
                    if (_attacksUsedPool.Value.Has(ce)) _attacksUsedPool.Value.Get(ce).Value = 0;
                }
            }
        }
    }
}
