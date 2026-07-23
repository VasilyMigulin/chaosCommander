namespace Game.Core.Backend
{
    /// <summary>
    /// Единая точка констант бэкенд-слоя (PlayFab Economy v1 + classic CloudScript + Title Data).
    ///
    /// Валюты v1 — Virtual Currency по коду ("GD"/"GM"), никаких item-id не нужно (в отличие от v2).
    /// Классификация предметов инвентаря — по префиксу CatalogItem Id. Имена функций = имена
    /// handlers.* в CloudScript-ревизии (Game Manager → Automation → Cloud Script).
    /// </summary>
    public static class BackendConfig
    {
        // ── Валюты (Virtual Currency, код) ─────────────────────────────────────
        public const string GoldCode   = "GD";   // Бабосик
        public const string GemsCode   = "GM";   // Безделушки (донат)
        public const string ScrapsCode = "SC";   // Обрывки — за распыление карт, тратятся на чёрном рынке

        // ── Классификация предметов по префиксу CatalogItem Id ──────────────────
        // Карта: "{expansionId}_{cardId}" (см. CardItemId). Бустер/аватар — с явным префиксом.
        public const string BoosterIdPrefix = "booster_";
        public const string AvatarIdPrefix  = "avatar_";

        // ── Имена CloudScript-функций (handlers.* в ревизии) ───────────────────
        public static class Fn
        {
            // Фундамент / миграция
            public const string MigrateLibrary   = "MigrateLibrary";

            // Входящие (награды, выданные удалённо → показать при входе)
            public const string GetInbox         = "GetInbox";

            // Профиль игрока (статистика: победы/поражения/достижения/…)
            public const string GetProfile       = "GetProfile";

            // Награды / задачи
            public const string GetDailyState    = "GetDailyState";
            public const string ClaimLoginReward = "ClaimLoginReward";
            public const string ClaimTask        = "ClaimTask";
            public const string ReportProgress      = "ReportTaskProgress";
            public const string ReportProgressBatch = "ReportTaskProgressBatch";

            // Магазин
            public const string GetShop          = "GetShop";
            public const string BuyStoreItem     = "BuyStoreItem";

            // Бустеры
            public const string OpenBooster      = "OpenBooster";

            // Чёрный рынок
            public const string GetBlackMarket   = "GetBlackMarket";
            public const string BuyBlackMarket   = "BuyBlackMarketCard";

            // Распыление карты → Обрывки (по редкости)
            public const string DustCard         = "DustCard";

            // DEV (только под флагом devMode в Title Data)
            public const string DevGrantCurrency = "DevGrantCurrency";
            public const string DevGrantBooster  = "DevGrantBooster";
            public const string DevGrantCard     = "DevGrantCard";
            public const string DevGrantExpansion = "DevGrantExpansion";   // выдать всю коллекцию сета
            public const string DevResetJournal      = "DevResetJournal";
            public const string DevCompleteTasks     = "DevCompleteTasks";
            public const string DevResetBlackMarket  = "DevResetBlackMarket";

            // Аукцион (модель ставок: лот на 1 час, единая валюта, победитель по дедлайну)
            public const string GetListings      = "GetAuctionListings";
            public const string ListCard         = "ListCardForSale";
            public const string CancelListing    = "CancelAuctionListing";
            public const string PlaceBid         = "PlaceAuctionBid";
            public const string ResolveAuctions  = "ResolveAuctions";   // крон/дев — расчёт истёкших лотов
        }
    }
}
