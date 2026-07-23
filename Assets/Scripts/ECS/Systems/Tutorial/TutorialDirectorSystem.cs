using System.Collections.Generic;
using Game.Core.Configs;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Service;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// Режиссёр ТУТОРИАЛА (только TutorialEcsHandler): линейный сценарий по bus-событиям боя.
    ///   Сетап: раздать руку из сюжетной колоды, выставить грушу оппонента на стол, показать стартовую
    ///          руку (PreStartPhaseBeginUIEvent — гасит лоадинг-оверлей), выдать первый ход игроку.
    ///   Шаги:  1) разыграй существо (CreatureInvokedEvent) → 2) походи (CreatureMovedEvent) →
    ///          3) убей грушу (CreatureDiedEvent ИИ) → [+мана] 4) разыграй заклинание (CardCastEvent
    ///          не-существа) → 5) летал (MatchEndedEvent Win → FirstRunFlow.TutorialDone).
    ///   Оппонент: колода/HP из туториального энкаунтера, ходы АВТО-ПАСУЮТСЯ (EndTurnRequestEvent).
    /// Подсказки — TutorialHintUIEvent (локализованные ключи ui.tutorial.*) → TutorialHintView.
    /// Поражение: TutorialDone не ставится → роутинг InitState вернёт в туториал заново.
    /// </summary>
    public sealed class TutorialDirectorSystem : IEcsInitSystem, IEcsRunSystem, IEcsDestroySystem, System.IDisposable
    {
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<PlayerComponent, LocalComponent>> _humanFilter = default;
        readonly EcsFilterInject<Inc<PlayerComponent, AiPlayerComponent>> _aiFilter = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<ActiveState> _activePool = default;
        readonly EcsPoolInject<EndTurnRequestEvent> _endReqPool = default;
        readonly EcsPoolInject<DeckComponent> _deckPool = default;
        readonly EcsPoolInject<HandComponent> _handPool = default;
        readonly EcsPoolInject<DeckTag> _deckTagPool = default;
        readonly EcsPoolInject<HandTag> _handTagPool = default;
        readonly EcsPoolInject<ManaComponent> _manaPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<CreatureTag> _creatureTagPool = default;
        readonly EcsPoolInject<CardViewDataComponent> _viewPool = default;
        readonly EcsPoolInject<NetworkEntityComponent> _netKeyPool = default;
        readonly EcsPoolInject<CommanderTag> _commanderPool = default;

        const int StartingHand = 4;
        const int ManaGift = 2;
        const int DummyRow = 1;   // груша НЕ на линии призыва ИИ: игрок успеет сходить, прежде чем ударить
        const int DummyCol = 2;

        /// <summary>Чего ждёт шаг: нажатия «Далее» (инфо) или конкретного события боя (действие).</summary>
        enum Wait { Continue, CreaturePlayed, CreatureMoved, EnemyDied, SpellCast, Won }

        readonly struct Beat
        {
            public readonly string Key, Text;
            public readonly Wait Wait;
            public readonly TutorialAnchorId Anchor;   // что подсветить (дырка в затемнении)
            public Beat(string key, string text, Wait wait, TutorialAnchorId anchor)
            { Key = key; Text = text; Wait = wait; Anchor = anchor; }
        }

        // СЦЕНАРИЙ. Инфо-шаги объясняют механику (ввод заблокирован), шаги-действия ждут события боя
        // (затемнение чисто визуальное — играть можно).
        static readonly Beat[] Script =
        {
            new Beat("ui.tutorial.commander", "Это твой командир. Всегда под рукой — выставляй когда вздумается. Прикончат — вернётся через ход, злой и с претензиями.", Wait.Continue, TutorialAnchorId.Commander),
            new Beat("ui.tutorial.gold",      "Золотишко. За него на стол выходят существа. В начале хода его становится больше и оно восстанавливается — копить бессмысленно, тратить приятно.", Wait.Continue, TutorialAnchorId.Gold),
            new Beat("ui.tutorial.playCreature", "Тащи существо из руки на свою переднюю линию. Золотишка хватает — не жмись.", Wait.CreaturePlayed, TutorialAnchorId.Hand),
            // Шаги про доску без якоря: доска — мировые 3D-объекты, uGUI-якорь на неё не повесить.
            // Инфо-шаги просто затемняют экран (ввод перекрыт), шаги-действия оверлей не показывают вовсе.
            new Beat("ui.tutorial.stats",     "У существа три числа. Атака — сколько отсыпет. Здоровье — сколько стерпит. Скорость — вот тут внимательнее.", Wait.Continue, TutorialAnchorId.None),
            new Beat("ui.tutorial.speed",     "Скорость — это действия, а не бег. Шаг стоит скорости, удар — тоже. Бить можно раз за ход. В начале хода всё восстанавливается.", Wait.Continue, TutorialAnchorId.None),
            new Beat("ui.tutorial.move",      "Кликни своё существо, потом соседнюю клетку. Шаг тратит скорость: кончилась — заверши ход, и она восстановится.", Wait.CreatureMoved, TutorialAnchorId.None),
            new Beat("ui.tutorial.attack",    "Дойди до чужого существа и вдарь: клик по своему, клик по врагу рядом. Скорости не хватило — заверши ход и продолжи в следующем.", Wait.EnemyDied, TutorialAnchorId.None),
            new Beat("ui.tutorial.mana",      "С трупа капнула мана. Она нужна для заклинаний и чар. Сама не появляется — добывается из чужих существ или разными хитростями.", Wait.Continue, TutorialAnchorId.Mana),
            // «Чары» идут ДО розыгрыша заклинания намеренно: инфо-шаг сразу ПОСЛЕ действия с прицеливанием
            // вставал бы модалкой поверх выбора цели (CardCastEvent летит раньше, чем игрок выбрал цель).
            // Заодно и по смыслу: чары — сразу после объяснения маны.
            new Beat("ui.tutorial.charms",    "Ещё бывают чары — постоянные штуки, тоже за ману. Больше пяти под контролем не удержишь.", Wait.Continue, TutorialAnchorId.None),
            new Beat("ui.tutorial.playSpell", "Мана есть — разыграй заклинание. У заклинаний бывают требования: нет подходящей цели — не сыграется.", Wait.SpellCast, TutorialAnchorId.Hand),
            new Beat("ui.tutorial.lethal",    "Финал. Пробейся к аватару противника и колоти, пока не кончится. Кончилась скорость — заверши ход.", Wait.Won, TutorialAnchorId.None),
        };

        int _beat = -1;      // -1 = сетап ещё не отработал
        bool _finished;

        // Флаги ждущего события. ВАЖНО: сбрасываются при входе в шаг — иначе действие, сделанное РАНЬШЕ
        // времени (напр. заклинание до объяснения маны), мгновенно проматывало бы будущий шаг.
        bool _creaturePlayed, _creatureMoved, _enemyDied, _spellCast, _won, _continue;
        int _humanId = -1, _aiId = -1;
        bool _subscribed;

        public void Init(IEcsSystems systems)
        {
            GameEventBus.Subscribe<CreatureInvokedEvent>(OnInvoked);
            GameEventBus.Subscribe<CreatureMovedEvent>(OnMoved);
            GameEventBus.Subscribe<CreatureDiedEvent>(OnDied);
            GameEventBus.Subscribe<CardCastEvent>(OnCast);
            GameEventBus.Subscribe<MatchEndedEvent>(OnMatchEnded);
            GameEventBus.Subscribe<TutorialContinueEvent>(OnContinue);
            _subscribed = true;
        }

        // EcsSystems.Destroy() ищет IEcsDestroySystem, не System.IDisposable — без этого моста Dispose()
        // фреймворк никогда не вызывал бы (см. EcsRunHandler/TutorialEcsHandler.Dispose → _allSystems.Destroy()).
        public void Destroy(IEcsSystems systems) => Dispose();

        public void Dispose()
        {
            if (!_subscribed) return;
            GameEventBus.Unsubscribe<CreatureInvokedEvent>(OnInvoked);
            GameEventBus.Unsubscribe<CreatureMovedEvent>(OnMoved);
            GameEventBus.Unsubscribe<CreatureDiedEvent>(OnDied);
            GameEventBus.Unsubscribe<CardCastEvent>(OnCast);
            GameEventBus.Unsubscribe<MatchEndedEvent>(OnMatchEnded);
            GameEventBus.Unsubscribe<TutorialContinueEvent>(OnContinue);
            _subscribed = false;
        }

        // ── bus-обработчики: только флаги (фильтрация по владельцу) ─────────────────────

        void OnInvoked(CreatureInvokedEvent e)
        {
            if (OwnerOf(e.CardEntity) == _humanId) _creaturePlayed = true;
        }

        void OnMoved(CreatureMovedEvent e)
        {
            if (OwnerOf(e.CreatureEntity) == _humanId) _creatureMoved = true;
        }

        void OnDied(CreatureDiedEvent e)
        {
            if (OwnerOf(e.CardEntity) == _aiId) _enemyDied = true;
        }

        void OnCast(CardCastEvent e)
        {
            if (e.CardEntity >= 0 && OwnerOf(e.CardEntity) == _humanId
                && !_creatureTagPool.Value.Has(e.CardEntity))
                _spellCast = true;
        }

        void OnMatchEnded(MatchEndedEvent e)
        {
            if (e.LocalResult == MatchResult.Win) _won = true;
        }

        void OnContinue(TutorialContinueEvent _) => _continue = true;

        int OwnerOf(int entity)
            => entity >= 0 && _ownerPool.Value.Has(entity) ? _ownerPool.Value.Get(entity).OwnerId : -999;

        // ── сценарий ─────────────────────────────────────────────────────────────────────

        public void Run(IEcsSystems systems)
        {
            int human = -1, ai = -1;
            foreach (var e in _humanFilter.Value) { human = e; break; }
            foreach (var e in _aiFilter.Value) { ai = e; break; }
            if (human < 0 || ai < 0) return;
            _humanId = _playerPool.Value.Get(human).PlayerId;
            _aiId = _playerPool.Value.Get(ai).PlayerId;

            // Оппонент-груша всегда пасует свой ход (шаги A/B EndTurnRequestSystem дренируют пайплайн сами).
            if (_activePool.Value.Has(ai) && !_endReqPool.Value.Has(ai))
                _endReqPool.Value.Add(ai);

            if (_finished) return;

            // ПОБЕДА ЗАВЕРШАЕТ ТУТОРИАЛ С ЛЮБОГО ШАГА: шустрый игрок может сделать летал раньше сценария.
            // Иначе TutorialDone не ставился бы → выход → роутинг возвращал в туториал («сцена перезапускается»).
            if (_won) { Finish("досрочный летал — сценарий свёрнут"); return; }

            // СЕТАП (один раз): рука, груша, показ руки в UI, первый ход игроку.
            if (_beat < 0)
            {
                DealStartingHand(human);
                SpawnDummy(ai);
                PreStartHandUiUtil.Publish(_world.Value, human);   // рука в UI + гасит лоадинг-оверлей
                TurnFlow.GrantTurn(_world.Value, human, 1);
                EnterBeat(0);
                Debug.Log("[Tutorial] сетап готов → шаг 1");
                return;
            }

            // Ждём того, что просит текущий шаг.
            if (!IsBeatSatisfied(Script[_beat].Wait)) return;

            // Мана капает ПЕРЕД объяснением про ману (следующий шаг — как раз про неё).
            if (Script[_beat].Wait == Wait.EnemyDied) GrantMana(human);

            if (_beat + 1 >= Script.Length) { Finish("сценарий пройден"); return; }
            EnterBeat(_beat + 1);
        }

        bool IsBeatSatisfied(Wait wait)
        {
            switch (wait)
            {
                case Wait.Continue:       return _continue;
                case Wait.CreaturePlayed: return _creaturePlayed;
                case Wait.CreatureMoved:  return _creatureMoved;
                case Wait.EnemyDied:      return _enemyDied;
                case Wait.SpellCast:      return _spellCast;
                case Wait.Won:            return _won;
                default:                  return true;
            }
        }

        // Вход в шаг. Флаги действий НАМЕРЕННО не сбрасываем: если игрок успел сделать действие раньше
        // (например, разыграл существо на инфо-шаге), шаг просто зачтётся — он его выполнил. Сброс приводил
        // к СОФТ-ЛОКУ: шаг ждал повтора того, что уже нечем сделать (кончилось золото/карты).
        // «Далее» сбрасываем — иначе один клик пролистал бы несколько инфо-шагов подряд.
        void EnterBeat(int index)
        {
            _beat = index;
            _continue = false;

            var beat = Script[index];
            bool info = beat.Wait == Wait.Continue;

            Hint(beat.Key, beat.Text, info);
            // ИНФО → ввод перекрыт целиком (иначе игрок сыграет наперёд и шаг-действие станет непроходим).
            // ДЕЙСТВИЕ → затемнение только подсказывает, куда смотреть; играть можно свободно.
            GameEventBus.Publish(new TutorialHighlightUIEvent
            {
                Anchor = beat.Anchor, Show = true, BlockAll = info
            });

            Debug.Log($"[Tutorial] шаг {index + 1}/{Script.Length}: ждём {beat.Wait}");
        }

        void Finish(string reason)
        {
            _finished = true;
            FirstRunFlow.TutorialDone = true;
            Hint("ui.tutorial.done", "Всё, ты обучен. Дальше будет только хуже — но веселее.", false);
            GameEventBus.Publish(new TutorialHighlightUIEvent { Show = false });   // снять затемнение
            Debug.Log($"[Tutorial] пройден ✓ ({reason})");
        }

        // Рука сетапа. ГАРАНТИРУЕМ существо и не-существо (заклинание/чары): иначе шаг «разыграй существо»
        // или «разыграй заклинание» физически непроходим — игроку остаётся бесконечно пасовать (тупик).
        void DealStartingHand(int human)
        {
            ref var deck = ref _deckPool.Value.Get(human);
            ref var hand = ref _handPool.Value.Get(human);
            if (deck.CardEntities == null) return;

            int creature = FindInDeck(deck.CardEntities, wantCreature: true);
            int spell    = FindInDeck(deck.CardEntities, wantCreature: false);

            if (creature >= 0) MoveToHand(deck.CardEntities, hand.CardEntities, creature);
            if (spell    >= 0) MoveToHand(deck.CardEntities, hand.CardEntities, spell);

            // Остальное добираем с верха колоды (конец списка).
            while (hand.CardEntities.Count < StartingHand && deck.CardEntities.Count > 0)
                MoveToHand(deck.CardEntities, hand.CardEntities, deck.CardEntities[deck.CardEntities.Count - 1]);

            deck.Count = deck.CardEntities.Count;
            hand.Count = hand.CardEntities.Count;

            if (creature < 0)
                Debug.LogWarning("[Tutorial] в колоде энкаунтера НЕТ существ — шаг «разыграй существо» не пройти.");
            if (spell < 0)
                Debug.LogWarning("[Tutorial] в колоде энкаунтера НЕТ заклинаний/чар — шаг «разыграй заклинание» не пройти.");
        }

        int FindInDeck(List<int> cards, bool wantCreature)
        {
            for (int i = cards.Count - 1; i >= 0; i--)
                if (_creatureTagPool.Value.Has(cards[i]) == wantCreature) return cards[i];
            return -1;
        }

        void MoveToHand(List<int> deckCards, List<int> handCards, int card)
        {
            deckCards.Remove(card);
            if (_deckTagPool.Value.Has(card)) _deckTagPool.Value.Del(card);
            if (!_handTagPool.Value.Has(card)) _handTagPool.Value.Add(card);
            handCards.Add(card);
        }

        // Груша: НОВАЯ сущность первой карты колоды энкаунтера, сразу на стол ИИ (ряд 1 — чтобы игрок
        // научился ходить, дойдя до неё). Скриптовый спавн — CreateCardEvent{InBoard} (как FillRow).
        void SpawnDummy(int ai)
        {
            var enc = PveEncounterLocator.Current;
            if (enc == null || enc.Cards == null || enc.Cards.Count == 0 || enc.Cards[0].Card == null)
            {
                Debug.LogWarning("[Tutorial] у энкаунтера нет карт — груша не выставлена (шаг «убей» встанет).");
                return;
            }

            ref var aiPlayer = ref _playerPool.Value.Get(ai);
            GameEventBus.Publish(new CreateCardEvent
            {
                ExpansionId        = enc.Cards[0].Card.ExpansionId,
                CardId             = enc.Cards[0].Card.CardId,
                NetworkEntityKey   = "tut-dummy",
                PlayerOwnerEntity  = ai,
                OwnerId            = aiPlayer.PlayerId,
                IsEnemy            = true,
                InBoard            = true,
                BoardRow           = DummyRow,
                BoardCol           = DummyCol,
                BoardOwnerId       = aiPlayer.PlayerId,
                RegisterInZoneList = false,
            });
        }

        void GrantMana(int human)
        {
            if (!_manaPool.Value.Has(human)) return;
            ref var mana = ref _manaPool.Value.Get(human);
            mana.Max = Mathf.Min(mana.Max + ManaGift, 10);
            mana.Current = Mathf.Min(mana.Current + ManaGift, mana.Max);
            GameEventBus.Publish(new ResourceChangedEvent
            {
                isLocalPlayer = true,
                Type = Game.Core.Service.EnumService.ResourceType.Mana,
                NewValue = mana.Current,
                MaxValue = mana.Max,
            });
        }

        static void Hint(string key, string fallback, bool needsContinue)
            => GameEventBus.Publish(new TutorialHintUIEvent
            {
                TextKey = key, FallbackText = fallback, NeedsContinue = needsContinue
            });
    }

    // === helper === Стартовая рука → UI (PreStartPhaseBeginUIEvent: закрывает мулиган-окно, если было,
    // гасит BattleLoadingOverlay, CardLayout показывает руку и командира). Общая для туториала;
    // NB: такой же приватный код живёт в PhotonRunHandler и RunMulliganReadySystem (менять синхронно!).
    internal static class PreStartHandUiUtil
    {
        public static void Publish(EcsWorld world, int playerEntity)
        {
            var playerPool = world.GetPool<PlayerComponent>();
            var handPool = world.GetPool<HandComponent>();
            var viewPool = world.GetPool<CardViewDataComponent>();
            var netKeyPool = world.GetPool<NetworkEntityComponent>();
            var commanderPool = world.GetPool<CommanderTag>();

            ref var player = ref playerPool.Get(playerEntity);
            ref var hand = ref handPool.Get(playerEntity);

            var handCards = new List<CardAddedToHandUIEvent>();
            CardAddedToHandUIEvent commanderCard = default;
            bool hasCommander = false;

            for (int i = 0; i < hand.Count; i++)
            {
                int cardEntity = hand.CardEntities[i];
                if (!viewPool.Has(cardEntity)) continue;

                ref var view = ref viewPool.Get(cardEntity);
                bool isCommander = commanderPool.Has(cardEntity);
                var evt = new CardAddedToHandUIEvent
                {
                    CardEntity  = cardEntity,
                    PlayerId    = player.PlayerId,
                    NetworkKey  = netKeyPool.Has(cardEntity) ? netKeyPool.Get(cardEntity).NetworkEntityKey : string.Empty,
                    Icon        = view.ArtImage,
                    CardType    = view.CardType,
                    Element     = view.Element,
                    Rarity      = view.Rarity,
                    CardName    = view.CardName,
                    IsCommander = isCommander,
                    Visual      = new Game.Core.Shared.CardVisualData
                    {
                        CardName    = view.CardName,
                        Description = view.Description,
                        Icon        = view.ArtImage,
                        CardType    = view.CardType,
                        Rarity      = view.Rarity,
                        Element     = view.Element,
                        CostType    = view.CostType,
                        CostAmount  = view.CostAmount,
                        IsCreature  = view.IsCreature,
                        Attack      = view.Attack,
                        MaxHealth   = view.MaxHealth,
                        Speed       = view.Speed,
                        IsCommander = isCommander,
                    },
                };

                if (isCommander) { commanderCard = evt; hasCommander = true; }
                else             { handCards.Add(evt); }
            }

            GameEventBus.Publish(new PreStartPhaseBeginUIEvent
            {
                HandCards     = handCards.ToArray(),
                CommanderCard = commanderCard,
                HasCommander  = hasCommander,
            });
        }
    }
}
