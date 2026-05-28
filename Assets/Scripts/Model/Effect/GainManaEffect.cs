using Game.Core.Shared.Interface;
using Game.Core.Ecs.Components;
using Leopotam.EcsLite;

namespace Game.Core.Model.Effect
{
    /// <summary>Даёт целевому игроку указанное количество маны.</summary>
    public class GainManaEffect : AbilityEffect
    {
        public int Amount;

        public GainManaEffect() { }

        public GainManaEffect(int amount)
        {
            Amount = amount;
        }

        private GainManaEffect(GainManaEffect source)
        {
            Amount = source.Amount;
        }

        public override void AddEffect(EcsWorld world, int effectEntity)
        {
            var pool = world.GetPool<GainManaEffectComponent>();
            if (!pool.Has(effectEntity))
            {
                ref var comp = ref pool.Add(effectEntity);
                comp.Amount = Amount;
            }
            else
            {
                pool.Get(effectEntity).Amount += Amount;
            }
        }

        public override IAbilityEffect Clone() => new GainManaEffect(this);
    }
}
