using Game.Core.Shared.Interface;
using Game.Core.Ecs.Components;
using Leopotam.EcsLite;

namespace Game.Core.Model.Effect
{
    /// <summary>
    /// Нанести Amount урона игроку-владельцу источника способности.
    /// </summary>
    public class DealDamageOwnerEffect : AbilityEffect
    {
        public int Amount;

        public DealDamageOwnerEffect() { }
        public DealDamageOwnerEffect(int amount) { Amount = amount; }
        private DealDamageOwnerEffect(DealDamageOwnerEffect source) { Amount = source.Amount; }

        public override void AddEffect(EcsWorld world, int effectEntity)
        {
            var pool = world.GetPool<DealDamageOwnerEffectComponent>();
            if (!pool.Has(effectEntity))
            {
                ref var c = ref pool.Add(effectEntity);
                c.Amount = Amount;
            }
            else
            {
                pool.Get(effectEntity).Amount += Amount;
            }
        }

        public override IAbilityEffect Clone() => new DealDamageOwnerEffect(this);
    }
}
