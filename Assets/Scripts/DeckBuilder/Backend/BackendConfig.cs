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
        public const string GoldCode = "GD";
        public const string GemsCode = "GM";

        // ── Классификация предметов по префиксу CatalogItem Id ──────────────────
        // Карта: "{expansionId}_{cardId}" (см. CardItemId). Бустер/аватар — с явным префиксом.
        public const string BoosterIdPrefix = "booster_";
        public const string AvatarIdPrefix  = "avatar_";

        // ── Имена CloudScript-функций (handlers.* в ревизии) ───────────────────
        public static class Fn
        {
            // Фундамент / миграция
            public const string MigrateLibrary   = "MigrateLibrary";

            // Награды / задачи
            public const string GetDailyState    = "GetDailyState";
            public const string ClaimLoginReward = "ClaimLoginReward";
            public const string ClaimTask        = "ClaimTask";
            public const string ReportProgress   = "ReportTaskProgress";

            // Магазин
            public const string GetShop          = "GetShop";
            public const string BuyStoreItem     = "BuyStoreItem";

            // Бустеры
            public const string OpenBooster      = "OpenBooster";

            // Чёрный рынок
            public const string GetBlackMarket   = "GetBlackMarket";
            public const string BuyBlackMarket   = "BuyBlackMarketCard";

            // Аукцион
            public const string GetListings      = "GetAuctionListings";
            public const string GetMyListings    = "GetMyAuctionListings";
            public const string ListCard         = "ListCardForSale";
            public const string CancelListing    = "CancelAuctionListing";
            public const string BuyListing       = "BuyAuctionListing";
        }
    }
}
