using Game.Core.Service;
using UnityEngine;

namespace Game.Core.Configs
{
    // === helper ===
    /// <summary>
    /// Единая точка получения ТЕКУЩЕГО энкаунтера PvE: приоритет — прямая ссылка из кампании
    /// (PveMode.EncounterAsset, ставит StoryModePanel), фолбэк — Resources-путь (дев-кнопка).
    /// Используют InitDeckSystem/InitPveOpponentSystem/RunAiTurnSystem/BattleState — чтобы логика
    /// «откуда берётся энкаунтер» не размазывалась по четырём местам.
    /// </summary>
    public static class PveEncounterLocator
    {
        public static PveEncounterConfig Current
        {
            get
            {
                if (PveMode.EncounterAsset is PveEncounterConfig direct) return direct;
                return Resources.Load<PveEncounterConfig>(PveMode.EncounterPath);
            }
        }
    }
}
