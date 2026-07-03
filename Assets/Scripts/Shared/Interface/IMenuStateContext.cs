using System;

namespace Game.Core.Shared.Interface
{
    /// <summary>UI-статус матчмейкинга (без завязки UI на сборку Photon — мост через MenuState).</summary>
    public enum MatchmakingUiStatus
    {
        Searching,      // ищем сессию / ждём соперника
        OpponentFound,  // соперник найден
        Loading,        // грузим бой
        Failed,         // не удалось
        Cancelled,      // отменено игроком
    }

    public interface IMenuStateContext
    {
        void StartMatchMaking();

        /// <summary>Отменить поиск (кнопка Cancel в FindOpponentPanel).</summary>
        void CancelMatchMaking();

        /// <summary>Смена UI-статуса поиска — FindOpponentPanel обновляет текст/кнопки.</summary>
        event Action<MatchmakingUiStatus> MatchmakingStatusChanged;

        /// <summary>Старт PvE-боя (стори/туториал): сперва чисто гасит активный матчмейкинг (иначе его
        /// асинхронный фейл после LoadScene заново открывал меню-канвас ПОВЕРХ боя), затем ставит PveMode
        /// и грузит BattleScene. encounterPath — путь энкаунтера в Resources (null = текущий/последний).</summary>
        void StartPveBattle(string encounterPath = null);
    }
}
