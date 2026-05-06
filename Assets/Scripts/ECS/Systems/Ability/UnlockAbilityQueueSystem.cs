using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Снимает LockState с очереди способностей когда все активности завершены:
    ///   - нет ResolveAbilityEvent
    ///   - нет активных эффектов (EffectComponent + ActiveState)
    ///   - нет ожидающих анимаций атаки (AttackAnimPendingTag)
    /// Публикует InputRestoredEvent если очередь пуста после разблокировки.
    /// </summary>
    public sealed class UnlockAbilityQueueSystem : IEcsRunSystem
    {
        readonly EcsFilterInject<Inc<LockState, AbilityQueueTag>> _lockedQueueFilter = default;
        readonly EcsFilterInject<Inc<ResolveAbilityEvent>> _resolveFilter = default;
        readonly EcsFilterInject<Inc<EffectComponent, ActiveState>> _activeEffectsFilter = default;
        readonly EcsFilterInject<Inc<AttackAnimPendingTag>> _animPendingFilter = default;
        readonly EcsPoolInject<LockState> _lockPool = default;
        readonly EcsPoolInject<AbilityQueueComponent> _queuePool = default;

        public void Run(IEcsSystems systems)
        {
            if (_lockedQueueFilter.Value.GetEntitiesCount() == 0)
                return;

            bool resolveActive = _resolveFilter.Value.GetEntitiesCount() > 0;
            bool effectsActive = _activeEffectsFilter.Value.GetEntitiesCount() > 0;
            bool animPending   = _animPendingFilter.Value.GetEntitiesCount() > 0;

            if (resolveActive || effectsActive || animPending)
                return;

            foreach (var queueEntity in _lockedQueueFilter.Value)
            {
                _lockPool.Value.Del(queueEntity);

                if (_queuePool.Value.Has(queueEntity))
                {
                    ref var q = ref _queuePool.Value.Get(queueEntity);
                    if (q.Abilities.Count == 0)
                        GameEventBus.Publish(new InputRestoredEvent());
                }
            }
        }
    }
}
