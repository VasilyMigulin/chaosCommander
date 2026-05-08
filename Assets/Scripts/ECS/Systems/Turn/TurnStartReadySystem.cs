using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Photon;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Ждёт пока на этом клиенте завершатся OnTurnStart-способности (фаза TurnStartAbilities).
    /// Когда очередь пуста и нет активных эффектов — вызывает RPC_NotifyTurnStartReady на хост.
    /// Хост после получения от обоих клиентов пришлёт RPC_PlayerTurnBegin.
    /// </summary>
    public sealed class TurnStartReadySystem : IEcsRunSystem
    {
        readonly EcsPoolInject<TurnPhaseState> _phasePool = default;
        readonly EcsFilterInject<Inc<TurnPhaseState, PlayerComponent>> _allPlayersFilter = default;
        readonly EcsFilterInject<Inc<AbilityQueueTag, AbilityQueueComponent>, Exc<LockState>> _queueFilter = default;
        readonly EcsPoolInject<AbilityQueueComponent> _queuePool = default;
        readonly EcsFilterInject<Inc<EffectComponent, ActiveState>> _activeEffectsFilter = default;
        readonly EcsCustomInject<PhotonRunHandler> _photon = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var entity in _allPlayersFilter.Value)
            {
                ref var phase = ref _phasePool.Value.Get(entity);
                if (phase.Phase != TurnPhase.TurnStartAbilities)
                    continue;

                bool queueHasItems = false;
                foreach (var qe in _queueFilter.Value)
                {
                    ref var q = ref _queuePool.Value.Get(qe);
                    queueHasItems = q.Abilities != null && q.Abilities.Count > 0;
                    break;
                }
                if (queueHasItems)
                    continue;

                if (_activeEffectsFilter.Value.GetEntitiesCount() > 0)
                    continue;

                // Всё отработало — переходим в ожидание подтверждения от хоста
                phase.Phase = TurnPhase.WaitingForHostAck;
                _photon.Value.RPC_NotifyTurnStartReady();
            }
        }
    }
}
