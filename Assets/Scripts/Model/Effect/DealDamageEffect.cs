using Game.Core.Shared.Interface;
using Game.Core.Ecs.Components;
using Leopotam.EcsLite;

namespace Game.Core.Model.Effect
{
    public class DealDamageEffect : AbilityEffect
    {
        public int Value;

        public DealDamageEffect() { }

        public DealDamageEffect(int value)
        {
            Value = value;
        }

        private DealDamageEffect(DealDamageEffect source)
        {
            Value = source.Value;
        }

        public override void AddEffect(EcsWorld world, int effectEntity)
        {
            if (!world.GetPool<TakeDamageEvent>().Has(effectEntity))
            {
                ref var dmg = ref world.GetPool<TakeDamageEvent>().Add(effectEntity);
                dmg.Amount = Value;
                dmg.Attacker = -1;
            }
            else
            {
                world.GetPool<TakeDamageEvent>().Get(effectEntity).Amount += Value;
            }
        }

        public override IAbilityEffect Clone()
        {
            return new DealDamageEffect(this);
        }
    }
}