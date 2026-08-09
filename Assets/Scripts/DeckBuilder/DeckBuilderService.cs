using Game.Core.Model.Card;
using Game.Core.Model.Card.Creature;
using Game.Core.Service;
using Game.Core.Shared.Interface;   // небоевые способности → правила сборки (зона отложенных)
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static UnityEngine.EventSystems.EventTrigger;

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

        // ── Сайдборд («отложенные», Сказочник) ───────────────────────────────
        // Зона существует, ТОЛЬКО пока её объявляет небоевая способность карты в колоде (или командира).
        // Убрали Сказочника — зона схлопывается, а отложенные карты возвращаются в библиотеку (см. SyncSideboard).

        readonly Dictionary<string, DeckCardEntry> _sideboardEntries = new Dictionary<string, DeckCardEntry>();

        /// <summary>Отложенные карты.</summary>
        public IReadOnlyDictionary<string, DeckCardEntry> SideboardEntries => _sideboardEntries;

        /// <summary>Сколько карт уже отложено.</summary>
        public int SideboardCount => _sideboardEntries.Values.Sum(e => e.DeckCount);

        /// <summary>Правила сборки, собранные с небоевых способностей КОМАНДИРА и карт колоды.
        /// Сайдборд в опрос НЕ входит: иначе отложенный Сказочник расширял бы зону сам себе.</summary>
        public DeckBuildRules Rules => NonBattleRules.Deck(DeckBuildAbilities());

        /// <summary>Сколько карт требуется отложить (0 — зоны нет).</summary>
        public int SideboardSize => Rules.SideboardSize;

        /// <summary>Зона отложенных активна (в колоде есть карта, которая её объявляет).</summary>
        public bool HasSideboard => SideboardSize > 0;

        /// <summary>Сайдборд добран полностью — колода готова к бою.</summary>
        public bool SideboardComplete => !HasSideboard || SideboardCount >= SideboardSize;

        IEnumerable<INonBattleAbility> DeckBuildAbilities()
        {
            if (Commander?.NonBattleAbilities != null)
                foreach (var a in Commander.NonBattleAbilities)
                    if (a != null) yield return a;

            foreach (var entry in _deckEntries.Values)
            {
                if (entry.DeckCount <= 0 || entry.Model?.NonBattleAbilities == null) continue;
                foreach (var a in entry.Model.NonBattleAbilities)
                    if (a != null) yield return a;
            }
        }

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
            _sideboardEntries.Clear();   // колода обнулена → зона отложенных больше ничем не объявлена
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
            _sideboardEntries.Clear();
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
        public AddResult TryAdd(CardModel card, bool checkColor = true)
        {
            if (Commander == null)
                return AddResult.NoCommander;

            // Карта-командир НЕ может лежать в колоде ещё и как обычная легендарка. Сравниваем по
            // ИДЕНТИЧНОСТИ (expansion+id), а не по ссылке: после реимпорта колоды командир — другой
            // экземпляр CardModel (из CardConfig), и сравнение ссылок это пропускало (баг с дублем).
            if (IsCommanderCard(card))
                return AddResult.IsCommanderCard;

            if (checkColor)
            {
                if (!IsColorAllowed(card))
                {
                    return AddResult.WrongColor; 
                }
            }

            if (card.Rarity == EnumService.Rarity.Exotic && HasExotic)
                return AddResult.ExoticLimitReached;

            var limit = CheckCopyLimits(card);
            if (limit != AddResult.Ok) return limit;

            string key = PlayerLibrary.MakeKey(card.ExpansionId, card.Id);
            if (!_deckEntries.TryGetValue(key, out var entry))
            {
                entry           = new DeckCardEntry(card, 0);
                _deckEntries[key] = entry;
            }
            entry.Increment();
            return AddResult.Ok;
        }

        /// <summary>Общее число копий карты, занятых игроком, — В ОБЕИХ ЗОНАХ.
        /// Решение юзера 2026-08-01: сайдборд вне РАЗМЕРА колоды, но внутри ЛИМИТА КОПИЙ — иначе он давал бы
        /// «пятую копию» в обход редкости, а редкость в этой игре и есть баланс.</summary>
        public int CopiesOf(CardModel card)
        {
            if (card == null) return 0;
            string key = PlayerLibrary.MakeKey(card.ExpansionId, card.Id);
            int n = 0;
            if (_deckEntries.TryGetValue(key, out var d))      n += d.DeckCount;
            if (_sideboardEntries.TryGetValue(key, out var s)) n += s.DeckCount;
            return n;
        }

        /// <summary>ЕДИНАЯ проверка «можно ли занять ещё одну копию» — одна на колоду и сайдборд, чтобы
        /// правило редкости и владения нельзя было обойти, положив карту в другую зону.</summary>
        AddResult CheckCopyLimits(CardModel card)
        {
            int current = CopiesOf(card);

            if (current >= MaxCopies(card.Rarity))
                return AddResult.CopyLimitReached;

            if (!PlayerLibrary.TryGet(card.ExpansionId, card.Id, out var owned) || owned.OwnedCount <= current)
                return AddResult.NotEnoughCopies;

            return AddResult.Ok;
        }

        /// <summary>Отложить одну копию карты (зона Сказочника).
        ///
        /// ЦВЕТ КОЛОДЫ ЗДЕСЬ НЕ ДЕЙСТВУЕТ (решение юзера 2026-08-02) — и это не послабление, а СМЫСЛ
        /// механики: Сказочник достаёт что угодно, включая карты вне цветов командира. С цветовым
        /// правилом он выродился бы в «ещё три карты той же колоды».
        ///
        /// Остальные правила действуют как в колоде: лимит копий по редкости считается СКВОЗЬ обе зоны
        /// (CheckCopyLimits), экзотика по-прежнему одна на колоду, командира отложить нельзя.</summary>
        public AddResult TryAddToSideboard(CardModel card)
        {
            if (Commander == null)          return AddResult.NoCommander;
            if (!HasSideboard)              return AddResult.NoSideboard;
            if (IsCommanderCard(card))      return AddResult.IsCommanderCard;
            if (SideboardCount >= SideboardSize) return AddResult.SideboardFull;

            if (card.Rarity == EnumService.Rarity.Exotic && HasExotic)
                return AddResult.ExoticLimitReached;

            var limit = CheckCopyLimits(card);
            if (limit != AddResult.Ok) return limit;

            string key = PlayerLibrary.MakeKey(card.ExpansionId, card.Id);
            if (!_sideboardEntries.TryGetValue(key, out var entry))
            {
                entry                  = new DeckCardEntry(card, 0);
                _sideboardEntries[key] = entry;
            }
            entry.Increment();
            return AddResult.Ok;
        }

        /// <summary>Вернуть отложенную карту в библиотеку.</summary>
        public bool TryRemoveFromSideboard(CardModel card)
        {
            if (card == null) return false;
            string key = PlayerLibrary.MakeKey(card.ExpansionId, card.Id);
            if (!_sideboardEntries.TryGetValue(key, out var entry) || entry.DeckCount == 0)
                return false;

            entry.Decrement();
            if (entry.DeckCount == 0) _sideboardEntries.Remove(key);
            return true;
        }

        /// <summary>Привести сайдборд к текущим правилам: зона исчезла (убрали Сказочника) → отложенные
        /// возвращаются в библиотеку; зона сузилась → срезаем лишние. Вызывать после ЛЮБОЙ правки колоды,
        /// иначе колода унесёт в бой отложенные карты без карты, которая их достаёт.</summary>
        public void SyncSideboard()
        {
            int size = SideboardSize;
            if (size <= 0) { _sideboardEntries.Clear(); return; }

            // Срезаем с конца детерминированно (по ключу), чтобы результат не зависел от порядка словаря.
            var keys = _sideboardEntries.Keys.OrderBy(k => k, System.StringComparer.Ordinal).ToList();
            for (int i = keys.Count - 1; i >= 0 && SideboardCount > size; i--)
            {
                var entry = _sideboardEntries[keys[i]];
                while (entry.DeckCount > 0 && SideboardCount > size) entry.Decrement();
                if (entry.DeckCount == 0) _sideboardEntries.Remove(keys[i]);
            }
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

            // Убрали карту, объявлявшую зону (Сказочника) → отложенные возвращаются в библиотеку.
            SyncSideboard();
            return true;
        }

        // ── Validation ───────────────────────────────────────────────────────

        /// <summary>Это карта текущего командира? По ИДЕНТИЧНОСТИ (expansion+id), не по ссылке — чтобы
        /// работало и после реимпорта колоды (командир из CardConfig — другой экземпляр). Ею редактор
        /// прячет командира из библиотеки и запрещает класть его в колоду второй раз.</summary>
        public bool IsCommanderCard(CardModel card)
            => Commander != null && card != null
               && card.ExpansionId == Commander.ExpansionId && card.Id == Commander.Id;

        public bool IsColorAllowed(CardModel card)
        {
            if (DebugFlags.IgnoreDeckColorRule) return true;   // тех-режим: собираем без правила цвета
            if (Commander == null) return false;

            // Если у карты есть хотя бы один флаг,
            // которого нет у командира → false
            return (card.Element & ~Commander.Element) == 0;
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

            // Сайдборд приводим к правилам ПЕРЕД выгрузкой: иначе сохранилась бы колода с отложенными
            // картами, но без карты, которая их достаёт (её могли убрать последним действием).
            SyncSideboard();
            foreach (var entry in _sideboardEntries.Values.Where(e => e.DeckCount > 0))
                data.Sideboard.Add(new OwnedCardData(entry.Model.ExpansionId, entry.Model.Id, entry.DeckCount));

            return data;
        }

        /// <summary>Загрузить ранее сохранённую колоду для редактирования.</summary>
        public void Import(SavedDeckData data, Game.Core.Configs.CardConfig config)
        {
            _deckEntries.Clear();
            _sideboardEntries.Clear();
            Commander = null;

            var cmdInstance = config.Get(data.Commander.ExpansionId, data.Commander.CardId);
            if (cmdInstance != null) Commander = cmdInstance.CardData;

            foreach (var entry in data.Cards)
            {
                var inst = config.Get(entry.ExpansionId, entry.CardId);
                if (inst?.CardData == null) continue;

                // Санация: командир не должен лежать в колоде обычной картой (старый баг с дублем).
                // Отсеиваем при загрузке — так уже испорченные колоды чинятся сами при первом открытии.
                if (IsCommanderCard(inst.CardData)) continue;

                string key = PlayerLibrary.MakeKey(entry.ExpansionId, entry.CardId);
                _deckEntries[key] = new DeckCardEntry(inst.CardData, entry.Count);
            }

            // Сайдборд — ПОСЛЕ колоды: зона существует, только если её объявляет карта колоды/командира,
            // а до заполнения _deckEntries правила ещё пусты и всё бы срезалось.
            if (data.Sideboard != null)
            {
                foreach (var entry in data.Sideboard)
                {
                    var inst = config.Get(entry.ExpansionId, entry.CardId);
                    if (inst?.CardData == null) continue;
                    if (IsCommanderCard(inst.CardData)) continue;

                    string key = PlayerLibrary.MakeKey(entry.ExpansionId, entry.CardId);
                    _sideboardEntries[key] = new DeckCardEntry(inst.CardData, entry.Count);
                }
            }
            SyncSideboard();
        }

        public enum AddResult
        {
            Ok,
            NoCommander,
            WrongColor,
            ExoticLimitReached,
            CopyLimitReached,
            NotEnoughCopies,
            IsCommanderCard,   // это карта командира — в колоду обычной картой нельзя
            NoSideboard,       // зоны отложенных нет (в колоде нет карты, которая её объявляет)
            SideboardFull,     // отложено уже столько, сколько требует способность
        }
    }
}
