using System;
using System.Collections.Generic;
using Game.Core.Instance.Card;
using UnityEngine;

namespace Game.Core.Configs
{
    // === class (config) ===
    /// <summary>
    /// Пресет колоды (Create → Game → Deck Preset): командир + карты С КОЛИЧЕСТВОМ.
    /// Сейчас — тестовые колоды (InitState.DeckPresets: собери несколько ассетов и выбирай активный
    /// индексом, вместо старого массива TestingDeck с элементом на каждую копию прямо в сцене).
    /// В будущем — заготовки/стартовые колоды для игроков (формат уже готов).
    /// </summary>
    [CreateAssetMenu(fileName = "deck_preset_001", menuName = "Game/Deck Preset")]
    public class DeckPreset : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public CardInstanceData Card;
            [Min(1)] public int Count;
        }

        [Tooltip("Название колоды (для выбора/UI).")]
        public string DeckName = "Колода";

        [Tooltip("Командир колоды.")]
        public CardInstanceData Commander;

        [Tooltip("Карты колоды: ассет + количество копий.")]
        public List<Entry> Cards = new();

        /// <summary>Все уникальные карты пресета (командир + состав) — напр. для авто-наполнения библиотеки.</summary>
        public IEnumerable<CardInstanceData> AllCards()
        {
            if (Commander != null) yield return Commander;
            foreach (var e in Cards)
                if (e.Card != null) yield return e.Card;
        }
    }
}
