using System;

namespace Game.Core.Shared.Interface
{
    /// <summary>
    /// Персистентный статик-хаб матчмейкинга для UI. Нужен потому, что MenuState живёт в MenuScene и
    /// уничтожается, когда Fusion грузит LobbyScene (Single-режим) — а UI (FindOpponentPanel) в
    /// DontDestroyOnLoad и переживает. Если панель зависит напрямую от MenuState, после перехода в лобби
    /// у неё «мёртвый мозг»: статус не приходит, Cancel не работает.
    ///
    /// Photon-сторона (MenuState) кормит сюда статус (SetStatus) и регистрирует обработчик отмены
    /// (CancelHandler — лямбда держит ссылку на ПЕРСИСТЕНТНЫЙ PhotonInitializer, поэтому работает даже
    /// после гибели MenuState). UI слушает StatusChanged и зовёт Cancel(). Оба статика → переживают сцены.
    /// </summary>
    public static class MatchmakingUiHub
    {
        public static event Action<MatchmakingUiStatus> StatusChanged;

        public static MatchmakingUiStatus Current { get; private set; } = MatchmakingUiStatus.Cancelled;

        /// <summary>Поиск сейчас активен (идёт/найден/грузится). Единственный источник правды для UI:
        /// гейтит повторное «начать поиск» (MenuState.StartMatchMaking) после ухода с панели поиска.</summary>
        public static bool SearchActive { get; private set; }

        /// <summary>Ставит Photon-сторона: полная отмена (завершить сессию + вернуться в меню).</summary>
        public static Action CancelHandler;

        public static void SetStatus(MatchmakingUiStatus status)
        {
            Current = status;
            SearchActive = status == MatchmakingUiStatus.Searching
                        || status == MatchmakingUiStatus.OpponentFound
                        || status == MatchmakingUiStatus.Loading;
            StatusChanged?.Invoke(status);
        }

        public static void Cancel()
        {
            if (CancelHandler != null) { CancelHandler(); return; }
            // Обработчик потерян (рассинхрон сцен/стейтов) — хотя бы не оставляем UI в «вечном поиске»:
            // показываем отмену и сбрасываем хаб, чтобы старт поиска снова работал.
            UnityEngine.Debug.LogWarning("[MatchmakingUiHub] Cancel без обработчика — сбрасываю хаб.");
            SetStatus(MatchmakingUiStatus.Cancelled);
            Reset();
        }

        public static void Reset()
        {
            CancelHandler = null;
            SearchActive = false;
            Current = MatchmakingUiStatus.Cancelled;
        }
    }
}
