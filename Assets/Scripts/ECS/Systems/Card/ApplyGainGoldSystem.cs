using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Обрабатывает GainGoldEffectComponent на effect entity.
    /// Применяет прирост золота к GoldComponent на TargetEntity.
    /// </summary>
    public sealed class ApplyGainGoldSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<HitComponent, GainGoldEffectComponent, TargetEntityComponent>> _filter = default;
        readonly EcsPoolInject<GainGoldEffectComponent> _gainGoldPool = default;
        readonly EcsPoolInject<TargetEntityComponent> _targetPool = default;
        readonly EcsPoolInject<GoldComponent> _goldPool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<LocalComponent> _localPool = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var effectEntity in _filter.Value)
            {
                ref var effect = ref _gainGoldPool.Value.Get(effectEntity);
                int targetEntity = _targetPool.Value.Get(effectEntity).TargetEntity;

                if (_goldPool.Value.Has(targetEntity))
                {
                    ref var gold = ref _goldPool.Value.Get(targetEntity);
                    gold.Current = System.Math.Min(gold.Current + effect.Amount, gold.Max);

                    int pid = _playerPool.Value.Has(targetEntity)
                        ? _playerPool.Value.Get(targetEntity).PlayerId
                        : targetEntity;

                    bool isLocalPlayer = _localPool.Value.Has(targetEntity); 

                    GameEventBus.Publish(new Game.Core.Events.ResourceChangedEvent
                    {
                        isLocalPlayer = isLocalPlayer,
                        Type = Game.Core.Service.EnumService.ResourceType.Gold,
                        NewValue = gold.Current,
                        MaxValue = gold.Max
                    });
                }

                _world.Value.DelEntity(effectEntity);
            }
        }
    }
}
