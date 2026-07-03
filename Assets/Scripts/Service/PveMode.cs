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

        /// <summary>Путь ассета PveEncounterConfig в Resources (папка Assets/Resources/Encounter/).</summary>
        public static string EncounterPath = "Encounter/encounter_001";

        public static void Reset()
        {
            Enabled = false;
            // EncounterPath не трогаем — удобная «последняя выбранная» для повторного входа.
        }

        /// <summary>Ключ PlayerPrefs «уровень пройден» (пишет BattleState при победе, читает StoryModePanel).
        /// MVP: локальный прогресс; при появлении серверного прогресса заменить хранилище здесь.</summary>
        public static string DoneKey(string encounterPath) => "pve_done_" + encounterPath;
    }
}
