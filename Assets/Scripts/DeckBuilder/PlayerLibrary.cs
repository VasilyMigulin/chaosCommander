using System;
using System.Collections.Generic;
using Game.Core.Configs;
using Game.Core.Model.Card;
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
        }

        // ── Save ─────────────────────────────────────────────────────────────

        /// <summary>Сохранить текущую библиотеку в PlayFab.</summary>
        public static void SaveToCloud(Action onSuccess = null, Action<string> onError = null)
        {
            PlayFabService.SaveLibrary(ToOwnedList(), onSuccess, onError);
        }

        // ── Mutate ────────────────────────────────────────────────────────────

        /// <summary>Добавить карту в библиотеку (при открытии бустера и т.п.).</summary>
        public static void AddCard(CardModel model, int count = 1)
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
            foreach (var model in models)
            {
                if (model == null) continue;
                AddCard(model, countEach);
            }
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
                if (instance != null && instance.CardData != null)
                    AddCard(instance.CardData, countEach); // Clone выполняется внутри AddCard
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        public static bool TryGet(string expansionId, int cardId, out CardEntry entry)
            => _entries.TryGetValue(MakeKey(expansionId, cardId), out entry);

        public static string MakeKey(string expansionId, int cardId) => $"{expansionId}_{cardId}";

        public static void Clear() => _entries.Clear();

        public static IEnumerable<OwnedCardData> ToOwnedList()
        {
            foreach (var kv in _entries)
                yield return new OwnedCardData(kv.Value.Model.ExpansionId, kv.Value.Model.Id, kv.Value.OwnedCount);
        }
    }
}
