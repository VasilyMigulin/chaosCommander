using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Обрабатывает DrawCardEvent на сущности игрока:
    ///   - берёт верхнюю карту из DeckComponent
    ///   - если в руке меньше MaxNonCommanderCards обычных карт — перевешивает DeckTag → HandTag
    ///   - если рука полна — карта ОСТАЁТСЯ в колоде (не добираем, не сжигаем)
    ///
    /// Сжигание карт — отдельная механика способностей (см. BurnCardSystem), здесь не применяется.
    /// </summary>
    public sealed class DrawCardSystem : IEcsRunSystem
    {
        readonly EcsPoolInject<DrawCardEvent> _drawPool = default;
        readonly EcsPoolInject<DeckComponent> _deckPool = default;
        readonly EcsPoolInject<HandComponent> _handPool = default;
        readonly EcsPoolInject<HandTag> _handTagPool = default;
        readonly EcsPoolInject<DeckTag> _deckTagPool = default;
        readonly EcsPoolInject<CommanderTag> _commanderPool = default;
        readonly EcsPoolInject<NetworkEntityComponent> _netKeyPool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<CardModelComponent> _modelPool = default;   // имя карты в логе фантома
        readonly EcsFilterInject<Inc<DrawCardEvent, DeckComponent, HandComponent>> _filter = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var playerEntity in _filter.Value)
            {
                ref var drawEvent = ref _drawPool.Value.Get(playerEntity);
                int count = drawEvent.Count > 0 ? drawEvent.Count : 1;
                bool sync = drawEvent.Sync;   // turn-start добор → синкнем оппоненту (см. ниже)
                int drawn = 0;
                var drawnEntities = sync ? new System.Collections.Generic.List<int>() : null;

                for (int c = 0; c < count; c++)
                {
                    ref var deck = ref _deckPool.Value.Get(playerEntity);
                    ref var hand = ref _handPool.Value.Get(playerEntity);

                    if (deck.Count == 0) break;

                    // ФАНТОМЫ РУКИ (баг 2026-07-30 «игра думает, что рука полная, а в ней 3 карты»):
                    // карта, оставшаяся в hand.CardEntities без HandTag, занимает место навсегда — рука
                    // «полна» при пустых слотах. Чистим ПЕРЕД проверкой лимита и логируем, кто завис.
                    SanitizeHand(playerEntity, ref hand);

                    // Рука полна — карта остаётся в колоде (без сжигания)
                    if (CountNonCommander(ref hand) >= HandComponent.MaxNonCommanderCards)
                        break;

                    int cardEntity = deck.CardEntities[0];

                    deck.CardEntities.RemoveAt(0);
                    deck.Count--;

                    hand.CardEntities.Add(cardEntity);
                    hand.Count++;

                    if (_deckTagPool.Value.Has(cardEntity))
                        _deckTagPool.Value.Del(cardEntity);

                    _handTagPool.Value.Add(cardEntity);
                    drawn++;
                    drawnEntities?.Add(cardEntity);

                    // Форензика синка: КАЖДЫЙ добор с ключом (turn-start И эффектные — на обоих клиентах).
                    // Эффектные доборы ре-ранятся «с верха» локально → при расхождении порядка колод здесь
                    // видно, какую именно карту снял каждый клиент (дифф [Draw]-строк той же границы).
                    UnityEngine.Debug.Log($"[Draw] p={(_playerPool.Value.Has(playerEntity) ? _playerPool.Value.Get(playerEntity).PlayerId : -1)} " +
                                          $"key={(_netKeyPool.Value.Has(cardEntity) ? _netKeyPool.Value.Get(cardEntity).NetworkEntityKey : cardEntity.ToString())} sync={sync}");

                    GameEventBus.Publish(new CardDrawnEvent
                    {
                        CardEntity = cardEntity,
                        PlayerId = playerEntity
                    });
                }

                _drawPool.Value.Del(playerEntity);

                // Синк: turn-start добор есть только у активного → сообщаем коллектору, он пошлёт ActionDrawData,
                // пассив снимет столько же верхних карт у этого игрока. Реплей/эффектные доборы (Sync=false) не шлём.
                if (sync && drawn > 0)
                    GameEventBus.Publish(new DeckDrawNetEvent
                    {
                        PlayerEntity  = playerEntity,
                        Count         = drawn,
                        DrawnEntities = drawnEntities.ToArray(),   // конкретные карты → синк по ключам
                    });
            }
        }

        /// <summary>
        /// Выкидывает из списка руки «фантомов» — сущности БЕЗ HandTag (карта уже разыграна/сброшена/ушла,
        /// но кто-то не убрал её из hand.CardEntities) и синхронизирует Count со списком. Без этого рука
        /// считается полной при визуально пустых слотах, и добор молча прекращается.
        /// Логируем КАЖДЫЙ случай: это всегда чья-то незакрытая перекладка — по имени карты видно, чья.
        /// </summary>
        private void SanitizeHand(int playerEntity, ref HandComponent hand)
        {
            if (hand.CardEntities == null) { hand.Count = 0; return; }

            for (int i = hand.CardEntities.Count - 1; i >= 0; i--)
            {
                int card = hand.CardEntities[i];
                if (_handTagPool.Value.Has(card)) continue;

                string name = _modelPool.Value.Has(card) ? _modelPool.Value.Get(card).CardName : "?";
                UnityEngine.Debug.LogWarning($"[Hand] ФАНТОМ в руке: entity={card} '{name}' без HandTag — убираю из списка руки");
                hand.CardEntities.RemoveAt(i);
            }

            if (hand.Count != hand.CardEntities.Count)
            {
                UnityEngine.Debug.LogWarning($"[Hand] счётчик руки разъехался со списком: Count={hand.Count}, факт={hand.CardEntities.Count} — синхронизирую");
                hand.Count = hand.CardEntities.Count;
            }
        }

        // Счёт по ФАКТИЧЕСКОМУ списку (не по hand.Count): при рассинхроне цикл по Count мог выйти за
        // границы списка (IndexOutOfRange) или недосчитать карты.
        private int CountNonCommander(ref HandComponent hand)
        {
            if (hand.CardEntities == null) return 0;
            int n = 0;
            for (int i = 0; i < hand.CardEntities.Count; i++)
            {
                if (!_commanderPool.Value.Has(hand.CardEntities[i]))
                    n++;
            }
            return n;
        }
    }
}
