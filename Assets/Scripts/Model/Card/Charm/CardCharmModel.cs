using Game.Core.Ecs.Components;
using Leopotam.EcsLite;

namespace Game.Core.Model.Card.Charm
{
    /// <summary>
    /// Data model for aura / charm cards.
    /// Played using Mana resource. Stays on board as a persistent effect source.
    /// Triggers on game events (turn start/end, ally death, etc.).
    /// </summary>
    public class CardCharmModel : CardModel
    {
        public override Game.Core.Service.EnumService.CardType GetCardType() => Game.Core.Service.EnumService.CardType.Charm;

        protected override void OnInit(EcsWorld world, int entityCard, bool isCommander)
        {
            world.GetPool<CharmTag>().Add(entityCard);
        }
    }
}

