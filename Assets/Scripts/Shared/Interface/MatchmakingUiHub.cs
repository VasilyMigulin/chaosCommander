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

        public static MatchmakingUiStatus Current { get; private set; } = MatchmakingUiStatus.Searching;

        /// <summary>Ставит Photon-сторона: полная отмена (завершить сессию + вернуться в меню).</summary>
        public static Action CancelHandler;

        public static void SetStatus(MatchmakingUiStatus status)
        {
            Current = status;
            StatusChanged?.Invoke(status);
        }

        public static void Cancel() => CancelHandler?.Invoke();

        public static void Reset()
        {
            CancelHandler = null;
            Current = MatchmakingUiStatus.Searching;
        }
    }
}
