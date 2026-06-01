using Game.Core.Shared.Interface;
using Game.Core.Ecs.Components;
using Leopotam.EcsLite;

namespace Game.Core.Model.Effect
{
    /// <summary>Заполняет руку владельца указанной моделью до лимита.</summary>
    public class FillHandEffect : AbilityEffect
    {
        public string ExpansionId;
        public int CardId;

        public FillHandEffect() { }
        public FillHandEffect(string expansionId, int cardId) { ExpansionId = expansionId; CardId = cardId; }
        private FillHandEffect(FillHandEffect source) { ExpansionId = source.ExpansionId; CardId = source.CardId; }

        public override void AddEffect(EcsWorld world, int effectEntity)
        {
            var pool = world.GetPool<FillHandEffectComponent>();
            if (!pool.Has(effectEntity))
            {
                ref var c = ref pool.Add(effectEntity);
                c.ExpansionId = ExpansionId; c.CardId = CardId;
            }
        }

        public override IAbilityEffect Clone() => new FillHandEffect(this);
    }
}
