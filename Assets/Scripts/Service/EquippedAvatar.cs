namespace Game.Core.Service
{
    // === helper ===
    /// <summary>
    /// Надетый косметический аватар игрока — единый источник «какой аватар показывать» (профиль/HUD/бой).
    /// Живёт в Service, чтобы видели ВСЕ слои (UI/ECS/Mono) без новых зависимостей.
    ///
    /// MVP: локально (PlayerPrefs), как MMR. РЕЛИЗ: значение кладёт бэкенд после логина (UserData
    /// "equipped_avatar"), а Set — пишет и в облако, чтобы аватар переезжал между устройствами.
    /// Пусто → аватар не выбран, слой показа подставит дефолт.
    /// </summary>
    public static class EquippedAvatar
    {
        const string Key = "equipped_avatar";

        static string _itemId;
        static bool _loaded;

        /// <summary>Каталожный itemId надетого аватара ("avatar_prince"). Пусто → не выбран.</summary>
        public static string ItemId
        {
            get
            {
                if (!_loaded) { _itemId = UnityEngine.PlayerPrefs.GetString(Key, ""); _loaded = true; }
                return _itemId;
            }
            set
            {
                _itemId = value ?? "";
                _loaded = true;
                UnityEngine.PlayerPrefs.SetString(Key, _itemId);
                UnityEngine.PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        public static bool HasAvatar => !string.IsNullOrEmpty(ItemId);

        /// <summary>Надетый аватар сменился — профиль/HUD обновляются в рантайме.</summary>
        public static event System.Action Changed;
    }
}
