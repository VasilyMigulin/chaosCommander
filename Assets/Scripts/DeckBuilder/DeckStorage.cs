using System;
using System.Collections.Generic;

namespace Game.Core.DeckBuilder
{
    public static class DeckStorage
    {
        static List<SavedDeckData> _cache = new List<SavedDeckData>();

        public static void LoadAll(Action<List<SavedDeckData>> onSuccess, Action<string> onError = null)
        {
            PlayFabService.LoadDecks(decks =>
            {
                _cache = decks ?? new List<SavedDeckData>();
                onSuccess?.Invoke(_cache);
            }, onError);
        }

        public static void SaveAll(IEnumerable<SavedDeckData> decks,
            Action onSuccess = null, Action<string> onError = null)
        {
            _cache = new List<SavedDeckData>(decks);
            PlayFabService.SaveDecks(_cache, onSuccess, onError);
        }

        public static void SaveOrReplace(SavedDeckData deck,
            Action onSuccess = null, Action<string> onError = null)
        {
            int idx = _cache.FindIndex(d => d.Name == deck.Name);
            if (idx >= 0) _cache[idx] = deck;
            else _cache.Add(deck);
            PlayFabService.SaveDecks(_cache, onSuccess, onError);
        }

        public static void Delete(string deckName,
            Action onSuccess = null, Action<string> onError = null)
        {
            _cache.RemoveAll(d => d.Name == deckName);
            PlayFabService.SaveDecks(_cache, onSuccess, onError);
        }

        public static IReadOnlyList<SavedDeckData> GetCached() => _cache;

        /// <summary>
        /// Записывает колоду прямо в кеш без обращения к облаку.
        /// Используется для тестовых колод, собранных в InitState.
        /// </summary>
        public static void SetTesting(SavedDeckData deck)
        {
            _cache.Clear();
            if (deck != null)
                _cache.Add(deck);
        }
    }
}
