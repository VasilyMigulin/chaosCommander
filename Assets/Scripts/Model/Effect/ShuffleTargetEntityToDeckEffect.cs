using Game.Core.Shared.Interface;
using Game.Core.Ecs.Components;
using Leopotam.EcsLite;

namespace Game.Core.Model.Effect
{
    /// <summary>
    /// Замешать конкретную сущность (TargetEntity) в колоду. В отличие от
    /// ShuffleCardEffect (создаёт новую сущность из модели), берёт существующую:
    /// снимает HandTag/BoardTag/GraveTag и кладёт в DeckComponent.
    /// </summary>
    public class ShuffleTargetEntityToDeckEffect : AbilityEffect
    {
        /// <summary>true — мешать в колоду оппонента (по сторонам), иначе — в свою.</summary>
        public bool IntoOpponentDeck;

        public ShuffleTargetEntityToDeckEffect() { }

        private ShuffleTargetEntityToDeckEffect(ShuffleTargetEntityToDeckEffect source)
        {
            IntoOpponentDeck = source.IntoOpponentDeck;
        }

        public override void AddEffect(EcsWorld world, int effectEntity)
        {
            var pool = world.GetPool<ShuffleTargetEntityEffectComponent>();
            if (pool.Has(effectEntity)) return;

            ref var comp = ref pool.Add(effectEntity);
            comp.IntoOpponentDeck = IntoOpponentDeck;
        }

        public override IAbilityEffect Clone() => new ShuffleTargetEntityToDeckEffect(this);
    }
}
