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

        /// <summary>Колода ИИ, СЛУЧАЙНО выбранная из PveEncounterConfig.DeckPool на ЭТОТ матч (см.
        /// InitPveOpponentSystem) — null, если пул не задан (тогда все читают Commander/Cards энкаунтера,
        /// как раньше). Единая точка: без неё BattleState (VS-экран, StartPveIntro) и InitPveOpponentSystem
        /// рандомили бы НЕЗАВИСИМО — на VS-экране был бы один командир, в бою другой (юзер 2026-08-21:
        /// «убрал печатного командира из полей — VS-каскад завис», раз он читал только Commander энкаунтера,
        /// не зная о пуле). InitPveOpponentSystem пишет ОДИН РАЗ за Init (IEcsInitSystem — раз в матч),
        /// BattleState.StartPveIntro читает СТРОГО ПОСЛЕ (EcsHandler.Init уже отработал).</summary>
        public static DeckPreset CurrentOpponentDeckPick { get; set; }
    }
}
