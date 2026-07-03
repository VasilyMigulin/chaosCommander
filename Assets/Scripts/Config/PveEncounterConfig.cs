using System;
using System.Collections.Generic;
using Game.Core.Instance.Card;
using UnityEngine;

namespace Game.Core.Configs
{
    // === class (config) ===
    /// <summary>
    /// Энкаунтер PvE: колода/командир ИИ + параметры поведения. Авторится как ассет
    /// (Create → Game → Pve Encounter) и кладётся в Resources/Encounter/ — бой грузит его по
    /// PveMode.EncounterPath (напр. "Encounter/encounter_001"). Для туториала/стори — по ассету на бой.
    /// Карты — те же CardInstanceData, что и у игрока (никакого отдельного формата).
    /// </summary>
    [CreateAssetMenu(fileName = "encounter_001", menuName = "Game/Pve Encounter")]
    public class PveEncounterConfig : ScriptableObject
    {
        [Serializable]
        public struct DeckEntry
        {
            public CardInstanceData Card;
            [Min(1)] public int Count;
        }

        [Header("Идентичность")]
        [Tooltip("Имя противника (для UI/логов).")]
        public string EncounterName = "Противник";

        [Header("Колода ИИ")]
        [Tooltip("Командир ИИ (может быть пустым — тогда без командира).")]
        public CardInstanceData Commander;
        [Tooltip("Карты колоды: ассет + количество копий.")]
        public List<DeckEntry> Cards = new();

        [Header("Параметры боя")]
        [Tooltip("Сколько карт ИИ берёт в стартовую руку (мулигана у ИИ нет).")]
        [Min(0)] public int StartingHand = 4;
        [Tooltip("HP ИИ-игрока.")]
        [Min(1)] public int Health = 30;

        [Header("Поведение ИИ")]
        [Tooltip("Пауза между действиями ИИ, сек (чтобы игрок успевал видеть ходы).")]
        [Min(0.1f)] public float ActionInterval = 0.9f;
    }
}
