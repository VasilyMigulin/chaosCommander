using Game.Core.Shared.Interface;
using Game.Core.Ecs.Components;
using Leopotam.EcsLite;

namespace Game.Core.Model.Effect
{
    /// <summary>
    /// Снимает Amount золота с TargetEntity (игрока). Используется «Штраф на предприятии».
    /// </summary>
    public class LoseGoldEffect : AbilityEffect
    {
        public int Amount;

        public LoseGoldEffect() { }
        public LoseGoldEffect(int amount) { Amount = amount; }
        private LoseGoldEffect(LoseGoldEffect source) { Amount = source.Amount; }

        public override void AddEffect(EcsWorld world, int effectEntity)
        {
            var pool = world.GetPool<LoseGoldEffectComponent>();
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

        public override IAbilityEffect Clone() => new LoseGoldEffect(this);
    }
}
