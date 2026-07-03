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
2. **Каталог (карты)**:
   - В Unity: `Tools → Backend → Export Card Catalog (JSON)` → `card_catalog.json` (все карты,
     ItemId = `expansion_cardId`, теги rarity/expansion).
   - Создай в **Catalog (v1)** предмет на каждую карту с этим **ItemId** и тегами (bulk-upload либо Admin API).
   - Добавь в каталог: бустер `booster_standard`, аватары `avatar_*`.
3. **Секретный ключ** для CloudScript **не нужен** — `server.*` уже title-авторитетны внутри CloudScript.

---

## Шаг 2. Title Data (Content → Title Data)

Залей ключи из `Server/TitleData/`:
- `boosterConfig`     ← `boosterConfig.json`
- `blackMarketConfig` ← `blackMarketConfig.json`
- `taskConfig`        ← `taskConfig.json`
- `cardPool`          ← сгенерируй: `Tools → Backend → Export Card Pool` → `cardPool.json`
  (пул для ролла бустеров: expansion → rarity → [itemId]).

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
| Магазин | `GetShop`, `BuyStoreItem` | ⏳ заготовка |
| Daily | `GetDailyState`, `ClaimLoginReward`, `ClaimTask`, `ReportTaskProgress` | ⏳ заготовка |
| Чёрный рынок | `GetBlackMarket`, `BuyBlackMarketCard` | ⏳ заготовка |
| Аукцион | `*AuctionListing*` | ⏳ заготовка |

## Заметки по масштабированию

- **Аукцион**: индекс листингов для MVP — Shared Group Data внутри PlayFab; при росте вынести во
  внешнюю БД. Escrow — `server.RevokeInventoryItem` (карта уходит у продавца при листинге).
- **Копии карт в v1** = отдельные ItemInstance. Для коллекции в сотни карт норм; при тысячах —
  рассмотреть переход на Economy v2 (тогда вернётся вариант с Azure/HTTP).
- `win_games` в P2P (Photon) — self-report; серверную валидацию исхода добавить позже.
