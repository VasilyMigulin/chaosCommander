using Game.Core.Shared.Interface;
using Game.Core.Ecs.Components;
using Leopotam.EcsLite;

namespace Game.Core.Model.Effect
{
    /// <summary>
    /// Разыграть карту, указанную в TargetEntity (обычно выбранную PickCardEffect).
    /// ApplyCastTargetCardSystem вешает CastEvent на цель.
    /// </summary>
    public class CastTargetCardEffect : AbilityEffect
    {
        public CastTargetCardEffect() { }
        private CastTargetCardEffect(CastTargetCardEffect _) { }

        public override void AddEffect(EcsWorld world, int effectEntity)
        {
            var pool = world.GetPool<CastTargetCardEffectComponent>();
            if (!pool.Has(effectEntity))
                pool.Add(effectEntity);
        }

        public override IAbilityEffect Clone() => new CastTargetCardEffect(this);
    }
}
