using System.Collections.Generic;
using Game.Core.Events;
using Game.Core.Service;

namespace Game.Core.Match
{
    /// <summary>
    /// Статический трекер матча. Накапливает историю всех игровых событий
    /// (разыгранные карты, нанесённый урон, гибели существ) и предоставляет
    /// методы запроса для проверки условий способностей.
    ///
    /// Жизненный цикл:
    ///   MatchTracker.Initialize() — вызвать при старте матча (сбрасывает всё).
    ///   MatchTracker.Shutdown()   — вызвать при выходе из матча (отписывается).
    /// </summary>
    public static class MatchTracker
    {
        // ── Хранилища ────────────────────────────────────────────────────────

        static readonly List<CardPlayRecord> _cardPlays   = new List<CardPlayRecord>();
        static readonly List<DamageRecord>   _damageDealt = new List<DamageRecord>();
        static readonly List<DeathRecord>    _deaths      = new List<DeathRecord>();

        // Быстрые счётчики
        // ключ: playerId → количество
        static readonly Dictionary<int, int> _cardsPlayedByPlayer   = new Dictionary<int, int>();
        static readonly Dictionary<int, int> _damageDealtByPlayer   = new Dictionary<int, int>();
        static readonly Dictionary<int, int> _damageReceivedByPlayer = new Dictionary<int, int>();
        static readonly Dictionary<int, int> _killsByPlayer         = new Dictionary<int, int>();

        // ключ: cardName → количество разыгрываний (все игроки)
        static readonly Dictionary<string, int> _cardPlaysByName = new Dictionary<string, int>();

        // ключ: element → количество разыгранных карт
        static readonly Dictionary<EnumService.Element, int> _cardsByElement = new Dictionary<EnumService.Element, int>();

        // ключ: rarity → количество разыгранных карт
        static readonly Dictionary<EnumService.Rarity, int> _cardsByRarity = new Dictionary<EnumService.Rarity, int>();

        // ключ: cardType → количество разыгранных карт
        static readonly Dictionary<EnumService.CardType, int> _cardsByType = new Dictionary<EnumService.CardType, int>();

        // текущий номер хода (обновляется из TurnStartedEvent)
        static int _currentTurn;

        static bool _initialized;

        // ── Lifecycle ────────────────────────────────────────────────────────

        public static void Initialize()
        {
            Clear();
            SubscribeEvents();
            _initialized = true;
        }

        public static void Shutdown()
        {
            UnsubscribeEvents();
            Clear();
            _initialized = false;
        }

        static void Clear()
        {
            _cardPlays.Clear();
            _damageDealt.Clear();
            _deaths.Clear();
            _cardsPlayedByPlayer.Clear();
            _damageDealtByPlayer.Clear();
            _damageReceivedByPlayer.Clear();
            _killsByPlayer.Clear();
            _cardPlaysByName.Clear();
            _cardsByElement.Clear();
            _cardsByRarity.Clear();
            _cardsByType.Clear();
            _currentTurn = 0;
        }

        static void SubscribeEvents()
        {
            GameEventBus.Subscribe<CardTrackedEvent>(OnCardPlayed);
            GameEventBus.Subscribe<DamageTrackedEvent>(OnDamageDealt);
            GameEventBus.Subscribe<DeathTrackedEvent>(OnCreatureDied);
            GameEventBus.Subscribe<TurnStartedEvent>(OnTurnStarted);
        }

        static void UnsubscribeEvents()
        {
            GameEventBus.Unsubscribe<CardTrackedEvent>(OnCardPlayed);
            GameEventBus.Unsubscribe<DamageTrackedEvent>(OnDamageDealt);
            GameEventBus.Unsubscribe<DeathTrackedEvent>(OnCreatureDied);
            GameEventBus.Unsubscribe<TurnStartedEvent>(OnTurnStarted);
        }

        // ── Обработчики событий ───────────────────────────────────────────────

        static void OnTurnStarted(TurnStartedEvent evt)
        {
            _currentTurn = evt.TurnNumber;
        }

        static void OnCardPlayed(CardTrackedEvent evt)
        {
            var record = new CardPlayRecord(
                evt.CardEntity,
                evt.ModelId,
                evt.CardName,
                evt.PlayerId,
                evt.Rarity,
                evt.Element,
                evt.CardType,
                _currentTurn);

            _cardPlays.Add(record);

            Increment(_cardsPlayedByPlayer, evt.PlayerId);
            Increment(_cardPlaysByName, evt.CardName ?? string.Empty);
            Increment(_cardsByElement, evt.Element);
            Increment(_cardsByRarity, evt.Rarity);
            Increment(_cardsByType, evt.CardType);
        }

        static void OnDamageDealt(DamageTrackedEvent evt)
        {
            var record = new DamageRecord(
                evt.SourceEntity,
                evt.TargetEntity,
                evt.SourcePlayerId,
                evt.Amount,
                _currentTurn);

            _damageDealt.Add(record);

            Increment(_damageDealtByPlayer, evt.SourcePlayerId, evt.Amount);
            Increment(_damageReceivedByPlayer, evt.TargetPlayerId, evt.Amount);
        }

        static void OnCreatureDied(DeathTrackedEvent evt)
        {
            var record = new DeathRecord(
                evt.CreatureEntity,
                evt.OwnerId,
                evt.CardName,
                evt.ModelId,
                _currentTurn);

            _deaths.Add(record);
            Increment(_killsByPlayer, evt.KillerPlayerId);
        }

        // ── Публичные методы запроса ──────────────────────────────────────────

        /// <summary>Проверяет произвольное условие.</summary>
        //public static bool Check(MatchCondition condition) => condition.Evaluate();

        // — Карты —

        /// <summary>Общее число разыгранных карт (оба игрока).</summary>
        public static int TotalCardsPlayed() => _cardPlays.Count;

        /// <summary>Число карт, разыгранных конкретным игроком.</summary>
        public static int CardsPlayedBy(int playerId)
            => _cardsPlayedByPlayer.TryGetValue(playerId, out int v) ? v : 0;

        /// <summary>Число разыгрываний карты с данным именем.</summary>
        public static int CardsPlayedWithName(string name, int playerId = -1)
        {
            if (playerId < 0)
                return _cardPlaysByName.TryGetValue(name ?? string.Empty, out int v) ? v : 0;

            int count = 0;
            foreach (var r in _cardPlays)
                if (r.PlayerId == playerId && r.CardName == name)
                    count++;
            return count;
        }

        /// <summary>Число разыгранных карт с данным элементом.</summary>
        public static int CardsPlayedWithElement(EnumService.Element element, int playerId = -1)
        {
            if (playerId < 0)
                return _cardsByElement.TryGetValue(element, out int v) ? v : 0;

            int count = 0;
            foreach (var r in _cardPlays)
                if (r.PlayerId == playerId && r.Element == element)
                    count++;
            return count;
        }

        /// <summary>Число разыгранных карт с данной редкостью.</summary>
        public static int CardsPlayedWithRarity(EnumService.Rarity rarity, int playerId = -1)
        {
            if (playerId < 0)
                return _cardsByRarity.TryGetValue(rarity, out int v) ? v : 0;

            int count = 0;
            foreach (var r in _cardPlays)
                if (r.PlayerId == playerId && r.Rarity == rarity)
                    count++;
            return count;
        }

        /// <summary>Число разыгранных карт данного типа (Creature / Spell / Charm).</summary>
        public static int CardsPlayedOfType(EnumService.CardType type, int playerId = -1)
        {
            if (playerId < 0)
                return _cardsByType.TryGetValue(type, out int v) ? v : 0;

            int count = 0;
            foreach (var r in _cardPlays)
                if (r.PlayerId == playerId && r.Type == type)
                    count++;
            return count;
        }

        // — Урон —

        /// <summary>Суммарный нанесённый урон игрока.</summary>
        public static int DamageDealtBy(int playerId)
            => _damageDealtByPlayer.TryGetValue(playerId, out int v) ? v : 0;

        /// <summary>Суммарный полученный урон игрока.</summary>
        public static int DamageReceivedBy(int playerId)
            => _damageReceivedByPlayer.TryGetValue(playerId, out int v) ? v : 0;

        /// <summary>Суммарный нанесённый урон (оба игрока).</summary>
        public static int TotalDamageDealt()
        {
            int total = 0;
            foreach (var r in _damageDealt) total += r.Amount;
            return total;
        }

        // — Гибели —

        /// <summary>Число убийств у игрока.</summary>
        public static int KillsByPlayer(int playerId)
            => _killsByPlayer.TryGetValue(playerId, out int v) ? v : 0;

        /// <summary>Число погибших существ конкретного игрока.</summary>
        public static int DeathsOfPlayer(int playerId)
        {
            int count = 0;
            foreach (var r in _deaths)
                if (r.OwnerId == playerId) count++;
            return count;
        }

        /// <summary>Число погибших существ с данным именем карты.</summary>
        public static int DeathsWithName(string name)
        {
            int count = 0;
            foreach (var r in _deaths)
                if (r.CardName == name) count++;
            return count;
        }

        // ── Вспомогательные методы ────────────────────────────────────────────

        static void Increment<TKey>(Dictionary<TKey, int> dict, TKey key, int amount = 1)
        {
            dict.TryGetValue(key, out int cur);
            dict[key] = cur + amount;
        }
    }
}
