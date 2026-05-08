using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Photon;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Ждёт пока на этом клиенте завершатся OnTurnEnd-способности (фаза TurnEndAbilities).
    /// Когда очередь пуста и нет активных эффектов — вызывает RPC_NotifyTurnEndReady на хост.
    /// Хост после получения от обоих клиентов пришлёт RPC_TurnStartPhaseBegin(nextPlayerId).
    /// </summary>
    public sealed class TurnEndReadySystem : IEcsRunSystem
    {
        readonly EcsPoolInject<TurnState> _turnStatePool = default;
        readonly EcsPoolInject<TurnPhaseState> _phasePool = default;
        readonly EcsPoolInject<TurnResourcesGrantedTag> _grantedPool = default;
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
                if (phase.Phase != TurnPhase.TurnEndAbilities)
                    continue;

                if (IsQueueBusy() || _activeEffectsFilter.Value.GetEntitiesCount() > 0)
                    continue;

                // Всё отработало — переходим в ожидание подтверждения от хоста
                phase.Phase = TurnPhase.WaitingForHostAck;

                // Снимаем TurnState и маркер ресурсов если это был активный игрок
                if (_turnStatePool.Value.Has(entity))
                {
                    _turnStatePool.Value.Del(entity);
                    if (_grantedPool.Value.Has(entity))
                        _grantedPool.Value.Del(entity);
                }

                _photon.Value.RPC_NotifyTurnEndReady();
            }
        }

        private bool IsQueueBusy()
        {
            foreach (var qe in _queueFilter.Value)
            {
                ref var q = ref _queuePool.Value.Get(qe);
                return q.Abilities != null && q.Abilities.Count > 0;
            }
            return false;
        }
    }
}