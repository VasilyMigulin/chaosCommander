using Game.Core.Shared.Interface;
using Game.Core.Ecs.Components;
using Leopotam.EcsLite;

namespace Game.Core.Model.Effect
{
    /// <summary>
    /// Удалить TargetEntity из игры безвозвратно. Не запускает OnDie.
    /// Эквивалент «Низвести до атомов».
    /// </summary>
    public class BanishEffect : AbilityEffect
    {
        public BanishEffect() { }
        private BanishEffect(BanishEffect _) { }

        public override void AddEffect(EcsWorld world, int effectEntity)
        {
            var pool = world.GetPool<BanishEffectComponent>();
            if (!pool.Has(effectEntity)) pool.Add(effectEntity);
        }

        public override IAbilityEffect Clone() => new BanishEffect(this);
    }
}
