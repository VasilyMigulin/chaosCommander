using Game.Core.Shared.Interface;
using Game.Core.Ecs.Components;
using Leopotam.EcsLite;

namespace Game.Core.Model.Effect
{
    /// <summary>Даёт целевому игроку указанное количество золота.</summary>
    public class GainGoldEffect : AbilityEffect
    {
        public int Amount;

        public GainGoldEffect() { }

        public GainGoldEffect(int amount)
        {
            Amount = amount;
        }

        private GainGoldEffect(GainGoldEffect source)
        {
            Amount = source.Amount;
        }

        public override void AddEffect(EcsWorld world, int effectEntity)
        {
            var pool = world.GetPool<GainGoldEffectComponent>();
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

        public override IAbilityEffect Clone() => new GainGoldEffect(this);
    }
}
