// =====================================================================================
// ChaosCommander — PlayFab Classic CloudScript (Economy v1).
// Загрузка: Game Manager → Automation → Cloud Script → Upload new revision → Save + Deploy.
// Всё server-authoritative: клиент зовёт handlers.* через ExecuteCloudScript, здесь идут
// нативные server.* (GrantItemsToUser / AddUserVirtualCurrency / GetUserInventory / ...).
// currentPlayerId — аутентифицированный PlayFabId вызвавшего (клиенту не доверяем).
//
// Реализованы: MigrateLibrary (фундамент), OpenBooster (бустеры).
// Остальные — заготовки (возвращают shaped-ответ), дописываются по фазам.
//
// ВАЖНО: имена ключей ответов PascalCase — должны совпадать с клиентскими DTO (Game.Core.Backend).
// ES5-стиль (var/function) — для совместимости с движком CloudScript.
// =====================================================================================

// ---- helpers ----------------------------------------------------------------

function getTitleJson(key) {
    var res = server.GetTitleData({ Keys: [key] });
    if (!res.Data || !res.Data[key]) return null;
    try { return JSON.parse(res.Data[key]); } catch (e) { return null; }
}

// GrantItemsToUser с чанкованием (лимит предметов на вызов).
function grantItems(playerId, itemIds) {
    if (!itemIds || itemIds.length === 0) return;
    var CHUNK = 25;
    for (var i = 0; i < itemIds.length; i += CHUNK) {
        server.GrantItemsToUser({ PlayFabId: playerId, ItemIds: itemIds.slice(i, i + CHUNK) });
    }
}

function readWallet(playerId) {
    var inv = server.GetUserInventory({ PlayFabId: playerId });
    var wallet = [];
    var vc = inv.VirtualCurrency || {};
    for (var code in vc) if (vc.hasOwnProperty(code)) wallet.push({ Code: code, Amount: vc[code] });
    return wallet;
}

// Выдать RewardBundle (валюты + карты + бустеры + аватары).
function grantReward(playerId, reward) {
    if (!reward) return;
    var i;
    if (reward.Currencies) for (i = 0; i < reward.Currencies.length; i++) {
        var c = reward.Currencies[i];
        server.AddUserVirtualCurrency({ PlayFabId: playerId, VirtualCurrency: c.Code, Amount: c.Amount });
    }
    var ids = [];
    if (reward.Cards) for (i = 0; i < reward.Cards.length; i++) {
        var card = reward.Cards[i];
        for (var k = 0; k < card.Amount; k++) ids.push(card.ItemId);
    }
    if (reward.Boosters) for (i = 0; i < reward.Boosters.length; i++) ids.push(reward.Boosters[i]);
    if (reward.Avatars)  for (i = 0; i < reward.Avatars.length; i++)  ids.push(reward.Avatars[i]);
    grantItems(playerId, ids);
}

function rollRarity(weights) {
    var total = 0, key;
    for (key in weights) if (weights.hasOwnProperty(key)) total += weights[key];
    var roll = Math.random() * (total <= 0 ? 1 : total);
    var acc = 0;
    for (key in weights) { if (!weights.hasOwnProperty(key)) continue; acc += weights[key]; if (roll <= acc) return key; }
    for (key in weights) if (weights.hasOwnProperty(key)) return key;
    return "common";
}

function emptyReward() { return { Currencies: [], Cards: [], Boosters: [], Avatars: [] }; }

// ---- Фундамент: миграция старой библиотеки --------------------------------------

handlers.MigrateLibrary = function (args, context) {
    var playerId = currentPlayerId;
    var result = { Migrated: false, CardsGranted: 0 };

    // уже мигрировано?
    var ro = server.GetUserReadOnlyData({ PlayFabId: playerId, Keys: ["library_migrated"] });
    if (ro.Data && ro.Data["library_migrated"]) return result;

    var ud = server.GetUserData({ PlayFabId: playerId, Keys: ["player_library"] });
    var granted = 0;
    if (ud.Data && ud.Data["player_library"]) {
        var owned = null;
        try { owned = JSON.parse(ud.Data["player_library"].Value); } catch (e) { owned = null; }
        if (owned && owned.Cards) {
            var ids = [];
            for (var i = 0; i < owned.Cards.length; i++) {
                var c = owned.Cards[i];
                if (!c.ExpansionId || c.Count <= 0) continue;
                for (var k = 0; k < c.Count; k++) ids.push(c.ExpansionId + "_" + c.CardId);
                granted += c.Count;
            }
            grantItems(playerId, ids);
        }
    }

    server.UpdateUserReadOnlyData({
        PlayFabId: playerId,
        Data: { "library_migrated": (new Date()).toISOString() }
    });

    result.Migrated = true;
    result.CardsGranted = granted;
    return result;
};

// ---- Бустеры: открытие ----------------------------------------------------------

handlers.OpenBooster = function (args, context) {
    var playerId = currentPlayerId;
    var resp = { Success: false, Reason: null, Wallet: null, Reward: emptyReward() };

    var boosterId = args && args.BoosterItemId;
    if (!boosterId) { resp.Reason = "bad_request"; return resp; }

    var cfgAll = getTitleJson("boosterConfig");
    var pool = getTitleJson("cardPool");
    if (!cfgAll || !pool) { resp.Reason = "config_missing"; return resp; }
    var cfg = cfgAll[boosterId];
    if (!cfg) { resp.Reason = "unknown_booster"; return resp; }

    // найти инстанс бустера и списать ПЕРЕД роллом (анти-дюп)
    var inv = server.GetUserInventory({ PlayFabId: playerId });
    var instanceId = null;
    for (var i = 0; i < inv.Inventory.length; i++) {
        if (inv.Inventory[i].ItemId === boosterId) { instanceId = inv.Inventory[i].ItemInstanceId; break; }
    }
    if (!instanceId) { resp.Reason = "not_owned"; return resp; }
    server.RevokeInventoryItem({ PlayFabId: playerId, ItemInstanceId: instanceId });

    // ролл слотов
    var byRarity = pool[cfg.expansion] || {};
    var grantedIds = [];
    for (var s = 0; s < cfg.slots.length; s++) {
        var rarity = rollRarity(cfg.slots[s].weights);
        var ids = byRarity[rarity];
        if (!ids || ids.length === 0) continue;
        grantedIds.push(ids[Math.floor(Math.random() * ids.length)]);
    }
    grantItems(playerId, grantedIds);

    // агрегировать в Reward.Cards
    var counts = {};
    for (var g = 0; g < grantedIds.length; g++) counts[grantedIds[g]] = (counts[grantedIds[g]] || 0) + 1;
    for (var id in counts) if (counts.hasOwnProperty(id)) resp.Reward.Cards.push({ ItemId: id, Amount: counts[id] });

    resp.Success = true;
    return resp;
};

// ---- Магазин (Phase 3) — заготовки ---------------------------------------------

handlers.GetShop = function (args, context) {
    // TODO(Phase 3): собрать витрину (Title Data "shopConfig" или Catalog v1 с ценами).
    return { Entries: [] };
};

handlers.BuyStoreItem = function (args, context) {
    var resp = { Success: false, Reason: "not_implemented", Wallet: null, Reward: emptyReward() };
    // TODO(Phase 3): цена из shopConfig/каталога → SubtractUserVirtualCurrency → GrantItemsToUser → Wallet.
    return resp;
};

// ---- Чёрный рынок (Phase 5) — заготовки -----------------------------------------

handlers.GetBlackMarket = function (args, context) {
    // TODO(Phase 5): rotationIndex по неделе (среда) → роллить набор из blackMarketConfig.pools,
    //  состояние в UserReadOnlyData "blackMarket", покупка гасит остальные офферы до ротации.
    return { RotationIndex: 0, ServerTimeUtc: (new Date()).toISOString(), NextRotationUtc: "", PurchasedThisRotation: false, Offers: [] };
};

handlers.BuyBlackMarketCard = function (args, context) {
    return { Success: false, Reason: "not_implemented", Wallet: null, Reward: emptyReward() };
};

// ---- Daily/Weekly + login (Phase 4) — заготовки ---------------------------------

handlers.GetDailyState = function (args, context) {
    // TODO(Phase 4): taskConfig + playerProgress; dayIndex/weekIndex по UTC; сброс; claimable/claimed; login.
    return {
        ServerTimeUtc: (new Date()).toISOString(),
        DailyResetHourUtc: 0, WeeklyResetDay: 3, WeeklyResetHourUtc: 0,
        Login: { StreakDay: 0, Available: false, Today: emptyReward() },
        Daily: [], Weekly: []
    };
};

handlers.ClaimLoginReward = function (args, context) {
    return { Success: false, Reason: "not_implemented", Wallet: null, Reward: emptyReward() };
};

handlers.ClaimTask = function (args, context) {
    return { Success: false, Reason: "not_implemented", Wallet: null, Reward: emptyReward() };
};

handlers.ReportTaskProgress = function (args, context) {
    // TODO(Phase 4): инкремент прогресса задач с совпадающим Type. win_games в P2P — self-report (хардить позже).
    return { Success: true, Reason: null, Wallet: null };
};

// ---- Аукцион (Phase 6) — заготовки ----------------------------------------------
// Модель: escrow (server.RevokeInventoryItem у продавца) + индекс листингов (Shared Group Data
// для MVP, позже внешняя БД) + атомарная покупка (Subtract/Add валют + GrantItemsToUser).

handlers.GetAuctionListings   = function (args, context) { return { Listings: [], ContinuationToken: null }; };
handlers.GetMyAuctionListings = function (args, context) { return { Listings: [], ContinuationToken: null }; };
handlers.ListCardForSale      = function (args, context) { return { Success: false, Reason: "not_implemented", Wallet: null, ListingId: null }; };
handlers.CancelAuctionListing = function (args, context) { return { Success: false, Reason: "not_implemented", Wallet: null }; };
handlers.BuyAuctionListing    = function (args, context) { return { Success: false, Reason: "not_implemented", Wallet: null, Reward: emptyReward() }; };
