using System.Linq;
using Game.Core.Configs;
using Game.Core.Service;
using Game.Core.Shared;
using UnityEngine;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Прогресс кампании в ОДНОМ месте: какая кампания активна, её бои и сколько пройдено.
    /// Общий источник для StoryModePanel (список уровней) и GamePanel (подпись «Глава 3 из 6» на
    /// карточке PvE) — иначе цифра на карточке разъедется со списком.
    ///
    /// Источник: ассеты CampaignConfig в Resources/Campaign (первый по имени — MVP: одна кампания).
    /// Фолбэк без кампаний — скан Resources/Encounter. Пройденность: PlayerPrefs[PveMode.DoneKey(имя ассета)]
    /// (пишет BattleState при победе в PvE).
    /// </summary>
    public static class CampaignProgress
    {
        public const string CampaignsFolder = "Campaign";
        public const string EncountersFolder = "Encounter";

        public struct State
        {
            /// <summary>null → кампаний нет, работает фолбэк-скан энкаунтеров (имени у набора нет).</summary>
            public CampaignConfig Campaign;
            public PveEncounterConfig[] Encounters;

            /// <summary>Сколько боёв пройдено ПОДРЯД с начала — прогрессия линейная (уровень открыт,
            /// если предыдущий пройден), так что это же и индекс текущего боя (0-based).</summary>
            public int DoneCount;

            public int Total => Encounters != null ? Encounters.Length : 0;
            public bool HasLevels => Total > 0;
            public bool Completed => HasLevels && DoneCount >= Total;

            /// <summary>Номер текущей главы, 1-based — для «Глава 3 из 6». У пройденной кампании = Total.</summary>
            public int CurrentChapter => Mathf.Clamp(DoneCount + 1, 1, Mathf.Max(1, Total));

            /// <summary>Название кампании. В ассете это либо сырой текст, либо ключ локализации
            /// (ui.story.campaign.*) — GetText вернёт перевод, а на неизвестном ключе сам текст.</summary>
            public string Name => Campaign != null && !string.IsNullOrEmpty(Campaign.CampaignName)
                ? CardTextLocalization.GetText(Campaign.CampaignName, Campaign.CampaignName)
                : string.Empty;
        }

        public static State Load()
        {
            var state = new State();

            var campaigns = Resources.LoadAll<CampaignConfig>(CampaignsFolder).OrderBy(c => c.name).ToArray();
            if (campaigns.Length > 0)
            {
                state.Campaign = campaigns[0];
                state.Encounters = campaigns[0].Encounters.Where(e => e != null).ToArray();
                if (campaigns.Length > 1)
                    Debug.LogWarning($"[CampaignProgress] Кампаний {campaigns.Length} — беру первую ('{campaigns[0].name}'); выбор кампании появится позже.");
            }
            else
            {
                state.Encounters = Resources.LoadAll<PveEncounterConfig>(EncountersFolder).OrderBy(e => e.name).ToArray();
            }

            // Считаем подряд с начала: первый НЕ пройденный и есть текущая глава.
            int done = 0;
            foreach (var enc in state.Encounters)
            {
                if (!IsDone(enc)) break;
                done++;
            }
            state.DoneCount = done;

            return state;
        }

        public static bool IsDone(PveEncounterConfig encounter)
            => encounter != null && PlayerPrefs.GetInt(PveMode.DoneKey(encounter.name), 0) == 1;
    }
}
