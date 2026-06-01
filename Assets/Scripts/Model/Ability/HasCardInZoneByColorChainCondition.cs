using Leopotam.EcsLite;
using Game.Core.Ecs.Components;
using Game.Core.Service;

namespace Game.Core.Model.Ability
{
    /// <summary>«Если у игрока в указанных зонах есть карта с одним из RequiredColors».</summary>
    [System.Serializable]
    public class HasCardInZoneByColorChainCondition : ChainCondition
    {
        public BuffZone Zones = BuffZone.Deck;
        public EnumService.Element RequiredColors;

        public override bool Evaluate(EcsWorld world, int abilityEntity, int sourceCard, int ownerPlayerId, int ownerPlayerEntity)
        {
            if (RequiredColors == 0) return true;

            var modelPool = world.GetPool<CardModelComponent>();
            var ownerPool = world.GetPool<OwnerComponent>();
            var redP = world.GetPool<RedTag>();
            var blueP = world.GetPool<BlueTag>();
            var greenP = world.GetPool<GreenTag>();
            var yelP = world.GetPool<YellowTag>();
            var whP = world.GetPool<WhiteTag>();
            var blP = world.GetPool<BlackTag>();

            bool HasColor(int e)
            {
                EnumService.Element c = 0;
                if (redP.Has(e))   c |= EnumService.Element.Red;
                if (blueP.Has(e))  c |= EnumService.Element.Blue;
                if (greenP.Has(e)) c |= EnumService.Element.Green;
                if (yelP.Has(e))   c |= EnumService.Element.Yellow;
                if (whP.Has(e))    c |= EnumService.Element.White;
                if (blP.Has(e))    c |= EnumService.Element.Black;
                return (c & RequiredColors) != 0;
            }

            if ((Zones & BuffZone.Hand) != 0)
            {
                var handPool = world.GetPool<HandComponent>();
                if (ownerPlayerEntity >= 0 && handPool.Has(ownerPlayerEntity))
                {
                    ref var hand = ref handPool.Get(ownerPlayerEntity);
                    if (hand.CardEntities != null)
                        foreach (var ce in hand.CardEntities) if (HasColor(ce)) return true;
                }
            }
            if ((Zones & BuffZone.Deck) != 0)
            {
                var deckPool = world.GetPool<DeckComponent>();
                if (ownerPlayerEntity >= 0 && deckPool.Has(ownerPlayerEntity))
                {
                    ref var deck = ref deckPool.Get(ownerPlayerEntity);
                    if (deck.CardEntities != null)
                        foreach (var ce in deck.CardEntities) if (HasColor(ce)) return true;
                }
            }
            if ((Zones & BuffZone.Grave) != 0)
            {
                foreach (var ce in world.Filter<GraveTag>().Inc<OwnerComponent>().End())
                {
                    if (ownerPool.Get(ce).OwnerId != ownerPlayerId) continue;
                    if (HasColor(ce)) return true;
                }
            }
            if ((Zones & BuffZone.Board) != 0)
            {
                foreach (var ce in world.Filter<BoardTag>().Inc<OwnerComponent>().End())
                {
                    if (ownerPool.Get(ce).OwnerId != ownerPlayerId) continue;
                    if (HasColor(ce)) return true;
                }
            }
            return false;
        }
    }
}
