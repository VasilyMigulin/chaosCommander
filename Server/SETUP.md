# ChaosCommander — настройка бэкенда (PlayFab Economy v1 + Classic CloudScript)

Стек: **всё внутри PlayFab, без Azure/карты/внешнего хостинга.** Серверная логика — JavaScript-ревизия
CloudScript. Клиентский слой уже написан (`Assets/Scripts/DeckBuilder/Backend/`) и настроен на
`ExecuteCloudScript`. Серверный код — `Server/CloudScript/cloudscript.js`.

> Папка `Server/AzureFunctions/` — **не используется** (альтернатива на случай, если позже появится
> Azure). Актуальный сервер — `Server/CloudScript/`.

Порядок фаз: **Фундамент** (валюта+инвентарь+миграция) → Бустеры → Магазин → Daily → Чёрный рынок → Аукцион.
Реализованы `MigrateLibrary` и `OpenBooster`; остальные handlers — заготовки (`not_implemented`), дописываются по фазам.

---

## Шаг 1. PlayFab — Economy v1 (Game Manager, title `185AA`)

Используем **классическую** экономику (Virtual Currency + Catalog), а не Economy v2.

1. **Виртуальные валюты**: Economy → **Currency / Virtual Currencies** → создать:
   - `GD` — Gold,
   - `GM` — Gems.
   (Двухбуквенный код — ровно тот, что шлёт клиент/CloudScript.)
2. **Каталог (карты + бустеры + аватары)**:
   - В Unity: `Tools → Backend → Export PlayFab Catalog v1 (upload-ready)` → `playfab_catalog_v1.json`.
     Файл уже в формате Game Manager: карты (ItemId = `expansion_cardId`, теги rarity/expansion),
     плюс `booster_standard`, `booster_premium`, `avatar_prince`, `avatar_gnidalf`.
   - Game Manager → **Economy → Catalogs** → выбрать каталог `main` → **Upload Catalog** → этот файл.
   - `IsStackable: false` менять НЕЛЬЗЯ: клиент считает копии карты как число ItemInstance с данным
     ItemId. Со стековым предметом PlayFab схлопнет 4 копии в один инстанс, и в библиотеке будет 1 карта.
   - (`Tools → Backend → Export Card Catalog (JSON)` — старый экспорт «для сверки», PlayFab его не примет.)
3. **Секретный ключ** для CloudScript **не нужен** — `server.*` уже title-авторитетны внутри CloudScript.

---

## Шаг 2. Title Data (Content → Title Data)

Залей ключи из `Server/TitleData/`:
- `boosterConfig`     ← `boosterConfig.json`
- `shopConfig`        ← `shopConfig.json`  ← **витрина магазина и цены**
- `blackMarketConfig` ← `blackMarketConfig.json`
- `taskConfig`        ← `taskConfig.json`
- `cardPool`          ← сгенерируй: `Tools → Backend → Export Card Pool` → `cardPool.json`
  (пул для ролла бустеров: expansion → rarity → [itemId]).

**Про `shopConfig`.** Цена живёт только здесь — клиент присылает в `BuyStoreItem` один `itemId`,
всё остальное сервер берёт из этого конфига, поэтому цену нельзя подделать, а витрину можно менять
удалённо без обновления приложения. Каждый `itemId` из витрины **обязан существовать в Catalog v1**
(иначе `GrantItemsToUser` упадёт, и покупка откатится с возвратом валюты — деньги игрок не потеряет,
но товар не получит). `displayName` можно писать сырым текстом или ключом локализации
(`ui.shop.booster_standard` — такие ключи уже заведены в `card_text.csv`).

---

## Шаг 3. CloudScript-ревизия

Game Manager → **Automation → Cloud Script → Revisions** → вставь содержимое
`Server/CloudScript/cloudscript.js` → **Save** → **Deploy** (сделать ревизию Live).

Проверка связки: в разделе Cloud Script есть тест-консоль — вызови `MigrateLibrary` от тестового игрока.

---

## Шаг 4. Клиент (Unity)

Ничего заполнять не нужно — v1 работает по коду валюты, item-id валют не требуются.
`FunctionService` уже вызывает `ExecuteCloudScript`, имена функций — `BackendConfig.Fn.*`
(совпадают с `handlers.*` в ревизии).

---

## Как это работает после настройки

- Логин → `InitState` → `BackendSession.Initialize`: серверное время → `MigrateLibrary`
  (одноразово переносит старую коллекцию из UserData в инвентарь v1) → `GetUserInventory`
  наполняет `PlayerLibrary` (копии = число ItemInstance по ItemId) + `PlayerWallet` (VirtualCurrency).
- Все выдачи/списания — в CloudScript (`server.GrantItemsToUser`/`AddUserVirtualCurrency`);
  клиент только просит через `FunctionService`.
- Идентичность карты: `expansion_cardId` (клиент `CardItemId` ↔ каталог ↔ `cardPool`) — держать в синке.

## Статус функций

| Фаза | handlers | Статус |
|---|---|---|
| Фундамент | `MigrateLibrary` | ✅ |
| Бустеры | `OpenBooster` | ✅ |
| Магазин | `GetShop`, `BuyStoreItem` | ✅ (нужен Title Data `shopConfig`) |
| Daily | `GetDailyState`, `ClaimLoginReward`, `ClaimTask`, `ReportTaskProgress` | ⏳ заготовка |
| Чёрный рынок | `GetBlackMarket`, `BuyBlackMarketCard` | ⏳ заготовка |
| Аукцион | `*AuctionListing*` | ⏳ заготовка |

## Заметки по масштабированию

- **Аукцион**: индекс листингов для MVP — Shared Group Data внутри PlayFab; при росте вынести во
  внешнюю БД. Escrow — `server.RevokeInventoryItem` (карта уходит у продавца при листинге).
- **Копии карт в v1** = отдельные ItemInstance. Для коллекции в сотни карт норм; при тысячах —
  рассмотреть переход на Economy v2 (тогда вернётся вариант с Azure/HTTP).
- `win_games` в P2P (Photon) — self-report; серверную валидацию исхода добавить позже.
