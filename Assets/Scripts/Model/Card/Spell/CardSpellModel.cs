using Game.Core.Ecs.Components;
using Leopotam.EcsLite;

namespace Game.Core.Model.Card.Spell
{
    /// <summary>
    /// Data model for spell cards.
    /// Played using Mana resource. Applies effects immediately, then goes to grave.
    /// </summary>
    public class CardSpellModel : CardModel
    {
        /// <summary>Whether this spell requires a board target.</summary>
        public bool RequiresTarget;

        public override Service.EnumService.CardType GetCardType() => Service.EnumService.CardType.Spell;

        protected override void OnInit(EcsWorld world, int entityCard, int playerOwnerEntity, bool isCommander)
        {
            world.GetPool<SpellTag>().Add(entityCard);
        }
    }
}

