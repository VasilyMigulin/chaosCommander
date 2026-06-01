using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Service;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Прибавляет N маны TargetEntity (игроку) и записывает RefundAmount в
    /// TemporaryManaComponent — в конце хода TemporaryManaRefundSystem заберёт обратно.
    /// </summary>
    public sealed class ApplyTemporaryManaSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<HitComponent, TemporaryManaEffectComponent, TargetEntityComponent>> _filter = default;
        readonly EcsPoolInject<TemporaryManaEffectComponent> _effPool = default;
        readonly EcsPoolInject<TargetEntityComponent> _targetPool = default;
        readonly EcsPoolInject<ManaComponent> _manaPool = default;
        readonly EcsPoolInject<TemporaryManaComponent> _tempPool = default;
        readonly EcsPoolInject<LocalComponent> _localPool = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var effectEntity in _filter.Value)
            {
                int amount = _effPool.Value.Get(effectEntity).Amount;
                int target = _targetPool.Value.Get(effectEntity).TargetEntity;

                if (_manaPool.Value.Has(target) && amount > 0)
                {
                    ref var mana = ref _manaPool.Value.Get(target);
                    mana.Current += amount;
                    if (mana.Current > mana.Max) mana.Max = mana.Current;

                    if (!_tempPool.Value.Has(target)) _tempPool.Value.Add(target);
                    _tempPool.Value.Get(target).RefundAmount += amount;

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
