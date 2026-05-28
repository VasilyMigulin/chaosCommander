using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Обрабатывает GainManaEffectComponent на effect entity.
    /// Применяет прирост маны к ManaComponent на TargetEntity.
    /// </summary>
    public sealed class ApplyGainManaSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<HitComponent, GainManaEffectComponent, TargetEntityComponent>> _filter = default;
        readonly EcsPoolInject<GainManaEffectComponent> _gainManaPool = default;
        readonly EcsPoolInject<TargetEntityComponent> _targetPool = default;
        readonly EcsPoolInject<ManaComponent> _manaPool = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var effectEntity in _filter.Value)
            {
                ref var effect = ref _gainManaPool.Value.Get(effectEntity);
                int targetEntity = _targetPool.Value.Get(effectEntity).TargetEntity;

                if (_manaPool.Value.Has(targetEntity))
                {
                    ref var mana = ref _manaPool.Value.Get(targetEntity);
                    mana.Current = System.Math.Min(mana.Current + effect.Amount, mana.Max);
                }

                _world.Value.DelEntity(effectEntity);
            }
        }
    }
}
