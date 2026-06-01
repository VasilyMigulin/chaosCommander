using Game.Core.Shared.Interface;
using Game.Core.Ecs.Components;
using Leopotam.EcsLite;

namespace Game.Core.Model.Effect
{
    /// <summary>Создать карту указанной модели в руку владельца цели.</summary>
    public class GiveCardToHandEffect : AbilityEffect
    {
        public string ExpansionId;
        public int CardId;
        public int Count = 1;

        public GiveCardToHandEffect() { }
        public GiveCardToHandEffect(string expansionId, int cardId, int count = 1)
        {
            ExpansionId = expansionId; CardId = cardId; Count = count;
        }

        private GiveCardToHandEffect(GiveCardToHandEffect source)
        {
            ExpansionId = source.ExpansionId; CardId = source.CardId; Count = source.Count;
        }

        public override void AddEffect(EcsWorld world, int effectEntity)
        {
            var pool = world.GetPool<GiveCardToHandEffectComponent>();
            if (!pool.Has(effectEntity))
            {
                ref var c = ref pool.Add(effectEntity);
                c.ExpansionId = ExpansionId; c.CardId = CardId; c.Count = Count;
            }
        }

        public override IAbilityEffect Clone() => new GiveCardToHandEffect(this);
    }
}
