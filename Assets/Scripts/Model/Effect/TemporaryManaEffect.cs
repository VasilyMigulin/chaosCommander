using Game.Core.Shared.Interface;
using Game.Core.Ecs.Components;
using Leopotam.EcsLite;

namespace Game.Core.Model.Effect
{
    /// <summary>«+N маны до конца хода». Освежающий напиток.</summary>
    public class TemporaryManaEffect : AbilityEffect
    {
        public int Amount;

        public TemporaryManaEffect() { }
        public TemporaryManaEffect(int amount) { Amount = amount; }
        private TemporaryManaEffect(TemporaryManaEffect s) { Amount = s.Amount; }

        public override void AddEffect(EcsWorld world, int effectEntity)
        {
            var pool = world.GetPool<TemporaryManaEffectComponent>();
            if (!pool.Has(effectEntity)) { ref var c = ref pool.Add(effectEntity); c.Amount = Amount; }
            else pool.Get(effectEntity).Amount += Amount;
        }

        public override IAbilityEffect Clone() => new TemporaryManaEffect(this);
    }
}
