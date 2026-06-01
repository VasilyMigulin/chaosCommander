using Game.Core.Shared.Interface;
using Game.Core.Ecs.Components;
using Leopotam.EcsLite;

namespace Game.Core.Model.Effect
{
    /// <summary>Снимает Amount маны с TargetEntity (игрока). Используется «Неудачливый чародей».</summary>
    public class LoseManaEffect : AbilityEffect
    {
        public int Amount;

        public LoseManaEffect() { }
        public LoseManaEffect(int amount) { Amount = amount; }
        private LoseManaEffect(LoseManaEffect source) { Amount = source.Amount; }

        public override void AddEffect(EcsWorld world, int effectEntity)
        {
            var pool = world.GetPool<LoseManaEffectComponent>();
            if (!pool.Has(effectEntity)) { ref var c = ref pool.Add(effectEntity); c.Amount = Amount; }
            else pool.Get(effectEntity).Amount += Amount;
        }

        public override IAbilityEffect Clone() => new LoseManaEffect(this);
    }
}
