using System.Collections.Generic;
using System.Linq;
using Game.Core.Model.Card;
using Game.Core.Model.Card.Creature;
using Game.Core.Service;
using UnityEngine;

namespace Game.Core.DeckBuilder
{
    /// <summary>
    /// Сервис сборки колоды. Не зависит от ECS.
    ///
    /// Правила:
    ///   - Commander: легендарная CardCreatureModel (выбирается первой)
    ///   - После выбора командира доступные карты фильтруются по цвету командира
    ///   - Common   — не более 4 копий
    ///   - Rare     — не более 3 копий
    ///   - Epic     — не более 2 копий
    ///   - Legendary — не более 1 копии
    ///   - Exotic   — не более 1 копии на всю колоду (включая командира)
    /// </summary>
    public class DeckBuilderService
    {
        // ── Лимиты ───────────────────────────────────────────────────────────

        public static int MaxCopies(EnumService.Rarity rarity)
        {
            switch (rarity)
            {
                case EnumService.Rarity.Common:    return 4;
                case EnumService.Rarity.Rare:      return 3;
                case EnumService.Rarity.Epic:      return 2;
                case EnumService.Rarity.Legendary: return 1;
                case EnumService.Rarity.Exotic:    return 1;
                default:                           return 0;
            }
        }

        // ── Состояние ────────────────────────────────────────────────────────

        public CardModel Commander { get; private set; }

        /// <summary>Карты в текущей колоде (без командира).</summary>
        public IReadOnlyDictionary<string, DeckCardEntry> DeckEntries => _deckEntries;

        /// <summary>Суммарное количество карт в колоде (без командира).</summary>
        public int TotalCards => _deckEntries.Values.Sum(e => e.DeckCount);

        /// <summary>Есть ли в колоде хотя бы одна Exotic-карта (не считая командира).</summary>
        public bool HasExotic => _deckEntries.Values
            .Any(e => e.Model.Rarity == EnumService.Rarity.Exotic && e.DeckCount > 0);

        readonly Dictionary<string, DeckCardEntry> _deckEntries = new Dictionary<string, DeckCardEntry>();

        // ── Commander ────────────────────────────────────────────────────────

        public bool TrySetCommander(CardModel card)
        {
            if (!IsValidCommander(card))
                return false;

            // Снять флаг с предыдущего командира
            if (Commander is Game.Core.Model.Card.Creature.CardCreatureModel prevCmd)
                prevCmd.IsCommander = false;

            Commander = card;
            if (card is Game.Core.Model.Card.Creature.CardCreatureModel creatureCmd)
                creatureCmd.IsCommander = true;

            _deckEntries.Clear();
            return true;
        }

        /// <summary>
        /// Убрать командира и полностью сбросить колоду.
        /// Используется кнопкой «Очистить».
        /// </summary>
        public void ClearAll()
        {
            if (Commander is Game.Core.Model.Card.Creature.CardCreatureModel cmd)
                cmd.IsCommander = false;

            Commander = null;
            _deckEntries.Clear();
        }

        /// <summary>
        /// Убрать только командира, оставив набранные карты.
        /// Используется когда игрок снимает командира через кнопку удаления.
        /// </summary>
        public void RemoveCommander()
        {
            if (Commander is Game.Core.Model.Card.Creature.CardCreatureModel cmd)
                cmd.IsCommander = false;

            Commander = null;
        }

        public static bool IsValidCommander(CardModel card)
        {
            return card != null
                && card.Rarity == EnumService.Rarity.Legendary
                && card is CardCreatureModel;
        }

        // ── Add / Remove ─────────────────────────────────────────────────────

        /// <summary>
        /// Добавить одну копию карты в колоду.
        /// Возвращает <see cref="AddResult"/> с причиной отказа при ошибке.
        /// </summary>
        public AddResult TryAdd(CardModel card)
        {
            if (Commander == null)
                return AddResult.NoCommander;

            if (!IsColorAllowed(card))
                return AddResult.WrongColor;

            if (card.Rarity == EnumService.Rarity.Legendary && IsValidCommander(card))
                return AddResult.IsCommander; // нельзя добавлять командиров как обычные карты

            if (card.Rarity == EnumService.Rarity.Exotic && HasExotic)
                return AddResult.ExoticLimitReached;

            string key = PlayerLibrary.MakeKey(card.ExpansionId, card.Id);

            _deckEntries.TryGetValue(key, out var entry);
            int current = entry?.DeckCount ?? 0;

            if (current >= MaxCopies(card.Rarity))
                return AddResult.CopyLimitReached;

            if (!PlayerLibrary.TryGet(card.ExpansionId, card.Id, out var owned) || owned.OwnedCount <= current)
                return AddResult.NotEnoughCopies;

            if (entry == null)
            {
                entry           = new DeckCardEntry(card, 0);
                _deckEntries[key] = entry;
            }
            entry.Increment();
            return AddResult.Ok;
        }

        /// <summary>
        /// Убрать одну копию карты из колоды.
        /// </summary>
        public bool TryRemove(CardModel card)
        {
            string key = PlayerLibrary.MakeKey(card.ExpansionId, card.Id);
            if (!_deckEntries.TryGetValue(key, out var entry) || entry.DeckCount == 0)
                return false;

            entry.Decrement();
            if (entry.DeckCount == 0)
                _deckEntries.Remove(key);
            return true;
        }

        // ── Validation ───────────────────────────────────────────────────────

        public bool IsColorAllowed(CardModel card)
        {
            if (Commander == null) return false;
            // Бесцветных/нейтральных карт в игре пока нет — строгая проверка по элементу
            return card.Element == Commander.Element;
        }

        /// <summary>Перевести колоду в SavedDeckData для сохранения.</summary>
        public SavedDeckData Export(string deckName)
        {
            if (Commander == null) return null;

            var data = new SavedDeckData
            {
                Name      = deckName,
                Commander = new OwnedCardData(Commander.ExpansionId, Commander.Id, 1),
            };

            foreach (var entry in _deckEntries.Values.Where(e => e.DeckCount > 0))
                data.Cards.Add(new OwnedCardData(entry.Model.ExpansionId, entry.Model.Id, entry.DeckCount));

            return data;
        }

        /// <summary>Загрузить ранее сохранённую колоду для редактирования.</summary>
        public void Import(SavedDeckData data, Game.Core.Configs.CardConfig config)
        {
            _deckEntries.Clear();
            Commander = null;

            var cmdInstance = config.Get(data.Commander.ExpansionId, data.Commander.CardId);
            if (cmdInstance != null) Commander = cmdInstance.CardData;

            foreach (var entry in data.Cards)
            {
                var inst = config.Get(entry.ExpansionId, entry.CardId);
                if (inst?.CardData == null) continue;

                string key = PlayerLibrary.MakeKey(entry.ExpansionId, entry.CardId);
                _deckEntries[key] = new DeckCardEntry(inst.CardData, entry.Count);
            }
        }

        public enum AddResult
        {
            Ok,
            NoCommander,
            WrongColor,
            IsCommander,
            ExoticLimitReached,
            CopyLimitReached,
            NotEnoughCopies,
        }
    }
}
