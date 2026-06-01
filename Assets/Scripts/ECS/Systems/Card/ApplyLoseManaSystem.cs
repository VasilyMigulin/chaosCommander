using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Service;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Применяет LoseManaEffectComponent: снимает ману с TargetEntity (игрока),
    /// публикует ResourceChangedEvent.
    /// </summary>
    public sealed class ApplyLoseManaSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<HitComponent, LoseManaEffectComponent, TargetEntityComponent>> _filter = default;
        readonly EcsPoolInject<LoseManaEffectComponent> _losePool = default;
        readonly EcsPoolInject<TargetEntityComponent> _targetPool = default;
        readonly EcsPoolInject<ManaComponent> _manaPool = default;
        readonly EcsPoolInject<LocalComponent> _localPool = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var effectEntity in _filter.Value)
            {
                int amount = _losePool.Value.Get(effectEntity).Amount;
                int target = _targetPool.Value.Get(effectEntity).TargetEntity;

                if (_manaPool.Value.Has(target) && amount > 0)
                {
                    ref var mana = ref _manaPool.Value.Get(target);
                    mana.Current = System.Math.Max(0, mana.Current - amount);

                    GameEventBus.Publish(new ResourceChangedEvent
                    {
                        isLocalPlayer = _localPool.Value.Has(target),
                        Type = EnumService.ResourceType.Mana,
                        NewValue = mana.Current,
                        MaxValue = mana.Max,
                    });
                }

                _world.Value.DelEntity(effectEntity);
            }
        }
    }
}
