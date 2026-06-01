using Game.Core.Shared.Interface;
using Game.Core.Ecs.Components;
using Leopotam.EcsLite;

namespace Game.Core.Model.Effect
{
    /// <summary>Временно забрать под контроль (до конца хода).</summary>
    public class TempTakeControlEffect : AbilityEffect
    {
        public TempTakeControlEffect() { }
        private TempTakeControlEffect(TempTakeControlEffect _) { }

        public override void AddEffect(EcsWorld world, int effectEntity)
        {
            var pool = world.GetPool<TempTakeControlEffectComponent>();
            if (!pool.Has(effectEntity)) pool.Add(effectEntity);
        }

        public override IAbilityEffect Clone() => new TempTakeControlEffect(this);
    }
}
