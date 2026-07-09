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
    public sealed class TutorialDirectorSystem : IEcsInitSystem, IEcsRunSystem, System.IDisposable
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
        const int DummyRow = 1;   // груша на ЗАДНЕМ ряду ИИ: игрок дойдёт за пару шагов и атакует
        const int DummyCol = 2;

        enum Step { Setup, PlayCreature, MoveCreature, KillEnemy, PlaySpell, Lethal, Done }
        Step _step = Step.Setup;

        // Флаги из bus-обработчиков (обрабатываем в Run — детерминированная точка кадра).
        bool _creaturePlayed, _creatureMoved, _enemyDied, _spellCast, _won;
        int _humanId = -1, _aiId = -1;
        bool _subscribed;

        public void Init(IEcsSystems systems)
        {
            GameEventBus.Subscribe<CreatureInvokedEvent>(OnInvoked);
            GameEventBus.Subscribe<CreatureMovedEvent>(OnMoved);
            GameEventBus.Subscribe<CreatureDiedEvent>(OnDied);
            GameEventBus.Subscribe<CardCastEvent>(OnCast);
            GameEventBus.Subscribe<MatchEndedEvent>(OnMatchEnded);
            _subscribed = true;
        }

        public void Dispose()
        {
            if (!_subscribed) return;
            GameEventBus.Unsubscribe<CreatureInvokedEvent>(OnInvoked);
            GameEventBus.Unsubscribe<CreatureMovedEvent>(OnMoved);
            GameEventBus.Unsubscribe<CreatureDiedEvent>(OnDied);
            GameEventBus.Unsubscribe<CardCastEvent>(OnCast);
            GameEventBus.Unsubscribe<MatchEndedEvent>(OnMatchEnded);
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

            // ПОБЕДА ЗАВЕРШАЕТ ТУТОРИАЛ С ЛЮБОГО ШАГА: шустрый игрок может сделать летал раньше сценария
            // (напр. убить аватара до «убей существо»). Раньше _won читался только на шаге Lethal →
            // TutorialDone не ставился → выход → роутинг возвращал в туториал («сцена перезапускается»).
            if (_won && _step != Step.Done)
            {
                FirstRunFlow.TutorialDone = true;
                Hint("ui.tutorial.done", "Победа! Обучение пройдено — вперёд, к созданию аккаунта.");
                _step = Step.Done;
                Debug.Log("[Tutorial] пройден ✓ (досрочный летал — сценарий свёрнут)");
                return;
            }

            switch (_step)
            {
                case Step.Setup:
                    DealStartingHand(human);
                    SpawnDummy(ai);
                    PreStartHandUiUtil.Publish(_world.Value, human);   // рука в UI + гасит лоадинг-оверлей
                    TurnFlow.GrantTurn(_world.Value, human, 1);
                    Hint("ui.tutorial.step1", "Разыграй существо: перетащи карту существа из руки на свою переднюю линию. Не хватает золота — нажми «Завершить ход».");
                    _step = Step.PlayCreature;
                    Debug.Log("[Tutorial] сетап готов → шаг 1 (разыграй существо)");
                    break;

                case Step.PlayCreature:
                    if (!_creaturePlayed) break;
                    Hint("ui.tutorial.step2", "Отлично! Теперь походи: кликни по своему существу, затем по соседней клетке. Кончились действия — «Завершить ход».");
                    _step = Step.MoveCreature;
                    break;

                case Step.MoveCreature:
                    if (!_creatureMoved) break;
                    Hint("ui.tutorial.step3", "Дойди до существа противника и атакуй: кликни по своему существу, затем по врагу на соседней клетке.");
                    _step = Step.KillEnemy;
                    break;

                case Step.KillEnemy:
                    if (!_enemyDied) break;
                    GrantMana(human);
                    Hint("ui.tutorial.step4", "Получена мана! Разыграй заклинание из руки (перетащи его из руки).");
                    _step = Step.PlaySpell;
                    break;

                case Step.PlaySpell:
                    if (!_spellCast) break;
                    Hint("ui.tutorial.step5", "Финальный удар! Дойди до задней линии противника и атакуй его аватар до победы.");
                    _step = Step.Lethal;
                    break;

                case Step.Lethal:
                    if (!_won) break;
                    FirstRunFlow.TutorialDone = true;
                    Hint("ui.tutorial.done", "Победа! Обучение пройдено — вперёд, к созданию аккаунта.");
                    _step = Step.Done;
                    Debug.Log("[Tutorial] пройден ✓ (флаг TutorialDone)");
                    break;
            }
        }

        void DealStartingHand(int human)
        {
            ref var deck = ref _deckPool.Value.Get(human);
            ref var hand = ref _handPool.Value.Get(human);
            int take = Mathf.Min(StartingHand, deck.CardEntities?.Count ?? 0);
            for (int i = 0; i < take; i++)
            {
                int card = deck.CardEntities[deck.CardEntities.Count - 1];   // верх колоды = конец списка
                deck.CardEntities.RemoveAt(deck.CardEntities.Count - 1);
                if (_deckTagPool.Value.Has(card)) _deckTagPool.Value.Del(card);
                if (!_handTagPool.Value.Has(card)) _handTagPool.Value.Add(card);
                hand.CardEntities.Add(card);
            }
            deck.Count = deck.CardEntities?.Count ?? 0;
            hand.Count = hand.CardEntities.Count;
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

        static void Hint(string key, string fallback)
            => GameEventBus.Publish(new TutorialHintUIEvent { TextKey = key, FallbackText = fallback });
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
