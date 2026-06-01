using Game.Core.Shared.Interface;
using Game.Core.Ecs.Components;
using Leopotam.EcsLite;

namespace Game.Core.Model.Effect
{
    /// <summary>
    /// Шаг цепочки: «раскопать карту» (выбрать одну из N предложенных).
    /// Запускает асинхронный пик через ApplyPickCardSystem, результат пишется в
    /// ChainStateComponent.ProducedEntity → следующий шаг с TargetSource = PreviousProduced
    /// получит выбранную карту как цель.
    /// </summary>
    public class PickCardEffect : AbilityEffect
    {
        public CardPickSourceType Source = CardPickSourceType.PlayerDeck;
        public int OfferCount = 3;
        public int[] UniquePoolModelIds;

        public PickCardEffect() { }

        private PickCardEffect(PickCardEffect source)
        {
            Source = source.Source;
            OfferCount = source.OfferCount;
            UniquePoolModelIds = source.UniquePoolModelIds != null
                ? (int[])source.UniquePoolModelIds.Clone()
                : null;
        }

        public override void AddEffect(EcsWorld world, int effectEntity)
        {
            var pool = world.GetPool<PickCardEffectComponent>();
            if (pool.Has(effectEntity)) return;

            ref var comp = ref pool.Add(effectEntity);
            comp.Source = Source;
            comp.OfferCount = OfferCount;
            comp.UniquePoolModelIds = UniquePoolModelIds;
        }

        public override IAbilityEffect Clone() => new PickCardEffect(this);
    }
}
