namespace Game.Core.Service
{
    // === helper ===
    /// <summary>
    /// Режим PvE (бой против ИИ, без Photon): статичный флаг + путь к ассету энкаунтера (Resources).
    /// Живёт в Service, чтобы был виден ВСЕМ сборкам (Mono/Components/Systems/States) без новых зависимостей.
    /// Ставится ДО загрузки BattleScene (дев-кнопка/меню), читается бутстрапом боя (BattleState) и
    /// PvE-ветками систем (InitPlayerSystem/TurnGate/EndTurnRequestSystem/RunAiTurnSystem/...).
    /// Сбрасывается при выходе из боя (BattleState.OnDestroy) — иначе следующий MP-матч сломается.
    /// </summary>
    public static class PveMode
    {
        /// <summary>Бой идёт против ИИ (локальная симуляция ОБОИХ игроков, сеть не используется).</summary>
        public static bool Enabled;

        /// <summary>Путь ассета PveEncounterConfig в Resources (папка Assets/Resources/Encounter/) —
        /// ДЕВ/фолбэк-канал. Кампания задаёт энкаунтер ПРЯМОЙ ссылкой (EncounterAsset) — приоритетнее пути.</summary>
        public static string EncounterPath = "Encounter/encounter_001";

        /// <summary>Прямая ссылка на ассет PveEncounterConfig текущего боя (ставит StoryModePanel из
        /// кампании). Тип object — Service не знает Configs; потребители кастят (PveEncounterLocator).</summary>
        public static UnityEngine.Object EncounterAsset;

        /// <summary>Стабильный id энкаунтера для прогресса (имя ассета). Пуст → берётся EncounterPath.</summary>
        public static string EncounterId;

        /// <summary>Ключ прогресса ТЕКУЩЕГО боя.</summary>
        public static string CurrentDoneKey()
            => DoneKey(string.IsNullOrEmpty(EncounterId) ? EncounterPath : EncounterId);

        public static void Reset()
        {
            Enabled = false;
            EncounterAsset = null;
            EncounterId = null;
            // EncounterPath не трогаем — удобная «последняя выбранная» для повторного входа с дев-кнопки.
        }

        /// <summary>Ключ PlayerPrefs «уровень пройден» (пишет BattleState при победе, читает StoryModePanel).
        /// MVP: локальный прогресс; при появлении серверного прогресса заменить хранилище здесь.</summary>
        public static string DoneKey(string encounterPath) => "pve_done_" + encounterPath;
    }
}
