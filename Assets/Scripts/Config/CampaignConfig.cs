using System.Collections.Generic;
using UnityEngine;

namespace Game.Core.Configs
{
    // === class (config) ===
    /// <summary>
    /// Кампания стори-мода: упорядоченный список энкаунтеров (Create → Game → Campaign).
    /// Класть в Resources/Campaign/ (напр. campaign_001) — StoryModePanel сам найдёт кампании
    /// (порядок по имени ассета) и построит список уровней из Encounters. Новая глава/кампания =
    /// новый ассет, код не трогаем. Прогрессия: уровень открыт, если предыдущий В ЭТОЙ кампании пройден.
    /// </summary>
    [CreateAssetMenu(fileName = "campaign_001", menuName = "Game/Campaign")]
    public class CampaignConfig : ScriptableObject
    {
        [Tooltip("Название кампании (ключ локализации ui.story.campaign.* или сырой текст).")]
        public string CampaignName = "Кампания";

        [Tooltip("Бои кампании по порядку (ассеты PveEncounterConfig).")]
        public List<PveEncounterConfig> Encounters = new();
    }
}
