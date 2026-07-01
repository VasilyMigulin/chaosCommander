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
        // Время жизни чары в ходах владельца: >0 → уничтожается в КОНЦЕ N-го хода; 0 → постоянная.
        public int TurnsAlive = 0;

        public override Game.Core.Service.EnumService.CardType GetCardType() => Game.Core.Service.EnumService.CardType.Charm;

        // Длительность для авто-суффикса описания чар.
        protected override int DescriptionDurationTurns => TurnsAlive;

        protected override void OnInit(EcsWorld world, int entityCard, bool isCommander)
        {
            world.GetPool<CharmTag>().Add(entityCard);
            if (TurnsAlive > 0)
                world.GetPool<CharmTimerComponent>().Add(entityCard).TurnsRemaining = TurnsAlive;
        }
    }
}

