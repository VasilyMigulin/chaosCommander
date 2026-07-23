using System;
using System.Collections.Generic;
using Game.Core.Backend;
using Game.Core.Configs;
using Game.Core.Model.Card;
using PlayFab.ClientModels;
using UnityEngine;

namespace Game.Core.DeckBuilder
{
    /// <summary>
    /// Рантайм-библиотека всех карт игрока.
    /// Load/Save работают через PlayFab.
    /// </summary>
    public static class PlayerLibrary
    {
        static readonly Dictionary<string, CardEntry> _entries = new Dictionary<string, CardEntry>();

        public static IReadOnlyDictionary<string, CardEntry> Entries => _entries;

        /// <summary>Библиотека изменилась (загрузка/выдача карт). Панели коллекции обновляются в рантайме.</summary>
        public static event Action Changed;
        static void RaiseChanged() => Changed?.Invoke();

        // ── Load ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Загрузить библиотеку из PlayFab, затем построить рантайм-модели через CardConfig.
        /// </summary>
        public static void LoadFromCloud(CardConfig config,
            Action onSuccess = null, Action<string> onError = null)
        {
            PlayFabService.LoadLibrary(list =>
            {
                Build(list, config);
                onSuccess?.Invoke();
            }, onError);
        }

        /// <summary>Построить из готового списка OwnedCardData (например после входа).</summary>
        public static void Build(IEnumerable<OwnedCardData> owned, CardConfig config)
        {
            _entries.Clear();
            if (owned == null) return;

            foreach (var data in owned)
            {
                var instance = config.Get(data.ExpansionId, data.CardId);
                if (instance == null || instance.CardData == null)
                {
                    Debug.LogWarning($"[PlayerLibrary] Card not found: {data.ExpansionId}_{data.CardId}");
                    continue;
                }

                string key = MakeKey(data.ExpansionId, data.CardId);
                if (_entries.TryGetValue(key, out var existing))
                    existing.AddCopies(data.Count);
                else
                    _entries[key] = new CardEntry(instance.CardData.Clone(), data.Count);
            }
            RaiseChanged();
        }

        // ── Load из Economy v2 инвентаря (актуальный путь) ─────────────────────

        /// <summary>
        /// Загрузить библиотеку из инвентаря Economy v2. Это АВТОРИТЕТНЫЙ источник владения картами
        /// (заменяет client-authoritative UserData JSON). Владение меняет только сервер.
        /// </summary>
        public static void LoadFromInventory(CardConfig config,
            Action onSuccess = null, Action<string> onError = null)
        {
            EconomyService.GetInventory(inv =>
            {
                BuildFromInventory(inv.Cards, config);
                PlayerWallet.SetAll(inv.Currencies);
                onSuccess?.Invoke();
            }, onError);
        }

        /// <summary>
        /// Построить рантайм-библиотеку из карт инвентаря v1. Копии карты = отдельные ItemInstance
        /// с одинаковым ItemId, поэтому считаем количество как число инстансов на ItemId.
        /// </summary>
        public static void BuildFromInventory(IEnumerable<ItemInstance> cardItems, CardConfig config)
        {
            _entries.Clear();
            if (cardItems == null) return;

            foreach (var item in cardItems)
            {
                if (item?.ItemId == null) continue;
                if (!CardItemId.TryParse(item.ItemId, out var expansionId, out var cardId)) continue;

                var instance = config.Get(expansionId, cardId);
                if (instance == null || instance.CardData == null)
                {
                    Debug.LogWarning($"[PlayerLibrary] Inventory card not found in CardConfig: {item.ItemId}");
                    continue;
                }

                string key = MakeKey(expansionId, cardId);
                if (_entries.TryGetValue(key, out var existing))
                    existing.AddCopies(1);
                else
                    _entries[key] = new CardEntry(instance.CardData.Clone(), 1);
            }
            RaiseChanged();
        }

        /// <summary>
        /// Применить выданные сервером карты (RewardBundle.Cards) к библиотеке — БЕЗ повторного запроса
        /// инвентаря: сервер уже сказал, что выдал. Резолвит item id → CardConfig → добавляет. Одно
        /// событие Changed на всю пачку. Зовётся после гранта карт / открытия бустера / покупки.
        /// </summary>
        public static void AddGranted(IEnumerable<GrantedCard> granted, CardConfig config)
        {
            if (granted == null || config == null) return;
            bool any = false;
            foreach (var g in granted)
            {
                if (g == null || !CardItemId.TryParse(g.ItemId, out var exp, out var id)) continue;
                var inst = config.Get(exp, id);
                if (inst == null || inst.CardData == null)
                {
                    Debug.LogWarning($"[PlayerLibrary] Выданная карта не найдена в CardConfig: {g.ItemId}");
                    continue;
                }
                AddCardInternal(inst.CardData, Mathf.Max(1, g.Amount));
                any = true;
            }
            if (any) RaiseChanged();
        }

        // ── Save (ЛЕГАСИ — только до полного перехода на Economy) ──────────────

        /// <summary>Сохранить текущую библиотеку в PlayFab UserData.
        /// ЛЕГАСИ: после миграции на Economy v2 владение меняет сервер, этот путь не использовать.</summary>
        public static void SaveToCloud(Action onSuccess = null, Action<string> onError = null)
        {
            PlayFabService.SaveLibrary(ToOwnedList(), onSuccess, onError);
        }

        // ── Mutate ────────────────────────────────────────────────────────────

        /// <summary>Добавить карту в библиотеку (при открытии бустера и т.п.).</summary>
        public static void AddCard(CardModel model, int count = 1)
        {
            AddCardInternal(model, count);
            RaiseChanged();
        }

        // Без события — для пачечных добавлений (одно Changed на всю пачку у вызывающего).
        static void AddCardInternal(CardModel model, int count)
        {
            string key = MakeKey(model.ExpansionId, model.Id);
            if (_entries.TryGetValue(key, out var entry))
                entry.AddCopies(count);
            else
                _entries[key] = new CardEntry(model.Clone(), count);
        }

        /// <summary>
        /// Добавить набор карт в библиотеку без сохранения в облако.
        /// Используется для тестовых карт и при открытии бустеров.
        /// </summary>
        public static void AddCards(IEnumerable<CardModel> models, int countEach = 1)
        {
            bool any = false;
            foreach (var model in models)
            {
                if (model == null) continue;
                AddCardInternal(model, countEach);
                any = true;
            }
            if (any) RaiseChanged();
        }

        /// <summary>
        /// Добавить карты из массива CardInstanceData без сохранения в облако.
        /// Используется для тестовых карт в InitState.
        /// </summary>
        public static void AddInstanceCards(Game.Core.Instance.Card.CardInstanceData[] instances, int countEach = 1)
        {
            if (instances == null) return;
            foreach (var instance in instances)
            {
                // ВАЖНО: null-проверка ДО доступа к CardData.Rarity. CardData может быть null, если у
                // карты слетели SerializeReference-данные (компайл-ошибка в Ability/Model.Card или
                // несохранённые правки перед входом в Play) — тогда просто пропускаем, а не падаем NRE.
                if (instance == null || instance.CardData == null)
                {
                    Debug.LogWarning("[PlayerLibrary] AddInstanceCards: пустой instance/CardData — пропуск " +
                                     "(вероятно слетели SerializeReference-данные карты; см. консоль на компайл-ошибки и сохрани проект перед Play).");
                    continue;
                }

                int count = instance.CardData.Rarity switch
                {
                    Service.EnumService.Rarity.Common    => 4,
                    Service.EnumService.Rarity.Rare      => 3,
                    Service.EnumService.Rarity.Epic      => 2,
                    Service.EnumService.Rarity.Legendary => 1,
                    Service.EnumService.Rarity.Exotic    => 1,
                    _ => countEach,
                };

                AddCardInternal(instance.CardData, count); // Clone выполняется внутри
            }
            RaiseChanged();
        }

        /// <summary>
        /// REVIEW-СБОРКА: заполнить библиотеку ВСЕЙ коллекцией из CardConfig в максимуме копий под лимиты
        /// колоды (комон 4 / рарка 3 / эпик 2 / лега 1 / экзот 1) — чтобы издатель сразу собирал ЛЮБЫЕ колоды.
        /// Полностью ЛОКАЛЬНО (сервер и реальное владение не трогаем). Зовётся ТОЛЬКО под дефайном
        /// PUBLISHER_REVIEW (см. InitState) — в проде не вызывается.
        /// </summary>
        public static void FillFullCollection(CardConfig config)
        {
            _entries.Clear();
            if (config == null || config.Expansions == null) { RaiseChanged(); return; }
            config.Rebuild();   // проставляет CardData.ExpansionId по каждому сету (ключи карт не схлопнутся)
            foreach (var exp in config.Expansions)
            {
                if (exp == null || exp.Cards == null) continue;
                foreach (var instance in exp.Cards)
                {
                    if (instance == null || instance.CardData == null) continue;
                    if (instance.CardData.IsToken) continue;   // токены/стори-карты (IsToken) в коллекцию не идут — как в CardPool.Matches
                    AddCardInternal(instance.CardData, MaxCopiesFor(instance.CardData.Rarity));
                }
            }
            Debug.Log($"[PlayerLibrary] REVIEW: коллекция разблокирована ({_entries.Count} уникальных карт).");
            RaiseChanged();
        }

        // Максимум копий карты по редкости = лимит колоды (совпадает с AddInstanceCards / DeckBuilderService).
        static int MaxCopiesFor(Service.EnumService.Rarity r) => r switch
        {
            Service.EnumService.Rarity.Common    => 4,
            Service.EnumService.Rarity.Rare      => 3,
            Service.EnumService.Rarity.Epic      => 2,
            Service.EnumService.Rarity.Legendary => 1,
            Service.EnumService.Rarity.Exotic    => 1,
            _ => 1,
        };

        /// <summary>Убрать копии карты (распыление «Порвать»). count &lt; 0 не бывает; 0 копий → запись удаляется.
        /// Событие Changed → коллекция обновится в рантайме.</summary>
        public static void RemoveCard(string expansionId, int cardId, int count = 1)
        {
            string key = MakeKey(expansionId, cardId);
            if (!_entries.TryGetValue(key, out var entry)) return;
            entry.RemoveCopies(count);
            if (entry.OwnedCount <= 0) _entries.Remove(key);
            RaiseChanged();
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        public static bool TryGet(string expansionId, int cardId, out CardEntry entry)
            => _entries.TryGetValue(MakeKey(expansionId, cardId), out entry);

        public static string MakeKey(string expansionId, int cardId) => $"{expansionId}_{cardId}";

        public static void Clear() { _entries.Clear(); RaiseChanged(); }

        public static IEnumerable<OwnedCardData> ToOwnedList()
        {
            foreach (var kv in _entries)
                yield return new OwnedCardData(kv.Value.Model.ExpansionId, kv.Value.Model.Id, kv.Value.OwnedCount);
        }
    }
}
