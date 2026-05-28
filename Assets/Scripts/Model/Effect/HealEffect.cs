using Game.Core.Shared.Interface;
using Game.Core.Ecs.Components;
using Leopotam.EcsLite;

namespace Game.Core.Model.Effect
{
    /// <summary>Лечит цель (существо или игрока) на указанное значение.</summary>
    public class HealEffect : AbilityEffect
    {
        public int Value;

        public HealEffect() { }

        public HealEffect(int value)
        {
            Value = value;
        }

        private HealEffect(HealEffect source)
        {
            Value = source.Value;
        }

        public override void AddEffect(EcsWorld world, int effectEntity)
        {
            var pool = world.GetPool<HealEffectComponent>();
            if (!pool.Has(effectEntity))
            {
                ref var comp = ref pool.Add(effectEntity);
                comp.Amount = Value;
            }
            else
            {
                pool.Get(effectEntity).Amount += Value;
            }
        }

        public override IAbilityEffect Clone() => new HealEffect(this);
    }
}
