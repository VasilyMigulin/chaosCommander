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
        [Tooltip("Командир ИИ (может быть пустым — тогда без командира). Игнорируется, если ниже задан DeckPool.")]
        public CardInstanceData Commander;
        [Tooltip("Карты колоды: ассет + количество копий. Игнорируется, если ниже задан DeckPool.")]
        public List<DeckEntry> Cards = new();
        [Tooltip("Пул готовых колод (DeckPreset) — если не пуст, при старте боя ИИ СЛУЧАЙНО берёт ОДНУ колоду " +
                 "отсюда вместо Commander/Cards выше (та же идея, что PlayerDeck, но с вариативностью — юзер " +
                 "устал биться против одной и той же колоды в тренировке). Пресеты собираются визуально в игре: " +
                 "Tools → Cards → Deck Code → Preset.")]
        public List<DeckPreset> DeckPool = new();

        [Header("Параметры боя")]
        [Tooltip("Сколько карт ИИ берёт в стартовую руку (мулигана у ИИ нет).")]
        [Min(0)] public int StartingHand = 4;
        [Tooltip("HP ИИ-игрока.")]
        [Min(1)] public int Health = 30;

        [Header("Сюжет")]
        [Tooltip("СЮЖЕТНАЯ колода ИГРОКА для этого боя (стори-командир типа Шального принца + карты). " +
                 "Пусто = игрок играет своей колодой. Позволяет давать в стори недоступные в PvP карты.")]
        public DeckPreset PlayerDeck;

        [Serializable]
        public struct StoryLine
        {
            [Tooltip("Портрет говорящего (говорящая голова).")]
            public Sprite Portrait;
            [Tooltip("Ключ локализации ИМЕНИ говорящего (напр. story.gnidalf.name). Пусто = без имени.")]
            public string SpeakerKey;
            [Tooltip("Ключ локализации РЕПЛИКИ (напр. story.c1e1.intro.1).")]
            public string TextKey;
            [Tooltip("Фолбэк-текст реплики, если ключа нет в таблице (и черновик до локализации).")]
            [TextArea] public string FallbackText;
            [Tooltip("Сколько секунд висит реплика.")]
            [Min(0.5f)] public float Duration;
        }

        [Tooltip("Реплики ПЕРЕД боем (после мулигана, когда виден борд). По очереди.")]
        public StoryLine[] IntroLines;
        [Tooltip("Реплики при ПОБЕДЕ игрока.")]
        public StoryLine[] VictoryLines;
        [Tooltip("Реплики при ПОРАЖЕНИИ игрока.")]
        public StoryLine[] DefeatLines;

        // ── Скриптованные события ПОСРЕДИ боя ─────────────────────────────────────────
        // «Чистый сюжетный триггер»: персонаж-НЕучастник (напр. Гнидальф в бою с предпоследним боссом)
        // появляется говорящей головой И совершает игровое действие (замешать 2 Вонючих облака игроку).
        // Каждое событие срабатывает ОДИН раз за бой. Исполняет PveScriptedEventSystem (только PvE).

        public enum ScriptedTriggerKind
        {
            OnGlobalTurn,      // на старте хода с глобальным номером TurnNumber
            OnPlayerHpBelow,   // HP ИГРОКА упало ниже HpThreshold
            OnAiHpBelow,       // HP ИИ упало ниже HpThreshold
        }

        public enum ScriptedActionKind
        {
            ShuffleCardToDeck, // втасовать Amount копий Card в колоду (TargetPlayer: игроку/ИИ)
            AddCardToHand,     // дать Amount копий Card в руку
            DealDamage,        // нанести Amount урона игроку/ИИ (аватару)
            PlayCard,          // ПО-НАСТОЯЩЕМУ разыграть Amount копий Card (не спавн!) — своё «при разыгрывании» сработает
            PlayCommander,     // форс-розыгрыш КОМАНДИРА игрока/ИИ из руки (Card/Amount игнорируются)
        }

        [Serializable]
        public struct ScriptedAction
        {
            public ScriptedActionKind Kind;
            [Tooltip("Карта для Shuffle/AddCard/PlayCard (напр. Вонючее облако). Для DealDamage не нужна.")]
            public CardInstanceData Card;
            [Tooltip("Количество карт / величина урона.")]
            [Min(1)] public int Amount;
            [Tooltip("true = действие НА ИГРОКА (его колода/рука/HP), false = на ИИ.")]
            public bool TargetPlayer;
        }

        [Serializable]
        public struct ScriptedEvent
        {
            [Tooltip("Заметка автора (в игре не показывается).")]
            public string Note;
            public ScriptedTriggerKind Trigger;
            [Tooltip("Для OnGlobalTurn: номер хода (глобальный, растёт на 1 каждый ход любого игрока).")]
            public int TurnNumber;
            [Tooltip("Для On*HpBelow: порог HP (сработает, когда станет НИЖЕ).")]
            public int HpThreshold;
            [Tooltip("Реплики говорящей головы (по очереди).")]
            public StoryLine[] Lines;
            [Tooltip("Игровые действия события.")]
            public ScriptedAction[] Actions;
        }

        [Tooltip("Скриптованные события посреди боя (говорящая голова + действие). Каждое — один раз.")]
        public ScriptedEvent[] ScriptedEvents;

        [Header("Награда")]
        [Tooltip("Карты за ПЕРВОЕ прохождение боя (в библиотеку игрока). Один из основных источников " +
                 "стартовых карт: стори-прогресс = коллекция. Повторные победы наград не дают.")]
        public List<DeckEntry> RewardCards = new();

        [Header("Поведение ИИ")]
        [Tooltip("Пауза между действиями ИИ, сек (чтобы игрок успевал видеть ходы).")]
        [Min(0.1f)] public float ActionInterval = 0.9f;
    }
}
