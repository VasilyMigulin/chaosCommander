using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Photon;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Единая система ожидания завершения способностей для фаз:
    ///   MatchStartAbilities → RPC_NotifyMatchStartReady
    ///   TurnStartAbilities  → RPC_NotifyTurnStartReady
    ///   TurnEndAbilities    → RPC_NotifyTurnEndReady + очистка TurnState
    /// Когда очередь пуста и нет активных эффектов — переводит игрока в WaitingForHostAck
    /// и отправляет соответствующий RPC на хост.
    /// </summary>
    public sealed class PhaseReadySystem : IEcsRunSystem
    {
        readonly EcsPoolInject<TurnPhaseState> _phasePool = default;
        readonly EcsPoolInject<TurnState> _turnStatePool = default;
        readonly EcsPoolInject<TurnResourcesGrantedTag> _grantedPool = default;
        readonly EcsFilterInject<Inc<TurnPhaseState, PlayerComponent, LocalComponent>> _localPlayerFilter = default;
        readonly EcsFilterInject<Inc<AbilityQueueTag, AbilityQueueComponent>, Exc<LockState>> _queueFilter = default;
        readonly EcsPoolInject<AbilityQueueComponent> _queuePool = default;
        readonly EcsFilterInject<Inc<EffectComponent, ActiveState>> _activeEffectsFilter = default;
        readonly EcsCustomInject<PhotonRunHandler> _photon = default;

        public void Run(IEcsSystems systems)
        {
            if (IsQueueBusy() || _activeEffectsFilter.Value.GetEntitiesCount() > 0)
                return;

            foreach (var entity in _localPlayerFilter.Value)
            {
                ref var phase = ref _phasePool.Value.Get(entity);

                switch (phase.Phase)
                {
                    case TurnPhase.MatchStartAbilities:
                        phase.Phase = TurnPhase.WaitingForHostAck;
                        _photon.Value.RPC_NotifyMatchStartReady();
                        break;

                    case TurnPhase.TurnStartAbilities:
                        phase.Phase = TurnPhase.WaitingForHostAck;
                        _photon.Value.RPC_NotifyTurnStartReady();
                        break;

                    case TurnPhase.TurnEndAbilities:
                        phase.Phase = TurnPhase.WaitingForHostAck;
                        if (_turnStatePool.Value.Has(entity))
                        {
                            _turnStatePool.Value.Del(entity);
                            if (_grantedPool.Value.Has(entity))
                                _grantedPool.Value.Del(entity);
                        }
                        _photon.Value.RPC_NotifyTurnEndReady();
                        break;
                }
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
