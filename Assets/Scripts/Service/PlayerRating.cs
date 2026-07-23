namespace Game.Core.Service
{
    // === helper ===
    /// <summary>
    /// Рейтинг локального игрока (MMR) — единственный источник правды для подбора соперника.
    /// Живёт в Service, чтобы его видели И Photon (выбор комнаты в MatchmakingService), И UI
    /// (GamePanel показывает ранг/MMR) — без зависимости Photon → Backend.
    ///
    /// MVP: значение локальное (PlayerPrefs), старт = 1000, по итогу матча пока никто не меняет —
    /// правится руками/дев-меню для теста подбора.
    /// РЕЛИЗ: MMR считает СЕРВЕР (PlayFab statistics): после логина кладёт значение сюда, после матча
    /// пересчитывает. Клиент его не вычисляет — иначе подделает и будет фармить новичков.
    /// </summary>
    public static class PlayerRating
    {
        public const int DefaultMmr = 1000;
        const string Key = "player_mmr";

        static int _mmr = -1;   // -1 = ещё не читали PlayerPrefs

        public static int Mmr
        {
            get
            {
                if (_mmr < 0) _mmr = UnityEngine.PlayerPrefs.GetInt(Key, DefaultMmr);
                return _mmr;
            }
            set
            {
                _mmr = UnityEngine.Mathf.Max(1, value);
                UnityEngine.PlayerPrefs.SetInt(Key, _mmr);
                UnityEngine.PlayerPrefs.Save();
            }
        }

        /// <summary>Звание словами. Пока заглушка-плейсхолдер: вместо бронза/серебро — «упоротые» титулы в духе
        /// «Горе-героев» (пороги «на глаз»). Настоящая система званий за универсальность — позже.</summary>
        public static string RankName => Mmr switch
        {
            < 800  => "Позор клана",
            < 1000 => "Юный дилетант",
            < 1200 => "Бывалый неудачник",
            < 1450 => "Средней паршивости герой",
            < 1700 => "Почти легенда",
            _      => "Горе-легенда",
        };
    }
}
