using Leopotam.EcsLite;
using Game.Core.Ecs.Components;

namespace Game.Core.Model.Ability
{
    /// <summary>«Если у игрока в зоне есть карта с указанным ModelId».</summary>
    [System.Serializable]
    public class HasCardInZoneByModelChainCondition : ChainCondition
    {
        public BuffZone Zones = BuffZone.Hand;
        public int ModelId;

        public override bool Evaluate(EcsWorld world, int abilityEntity, int sourceCard, int ownerPlayerId, int ownerPlayerEntity)
        {
            var modelPool = world.GetPool<CardModelComponent>();
            var ownerPool = world.GetPool<OwnerComponent>();

            if ((Zones & BuffZone.Hand) != 0)
            {
                var handPool = world.GetPool<HandComponent>();
                if (ownerPlayerEntity >= 0 && handPool.Has(ownerPlayerEntity))
                {
                    ref var hand = ref handPool.Get(ownerPlayerEntity);
                    if (hand.CardEntities != null)
                        foreach (var ce in hand.CardEntities)
                            if (modelPool.Has(ce) && modelPool.Get(ce).ModelId == ModelId) return true;
                }
            }
            if ((Zones & BuffZone.Deck) != 0)
            {
                var deckPool = world.GetPool<DeckComponent>();
                if (ownerPlayerEntity >= 0 && deckPool.Has(ownerPlayerEntity))
                {
                    ref var deck = ref deckPool.Get(ownerPlayerEntity);
                    if (deck.CardEntities != null)
                        foreach (var ce in deck.CardEntities)
                            if (modelPool.Has(ce) && modelPool.Get(ce).ModelId == ModelId) return true;
                }
            }
            if ((Zones & BuffZone.Grave) != 0)
            {
                foreach (var ce in world.Filter<GraveTag>().Inc<OwnerComponent>().Inc<CardModelComponent>().End())
                {
                    if (ownerPool.Get(ce).OwnerId != ownerPlayerId) continue;
                    if (modelPool.Get(ce).ModelId == ModelId) return true;
                }
            }
            if ((Zones & BuffZone.Board) != 0)
            {
                foreach (var ce in world.Filter<BoardTag>().Inc<OwnerComponent>().Inc<CardModelComponent>().End())
                {
                    if (ownerPool.Get(ce).OwnerId != ownerPlayerId) continue;
                    if (modelPool.Get(ce).ModelId == ModelId) return true;
                }
            }
            return false;
        }
    }
}
