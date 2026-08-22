using System.Collections.Generic;
using Game.Core.Configs;
using Game.Core.Ecs.Components;
using Game.Core.Service;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// PvE: строит колоду/руку/командира ИИ-игрока из PveEncounterConfig (Resources, путь в
    /// PveMode.EncounterPath). Регистрировать ПОСЛЕ InitDeckSystem (человек уже создан) и ДО
    /// InitMulliganSystem (мулиган только у человека — ИИ берёт стартовую руку сразу здесь).
    /// Ключи карт ИИ — "2-N" (префикс = PlayerId ИИ; человек — "1-N" → коллизий нет).
    /// Карты ИИ — РЕАЛЬНЫЕ сущности (не зеркало): PvE-симуляция локальная и полная.
    /// </summary>
    public sealed class InitPveOpponentSystem : IEcsInitSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsSharedInject<IGameStateContext> _state = default;
        readonly EcsFilterInject<Inc<PlayerComponent, AiPlayerComponent, DeckComponent, HandComponent>> _aiFilter = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<DeckComponent> _deckPool = default;
        readonly EcsPoolInject<HandComponent> _handPool = default;
        readonly EcsPoolInject<HealthComponent> _healthPool = default;
        readonly EcsPoolInject<NetworkEntityComponent> _netKeyPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<DeckTag> _deckTagPool = default;
        readonly EcsPoolInject<HandTag> _handTagPool = default;
        readonly EcsPoolInject<EnemyCardTag> _enemyTagPool = default;

        int _keyCounter;

        public void Init(IEcsSystems systems)
        {
            if (!PveMode.Enabled) return;

            int aiEntity = -1;
            foreach (var e in _aiFilter.Value) { aiEntity = e; break; }
            if (aiEntity < 0) { Debug.LogError("[InitPveOpponentSystem] ИИ-игрок не найден (InitPlayerSystem PvE-ветка не отработала?)"); return; }

            var encounter = PveEncounterLocator.Current;
            if (encounter == null)
            {
                Debug.LogError($"[InitPveOpponentSystem] Энкаунтер не найден (ни прямой ссылки кампании, ни ассета по пути '{PveMode.EncounterPath}'). " +
                               "Создай ассет (Create → Game → Pve Encounter) в Assets/Resources/Encounter/. ИИ останется без колоды.");
                return;
            }

            ref var player = ref _playerPool.Value.Get(aiEntity);
            int aiPlayerId = player.PlayerId;

            // HP из энкаунтера
            ref var hp = ref _healthPool.Value.Get(aiEntity);
            hp.Max = encounter.Health;
            hp.BaseMax = encounter.Health;   // без BaseMax RecalculateValue (DrainHealth и пр.) обнулит базу
            hp.Current = encounter.Health;

            // ── Колода: пул пресетов (если задан) СЛУЧАЙНО выбирает ОДНУ колоду вместо фиксированных
            // Commander/Cards — тренировка не должна каждый раз биться против одной и той же (юзер 2026-08-21).
            // Пик — ОДИН РАЗ за матч (Init — IEcsInitSystem), пишем в PveEncounterLocator, чтобы BattleState.
            // StartPveIntro (VS-экран, читает СТРОГО ПОСЛЕ) видел ТОТ ЖЕ командира, а не рандомил заново —
            // иначе VS показал бы одного, а в бою оказался бы другой (или пусто, если Commander/Cards пустые).
            PveEncounterLocator.CurrentOpponentDeckPick = null;
            var commanderCard = encounter.Commander;
            IReadOnlyList<PveEncounterConfig.DeckEntry> cardEntries = encounter.Cards;
            if (encounter.DeckPool != null && encounter.DeckPool.Count > 0)
            {
                var preset = encounter.DeckPool[Random.Range(0, encounter.DeckPool.Count)];
                if (preset == null)
                {
                    Debug.LogWarning("[InitPveOpponentSystem] В DeckPool есть пустой (null) элемент — откатываюсь " +
                                     "на Commander/Cards энкаунтера. Проверь список DeckPool в инспекторе.");
                }
                else
                {
                    commanderCard = preset.Commander;
                    var fromPreset = new List<PveEncounterConfig.DeckEntry>(preset.Cards.Count);
                    foreach (var e in preset.Cards)
                        fromPreset.Add(new PveEncounterConfig.DeckEntry { Card = e.Card, Count = e.Count });
                    cardEntries = fromPreset;
                    PveEncounterLocator.CurrentOpponentDeckPick = preset;
                    Debug.Log($"[InitPveOpponentSystem] Колода ИИ из пула: '{preset.DeckName}'.");
                }
            }

            // ── Колода ──
            var deckEntities = new List<int>();
            foreach (var entry in cardEntries)
            {
                if (entry.Card == null || entry.Card.CardData == null) continue;
                int copies = Mathf.Max(1, entry.Count);
                for (int i = 0; i < copies; i++)
                {
                    int card = CreateAiCard(entry.Card.CardData, aiEntity, aiPlayerId, isCommander: false);
                    if (card >= 0) deckEntities.Add(card);
                }
            }

            deckEntities.Shuffle();   // локальный рандом: PvE не синкается — детерминизм не нужен

            ref var deck = ref _deckPool.Value.Get(aiEntity);
            deck.CardEntities = deckEntities;
            deck.Count = deckEntities.Count;

            ref var hand = ref _handPool.Value.Get(aiEntity);

            // ── Командир → рука (index 0), как у человека ──
            if (commanderCard != null && commanderCard.CardData != null)
            {
                int commander = CreateAiCard(commanderCard.CardData, aiEntity, aiPlayerId, isCommander: true);
                if (commander >= 0)
                {
                    if (_deckTagPool.Value.Has(commander)) _deckTagPool.Value.Del(commander);
                    if (!_handTagPool.Value.Has(commander)) _handTagPool.Value.Add(commander);
                    hand.CardEntities.Insert(0, commander);
                }
            }

            // ── Стартовая рука (мулигана у ИИ нет — просто верхние N) ──
            int take = Mathf.Min(encounter.StartingHand, deck.CardEntities.Count);
            for (int i = 0; i < take; i++)
            {
                int card = deck.CardEntities[deck.CardEntities.Count - 1];
                deck.CardEntities.RemoveAt(deck.CardEntities.Count - 1);
                if (_deckTagPool.Value.Has(card)) _deckTagPool.Value.Del(card);
                if (!_handTagPool.Value.Has(card)) _handTagPool.Value.Add(card);
                hand.CardEntities.Add(card);
            }
            deck.Count = deck.CardEntities.Count;
            hand.Count = hand.CardEntities.Count;

            Debug.Log($"[InitPveOpponentSystem] '{encounter.EncounterName}': deck={deck.Count} hand={hand.Count} hp={hp.Max}");
        }

        int CreateAiCard(Game.Core.Model.Card.CardModel model, int aiEntity, int aiPlayerId, bool isCommander)
        {
            int card = model.Init(_world.Value, aiEntity, isCommander);

            string key = aiPlayerId + "-" + _keyCounter++;   // "2-N": та же схема, что NetKey, но свой префикс

            if (!_netKeyPool.Value.Has(card))
                _netKeyPool.Value.Add(card).NetworkEntityKey = key;

            if (!_ownerPool.Value.Has(card))
            {
                ref var owner = ref _ownerPool.Value.Add(card);
                owner.OwnerId = aiPlayerId;
                owner.EntityKey = key;
            }

            if (!isCommander && !_deckTagPool.Value.Has(card))
                _deckTagPool.Value.Add(card);

            if (!_enemyTagPool.Value.Has(card))
                _enemyTagPool.Value.Add(card);   // для человека карты ИИ — вражеские

            _state.Value.AddEntity(card, localKey: card.ToString(), networkKey: key);
            return card;
        }
    }
}
