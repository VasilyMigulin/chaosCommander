// =====================================================================================
// ChaosCommander — PlayFab Classic CloudScript (Economy v1).
// Загрузка: Game Manager → Automation → Cloud Script → Upload new revision → Save + Deploy.
// Всё server-authoritative: клиент зовёт handlers.* через ExecuteCloudScript, здесь идут
// нативные server.* (GrantItemsToUser / AddUserVirtualCurrency / GetUserInventory / ...).
// currentPlayerId — аутентифицированный PlayFabId вызвавшего (клиенту не доверяем).
//
// Реализованы: MigrateLibrary (фундамент), OpenBooster (бустеры), GetShop/BuyStoreItem (магазин).
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

// Списать НЕСКОЛЬКО инстансов ОДНИМ пакетным вызовом (RevokeInventoryItems), чанкуя. Списывать по одному
// (RevokeInventoryItem в цикле) нельзя: CloudScript ограничивает число API-вызовов на выполнение, и
// распыление/открытие пачкой упиралось бы в лимит (CloudScriptAPIRequestError).
function revokeInstances(playerId, instanceIds) {
    if (!instanceIds || instanceIds.length === 0) return;
    var CHUNK = 25;
    for (var i = 0; i < instanceIds.length; i += CHUNK) {
        var slice = instanceIds.slice(i, i + CHUNK), items = [];
        for (var j = 0; j < slice.length; j++) items.push({ PlayFabId: playerId, ItemInstanceId: slice[j] });
        server.RevokeInventoryItems({ Items: items });
    }
}

// Карта валют {code: amount} → список для клиента.
function walletListFrom(vc) {
    var wallet = [];
    for (var code in vc) if (vc.hasOwnProperty(code)) wallet.push({ Code: code, Amount: vc[code] });
    return wallet;
}

// СНИМОК балансов — именно копия, а не ссылка на объект ответа API: снимок нужен, чтобы сравнить
// кошелёк до и после выдачи (см. купоны в RedeemPromo), а ссылка менялась бы вместе с оригиналом.
function copyVc(vc) {
    var out = {};
    for (var code in vc) if (vc.hasOwnProperty(code)) out[code] = vc[code];
    return out;
}

function readWallet(playerId) {
    return walletListFrom(server.GetUserInventory({ PlayFabId: playerId }).VirtualCurrency || {});
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

var MAX_OPEN_AT_ONCE = 5;   // потолок мульти-открытия

handlers.OpenBooster = function (args, context) {
    var playerId = currentPlayerId;
    var resp = { Success: false, Reason: null, Wallet: null, Reward: emptyReward(), Opened: 0 };

    var boosterId = args && args.BoosterItemId;
    if (!boosterId) { resp.Reason = "bad_request"; return resp; }

    var want = (args && args.Count) ? args.Count : 1;   // сколько бустеров открыть за раз
    if (want < 1) want = 1;
    if (want > MAX_OPEN_AT_ONCE) want = MAX_OPEN_AT_ONCE;

    var cfgAll = getTitleJson("boosterConfig");
    var pool = getTitleJson("cardPool");
    if (!cfgAll || !pool) { resp.Reason = "config_missing"; return resp; }
    var cfg = cfgAll[boosterId];
    if (!cfg) { resp.Reason = "unknown_booster"; return resp; }

    // Бустер привязан к прохождению стори-кампании (Похищение принцессы и т.п.) — флаг пишет
    // ClaimCampaignReward ПОСЛЕ выдачи награды, единственного источника такого бустера. Проверка тут —
    // защита на будущее (дев-грант вне прохождения, возможная продажа в магазине), а не основной путь.
    if (cfg.requiresCampaign) {
        var campaignFlagKey = "campaign_done_" + cfg.requiresCampaign;
        var campaignRo = server.GetUserReadOnlyData({ PlayFabId: playerId, Keys: [campaignFlagKey] });
        if (!campaignRo.Data || !campaignRo.Data[campaignFlagKey]) { resp.Reason = "campaign_not_completed"; return resp; }
    }

    // собрать до want инстансов бустера (сколько есть в наличии)
    var inv = server.GetUserInventory({ PlayFabId: playerId });
    var instanceIds = [];
    for (var i = 0; i < inv.Inventory.length && instanceIds.length < want; i++) {
        if (inv.Inventory[i].ItemId === boosterId) instanceIds.push(inv.Inventory[i].ItemInstanceId);
    }
    if (instanceIds.length === 0) { resp.Reason = "not_owned"; return resp; }

    revokeInstances(playerId, instanceIds);   // списать ПЕРЕД роллом (анти-дюп), пакетно

    var byRarity = pool[cfg.expansion] || {};
    var grantedIds = [];
    for (var b = 0; b < instanceIds.length; b++) {
        for (var s = 0; s < cfg.slots.length; s++) {
            var rarity = rollRarity(cfg.slots[s].weights);
            var ids = byRarity[rarity];
            if (!ids || ids.length === 0) continue;
            grantedIds.push(ids[Math.floor(Math.random() * ids.length)]);
        }
    }
    grantItems(playerId, grantedIds);

    // агрегировать в Reward.Cards
    var counts = {};
    for (var g = 0; g < grantedIds.length; g++) counts[grantedIds[g]] = (counts[grantedIds[g]] || 0) + 1;
    for (var id in counts) if (counts.hasOwnProperty(id)) resp.Reward.Cards.push({ ItemId: id, Amount: counts[id] });

    var opened = instanceIds.length;   // фактически открыто (может быть < want, если бустеров меньше)
    try { reportServerProgress(playerId, "open_boosters", opened); } catch (e3) {}

    resp.Success = true;
    resp.Opened = opened;
    return resp;
};

// ---- Кампании: разовая награда за ПОЛНОЕ прохождение стори-кампании ---------------
// Конфиг — Title Data "campaignRewards" (CampaignId → {boosterId, count}). Идемпотентно: флаг
// UserReadOnlyData["campaign_done_"+CampaignId] проверяется/пишется здесь же (тот же паттерн, что
// library_migrated у MigrateLibrary) — повторный вызов ничего не выдаёт. Прохождение САМО подтверждает
// клиент (PlayerPrefs-прогресс кампании, как и остальной PvE-прогресс в проекте на этой фазе) — сервер
// не переигрывает бои, а лишь не даёт вызвать это дважды. Флаг заодно читает OpenBooster (requiresCampaign).
handlers.ClaimCampaignReward = function (args, context) {
    var playerId = currentPlayerId;
    var resp = { Success: false, Reason: null, Wallet: null, Reward: emptyReward() };

    var campaignId = args && args.CampaignId;
    if (!campaignId) { resp.Reason = "bad_request"; return resp; }

    var flagKey = "campaign_done_" + campaignId;
    var ro = server.GetUserReadOnlyData({ PlayFabId: playerId, Keys: [flagKey] });
    if (ro.Data && ro.Data[flagKey]) { resp.Reason = "already_claimed"; return resp; }

    var cfg = (getTitleJson("campaignRewards") || {})[campaignId];
    if (!cfg || !cfg.boosterId) { resp.Reason = "unknown_campaign"; return resp; }

    var count = cfg.count > 0 ? cfg.count : 1;
    var ids = [];
    for (var i = 0; i < count; i++) ids.push(cfg.boosterId);

    try { grantItems(playerId, ids); }
    catch (e) { resp.Reason = "grant_error: " + (e && e.message ? e.message : ("" + e)); return resp; }

    // Флаг ПОСЛЕ успешной выдачи — если grantItems упал, повторный вызов не будет заблокирован "already_claimed".
    var data = {};
    data[flagKey] = (new Date()).toISOString();
    server.UpdateUserReadOnlyData({ PlayFabId: playerId, Data: data });

    resp.Success = true;
    resp.Reward.Boosters = ids;
    resp.Wallet = readWallet(playerId);
    return resp;
};

// ---- DEV (работают ТОЛЬКО при Title Data devMode === "true") ---------------------

// Дев-гранты разрешены, если ЛИБО глобальный devMode (тест-тайтл), ЛИБО у вызвавшего флаг isDev
// в UserReadOnlyData (аккаунт разработчика). isDev пишет только сервер/Game Manager — клиент не может.
// Так можно держать devMode=false в проде, а гранты оставить доступными ТОЛЬКО своим аккаунтам.
function devEnabled() {
    var d = server.GetTitleData({ Keys: ["devMode"] });
    if (d.Data && d.Data.devMode === "true") return true;

    var ro = server.GetUserReadOnlyData({ PlayFabId: currentPlayerId, Keys: ["isDev"] });
    return ro.Data && ro.Data.isDev && ro.Data.isDev.Value === "true";
}

handlers.DevGrantCurrency = function (args, context) {
    if (!devEnabled()) return { Success: false, Reason: "dev_disabled", Wallet: null, Reward: emptyReward() };
    server.AddUserVirtualCurrency({ PlayFabId: currentPlayerId, VirtualCurrency: args.Code, Amount: args.Amount });
    return { Success: true, Reason: null, Wallet: readWallet(currentPlayerId), Reward: emptyReward() };
};

handlers.DevGrantBooster = function (args, context) {
    if (!devEnabled()) return { Success: false, Reason: "dev_disabled", Wallet: null, Reward: emptyReward() };
    var n = args.Count || 1, ids = [];
    for (var i = 0; i < n; i++) ids.push(args.ItemId);
    grantItems(currentPlayerId, ids);
    var reward = emptyReward(); reward.Boosters.push(args.ItemId);
    return { Success: true, Reason: null, Wallet: readWallet(currentPlayerId), Reward: reward };
};

handlers.DevGrantCard = function (args, context) {
    if (!devEnabled()) return { Success: false, Reason: "dev_disabled", Wallet: null, Reward: emptyReward() };
    var n = args.Count || 1, ids = [];
    for (var i = 0; i < n; i++) ids.push(args.ItemId);
    grantItems(currentPlayerId, ids);
    var reward = emptyReward(); reward.Cards.push({ ItemId: args.ItemId, Amount: n });
    return { Success: true, Reason: null, Wallet: readWallet(currentPlayerId), Reward: reward };
};

// Выдать ВСЮ коллекцию экспеншена (по 1 копии каждой карты) — из cardPool. Быстрый тест магазина/коллекции.
// Перед выдачей СВЕРЯЕМ id с каталогом: если в cardPool затесался id, которого уже нет в каталоге,
// GrantItemsToUser роняет ВЕСЬ чанк (ItemNotFound → CloudScriptAPIRequestError), и «выдать всё» падает
// целиком. Бустеры этот id могли ни разу не выпасть — поэтому баг вылезает только на массовой выдаче. Битые
// id пропускаем (Reason="skipped_N"), а реальную ошибку выдачи ловим и отдаём текстом — не глотаем в 500.
handlers.DevGrantExpansion = function (args, context) {
    if (!devEnabled()) return { Success: false, Reason: "dev_disabled", Wallet: null, Reward: emptyReward(), Granted: 0 };
    var exp = args && args.ExpansionId;
    if (!exp) return { Success: false, Reason: "bad_request", Wallet: null, Reward: emptyReward(), Granted: 0 };

    var pool = getTitleJson("cardPool");
    if (!pool || !pool[exp]) return { Success: false, Reason: "unknown_expansion", Wallet: null, Reward: emptyReward(), Granted: 0 };

    var byRarity = pool[exp], ids = [], rar, arr, i;
    for (rar in byRarity) {
        if (!byRarity.hasOwnProperty(rar)) continue;
        arr = byRarity[rar];
        for (i = 0; i < arr.length; i++) ids.push(arr[i]);
    }

    // Множество валидных id из каталога карт ("main" — тот же, из которого читает распыление). Каталог пуст
    // (не та версия / недоступен) → НЕ режем, грантим как есть, пусть упадёт с внятной ошибкой в catch ниже.
    var grantIds = ids, skipped = 0;
    try {
        var cat = server.GetCatalogItems({ CatalogVersion: "main" });
        var catList = (cat && cat.Catalog) || [];
        if (catList.length > 0) {
            var valid = {};
            for (i = 0; i < catList.length; i++) valid[catList[i].ItemId] = true;
            grantIds = [];
            for (i = 0; i < ids.length; i++) { if (valid[ids[i]]) grantIds.push(ids[i]); else skipped++; }
        }
    } catch (eCat) {
        return { Success: false, Reason: "catalog_error: " + (eCat && eCat.message ? eCat.message : ("" + eCat)),
                 Wallet: null, Reward: emptyReward(), Granted: 0 };
    }

    try { grantItems(currentPlayerId, grantIds); }   // чанкуется по 25
    catch (eGrant) {
        return { Success: false, Reason: "grant_error: " + (eGrant && eGrant.message ? eGrant.message : ("" + eGrant)),
                 Wallet: readWallet(currentPlayerId), Reward: emptyReward(), Granted: 0 };
    }

    var reward = emptyReward(), counts = {}, g;
    for (g = 0; g < grantIds.length; g++) counts[grantIds[g]] = (counts[grantIds[g]] || 0) + 1;
    for (var id in counts) if (counts.hasOwnProperty(id)) reward.Cards.push({ ItemId: id, Amount: counts[id] });
    return { Success: true, Reason: (skipped > 0 ? ("skipped_" + skipped) : null),
             Wallet: readWallet(currentPlayerId), Reward: reward, Granted: grantIds.length };
};

// ---- REVIEW-аккаунт (билд для издателя) -----------------------------------------
// IsReviewBuild на клиенте зовёт это один раз после логина: помечаем аккаунт как ревью (тег "review_account" +
// флаг с датой в UserReadOnlyData) и выдаём стартовые бустеры, чтобы издатель проверил открытие. Тег делает
// такие аккаунты сегментируемыми в Game Manager → массовый сброс/удаление после ревью. Идемпотентно: повторный
// вызов НЕ досыпает бустеры (тег переставляем всегда — не вредит). Коллекцию НЕ трогаем — её открывает клиент
// локально (PlayerLibrary.FillFullCollection). Гейт — Title Data reviewSetup === "true" (включаешь на время
// раздачи билда, потом выключаешь), чтобы обычный игрок не выдал себе 100 бустеров.
var REVIEW_BOOSTER_ID = "booster_standard";
var REVIEW_BOOSTER_COUNT = 100;

function reviewSetupEnabled() {
    var d = server.GetTitleData({ Keys: ["reviewSetup"] });
    return d.Data && d.Data.reviewSetup === "true";
}

handlers.SetupReviewAccount = function (args, context) {
    if (!reviewSetupEnabled()) return { Success: false, Reason: "review_disabled", BoostersGranted: 0 };
    var playerId = currentPlayerId;

    // тег для сегментации/чистки — ставим всегда (идемпотентно, не вредит)
    try { server.AddPlayerTag({ PlayFabId: playerId, TagName: "review_account" }); } catch (e) {}

    // бустеры выдаём ОДИН раз (по флагу reviewAccount) — иначе каждый вход досыпал бы ещё 100
    var ro = server.GetUserReadOnlyData({ PlayFabId: playerId, Keys: ["reviewAccount"] });
    if (ro.Data && ro.Data["reviewAccount"]) return { Success: true, Reason: "already_setup", BoostersGranted: 0 };

    var ids = [];
    for (var i = 0; i < REVIEW_BOOSTER_COUNT; i++) ids.push(REVIEW_BOOSTER_ID);
    try { grantItems(playerId, ids); }
    catch (e2) { return { Success: false, Reason: "grant_error: " + (e2 && e2.message ? e2.message : ("" + e2)), BoostersGranted: 0 }; }

    server.UpdateUserReadOnlyData({ PlayFabId: playerId, Data: { "reviewAccount": (new Date()).toISOString() } });
    return { Success: true, Reason: null, BoostersGranted: REVIEW_BOOSTER_COUNT };
};

// Сброс журнала (прогресс/клеймы/серия входа) — чтобы прогнать цикл заново.
handlers.DevResetJournal = function (args, context) {
    if (!devEnabled()) return { Success: false, Reason: "dev_disabled" };
    server.UpdateUserReadOnlyData({ PlayFabId: currentPlayerId, KeysToRemove: ["journal"] });
    return { Success: true, Reason: null };
};

// Завершить ВСЕ незаклеймленные задачи (прогресс → target) одной ЗАПИСЬЮ — без гонок (4 репорта параллельно
// затирали бы друг друга). Для проверки клейма всех разом.
handlers.DevCompleteTasks = function (args, context) {
    if (!devEnabled()) return { Success: false, Reason: "dev_disabled" };
    var cfg = getTitleJson("taskConfig"); if (!cfg) return { Success: false, Reason: "config_missing" };
    var s = reconcileJournal(readJournal(currentPlayerId), journalPeriods(cfg, new Date())), i;
    if (cfg.daily)  for (i = 0; i < cfg.daily.length; i++)  if (!s.dailyClaimed[cfg.daily[i].id])   s.daily[cfg.daily[i].id]   = cfg.daily[i].target;
    if (cfg.weekly) for (i = 0; i < cfg.weekly.length; i++) if (!s.weeklyClaimed[cfg.weekly[i].id]) s.weekly[cfg.weekly[i].id] = cfg.weekly[i].target;
    writeJournal(currentPlayerId, s);
    return { Success: true, Reason: null };
};

// ---- Магазин --------------------------------------------------------------------
// Витрина и цены живут ТОЛЬКО в Title Data "shopConfig". BuyStoreItem принимает от клиента
// один ItemId — цену, количество и категорию сервер берёт из конфига сам. Поэтому подделать
// цену с клиента невозможно, а витрину можно менять удалённо, без обновления приложения.

// Потолок покупки пачкой. Покупка дешёвая: одно списание валюты + выдача, чанкованная по 25 предметов
// (100 штук = 4 вызова GrantItemsToUser). Реальным ограничителем всё равно служат деньги игрока.
// НЕ путать с MAX_OPEN_AT_ONCE: там на КАЖДЫЙ бустер идёт отдельный RevokeInventoryItem, и сотня
// открытий упёрлась бы в лимит API-вызовов на одно выполнение скрипта.
var MAX_BUY_AT_ONCE = 99;

function shopEntries() {
    var cfg = getTitleJson("shopConfig");
    if (!cfg || !cfg.entries) return [];
    return cfg.entries;
}

function findShopEntry(itemId) {
    var entries = shopEntries();
    for (var i = 0; i < entries.length; i++) {
        if (entries[i].itemId === itemId && entries[i].enabled !== false) return entries[i];
    }
    return null;
}

function ownedItemIds(playerId) {
    var inv = server.GetUserInventory({ PlayFabId: playerId });
    var owned = {};
    for (var i = 0; i < inv.Inventory.length; i++) owned[inv.Inventory[i].ItemId] = true;
    return owned;
}

handlers.GetShop = function (args, context) {
    var playerId = currentPlayerId;
    var entries = shopEntries();
    var owned = ownedItemIds(playerId);
    var out = [];

    for (var i = 0; i < entries.length; i++) {
        var e = entries[i];
        if (e.enabled === false) continue;

        out.push({
            ItemId:      e.itemId,
            DisplayName: e.displayName || e.itemId,
            Category:    e.category || "booster",
            PriceCode:   e.priceCode || "GD",
            PriceAmount: e.priceAmount || 0,
            // Уникальное (аватар) уже куплено → клиент покажет «Куплено» вместо цены.
            AlreadyOwned: e.unique === true && owned[e.itemId] === true
        });
    }
    return { Entries: out };
};

handlers.BuyStoreItem = function (args, context) {
    var playerId = currentPlayerId;
    var resp = { Success: false, Reason: null, Wallet: null, Reward: emptyReward() };

    var itemId = args && args.ItemId;
    if (!itemId) { resp.Reason = "bad_request"; return resp; }

    var entry = findShopEntry(itemId);
    if (!entry) { resp.Reason = "unknown_item"; return resp; }   // нет в витрине или выключено

    if (entry.unique === true && ownedItemIds(playerId)[itemId]) {
        resp.Reason = "already_owned";
        return resp;
    }

    // Сколько ЕДИНИЦ покупаем за раз (кнопки +/− в диалоге). ШТУЧНОЕ берём строго по одной:
    // durable-предметы (unique) и аватары — второй экземпляр им бессмыслен. Категорию проверяем ОТДЕЛЬНО
    // от флага: если в shopConfig у аватара забыли unique:true, пачкой его всё равно не купят.
    var want = (args && args.Count) ? args.Count : 1;
    if (want < 1) want = 1;
    if (want > MAX_BUY_AT_ONCE) want = MAX_BUY_AT_ONCE;
    if (entry.unique === true || entry.category === "avatar") want = 1;

    var price = entry.priceAmount || 0;
    var code  = entry.priceCode || "GD";
    var qty   = ((entry.quantity && entry.quantity > 0) ? entry.quantity : 1) * want;
    var total = price * want;

    // 1) СНАЧАЛА списываем. Проверка «хватает ли денег» — это и есть Subtract: PlayFab сам упадёт
    //    при нехватке. Своей проверкой баланса не занимаемся (гонка двух покупок в один момент).
    if (total > 0) {
        try {
            server.SubtractUserVirtualCurrency({ PlayFabId: playerId, VirtualCurrency: code, Amount: total });
        } catch (e) {
            resp.Reason = "not_enough_currency";
            resp.Wallet = readWallet(playerId);
            return resp;
        }
    }

    // 2) Выдаём. Если выдача упала — ВОЗВРАЩАЕМ деньги: иначе игрок заплатил и не получил ничего.
    try {
        var ids = [];
        for (var i = 0; i < qty; i++) ids.push(itemId);
        grantItems(playerId, ids);
    } catch (e2) {
        if (total > 0) {
            server.AddUserVirtualCurrency({ PlayFabId: playerId, VirtualCurrency: code, Amount: total });
        }
        resp.Reason = "grant_failed";
        resp.Wallet = readWallet(playerId);
        return resp;
    }

    // 3) Что выдали — по этому клиент играет попап/reveal.
    var k;
    if (entry.category === "avatar") {
        for (k = 0; k < qty; k++) resp.Reward.Avatars.push(itemId);
    } else if (entry.category === "card") {
        resp.Reward.Cards.push({ ItemId: itemId, Amount: qty });
    } else {
        for (k = 0; k < qty; k++) resp.Reward.Boosters.push(itemId);
    }

    resp.Success = true;
    resp.Wallet = readWallet(playerId);
    return resp;
};

// ---- Чёрный рынок ---------------------------------------------------------------
// Набор офферов ДЕТЕРМИНИРОВАН номером недели (rotationIndex): сид = rotationIndex → у всех игроков в
// эту неделю ОДИН набор, на следующей неделе автоматически другой, БЕЗ перезаливки. Пул (blackMarketConfig)
// больше числа слотов — сервер выбирает нужное кол-во по редкостям. Купил ОДНУ карту → остальные гаснут до
// ротации (флаг в UserReadOnlyData "blackMarket"). Ротация по weeklyDayUtc/hourUtc (по умолч. среда).

var BM_RARITIES = ["common", "rare", "epic", "legendary", "exotic"];
function bmCap(r) { return r.charAt(0).toUpperCase() + r.slice(1); }

// Сид-PRNG (LCG, без Math.imul — движок CloudScript старый). Детерминированно от сида.
function seededRng(seed) {
    var s = (seed >>> 0) || 1;
    return function () { s = (s * 1664525 + 1013904223) % 4294967296; return s / 4294967296; };
}
// Детерминированно выбрать count элементов из пула (Fisher-Yates по сид-PRNG).
function bmPick(pool, count, seed) {
    var arr = pool.slice(), rng = seededRng(seed), i, j, tmp;
    for (i = arr.length - 1; i > 0; i--) { j = Math.floor(rng() * (i + 1)); tmp = arr[i]; arr[i] = arr[j]; arr[j] = tmp; }
    return arr.slice(0, count);
}
// Ротация. index — АБСОЛЮТНЫЙ номер недели с 1970 (СИД набора: монотонный, наборы не повторяются из года в
// год; и он же для сравнения «сменилась ли ротация»). weekOfYear — номер недели В ТЕКУЩЕМ ГОДУ (1..53), для
// ПОКАЗА игроку («Неделя 5»). Привязка границы к weeklyDayUtc/hourUtc.
function bmRotation(cfg, now) {
    var r = cfg.rotation || { weeklyDayUtc: 3, hourUtc: 0 };
    var t = now.getTime();
    var offset = (((r.weeklyDayUtc - 4 + 7) % 7) * 86400000) + (r.hourUtc * 3600000);
    var index = Math.floor((t - offset) / (7 * 86400000));
    var nextMs = (index + 1) * (7 * 86400000) + offset;
    var yearStart = Date.UTC(now.getUTCFullYear(), 0, 1);
    var weekOfYear = Math.floor(Math.floor((t - yearStart) / 86400000) / 7) + 1;
    return { index: index, weekOfYear: weekOfYear, nextUtc: (new Date(nextMs)).toISOString() };
}
// Набор офферов ротации (одинаковый в GetBlackMarket и BuyBlackMarketCard — сервер сверяет покупку с ним).
// Дедуп на два уровня: повторы id ВНУТРИ пула и один и тот же item по ВСЕМУ набору (карта попала в два пула
// или случайно продублирована в конфиге не должны давать два одинаковых оффера). Лучше на 1 оффер меньше,
// чем дубль. bmPick (Fisher-Yates) и так не повторяет в пределах одного пула — это подстраховка от кривого конфига.
function bmBuildOffers(cfg, rotationIndex) {
    var offers = [], used = {}, ri, rar, pool, count, price, picked, j, id;
    for (ri = 0; ri < BM_RARITIES.length; ri++) {
        rar = BM_RARITIES[ri];
        pool = bmDedupe((cfg.pools && cfg.pools[rar]) || []);
        count = (cfg.slots && cfg.slots[rar]) || 0;
        price = (cfg.prices && cfg.prices[rar]) || { code: "GD", amount: 0 };
        picked = bmPick(pool, count, (rotationIndex * 131 + ri * 17 + 1));
        for (j = 0; j < picked.length; j++) {
            id = picked[j];
            if (used[id]) continue;   // этот item уже в наборе (другой пул / повтор) — пропускаем
            used[id] = true;
            offers.push({ ItemId: id, Rarity: bmCap(rar), PriceCode: price.code, PriceAmount: price.amount, Available: true });
        }
    }
    return offers;
}
// Убрать повторы id из массива, сохранив порядок.
function bmDedupe(arr) {
    var seen = {}, out = [], i;
    for (i = 0; i < arr.length; i++) if (!seen[arr[i]]) { seen[arr[i]] = true; out.push(arr[i]); }
    return out;
}
function readBM(playerId) {
    var ro = server.GetUserReadOnlyData({ PlayFabId: playerId, Keys: ["blackMarket"] });
    if (ro.Data && ro.Data["blackMarket"]) { try { return JSON.parse(ro.Data["blackMarket"].Value); } catch (e) {} }
    return null;
}
function writeBM(playerId, s) { server.UpdateUserReadOnlyData({ PlayFabId: playerId, Data: { "blackMarket": JSON.stringify(s) } }); }
// Смена ротации → сбрасываем «куплено» (свежая ротация — снова можно купить одну).
function reconcileBM(s, rotationIndex) {
    if (!s || s.rotation !== rotationIndex) return { rotation: rotationIndex, purchased: null };
    return s;
}

handlers.GetBlackMarket = function (args, context) {
    var playerId = currentPlayerId, now = new Date();
    var cfg = getTitleJson("blackMarketConfig");
    if (!cfg) return { RotationIndex: 0, ServerTimeUtc: now.toISOString(), NextRotationUtc: "", PurchasedThisRotation: false, Offers: [] };

    var rot = bmRotation(cfg, now);
    var state = reconcileBM(readBM(playerId), rot.index);   // НЕ пишем (лениво, как журнал): персист делает покупка
    var purchased = state.purchased != null;

    var offers = bmBuildOffers(cfg, rot.index);   // сид — АБСОЛЮТНЫЙ индекс (набор не повторяется ежегодно)
    for (var i = 0; i < offers.length; i++) offers[i].Available = !purchased;   // купил одну → все гаснут до ротации

    // RotationIndex для клиента = номер недели в году (показ). Внутри сид/сравнение — абсолютный rot.index.
    return { RotationIndex: rot.weekOfYear, ServerTimeUtc: now.toISOString(), NextRotationUtc: rot.nextUtc,
             PurchasedThisRotation: purchased, Offers: offers };
};

handlers.BuyBlackMarketCard = function (args, context) {
    var playerId = currentPlayerId;
    var resp = { Success: false, Reason: null, Wallet: null, Reward: emptyReward() };
    var itemId = args && args.ItemId; if (!itemId) { resp.Reason = "bad_request"; return resp; }
    var cfg = getTitleJson("blackMarketConfig"); if (!cfg) { resp.Reason = "config_missing"; return resp; }

    var rot = bmRotation(cfg, new Date());
    var state = reconcileBM(readBM(playerId), rot.index);
    if (state.purchased != null) { resp.Reason = "already_purchased"; return resp; }   // одна покупка за ротацию

    // Сверяем, что itemId РЕАЛЬНО в наборе этой ротации (анти-подделка), берём его цену оттуда.
    var offers = bmBuildOffers(cfg, rot.index), offer = null;
    for (var i = 0; i < offers.length; i++) if (offers[i].ItemId === itemId) { offer = offers[i]; break; }
    if (!offer) { resp.Reason = "unknown_offer"; return resp; }

    if (offer.PriceAmount > 0) {
        try { server.SubtractUserVirtualCurrency({ PlayFabId: playerId, VirtualCurrency: offer.PriceCode, Amount: offer.PriceAmount }); }
        catch (e) { resp.Reason = "not_enough_currency"; resp.Wallet = readWallet(playerId); return resp; }
    }
    try { grantItems(playerId, [itemId]); }
    catch (e2) {
        if (offer.PriceAmount > 0) server.AddUserVirtualCurrency({ PlayFabId: playerId, VirtualCurrency: offer.PriceCode, Amount: offer.PriceAmount });
        resp.Reason = "grant_failed"; resp.Wallet = readWallet(playerId); return resp;
    }

    state.purchased = itemId;
    writeBM(playerId, state);   // гасит остальные офферы до ротации

    resp.Reward.Cards.push({ ItemId: itemId, Amount: 1 });
    resp.Success = true; resp.Wallet = readWallet(playerId);
    return resp;
};

// Дев: сбросить состояние чёрного рынка (снять «куплено» этой ротации) — прогнать покупку заново.
handlers.DevResetBlackMarket = function (args, context) {
    if (!devEnabled()) return { Success: false, Reason: "dev_disabled" };
    server.UpdateUserReadOnlyData({ PlayFabId: currentPlayerId, KeysToRemove: ["blackMarket"] });
    return { Success: true, Reason: null };
};

// ---- Распыление карты в Обрывки («Порвать») -------------------------------------
// Редкость берём АВТОРИТЕТНО из тега каталога (rarity_*), не с клиента. Значения = DustValues (C#):
// common 2 / rare 4 / epic 8 / legendary 16 / exotic 32. Списываем ОДИН инстанс, начисляем SC (Обрывки).

var DUST_VALUES = { common: 2, rare: 4, epic: 8, legendary: 16, exotic: 32 };

function dustAmountForItem(itemId) {
    var cat = server.GetCatalogItems({ CatalogVersion: "main" });
    if (!cat || !cat.Catalog) return 0;
    for (var i = 0; i < cat.Catalog.length; i++) {
        if (cat.Catalog[i].ItemId !== itemId) continue;
        var tags = cat.Catalog[i].Tags || [];
        for (var j = 0; j < tags.length; j++)
            if (tags[j].indexOf("rarity_") === 0) return DUST_VALUES[tags[j].substring(7)] || 0;
        return 0;
    }
    return 0;
}

var MAX_DUST_AT_ONCE = 99;   // потолок распыления пачкой («порвать все дубли этой карты»)

handlers.DustCard = function (args, context) {
    var playerId = currentPlayerId;
    var resp = { Success: false, Reason: null, Wallet: null, Amount: 0, RemainingCount: 0, Dusted: 0 };
    var itemId = args && args.ItemId; if (!itemId) { resp.Reason = "bad_request"; return resp; }

    var want = (args && args.Count) ? args.Count : 1;   // сколько копий распылить (кнопка «все дубли»)
    if (want < 1) want = 1;
    if (want > MAX_DUST_AT_ONCE) want = MAX_DUST_AT_ONCE;

    // Собрать до want инстансов карты. Токены/бустеры/аватары не распыляются (нет rarity-тега → unknown_rarity).
    var inv = server.GetUserInventory({ PlayFabId: playerId });
    var instanceIds = [], owned = 0;
    for (var i = 0; i < inv.Inventory.length; i++)
        if (inv.Inventory[i].ItemId === itemId) { owned++; if (instanceIds.length < want) instanceIds.push(inv.Inventory[i].ItemInstanceId); }
    if (owned <= 0) { resp.Reason = "not_owned"; return resp; }

    var per = dustAmountForItem(itemId);
    if (per <= 0) { resp.Reason = "unknown_rarity"; return resp; }

    var dusted = instanceIds.length;   // фактически распылено (может быть < want, если копий меньше)
    revokeInstances(playerId, instanceIds);   // пакетное списание (не по одному — иначе лимит API-вызовов)
    var total = per * dusted;
    server.AddUserVirtualCurrency({ PlayFabId: playerId, VirtualCurrency: "SC", Amount: total });

    resp.Success = true; resp.Amount = total; resp.Dusted = dusted; resp.RemainingCount = owned - dusted;
    resp.Wallet = readWallet(playerId);
    return resp;
};

// ---- Промокоды (крауд-плюшки, кампании, коды бэкеров) ---------------------------
// Один хендлер — ДВА источника кодов, клиент шлёт просто строку и не знает, какой сработал:
//   1) СВОИ кампании — Title Data "promoConfig": код многоразовый, но с лимитом на аккаунт, окном
//      дат, общим потолком активаций и опциональным тегом игрока. Награда — тот же RewardBundle,
//      что у задач/бустеров (грузится через toRewardBundle → grantReward).
//   2) ПЕРСОНАЛЬНЫЕ коды бэкеров — НАТИВНЫЕ купоны PlayFab (Game Manager → Economy → Catalogs →
//      Coupons: генерит N уникальных кодов + CSV). Одноразовость гарантирует сам сервис, хранить
//      список кодов не надо.
//
// Купон умеет выдать РОВНО ОДИН предмет и НЕ умеет валюту напрямую — поэтому на тиры заводим
// bundle-предмет (bundle_founder_*), он разворачивается при выдаче. Что именно упало, определяем
// НЕ по конфигу, а по факту: GrantedItems + ДИФФ КОШЕЛЬКА до/после (валюта из бандла иначе не видна).

var PROMO_KEY = "promo";                  // UserReadOnlyData: { redeemed:{KEY:раз}, att:попыток, attHour:час }
var PROMO_GROUP = "promo_counters";       // Shared Group Data: ключ "code:{KEY}" → { n: активаций }
var PROMO_MAX_ATTEMPTS_PER_HOUR = 12;     // анти-перебор коротких кампанийных кодов
var PROMO_MAX_LEN = 64;

// Ключ поиска: регистр и разделители не важны — «shh launch» = «SHH-LAUNCH» = «shhlaunch».
function promoKey(code) { return ("" + code).toUpperCase().replace(/[^A-Z0-9]/g, ""); }

// "2026-12-31T23:59:59Z" → epoch ms. Разбираем САМИ, а не через new Date(str): парсинг ISO-строк
// появился только в ES5, и на движке постарше окно дат молча превратилось бы в NaN (= не действует).
// Время трактуем как UTC — как и весь остальной серверный расчёт периодов.
function isoToMs(s) {
    if (!s) return NaN;
    var m = /^(\d{4})-(\d{2})-(\d{2})(?:[T ](\d{2}):(\d{2})(?::(\d{2}))?)?/.exec("" + s);
    if (!m) return NaN;
    return Date.UTC(+m[1], +m[2] - 1, +m[3], +(m[4] || 0), +(m[5] || 0), +(m[6] || 0));
}

function readPromoState(playerId) {
    var ro = server.GetUserReadOnlyData({ PlayFabId: playerId, Keys: [PROMO_KEY] });
    if (ro.Data && ro.Data[PROMO_KEY]) { try { return JSON.parse(ro.Data[PROMO_KEY].Value); } catch (e) { } }
    return null;
}
function writePromoState(playerId, s) {
    var d = {}; d[PROMO_KEY] = JSON.stringify(s);
    server.UpdateUserReadOnlyData({ PlayFabId: playerId, Data: d });
}

// Общий потолок активаций кода. Читаем/пишем ТОЛЬКО когда globalLimit задан — иначе лишние вызовы API.
// Гонка (два игрока одновременно) может дать перелив на пару активаций — на 5000 кодов это не важно.
function readPromoCount(key) {
    try { server.CreateSharedGroup({ SharedGroupId: PROMO_GROUP }); } catch (e) { /* уже создана */ }
    var res;
    try { res = server.GetSharedGroupData({ SharedGroupId: PROMO_GROUP, Keys: ["code:" + key] }); }
    catch (e2) { return 0; }
    var cell = res && res.Data && res.Data["code:" + key];
    if (!cell || !cell.Value) return 0;
    try { return JSON.parse(cell.Value).n || 0; } catch (e3) { return 0; }
}
function writePromoCount(key, n) {
    var data = {}; data["code:" + key] = JSON.stringify({ n: n });
    try { server.UpdateSharedGroupData({ SharedGroupId: PROMO_GROUP, Data: data }); } catch (e) { }
}

// Отказ: считаем попытку (анти-перебор) и сохраняем состояние.
function promoFail(playerId, state, resp, reason) {
    state.att = (state.att || 0) + 1;
    writePromoState(playerId, state);
    resp.Reason = reason;
    return resp;
}

// Имя ошибки PlayFab из исключения server.* (форма объекта не гарантирована → всё в try).
function apiErrorName(e) {
    try { if (e && e.apiErrorInfo && e.apiErrorInfo.apiError) return e.apiErrorInfo.apiError.error || ""; }
    catch (x) { }
    return "";
}

// Что реально выдал купон: обёртку (bundle_/container_) прячем — игроку показываем содержимое.
function rewardFromGrantedItems(items) {
    var b = emptyReward(), cardMap = {}, i, id;
    for (i = 0; i < (items ? items.length : 0); i++) {
        id = items[i].ItemId || "";
        if (!id || id.indexOf("bundle_") === 0 || id.indexOf("container_") === 0) continue;
        if (id.indexOf("booster_") === 0) { b.Boosters.push(id); continue; }
        if (id.indexOf("avatar_") === 0) { b.Avatars.push(id); continue; }
        cardMap[id] = (cardMap[id] || 0) + 1;
    }
    for (id in cardMap) if (cardMap.hasOwnProperty(id)) b.Cards.push({ ItemId: id, Amount: cardMap[id] });
    return b;
}

handlers.RedeemPromo = function (args, context) {
    var playerId = currentPlayerId;
    var resp = { Success: false, Reason: null, Wallet: null, Reward: emptyReward(), TitleKey: null, Tag: null };

    var raw = (args && args.Code) ? ("" + args.Code).replace(/^\s+|\s+$/g, "") : "";
    var key = promoKey(raw);
    if (!raw || !key || raw.length > PROMO_MAX_LEN) { resp.Reason = "bad_request"; return resp; }

    var cfg = getTitleJson("promoConfig") || {};
    var state = readPromoState(playerId) || {};
    if (!state.redeemed) state.redeemed = {};

    // Анти-перебор: N попыток в час на аккаунт (успешные обнуляют счётчик).
    var hour = Math.floor((new Date()).getTime() / 3600000);
    if (state.attHour !== hour) { state.attHour = hour; state.att = 0; }
    if (state.att >= (cfg.maxAttemptsPerHour || PROMO_MAX_ATTEMPTS_PER_HOUR)) {
        resp.Reason = "promo_throttled"; return resp;   // попытку НЕ считаем — иначе бан не кончится
    }

    // ── 1) Своя кампания из promoConfig ──────────────────────────────────────────
    var entry = null, list = cfg.codes || [], i;
    for (i = 0; i < list.length; i++) if (promoKey(list[i].code) === key) { entry = list[i]; break; }

    if (entry) {
        // Кривую дату в конфиге игнорируем (NaN-сравнения), а не считаем код просроченным:
        // опечатка в promoConfig не должна гасить живую кампанию.
        var now = (new Date()).getTime(), from = isoToMs(entry.from), until = isoToMs(entry.until);
        if (entry.enabled === false) return promoFail(playerId, state, resp, "promo_disabled");
        if (!isNaN(from) && now < from) return promoFail(playerId, state, resp, "promo_not_started");
        if (!isNaN(until) && now > until) return promoFail(playerId, state, resp, "promo_expired");

        var perAccount = (typeof entry.perAccount === "number") ? entry.perAccount : 1;   // 0 = без лимита
        var mine = state.redeemed[key] || 0;
        if (perAccount > 0 && mine >= perAccount) return promoFail(playerId, state, resp, "promo_used");

        var used = 0;
        if (entry.globalLimit > 0) {
            used = readPromoCount(key);
            if (used >= entry.globalLimit) return promoFail(playerId, state, resp, "promo_limit");
        }

        var bundle = toRewardBundle(entry.reward);
        try { grantReward(playerId, bundle); }
        catch (e) { resp.Reason = "grant_error: " + (e && e.message ? e.message : ("" + e)); return resp; }

        if (entry.tag) { try { server.AddPlayerTag({ PlayFabId: playerId, TagName: entry.tag }); } catch (e2) { } }
        if (entry.globalLimit > 0) writePromoCount(key, used + 1);

        state.redeemed[key] = mine + 1;
        state.att = 0;
        writePromoState(playerId, state);

        resp.Success = true;
        resp.Reward = bundle;
        resp.Wallet = readWallet(playerId);
        resp.TitleKey = entry.titleKey || null;
        resp.Tag = entry.tag || null;
        return resp;
    }

    // ── 2) Нативный купон PlayFab (персональные коды бэкеров) ────────────────────
    var catalog = cfg.couponCatalog || "main";
    var before = copyVc(server.GetUserInventory({ PlayFabId: playerId }).VirtualCurrency || {});
    var redeemed = null, errName = "";
    try { redeemed = server.RedeemCoupon({ PlayFabId: playerId, CouponCode: raw, CatalogVersion: catalog }); }
    catch (e3) {
        errName = apiErrorName(e3);
        // Коды из Game Manager строчные — игрок мог ввести капсом. Пробуем ещё раз в нижнем регистре.
        var lower = raw.toLowerCase();
        if (lower !== raw) {
            try { redeemed = server.RedeemCoupon({ PlayFabId: playerId, CouponCode: lower, CatalogVersion: catalog }); }
            catch (e4) { errName = apiErrorName(e4); }
        }
    }

    if (!redeemed) {
        // Разделяем «нет такого» и «уже использован» — иначе бэкер не поймёт, что код сгорел.
        var reason = (errName.indexOf("Redeemed") >= 0 || errName.indexOf("Used") >= 0) ? "promo_used" : "promo_unknown";
        return promoFail(playerId, state, resp, reason);
    }

    var granted = redeemed.GrantedItems || [];
    var reward = rewardFromGrantedItems(granted);

    // Валюта из бандла в GrantedItems не видна — берём диффом кошелька.
    var afterVc = server.GetUserInventory({ PlayFabId: playerId }).VirtualCurrency || {};
    for (var code in afterVc) {
        if (!afterVc.hasOwnProperty(code)) continue;
        var delta = (afterVc[code] || 0) - (before[code] || 0);
        if (delta > 0) reward.Currencies.push({ Code: code, Amount: delta });
    }

    // Тег тира по выданному предмету (в т.ч. по обёртке-бандлу) — статус «Основатель» переживает вайп инвентаря.
    var tagMap = cfg.couponTags || {};
    for (i = 0; i < granted.length; i++) {
        var gid = granted[i].ItemId || "";
        if (!tagMap.hasOwnProperty(gid)) continue;   // hasOwnProperty, а не просто [gid]: id предмета
        var tag = tagMap[gid];                       // может совпасть с именем из прототипа Object
        if (!tag) continue;
        try { server.AddPlayerTag({ PlayFabId: playerId, TagName: tag }); } catch (e5) { }
        resp.Tag = tag;
    }

    state.redeemed[key] = (state.redeemed[key] || 0) + 1;
    state.att = 0;
    writePromoState(playerId, state);

    resp.Success = true;
    resp.Reward = reward;
    resp.Wallet = walletListFrom(afterVc);
    resp.TitleKey = cfg.couponTitleKey || null;
    return resp;
};

// Дев: забыть все активированные промокоды этого игрока — прогнать ввод заново.
// Нативные купоны это НЕ возвращает (они сгорают в сервисе), только свои кампании из promoConfig.
handlers.DevResetPromo = function (args, context) {
    if (!devEnabled()) return { Success: false, Reason: "dev_disabled" };
    server.UpdateUserReadOnlyData({ PlayFabId: currentPlayerId, KeysToRemove: [PROMO_KEY] });
    return { Success: true, Reason: null };
};

// ---- Журнал: ежедневные/еженедельные задачи + вход ------------------------------
// Модель: конфиг (taskConfig) СТАТИЧНЫЙ; ротацию/сброс считает сервер по UTC-времени, БЕЗ перезаливки
// и кронов. Период дня = floor((now − dailyHour)/24ч); недельный привязан к weeklyDay/weeklyHour. Сброс
// ЛЕНИВЫЙ, по-игроку: сменился период → обнуляем ЕГО прогресс/клеймы (при следующем запросе). Прогресс —
// в UserReadOnlyData "journal" (пишет только сервер, клиент подделать не может).

function journalPeriods(cfg, now) {
    var r = cfg.resets || { dailyHourUtc: 0, weeklyDayUtc: 3, weeklyHourUtc: 0 };
    var t = now.getTime();
    var day = Math.floor((t - r.dailyHourUtc * 3600000) / 86400000);
    // 1970-01-01 = четверг (dow=4). Смещаем, чтобы недельная граница легла на weeklyDay/weeklyHour.
    var offset = (((r.weeklyDayUtc - 4 + 7) % 7) * 86400000) + (r.weeklyHourUtc * 3600000);
    var week = Math.floor((t - offset) / (7 * 86400000));
    return { day: day, week: week, resets: r };
}

function readJournal(playerId) {
    var ro = server.GetUserReadOnlyData({ PlayFabId: playerId, Keys: ["journal"] });
    if (ro.Data && ro.Data["journal"]) { try { return JSON.parse(ro.Data["journal"].Value); } catch (e) {} }
    return null;
}
function writeJournal(playerId, s) {
    server.UpdateUserReadOnlyData({ PlayFabId: playerId, Data: { "journal": JSON.stringify(s) } });
}

// Приводим состояние к текущим периодам: сменился период → прогресс/клеймы обнуляются.
function reconcileJournal(s, p) {
    if (!s) s = {};
    if (s.dayPeriod !== p.day)  { s.dayPeriod = p.day;  s.daily = {};  s.dailyClaimed = {}; }
    if (s.weekPeriod !== p.week) { s.weekPeriod = p.week; s.weekly = {}; s.weeklyClaimed = {}; }
    if (!s.daily) s.daily = {}; if (!s.dailyClaimed) s.dailyClaimed = {};
    if (!s.weekly) s.weekly = {}; if (!s.weeklyClaimed) s.weeklyClaimed = {};
    if (!s.login) s.login = { day: 0, period: -1 };
    return s;
}

// config-награда (lowercase) → RewardBundle (PascalCase) для клиента и grantReward.
function toRewardBundle(r) {
    var b = emptyReward(); if (!r) return b; var i;
    if (r.currencies) for (i = 0; i < r.currencies.length; i++) b.Currencies.push({ Code: r.currencies[i].code, Amount: r.currencies[i].amount });
    if (r.cards)      for (i = 0; i < r.cards.length; i++)      b.Cards.push({ ItemId: r.cards[i].itemId, Amount: r.cards[i].amount });
    if (r.boosters)   for (i = 0; i < r.boosters.length; i++)   b.Boosters.push(r.boosters[i]);
    if (r.avatars)    for (i = 0; i < r.avatars.length; i++)    b.Avatars.push(r.avatars[i]);
    return b;
}
// Главная награда для КОМПАКТНОЙ ячейки дня: {amount, code, itemId}.
function primaryReward(r) {
    if (r) {
        if (r.currencies && r.currencies.length) return { amount: r.currencies[0].amount, code: r.currencies[0].code, itemId: "" };
        if (r.cards && r.cards.length)           return { amount: r.cards[0].amount, code: "", itemId: r.cards[0].itemId };
        if (r.boosters && r.boosters.length)     return { amount: 1, code: "", itemId: r.boosters[0] };
        if (r.avatars && r.avatars.length)       return { amount: 1, code: "", itemId: r.avatars[0] };
    }
    return { amount: 0, code: "", itemId: "" };
}
// Какой день входа клеймится сегодня + доступность.
function loginToday(s, dayPeriod) {
    var login = s.login || { day: 0, period: -1 };
    var available = login.period !== dayPeriod;
    var today;
    if (!available) today = login.day;                              // уже забрал сегодня
    else if (login.period === dayPeriod - 1) today = (login.day >= 7 ? 1 : login.day + 1);  // серия продолжается
    else today = 1;                                                 // серия прервана / первый вход
    return { available: available, today: today < 1 ? 1 : today };
}
function buildTaskStates(defs, progMap, claimedMap) {
    var out = []; if (!defs) return out;
    for (var i = 0; i < defs.length; i++) {
        var d = defs[i], prog = progMap[d.id] || 0, claimed = claimedMap[d.id] === true;
        out.push({ Id: d.id, Type: d.type, Progress: prog, Target: d.target,
                   Claimable: prog >= d.target && !claimed, Claimed: claimed, Reward: toRewardBundle(d.reward) });
    }
    return out;
}
// Инкремент задач с совпадающим type (не трогаем забранные). Общий для клиентского репорта и серверного.
function bumpProgress(defs, progMap, claimedMap, type, amount) {
    if (!defs) return;
    for (var i = 0; i < defs.length; i++) {
        var d = defs[i];
        if (d.type !== type || claimedMap[d.id] === true) continue;
        var cur = (progMap[d.id] || 0) + amount;
        progMap[d.id] = cur > d.target ? d.target : cur;
    }
}
// Серверный репорт прогресса (напр. open_boosters из OpenBooster) — клиент его не шлёт.
function reportServerProgress(playerId, type, amount) {
    var cfg = getTitleJson("taskConfig"); if (!cfg) return;
    var s = reconcileJournal(readJournal(playerId), journalPeriods(cfg, new Date()));
    bumpProgress(cfg.daily, s.daily, s.dailyClaimed, type, amount);
    bumpProgress(cfg.weekly, s.weekly, s.weeklyClaimed, type, amount);
    writeJournal(playerId, s);
}

handlers.GetDailyState = function (args, context) {
    var playerId = currentPlayerId;
    var now = new Date();
    var cfg = getTitleJson("taskConfig");
    if (!cfg) {
        return { ServerTimeUtc: now.toISOString(), DailyResetHourUtc: 0, WeeklyResetDay: 3, WeeklyResetHourUtc: 0,
                 Login: { StreakDay: 0, Available: false, Today: emptyReward(), Days: [] }, Daily: [], Weekly: [] };
    }
    var p = journalPeriods(cfg, now);
    var s = reconcileJournal(readJournal(playerId), p);
    // НЕ пишем тут: reconcile детерминирован от времени, дисплей всегда верный. Персист сброса делает
    // ближайший Claim/Report. Иначе GetDailyState писал бы на КАЖДОЕ открытие → лимит записи PlayFab.

    var lt = loginToday(s, p.day);
    var days = [], rewards = cfg.loginRewards || [], todayReward = emptyReward();
    for (var i = 0; i < rewards.length; i++) {
        var d = rewards[i], pr = primaryReward(d.reward);
        days.push({ Day: d.day, RewardAmount: pr.amount, RewardCode: pr.code, RewardItemId: pr.itemId,
                    Claimed: d.day < lt.today || (!lt.available && d.day === lt.today), Today: d.day === lt.today });
        if (d.day === lt.today) todayReward = toRewardBundle(d.reward);
    }

    return {
        ServerTimeUtc: now.toISOString(),
        DailyResetHourUtc: p.resets.dailyHourUtc, WeeklyResetDay: p.resets.weeklyDayUtc, WeeklyResetHourUtc: p.resets.weeklyHourUtc,
        Login: { StreakDay: lt.today, Available: lt.available, Today: todayReward, Days: days },
        Daily: buildTaskStates(cfg.daily, s.daily, s.dailyClaimed),
        Weekly: buildTaskStates(cfg.weekly, s.weekly, s.weeklyClaimed)
    };
};

handlers.ClaimLoginReward = function (args, context) {
    var playerId = currentPlayerId;
    var resp = { Success: false, Reason: null, Wallet: null, Reward: emptyReward() };
    var cfg = getTitleJson("taskConfig"); if (!cfg) { resp.Reason = "config_missing"; return resp; }
    var p = journalPeriods(cfg, new Date());
    var s = reconcileJournal(readJournal(playerId), p);
    var lt = loginToday(s, p.day);
    if (!lt.available) { resp.Reason = "already_claimed"; return resp; }

    var rewards = cfg.loginRewards || [], reward = null;
    for (var i = 0; i < rewards.length; i++) if (rewards[i].day === lt.today) { reward = rewards[i].reward; break; }
    var bundle = toRewardBundle(reward);
    grantReward(playerId, bundle);

    s.login = { day: lt.today, period: p.day };
    writeJournal(playerId, s);
    resp.Success = true; resp.Reward = bundle; resp.Wallet = readWallet(playerId);
    return resp;
};

handlers.ClaimTask = function (args, context) {
    var playerId = currentPlayerId;
    var resp = { Success: false, Reason: null, Wallet: null, Reward: emptyReward() };
    var taskId = args && args.TaskId; if (!taskId) { resp.Reason = "bad_request"; return resp; }
    var cfg = getTitleJson("taskConfig"); if (!cfg) { resp.Reason = "config_missing"; return resp; }
    var s = reconcileJournal(readJournal(playerId), journalPeriods(cfg, new Date()));

    var def = null, weekly = false, k;
    if (cfg.daily) for (k = 0; k < cfg.daily.length; k++) if (cfg.daily[k].id === taskId) { def = cfg.daily[k]; break; }
    if (!def && cfg.weekly) for (k = 0; k < cfg.weekly.length; k++) if (cfg.weekly[k].id === taskId) { def = cfg.weekly[k]; weekly = true; break; }
    if (!def) { resp.Reason = "unknown_task"; return resp; }

    var progMap = weekly ? s.weekly : s.daily, claimedMap = weekly ? s.weeklyClaimed : s.dailyClaimed;
    if (claimedMap[taskId] === true) { resp.Reason = "already_claimed"; return resp; }
    if ((progMap[taskId] || 0) < def.target) { resp.Reason = "not_complete"; return resp; }

    var bundle = toRewardBundle(def.reward);
    grantReward(playerId, bundle);
    claimedMap[taskId] = true;
    writeJournal(playerId, s);
    resp.Success = true; resp.Reward = bundle; resp.Wallet = readWallet(playerId);
    return resp;
};

handlers.ReportTaskProgress = function (args, context) {
    var playerId = currentPlayerId;
    var type = args && args.Type, amount = (args && args.Amount) || 1;
    if (!type) return { Success: false, Reason: "bad_request", Wallet: null };
    // Ловим ошибку server.* API и отдаём её текст клиенту — иначе видим лишь общий CloudScriptAPIRequestError.
    try {
        reportServerProgress(playerId, type, amount);
    } catch (e) {
        return { Success: false, Reason: "report_error: " + (e && e.message ? e.message : ("" + e)), Wallet: null };
    }
    return { Success: true, Reason: null, Wallet: null };
};

// Пачка прогресса ОДНИМ read-modify-write журнала (клиент шлёт накопленное за матч). Одиночные
// ReportTaskProgress в цикле устроили бы гонку за ключ "journal" (параллельные записи затирают друг
// друга → потерянный прогресс) — тут одна запись на весь набор. Items = [{Type, Amount}].
handlers.ReportTaskProgressBatch = function (args, context) {
    var playerId = currentPlayerId;
    var items = args && args.Items;
    if (!items || !items.length) return { Success: true, Reason: null, Wallet: null };
    try {
        var cfg = getTitleJson("taskConfig");
        if (cfg) {
            var s = reconcileJournal(readJournal(playerId), journalPeriods(cfg, new Date()));
            for (var i = 0; i < items.length; i++) {
                var t = items[i] && items[i].Type, a = (items[i] && items[i].Amount) || 0;
                if (!t || a <= 0) continue;
                bumpProgress(cfg.daily,  s.daily,  s.dailyClaimed,  t, a);
                bumpProgress(cfg.weekly, s.weekly, s.weeklyClaimed, t, a);
            }
            writeJournal(playerId, s);
        }
    } catch (e) {
        return { Success: false, Reason: "report_error: " + (e && e.message ? e.message : ("" + e)), Wallet: null };
    }
    return { Success: true, Reason: null, Wallet: null };
};

// ---- Профиль / входящие ---------------------------------------------------------

handlers.GetProfile = function (args, context) {
    var playerId = currentPlayerId;

    // Имя: title display name, иначе username, иначе «Player» (гость без имени).
    var name = "Player";
    try {
        var acc = server.GetUserAccountInfo({ PlayFabId: playerId });
        if (acc && acc.UserInfo) {
            if (acc.UserInfo.TitleInfo && acc.UserInfo.TitleInfo.DisplayName) name = acc.UserInfo.TitleInfo.DisplayName;
            else if (acc.UserInfo.Username) name = acc.UserInfo.Username;
        }
    } catch (e) { }

    // Карт собрано = предметы инвентаря, кроме бустеров/аватаров.
    var cards = 0;
    try {
        var inv = server.GetUserInventory({ PlayFabId: playerId });
        for (var i = 0; i < inv.Inventory.length; i++) {
            var id = inv.Inventory[i].ItemId || "";
            if (id.indexOf("booster_") === 0 || id.indexOf("avatar_") === 0) continue;
            cards++;
        }
    } catch (e2) { }

    // Победы/поражения/бустеры — из статистики игрока. Сервер пишет её позже (после валидации исходов),
    // пока статистик нет → нули. Профиль всё равно откроется с реальным числом карт и именем.
    var stats = {};
    try {
        var st = server.GetPlayerStatistics({ PlayFabId: playerId });
        if (st && st.Statistics) for (var s = 0; s < st.Statistics.length; s++) stats[st.Statistics[s].StatisticName] = st.Statistics[s].Value;
    } catch (e3) { }

    var wins = stats["wins"] || 0;
    var losses = stats["losses"] || 0;

    return {
        Name: name,
        Rank: "",                              // звание — клиентская косметика (PlayerRating.RankName)
        Mmr: stats["mmr"] || 0,                // пишет ReportMatchResult (Elo по взаимному подтверждению)
        Level: 1,
        Xp01: 0,
        Wins: wins,
        Losses: losses,
        GamesPlayed: wins + losses,
        AchievementsEarned: 0,
        AchievementsTotal: 0,
        BoostersOpened: stats["boosters_opened"] || 0,
        CardsCollected: cards
    };
};

handlers.GetInbox = function (args, context) {
    // Удалённых наград пока нет → пусто. Позже: читать UserInternalData "inbox", отдать и пометить показанным.
    return { Entries: [] };
};

// ---- Рейтинг (MMR, Elo) ---------------------------------------------------------
// Авторитетного игрового сервера нет (P2P lockstep-replay) → исход шлют ОБА клиента, Elo
// применяется только когда отчёты СОШЛИСЬ (win↔lose или draw↔draw + перекрёстная сверка id).
// Хранилище — Shared Group Data "match_reports" (паттерн auction_house): каждый игрок пишет
// СВОЙ ключ "rep:{matchId}:{playFabId}" (записи не затирают друг друга), итог — "done:{matchId}"
// с новыми MMR обоих (повторный вызов идемпотентно отдаёт сохранённый результат).
// Гонка одновременных отчётов (в Classic нет CAS): сужается клиентским джиттером (проигравший
// шлёт с задержкой ~2с); если оба всё же не увидели друг друга (двойной Pending) — пару дорешает
// maintainRatingGroup при следующем же вызове (клиент ретраит Report через ~3с).
// Rage-quit (второй отчёт так и не пришёл): одиночный отчёт старше RATING_SINGLE_APPLY_MS
// применяется односторонне тем же maintainRatingGroup (лениво, крон не нужен).

var RATING_GROUP = "match_reports";
var RATING_K = 32;
var RATING_DEFAULT = 1000;
var RATING_MIN = 1;
var RATING_SINGLE_APPLY_MS = 10 * 60 * 1000;    // одиночный отчёт применяем через 10 минут
var RATING_REPORT_TTL_MS = 24 * 60 * 60 * 1000; // done-записи старше суток чистим

function ensureRatingGroup() {
    try { server.CreateSharedGroup({ SharedGroupId: RATING_GROUP }); } catch (e) { /* уже создана */ }
}

function readRatingGroup() {
    var res;
    try { res = server.GetSharedGroupData({ SharedGroupId: RATING_GROUP }); }
    catch (e) { ensureRatingGroup(); res = server.GetSharedGroupData({ SharedGroupId: RATING_GROUP }); }
    return (res && res.Data) || {};
}

function readStats(playerId) {
    var stats = {};
    try {
        var st = server.GetPlayerStatistics({ PlayFabId: playerId });
        if (st && st.Statistics) for (var i = 0; i < st.Statistics.length; i++) stats[st.Statistics[i].StatisticName] = st.Statistics[i].Value;
    } catch (e) { }
    return stats;
}

function writeStats(playerId, dict) {
    var list = [];
    for (var k in dict) if (dict.hasOwnProperty(k)) list.push({ StatisticName: k, Value: dict[k] });
    server.UpdatePlayerStatistics({ PlayFabId: playerId, Statistics: list });
}

function eloExpected(myMmr, oppMmr) { return 1 / (1 + Math.pow(10, (oppMmr - myMmr) / 400)); }

// Золото за матч (обоим, по исходу). Конфиг — Title Data ratingConfig.matchReward, дефолты в коде.
function matchRewardCfg() {
    var c = getTitleJson("ratingConfig");
    var r = (c && c.matchReward) || {};
    return { code: r.code || "GD",
             win:  (typeof r.win  === "number") ? r.win  : 25,
             lose: (typeof r.lose === "number") ? r.lose : 10,
             draw: (typeof r.draw === "number") ? r.draw : 15 };
}
function matchRewardFor(outcome, cfg) { return outcome === "win" ? cfg.win : (outcome === "lose" ? cfg.lose : cfg.draw); }

// Применить расчёт матча паре: Elo + wins/losses + золото за матч ОБОИМ. aScore: 1 победа a,
// 0 поражение a, 0.5 ничья. aStatsOpt/bStatsOpt — статы, если вызывающий их уже читал (экономия
// API-вызовов). Возвращает { m: {id→newMmr}, r: {id→золото}, rc: код валюты }.
function applyEloPair(aId, aScore, bId, aStatsOpt, bStatsOpt) {
    var aStats = aStatsOpt || readStats(aId), bStats = bStatsOpt || readStats(bId);
    var aMmr = aStats["mmr"] || RATING_DEFAULT, bMmr = bStats["mmr"] || RATING_DEFAULT;
    var aNew = Math.max(RATING_MIN, Math.round(aMmr + RATING_K * (aScore - eloExpected(aMmr, bMmr))));
    var bScore = 1 - aScore;
    var bNew = Math.max(RATING_MIN, Math.round(bMmr + RATING_K * (bScore - eloExpected(bMmr, aMmr))));

    var aUpd = { mmr: aNew }, bUpd = { mmr: bNew };
    if (aScore === 1)      { aUpd.wins   = (aStats["wins"]   || 0) + 1; bUpd.losses = (bStats["losses"] || 0) + 1; }
    else if (aScore === 0) { aUpd.losses = (aStats["losses"] || 0) + 1; bUpd.wins   = (bStats["wins"]   || 0) + 1; }
    // ничья: только mmr (при разных рейтингах ожидание ≠ 0.5 — слабый подрастает)
    writeStats(aId, aUpd);
    writeStats(bId, bUpd);

    // Золото за матч. Ошибка гранта не роняет расчёт (Elo уже записан) — просто нет награды в ответе.
    var rcfg = matchRewardCfg();
    var aOutcome = aScore === 1 ? "win" : (aScore === 0 ? "lose" : "draw");
    var bOutcome = aScore === 1 ? "lose" : (aScore === 0 ? "win" : "draw");
    var aRw = matchRewardFor(aOutcome, rcfg), bRw = matchRewardFor(bOutcome, rcfg);
    try { if (aRw > 0) server.AddUserVirtualCurrency({ PlayFabId: aId, VirtualCurrency: rcfg.code, Amount: aRw }); } catch (eA) { aRw = 0; }
    try { if (bRw > 0) server.AddUserVirtualCurrency({ PlayFabId: bId, VirtualCurrency: rcfg.code, Amount: bRw }); } catch (eB) { bRw = 0; }

    var res = { m: {}, r: {}, rc: rcfg.code };
    res.m[aId] = aNew; res.m[bId] = bNew;
    res.r[aId] = aRw;  res.r[bId] = bRw;
    return res;
}

function ratingDoneValue(mmrByPlayer, deltaByPlayer, rewardByPlayer, rewardCode, conflict) {
    return JSON.stringify({ t: nowMs(), c: conflict ? 1 : 0,
                            m: mmrByPlayer || {}, d: deltaByPlayer || {},
                            r: rewardByPlayer || {}, rc: rewardCode || "" });
}

function parseRatingDone(raw) {
    try { var v = JSON.parse(raw); return v && typeof v === "object" ? v : null; } catch (e) { return null; }
}

// Ленивое обслуживание группы (зовётся из Report/GetRating, ошибки глотает — уборка не роняет вызов):
//   • матч с ДВУМЯ отчётами без done (оба словили Pending в гонке) → дорешать: сверка + Elo/конфликт;
//   • одиночный отчёт старше порога (rage-quit соперника) → применить односторонне;
//   • done старше TTL и осиротевшие/битые rep-ключи → удалить.
// За вызов дорешивается максимум ОДИН матч — иначе упрёмся в лимит API-вызовов CloudScript.
function maintainRatingGroup(data) {
    try {
        var now = nowMs(), key, parts;
        var byMatch = {}, doneByMatch = {}, toRemove = [];

        for (key in data) {
            if (!data.hasOwnProperty(key)) continue;
            parts = key.split(":");
            if (parts[0] === "done" && parts.length === 2) {
                var dv = parseRatingDone(data[key].Value);
                doneByMatch[parts[1]] = dv;
                if (!dv || now - (dv.t || 0) > RATING_REPORT_TTL_MS) toRemove.push(key);
            } else if (parts[0] === "rep" && parts.length === 3) {
                var rep = null;
                try { rep = JSON.parse(data[key].Value); } catch (e) { rep = null; }
                if (!rep || !rep.opp || !rep.o) { toRemove.push(key); continue; }
                if (!byMatch[parts[1]]) byMatch[parts[1]] = [];
                byMatch[parts[1]].push({ key: key, playerId: parts[2], rep: rep });
            }
        }

        var resolvedOne = false, upd = {};
        for (var mid in byMatch) {
            if (!byMatch.hasOwnProperty(mid)) continue;
            var reps = byMatch[mid];

            // матч уже закрыт → rep-ключи осиротели
            if (doneByMatch.hasOwnProperty(mid)) {
                for (var r0 = 0; r0 < reps.length; r0++) toRemove.push(reps[r0].key);
                continue;
            }
            if (resolvedOne) continue;

            if (reps.length >= 2) {
                // двойной Pending из гонки: сверяем первую пару
                var a = reps[0], b = reps[1];
                var pairConsistent = a.rep.opp === b.playerId && b.rep.opp === a.playerId &&
                    ((a.rep.o === "win" && b.rep.o === "lose") ||
                     (a.rep.o === "lose" && b.rep.o === "win") ||
                     (a.rep.o === "draw" && b.rep.o === "draw"));
                if (pairConsistent) {
                    var beforeA = readStats(a.playerId), beforeB = readStats(b.playerId);
                    var aScore = a.rep.o === "win" ? 1 : (a.rep.o === "lose" ? 0 : 0.5);
                    var pair = applyEloPair(a.playerId, aScore, b.playerId, beforeA, beforeB);
                    var deltas = {};
                    deltas[a.playerId] = pair.m[a.playerId] - (beforeA["mmr"] || RATING_DEFAULT);
                    deltas[b.playerId] = pair.m[b.playerId] - (beforeB["mmr"] || RATING_DEFAULT);
                    upd["done:" + mid] = ratingDoneValue(pair.m, deltas, pair.r, pair.rc, false);
                } else {
                    upd["done:" + mid] = ratingDoneValue(null, null, null, null, true);
                }
                toRemove.push(a.key); toRemove.push(b.key);
                resolvedOne = true;
            } else if (reps.length === 1 && now - (reps[0].rep.t || 0) > RATING_SINGLE_APPLY_MS) {
                // rage-quit: применяем единственный отчёт как есть
                var solo = reps[0];
                var beforeSolo = readStats(solo.playerId), beforeOppS = readStats(solo.rep.opp);
                var soloScore = solo.rep.o === "win" ? 1 : (solo.rep.o === "lose" ? 0 : 0.5);
                var pairSolo = applyEloPair(solo.playerId, soloScore, solo.rep.opp, beforeSolo, beforeOppS);
                var deltasSolo = {};
                deltasSolo[solo.playerId] = pairSolo.m[solo.playerId] - (beforeSolo["mmr"] || RATING_DEFAULT);
                deltasSolo[solo.rep.opp] = pairSolo.m[solo.rep.opp] - (beforeOppS["mmr"] || RATING_DEFAULT);
                upd["done:" + mid] = ratingDoneValue(pairSolo.m, deltasSolo, pairSolo.r, pairSolo.rc, false);
                toRemove.push(solo.key);
                resolvedOne = true;
            }
        }

        var hasUpd = false;
        for (var uk in upd) if (upd.hasOwnProperty(uk)) { hasUpd = true; break; }
        if (hasUpd || toRemove.length > 0) {
            var req = { SharedGroupId: RATING_GROUP };
            if (hasUpd) req.Data = upd;
            if (toRemove.length > 0) req.KeysToRemove = toRemove;
            server.UpdateSharedGroupData(req);
        }
    } catch (eM) { /* уборка не должна ронять основной вызов */ }
}

handlers.ReportMatchResult = function (args, context) {
    var me = currentPlayerId;
    var matchId = args && args.MatchId;
    var oppId = args && args.OpponentPlayFabId;
    var outcome = args && args.Outcome;   // "win" / "lose" / "draw"
    if (!matchId || !oppId || oppId === me ||
        (outcome !== "win" && outcome !== "lose" && outcome !== "draw"))
        return { Applied: false, Pending: false, Conflict: false, Reason: "bad_request", Mmr: 0, Delta: 0,
                 RewardCode: "", RewardAmount: 0, Wallet: null };

    var data = readRatingGroup();   // сам создаёт группу при первом обращении (catch внутри)

    var myStats = readStats(me);
    var myMmrBefore = myStats["mmr"] || RATING_DEFAULT;

    // Матч уже рассчитан (ретрай/второй вызов после гонки) → идемпотентно отдать сохранённый итог.
    var doneKey = "done:" + matchId;
    if (data[doneKey]) {
        var done = parseRatingDone(data[doneKey].Value);
        if (done && done.c) return { Applied: false, Pending: false, Conflict: true, Reason: "conflict", Mmr: myMmrBefore, Delta: 0,
                                     RewardCode: "", RewardAmount: 0, Wallet: null };
        var savedMmr = done && done.m && done.m[me];
        var savedDelta = (done && done.d && done.d[me]) || 0;
        return { Applied: true, Pending: false, Conflict: false, Reason: null,
                 Mmr: (typeof savedMmr === "number" ? savedMmr : myMmrBefore), Delta: savedDelta,
                 RewardCode: (done && done.rc) || "", RewardAmount: (done && done.r && done.r[me]) || 0,
                 Wallet: readWallet(me) };
    }

    var myKey = "rep:" + matchId + ":" + me;
    var oppKey = "rep:" + matchId + ":" + oppId;

    // Пишем свой отчёт (свой ключ — затирание чужого невозможно; повторная запись своего безвредна).
    var updRep = {};
    updRep[myKey] = JSON.stringify({ o: outcome, opp: oppId, t: nowMs() });
    server.UpdateSharedGroupData({ SharedGroupId: RATING_GROUP, Data: updRep });

    var oppRep = null;
    if (data[oppKey]) { try { oppRep = JSON.parse(data[oppKey].Value); } catch (e) { oppRep = null; } }

    if (!oppRep) {
        maintainRatingGroup(data);   // попутная уборка/дорешивание чужих зависших пар
        return { Applied: false, Pending: true, Conflict: false, Reason: null, Mmr: myMmrBefore, Delta: 0,
                 RewardCode: "", RewardAmount: 0, Wallet: null };
    }

    // Перекрёстная сверка: его отчёт указывает на нас и комплементарен нашему.
    var consistent = oppRep.opp === me &&
        ((outcome === "win"  && oppRep.o === "lose") ||
         (outcome === "lose" && oppRep.o === "win")  ||
         (outcome === "draw" && oppRep.o === "draw"));

    if (!consistent) {
        var updConflict = {};
        updConflict[doneKey] = ratingDoneValue(null, null, null, null, true);
        server.UpdateSharedGroupData({ SharedGroupId: RATING_GROUP, Data: updConflict, KeysToRemove: [myKey, oppKey] });
        return { Applied: false, Pending: false, Conflict: true, Reason: "conflict", Mmr: myMmrBefore, Delta: 0,
                 RewardCode: "", RewardAmount: 0, Wallet: null };
    }

    // Сошлись → Elo + золото за матч, итоги в done (done нельзя писать раньше — в нём уже новые MMR;
    // окно двойного применения в гонке сужают done-гейт на входе + клиентский джиттер проигравшего).
    var oppStats = readStats(oppId);
    var myScore = outcome === "win" ? 1 : (outcome === "lose" ? 0 : 0.5);
    var pairNow = applyEloPair(me, myScore, oppId, myStats, oppStats);
    var deltasPair = {};
    deltasPair[me] = pairNow.m[me] - myMmrBefore;
    deltasPair[oppId] = pairNow.m[oppId] - (oppStats["mmr"] || RATING_DEFAULT);
    var updDone = {};
    updDone[doneKey] = ratingDoneValue(pairNow.m, deltasPair, pairNow.r, pairNow.rc, false);
    server.UpdateSharedGroupData({ SharedGroupId: RATING_GROUP, Data: updDone, KeysToRemove: [myKey, oppKey] });

    return { Applied: true, Pending: false, Conflict: false, Reason: null, Mmr: pairNow.m[me], Delta: deltasPair[me],
             RewardCode: pairNow.rc, RewardAmount: pairNow.r[me] || 0, Wallet: readWallet(me) };
};

handlers.GetRating = function (args, context) {
    // Попутно дорешиваем зависшие матчи (rage-quit / двойной Pending) — до чтения своих статов,
    // чтобы применённый здесь же результат сразу попал в ответ.
    try { maintainRatingGroup(readRatingGroup()); } catch (e) { }
    var stats = readStats(currentPlayerId);
    return { Mmr: stats["mmr"] || RATING_DEFAULT, Wins: stats["wins"] || 0, Losses: stats["losses"] || 0 };
};

handlers.DevSetMmr = function (args, context) {
    if (!devEnabled()) return { Success: false, Reason: "dev_disabled", Mmr: 0 };
    var v = Math.max(RATING_MIN, Math.round((args && args.Mmr) || RATING_DEFAULT));
    writeStats(currentPlayerId, { mmr: v });
    return { Success: true, Reason: null, Mmr: v };
};

// ---- Аукцион (модель СТАВОК) ----------------------------------------------------
// Почему ставки, а не «купи-сейчас»: в Classic нет compare-and-swap, поэтому мгновенная покупка
// одного лота двумя игроками = гонка (дубль/двойная оплата). Ставки убирают гонку — победитель
// определяется ОДИН раз на дедлайне единым авторитетом (ResolveAuctions, крон), а не в клик.
//
// Хранилище — Shared Group Data "auction_house": каждый лот = ключ "lot:{id}", каждая ставка =
// свой ключ "bid:{lotId}:{bidderId}" (разные игроки пишут разные ключи → ставки не затирают друг
// друга). Escrow: карта списывается у продавца при выставлении; деньги ставки списываются сразу,
// проигравшим возврат при закрытии. Комиссия — buyer's premium: победитель платит ставку×(1+fee),
// продавец получает ставку, разница СГОРАЕТ (сток валюты). Лимит лотов (maxLots) — намеренная фишка.
//
// ОГРАНИЧЕНИЕ MVP: одновременный запуск ResolveAuctions (крон + дев в один момент) может дважды
// рассчитать один лот — крон ходит соло, для теста жмём дев-кнопку в одиночку. При взлёте — внешняя БД.

var AUCTION_GROUP = "auction_house";

function nowMs() { return (new Date()).getTime(); }

function ensureAuctionGroup() {
    try { server.CreateSharedGroup({ SharedGroupId: AUCTION_GROUP }); } catch (e) { /* уже создана */ }
}

// Прочитать всю группу и разложить на lots{lotId→meta} и bids{lotId→{bidderId→bid}}.
function readAuctionGroup() {
    ensureAuctionGroup();
    var res;
    try { res = server.GetSharedGroupData({ SharedGroupId: AUCTION_GROUP }); }
    catch (e) { ensureAuctionGroup(); res = server.GetSharedGroupData({ SharedGroupId: AUCTION_GROUP }); }
    var data = (res && res.Data) || {};
    var lots = {}, bids = {}, key, obj, parts;
    for (key in data) {
        if (!data.hasOwnProperty(key) || !data[key] || !data[key].Value) continue;
        try { obj = JSON.parse(data[key].Value); } catch (e2) { continue; }
        if (key.indexOf("lot:") === 0) { lots[key.substring(4)] = obj; }
        else if (key.indexOf("bid:") === 0) {
            parts = key.split(":");                       // ["bid", lotId, bidderId] — id без ":"
            if (parts.length === 3) { if (!bids[parts[1]]) bids[parts[1]] = {}; bids[parts[1]][parts[2]] = obj; }
        }
    }
    return { lots: lots, bids: bids };
}

function countKeys(o) { var n = 0, k; for (k in o) if (o.hasOwnProperty(k)) n++; return n; }

function auctionConfig() {
    var c = getTitleJson("auctionConfig") || {};
    return {
        maxLots:        c.maxLots || 40,
        lotDurationMin: c.lotDurationMin || 60,
        feePercent:     (typeof c.feePercent === "number") ? c.feePercent : 10,
        minBid:         c.minBid || { GD: 10, GM: 1 }
    };
}

// Что спишется с покупателя при ставке amount (ceil комиссии). Должно совпадать с Lot.WithFee (C#).
function feeFor(amount, feePercent) { return amount + Math.ceil(amount * feePercent / 100); }

function displayName(playerId) {
    var name = "Player";
    try {
        var acc = server.GetUserAccountInfo({ PlayFabId: playerId });
        if (acc && acc.UserInfo) {
            if (acc.UserInfo.TitleInfo && acc.UserInfo.TitleInfo.DisplayName) name = acc.UserInfo.TitleInfo.DisplayName;
            else if (acc.UserInfo.Username) name = acc.UserInfo.Username;
        }
    } catch (e) { }
    return name;
}

handlers.GetAuctionListings = function (args, context) {
    var me = currentPlayerId;
    var cfg = auctionConfig();
    var g = readAuctionGroup();
    var now = nowMs();

    var ids = [], lotId;
    for (lotId in g.lots) if (g.lots.hasOwnProperty(lotId)) ids.push(lotId);
    ids.sort(function (a, b) { return g.lots[a].EndsAt - g.lots[b].EndsAt; });   // скоро закрывающиеся сверху

    var lots = [];
    for (var n = 0; n < ids.length; n++) {
        var m = g.lots[ids[n]];
        var b = g.bids[ids[n]] || {};
        var high = 0, highId = null, highName = null, count = 0, mine = 0, bid;
        for (bid in b) {
            if (!b.hasOwnProperty(bid)) continue;
            count++;
            if (bid === me) mine = b[bid].Hammer;
            if (b[bid].Hammer > high) { high = b[bid].Hammer; highId = bid; highName = b[bid].BidderName; }
        }
        lots.push({
            LotId: ids[n], ItemId: m.ItemId, SellerId: m.SellerId, SellerName: m.SellerName,
            Currency: m.Currency, MinBid: m.MinBid, CurrentBid: high, CurrentBidderId: highId,
            CurrentBidderName: highName, BidCount: count, MyBid: mine,
            EndsAtUtc: (new Date(m.EndsAt)).toISOString(), Ended: m.EndsAt <= now
        });
    }

    return {
        Lots: lots, MaxLots: cfg.maxLots, LotCount: countKeys(g.lots),
        FeePercent: cfg.feePercent, LotDurationMin: cfg.lotDurationMin,
        ServerTimeUtc: (new Date(now)).toISOString()
    };
};

handlers.ListCardForSale = function (args, context) {
    var me = currentPlayerId;
    var resp = { Success: false, Reason: null, Wallet: null, LotId: null };
    var itemId = args && args.ItemId;
    var currency = args && args.Currency;
    var minBid = args && args.MinBid;
    if (!itemId || !currency) { resp.Reason = "bad_request"; return resp; }
    if (currency !== "GD" && currency !== "GM") { resp.Reason = "bad_currency"; return resp; }
    if (itemId.indexOf("booster_") === 0 || itemId.indexOf("avatar_") === 0) { resp.Reason = "not_sellable"; return resp; }

    var cfg = auctionConfig();
    var floor = (cfg.minBid && cfg.minBid[currency]) || 1;
    if (!minBid || minBid < floor) { resp.Reason = "bid_too_low"; return resp; }

    var g = readAuctionGroup();
    if (countKeys(g.lots) >= cfg.maxLots) { resp.Reason = "auction_full"; return resp; }

    // владение + инстанс на escrow
    var inv = server.GetUserInventory({ PlayFabId: me });
    var instanceId = null;
    for (var i = 0; i < inv.Inventory.length; i++)
        if (inv.Inventory[i].ItemId === itemId) { instanceId = inv.Inventory[i].ItemInstanceId; break; }
    if (!instanceId) { resp.Reason = "not_owned"; return resp; }

    server.RevokeInventoryItem({ PlayFabId: me, ItemInstanceId: instanceId });   // карта → escrow

    var now = nowMs();
    var lotId = me + "_" + now + "_" + Math.floor(Math.random() * 100000);
    var meta = {
        ItemId: itemId, SellerId: me, SellerName: displayName(me), Currency: currency,
        MinBid: minBid, EndsAt: now + cfg.lotDurationMin * 60000, Created: now
    };
    var data = {}; data["lot:" + lotId] = JSON.stringify(meta);
    try { server.UpdateSharedGroupData({ SharedGroupId: AUCTION_GROUP, Data: data }); }
    catch (e) { grantItems(me, [itemId]); resp.Reason = "list_failed"; resp.Wallet = readWallet(me); return resp; }   // не потерять карту

    resp.Success = true; resp.LotId = lotId; resp.Wallet = readWallet(me);
    return resp;
};

handlers.CancelAuctionListing = function (args, context) {
    var me = currentPlayerId;
    var resp = { Success: false, Reason: null, Wallet: null };
    var lotId = args && args.LotId; if (!lotId) { resp.Reason = "bad_request"; return resp; }

    var g = readAuctionGroup();
    var m = g.lots[lotId]; if (!m) { resp.Reason = "not_found"; return resp; }
    if (m.SellerId !== me) { resp.Reason = "not_owner"; return resp; }
    if (countKeys(g.bids[lotId] || {}) > 0) { resp.Reason = "has_bids"; return resp; }   // со ставками снять нельзя

    grantItems(me, [m.ItemId]);   // карта обратно
    var data = {}; data["lot:" + lotId] = null;
    server.UpdateSharedGroupData({ SharedGroupId: AUCTION_GROUP, Data: data });

    resp.Success = true; resp.Wallet = readWallet(me);
    return resp;
};

handlers.PlaceAuctionBid = function (args, context) {
    var me = currentPlayerId;
    var resp = { Success: false, Reason: null, Wallet: null, CurrentBid: 0, EndsAtUtc: null };
    var lotId = args && args.LotId;
    var amount = args && args.Amount;
    if (!lotId || !amount || amount <= 0) { resp.Reason = "bad_request"; return resp; }

    var cfg = auctionConfig();
    var g = readAuctionGroup();
    var m = g.lots[lotId]; if (!m) { resp.Reason = "not_found"; return resp; }
    if (m.EndsAt <= nowMs()) { resp.Reason = "ended"; return resp; }
    if (m.SellerId === me) { resp.Reason = "own_lot"; return resp; }
    if (amount < m.MinBid) { resp.Reason = "bid_too_low"; return resp; }

    var b = g.bids[lotId] || {};
    var high = 0, bid;
    for (bid in b) { if (b.hasOwnProperty(bid) && b[bid].Hammer > high) high = b[bid].Hammer; }
    if (amount <= high) { resp.Reason = "outbid"; return resp; }   // строго выше текущей высшей

    var prevEscrow = (b[me] && b[me].Escrow) || 0;
    var newEscrow = feeFor(amount, cfg.feePercent);
    var delta = newEscrow - prevEscrow;
    if (delta > 0) {
        try { server.SubtractUserVirtualCurrency({ PlayFabId: me, VirtualCurrency: m.Currency, Amount: delta }); }
        catch (e) { resp.Reason = "not_enough_currency"; resp.Wallet = readWallet(me); return resp; }
    }

    var data = {};
    data["bid:" + lotId + ":" + me] = JSON.stringify({ Hammer: amount, Escrow: newEscrow, BidderName: displayName(me), Ts: nowMs() });
    try { server.UpdateSharedGroupData({ SharedGroupId: AUCTION_GROUP, Data: data }); }
    catch (e2) {
        if (delta > 0) server.AddUserVirtualCurrency({ PlayFabId: me, VirtualCurrency: m.Currency, Amount: delta });   // вернуть эскроу
        resp.Reason = "bid_failed"; resp.Wallet = readWallet(me); return resp;
    }

    resp.Success = true; resp.CurrentBid = amount;
    resp.EndsAtUtc = (new Date(m.EndsAt)).toISOString(); resp.Wallet = readWallet(me);
    return resp;
};

// Расчёт истёкших лотов. Зовётся Scheduled Task (крон) и дев-кнопкой. currentPlayerId тут может быть пуст
// (крон) — не используем. Победитель = высшая ставка (ничья → ранняя по времени).
handlers.ResolveAuctions = function (args, context) {
    var g = readAuctionGroup();
    var now = nowMs();
    var remove = {}, resolved = 0, lotId;

    for (lotId in g.lots) {
        if (!g.lots.hasOwnProperty(lotId)) continue;
        var m = g.lots[lotId];
        if (m.EndsAt > now) continue;   // ещё идёт

        var b = g.bids[lotId] || {};
        var winId = null, winHammer = 0, winTs = 0, bid;
        for (bid in b) {
            if (!b.hasOwnProperty(bid)) continue;
            var e = b[bid];
            if (e.Hammer > winHammer || (e.Hammer === winHammer && (winTs === 0 || e.Ts < winTs))) {
                winId = bid; winHammer = e.Hammer; winTs = e.Ts;
            }
        }

        if (winId) {
            grantItems(winId, [m.ItemId]);                                                                      // карта победителю
            server.AddUserVirtualCurrency({ PlayFabId: m.SellerId, VirtualCurrency: m.Currency, Amount: winHammer });   // продавцу (комиссия сгорела)
            for (var lb in b) {                                                                                 // возврат проигравшим (полный эскроу)
                if (b.hasOwnProperty(lb) && lb !== winId)
                    server.AddUserVirtualCurrency({ PlayFabId: lb, VirtualCurrency: m.Currency, Amount: b[lb].Escrow });
            }
        } else {
            grantItems(m.SellerId, [m.ItemId]);                                                                 // ставок нет → карту назад
        }

        remove["lot:" + lotId] = null;
        for (var rb in b) if (b.hasOwnProperty(rb)) remove["bid:" + lotId + ":" + rb] = null;
        resolved++;
    }

    if (resolved > 0) server.UpdateSharedGroupData({ SharedGroupId: AUCTION_GROUP, Data: remove });
    return { Success: true, Reason: null, Resolved: resolved };
};