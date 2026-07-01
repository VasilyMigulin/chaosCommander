 # Локализация карт — настройка

Система текста карт: **локализация + шаблонизация описаний** (динамические числа `*N*`,
авто-болд ключевых фраз, авто-суффикс длительности чар).

Код уже на месте и компилируется **без** настроенных таблиц — до настройки всё показывается
по-русски (фоллбэк `DefaultCardTextProvider`). Чтобы заработал перевод, нужно один раз создать
ассеты Unity Localization (это нельзя сделать из скрипта надёжно — делается в UI редактора).

## 1. Дождаться импорта пакета
В `Packages/manifest.json` добавлен `com.unity.localization`. Откройте проект — Package Manager
подтянет пакет (и его зависимость Addressables). После импорта определится символ
`UNITY_LOCALIZATION_PRESENT`, и подключатся сборки `Game.Core.Localization(.Editor)`.

## 2. Создать локали и таблицу (один раз, в редакторе)
1. `Edit → Project Settings → Localization` → **Create** (создаст `LocalizationSettings`).
2. `Window → Asset Management → Localization Tables`:
   - **Locale Generator** → отметить **Russian (ru)** и **English (en)** → Add Locales.
   - Сделать **ru** дефолтным (Project Settings → Localization → Default/Specific Locale = ru).
   - **New Table Collection** → тип **String Table Collection**, имя строго **`CardText`** → Create.

## 3. Залить тексты карт
Меню **`Tools → Localization`**:
- **Import full CardText CSV (key;ru;en, creates keys)** — РЕКОМЕНДУЕМЫЙ путь. Берёт уже готовый
  `Assets/Localization/card_text.csv` (RU в новом формате `*N*` + английский, заполнен заранее),
  создаёт ключи и заливает обе колонки. Один клик — и таблица готова.
- **Build CardText table (RU from assets)** — альтернатива: пройдёт по всем `CardInstanceData`,
  создаст ключи `card.{expansionId}.{id}.name`/`.desc` и зальёт русский из авторских `Name`/`Description`
  (старый формат из ассетов). EN — пустой.
- **Export / Import EN CSV** — для последующего перевода через `card_text_ru.csv`.

> `card_text.csv` — источник-правды для таблицы (RU-шаблоны + EN). Ассеты карт правкой описаний
> НЕ трогаются: рантайм при наличии ключа берёт таблицу, ассет остаётся лишь фоллбэком. Если хотите
> привести и инспектор ассетов к новому виду — это отдельная задача (скриптом по `*.asset`).

> Авторинг карт не меняется: вы как и раньше пишете русский прямо в `Description` ассета —
> это источник-правды для `ru` и фоллбэк. Перевод — слой сверху.

## 4. Как писать описания (шаблон)
- **Динамическое число:** `*N*`, где `N` — базовое значение. Пример: `Нанесите *1* урона оппоненту`.
  Звёздочки в игре не показываются; число берётся из эффекта (i-й `*N*` ↔ i-й эффект-значение
  по порядку способностей/эффектов: `DealDamage`, `Heal`, `BuffStats(atk,hp,speed)`, `GainMana`,
  `GainGold`, `Draw`, `DeathTimer`). Под будущие модификаторы урона число подменится само.
- **Обычное число без звёздочек** остаётся статикой (его не трогаем).
- **Ключевые фразы** («При разыгрывании», «В конце хода», «При смерти» и т.п.) **автоматически**
  становятся жирными — пишите их обычным текстом. Полный список — в `CardTextLocalization`.
- **Чары:** строку длительности **дописывать не нужно** — она добавляется автоматически из
  `CardCharmModel.TurnsAlive`: `0` → «До конца матча», иначе «Действует N ход(а/ов)».

## 5. Архитектура (где что лежит)
- `Shared/Interface/Ability/IDynamicValue.cs` — эффект отдаёт число(а) для `*N*`.
- `Shared/Card/CardTextLocalization.cs` — шлюз локализации + RU/EN-таблицы ключевых фраз и склонений.
- `Shared/Card/CardDescriptionFormatter.cs` — проходы: локализация → `*N*` → болд → суффикс чар.
- `ECS/Systems/Card/CardDynamicValues.cs` — сбор живых значений эффектов карты (для ре-рендера
  под модификаторы).
- `Localization/UnityLocalizationCardTextProvider.cs` — бэкенд на Unity Localization (подключается
  на старте, только при наличии пакета).
- `Localization/Editor/CardLocTableBuilder.cs` — тулы наполнения таблицы.
- Точки применения форматтера: `CardModel.Init` (in-battle, `CardViewDataComponent`) и
  `CardVisualDataFactory.From` (библиотека/колодостроитель).

## 6. Живой ре-рендер под модификаторы (когда появятся)
Сейчас числа берутся базовыми (на момент `Init`). Когда добавите механику вроде «все заклинания
+1 урона», сделайте ре-рендер по образцу `CardCostChangedEvent`: соберите значения
`CardDynamicValues.Collect(world, cardEntity, playerEntity)` и вызовите
`CardDescriptionFormatter.Format(descKey, template, cardType, turns, live)`, затем обновите текст
во вьюшке. `IDynamicValue.GetDynamicValue` уже должен читать те же модификаторы, что и `Apply`.
