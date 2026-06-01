using Game.Core.Shared.Interface;
using Game.Core.Ecs.Components;
using Leopotam.EcsLite;

namespace Game.Core.Model.Effect
{
    /// <summary>
    /// Забрать TargetEntity (существо на доске) под контроль источника.
    /// Эквивалент «Чертовщина», «Машина пропаганды», «Обращение в веру».
    /// </summary>
    public class TakeControlEffect : AbilityEffect
    {
        public TakeControlEffect() { }
        private TakeControlEffect(TakeControlEffect _) { }

        public override void AddEffect(EcsWorld world, int effectEntity)
        {
            var pool = world.GetPool<TakeControlEffectComponent>();
            if (!pool.Has(effectEntity)) pool.Add(effectEntity);
        }

        public override IAbilityEffect Clone() => new TakeControlEffect(this);
    }
}
