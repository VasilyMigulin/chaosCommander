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
        // Время жизни чары в ходах владельца: >0 → уничтожается, когда счётчик дойдёт до 0; 0 → постоянная.
        public int TurnsAlive = 0;

        [UnityEngine.Tooltip("КОГДА списывается ход жизни. TurnEnd (умолч., как у всех старых чар) — в конце " +
                             "хода владельца: 1 = «до конца этого хода», 2 = «переживёт ход оппонента». " +
                             "TurnStart — в начале хода владельца, ПОСЛЕ срабатывания «в начале хода»: " +
                             "1 = «сработает ровно один раз, в мой следующий ход» (иначе такая чара умирала, " +
                             "ни разу не отработав).")]
        public CharmTickMoment TickMoment = CharmTickMoment.TurnEnd;

        [UnityEngine.Tooltip("Длительность ЗАФИКСИРОВАНА: никакой модификатор длительности (Прокачать чары, " +
                             "Зачарованный, Зачаровать матч — ни продление, ни постоянство) на эту чару не " +
                             "действует. Для карт вроде «Очарование принцессы», где длительность — часть баланса.")]
        public bool FixTurns = false;

        public override Game.Core.Service.EnumService.CardType GetCardType() => Game.Core.Service.EnumService.CardType.Charm;

        // Длительность для авто-суффикса описания чар.
        protected override int DescriptionDurationTurns => TurnsAlive;

        protected override void OnInit(EcsWorld world, int entityCard, int playerOwnerEntity, bool isCommander)
        {
            world.GetPool<CharmTag>().Add(entityCard);
            if (FixTurns) world.GetPool<FixedCharmDurationTag>().Add(entityCard);
            if (TurnsAlive > 0)
            {
                // ВАЖНО: бонус длительности владельца (Зачарованный) читается НЕ здесь. OnInit для
                // карт стартовой колоды вызывается ОДИН РАЗ при старте матча (InitDeckSystem), задолго
                // до того, как Зачарованный вообще мог быть разыгран, — а повторно для уже созданной
                // сущности OnInit не вызывается. Бонус применяется при фактическом выходе чары на стол
                // (RunMoveCardToBoardSystem) — там он видит АКТУАЛЬНОЕ состояние CharmDurationBonusService,
                // и там же проверяется FixedCharmDurationTag.
                ref var timer = ref world.GetPool<CharmTimerComponent>().Add(entityCard);
                timer.TurnsRemaining = TurnsAlive;
                timer.Moment = TickMoment;
            }
        }
    }
}

