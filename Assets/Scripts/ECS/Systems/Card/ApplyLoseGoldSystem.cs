using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Service;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Применяет LoseGoldEffectComponent: снимает золото с TargetEntity (игрока),
    /// публикует ResourceChangedEvent.
    /// </summary>
    public sealed class ApplyLoseGoldSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<HitComponent, LoseGoldEffectComponent, TargetEntityComponent>> _filter = default;
        readonly EcsPoolInject<LoseGoldEffectComponent> _losePool = default;
        readonly EcsPoolInject<TargetEntityComponent> _targetPool = default;
        readonly EcsPoolInject<GoldComponent> _goldPool = default;
        readonly EcsPoolInject<LocalComponent> _localPool = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var effectEntity in _filter.Value)
            {
                int amount = _losePool.Value.Get(effectEntity).Amount;
                int target = _targetPool.Value.Get(effectEntity).TargetEntity;

                if (_goldPool.Value.Has(target) && amount > 0)
                {
                    ref var gold = ref _goldPool.Value.Get(target);
                    gold.Current = System.Math.Max(0, gold.Current - amount);

                    GameEventBus.Publish(new ResourceChangedEvent
                    {
                        isLocalPlayer = _localPool.Value.Has(target),
                        Type = EnumService.ResourceType.Gold,
                        NewValue = gold.Current,
                        MaxValue = gold.Max,
                    });
                }

                _world.Value.DelEntity(effectEntity);
            }
        }
    }
}
