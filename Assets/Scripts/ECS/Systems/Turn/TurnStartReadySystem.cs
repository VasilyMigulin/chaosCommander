using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Переводит фазу хода из TurnStartAbilities → PlayerTurn
    /// когда очередь способностей пуста и нет активных эффектов.
    /// После перехода TurnStartResourceSystem выдаёт ресурсы и карту.
    /// </summary>
    public sealed class TurnStartReadySystem : IEcsRunSystem
    {
        readonly EcsPoolInject<TurnPhaseState> _phasePool = default;
        readonly EcsFilterInject<Inc<TurnState, TurnPhaseState>> _activePlayerFilter = default;
        readonly EcsFilterInject<Inc<AbilityQueueTag, AbilityQueueComponent>, Exc<LockState>> _queueFilter = default;
        readonly EcsFilterInject<Inc<EffectComponent, ActiveState>> _activeEffectsFilter = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var entity in _activePlayerFilter.Value)
            {
                ref var phase = ref _phasePool.Value.Get(entity);
                if (phase.Phase != TurnPhase.TurnStartAbilities)
                    continue;

                bool queueBusy = _queueFilter.Value.GetEntitiesCount() == 0
                    ? false
                    : HasPendingAbilities();

                bool effectsActive = _activeEffectsFilter.Value.GetEntitiesCount() > 0;

                if (!queueBusy && !effectsActive)
                    phase.Phase = TurnPhase.PlayerTurn;
            }
        }

        private bool HasPendingAbilities()
        {
            foreach (var q in _queueFilter.Value)
                return true;
            return false;
        }
    }
}
