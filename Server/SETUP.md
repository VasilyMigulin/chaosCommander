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

## Шаг 5. Промокоды (крауд-плюшки, акции, коды бэкеров)

Один хендлер `RedeemPromo` обслуживает **два источника кодов**, клиент шлёт просто строку из поля
ввода в настройках и не знает, какой из них сработал.

### A. Свои кампании — Title Data `promoConfig`

Многоразовый код с лимитами. Залей `Server/TitleData/promoConfig.json` в ключ `promoConfig`.
Поля записи:

| поле | смысл |
|---|---|
| `code` | как игрок вводит. При сверке регистр и дефисы/пробелы игнорируются: `shh launch` = `SHH-LAUNCH` |
| `enabled` | `false` — код временно не работает (отказ `promo_disabled`) |
| `from` / `until` | окно действия, UTC ISO. Можно опустить |
| `perAccount` | сколько раз может активировать один аккаунт (`0` = без лимита) |
| `globalLimit` | общий потолок активаций (`0` = без потолка) |
| `tag` | тег игрока в PlayFab — сегментация + вечный статус (инвентарь его не теряет) |
| `titleKey` | ключ локализации для заголовка попапа награды |
| `reward` | `currencies` / `cards` / `boosters` / `avatars` — как в `taskConfig` |

Менять кампании можно **на живую**, без передеплоя клиента и ревизии: правишь Title Data — и всё.

### B. Персональные коды бэкеров — нативные купоны PlayFab

Их **не надо** описывать в `promoConfig`: если код там не найден, сервер сам пробует
`server.RedeemCoupon`. Одноразовость и уникальность обеспечивает сам PlayFab.

1. **Заведи предмет-обёртку** в каталоге `main`: купон умеет выдать **ровно один** предмет и
   **не умеет валюту**, поэтому на тир заводится **Bundle** — `bundle_founder_bronze` /
   `_silver` / `_gold`. В бандл кладём бустеры, аватар и валюту. В карточке бандла выставь
   **Expiration = 15 секунд** — бандл развернётся сразу, и игрок увидит содержимое, а не обёртку.
2. **Сгенерируй коды**: Game Manager → **Economy → Catalogs** → вкладка **Bundles** → ссылка
   **Coupons** → указать количество и предмет → **скачать CSV**. Коды вида `65g-d4q5-zph`.
3. **Пропиши тег тира** в `promoConfig.couponTags`: `"bundle_founder_gold": "founder_gold"` —
   тогда активация купона повесит на аккаунт тег статуса.
4. **Разошли коды** бэкерам (у Планеты выгружаешь список спонсоров и делаешь рассылку — публичного
   API у платформы нет, шаг ручной).

Игрок вводит код в **Настройках** → `PromoService.Redeem` → `RedeemPromo` → попап «вы получили».
Обёртку `bundle_*` клиенту не показываем, валюту из бандла сервер вычисляет **диффом кошелька**
до/после (в ответе `RedeemCoupon` её не видно).

### Защита и отладка

- **Анти-перебор**: `maxAttemptsPerHour` (по умолчанию 12) неудачных попыток на аккаунт в час.
- **Общий счётчик** активаций — Shared Group Data `promo_counters`; при гонке возможен перелив на
  пару активаций, для потолков в тысячи это неважно.
- **Дев-меню (F2) → ПРОМОКОД**: ввести код и «Забыть активированные» — сбрасывает только свои
  кампании из `promoConfig`. Нативный купон сгорает в сервисе, повторно его не активировать —
  для теста генерь новую пачку купонов.
- Коды отказа для UI: `promo_unknown`, `promo_used`, `promo_expired`, `promo_not_started`,
  `promo_disabled`, `promo_limit`, `promo_throttled` (тексты — `UIStrings.BackendReason`).

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
| Промокоды | `RedeemPromo`, `DevResetPromo` | ✅ (нужен Title Data `promoConfig`) |

## Заметки по масштабированию

- **Аукцион**: индекс листингов для MVP — Shared Group Data внутри PlayFab; при росте вынести во
  внешнюю БД. Escrow — `server.RevokeInventoryItem` (карта уходит у продавца при листинге).
- **Копии карт в v1** = отдельные ItemInstance. Для коллекции в сотни карт норм; при тысячах —
  рассмотреть переход на Economy v2 (тогда вернётся вариант с Azure/HTTP).
- `win_games` в P2P (Photon) — self-report; серверную валидацию исхода добавить позже.
