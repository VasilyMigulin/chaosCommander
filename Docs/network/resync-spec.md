# Спецификация self-heal ресинка (снэпшот мира)

> **СТАТУС v1 РЕАЛИЗОВАН (2026-07-27):** WorldSnapshotData (Network/), WorldStateHash + WorldResyncSystem
> (ECS/Systems/Network/), ResyncBus (Network/), RPC_RequestWorldResync + RPC_WorldSnapshotChunk
> (PhotonRunHandler), ResetCardEntityMaps (IGameStateContext/BattleState), ResyncFadeView (UI/Feature/Battle).
> В v1 из корзины A НЕ вошло (осознанно, ловится контрольной чексуммой как «не сошлась»): TrackedBuffs
> (IBuffable), CastMultiplierService, AbilityResolveCounter/латчи/OnMatchStartTrigger._fired, цветовые маски,
> TempControl/OriginalOwner, MatchTracker, ActiveState/TimeRemaining (ресинкается только пассив — его
> turn-стейтов и нет). Границы v2 — по списку ниже.

Инвентаризация всего игрового состояния для DTO снэпшота (фаза 3 сетевой устойчивости).
Дизайн: при десинке (расхождение чексумм `TurnChecksumSystem`) или реконнекте АКТИВНЫЙ клиент
(симулятор = авторитет) отдаёт снэпшот мира; пассив применяет его ПОД ЗАТЕМНЕНИЕМ экрана
(фейд → пересборка сущностей/вьюх → проявление), после применения обе стороны пересчитывают
чексумму (самоконтроль). Транспорт — чанкер `SendDeckSnapshotChunked`/`RPC_SyncDeckSnapshotChunk`.

Итог по объёму: из ~250 struct-компонентов в снэпшот попадает ~45-50 типов, схлопываясь в ~32 поля
DTO. Вес снэпшота — десятки КБ разово (не поток). Главная работа — полнота, ниже — полный перечень.

---

## A. ЗЕРКАЛИРУЕМОЕ → в снэпшот

### A1. Запись карты (PK = NetworkEntityComponent.NetworkEntityKey)
- `CardModelComponent`: ТОЛЬКО `ExpansionId` + `ModelId` (остальное восстановит `CardModel.Init` из CardConfig).
- `OwnerComponent.OwnerId` (абсолютный PlayerId; `Own/EnemyCardTag` НЕ слать — клиент-относительные,
  восстанавливаются сравнением с локальным игроком, как `CreateCardSystem` по `IsEnemy`).
- `OriginalOwnerComponent.OwnerId` (если есть; ставится при первом TakeControl, не пересчитывается).
- Зона: `Hand/Deck/Board/Grave/LimboTag` → один enum-байт.
- `BoardPositionComponent`: Row, Col, OwnerId.
- `AttackComponent`: `Base`, `Modifiers[]`, `ModifiersPermanent[]` (`Value` производный — RecalculateValue).
  ⚠️ Списки РАЗДЕЛЬНО: ClearModifiers при смерти чистит только мягкие — склейка = отложенный десинк.
- `HealthComponent`: `Current`, `BaseMax`, `Modifiers[]`, `ModifiersPermanent[]` (`Max` производный).
- `SpeedComponent`: `BaseMax`, `Remaining`, `Modifiers[]`, `ModifiersPermanent[]` (⚠️ `Remaining` обязателен —
  в чексумму намеренно не входит, но при ресинке восстановить нечем).
- Cost (Gold/Mana/HealthCost — ровно один): тег типа + `Base`, `Modifiers[]`, `ModifiersPermanent[]`.
- `AttacksUsedComponent.Value`.
- Таймеры: `CharmTimerComponent.TurnsRemaining`, `CreatureTimerComponent.TurnsRemaining` (это и есть
  death-timer), `DurationAuraComponent.TurnsRemaining`, `CommanderCooldownComponent.TurnsRemaining`.
- `CommanderHandTracker.WasInHand` (иначе RunCommanderCooldownSystem навесит ложный кулдаун).
- `GeneratedCardCounterComponent.Next` — ⚠️ КРИТИЧНО: из него ключи порождённых `{casterKey}~gen{N}`;
  потеря → коллизия ключей → новый невоспроизводимый десинк.
- `TempControlledComponent`: `OriginalOwnerId`, `ExpiresOnPlayerId`, `TurnsRemaining`
  (`OriginalWasOwn` НЕ слать — клиент-относительное).
- Цветовые теги (Red..Black) → битовая маска (мутируются AddColor/RemoveColor — из Element не выводятся).
- `AppliedBuffsComponent.Records[]` → `{TargetNetKey, Atk, Hp, Speed}` (реверт-лист tracked-аур;
  пересчёта НЕТ по дизайну — обязателен в снэпшоте).
- `TrackedBuffsComponent.Items[]` — ⚠️ САМОЕ ПРОБЛЕМНОЕ: `IBuffable` полиморфен. Решение: слать
  `(sourceAbilityIndex, targetNetKey)` и пере-получать IBuffable из пересозданной способности источника
  (по индексу эффекта в контейнере), НЕ сериализовать объект.
- `BuffPerCharmComponent.AppliedAttack/AppliedHealth` (⚠️ забыть = удвоение баффа первым тиком).
- `DeadTag` (страховка «умер, но не убран»).
- НЕ слать (ставит CardModel.Init из ассета): Token/Commander/Spell/Creature/CharmTag, редкость,
  MulliganModifier, SummonVfx, Archetype, CardViewData, ViewRef.

### A2. Запись ability (адрес = cardNetKey + AbilityIndex)
- `AbilityResolveCounterComponent.Count` (SelfResolves — Нечищенный источник).
- `AbilityOriginComponent.OriginOwnerId` (грант чужой карте).
- `TakeDamageReceivedLatch.Accumulated` + `TakeDamageReceivedLatchedTag` (rule-латч, живёт до конца матча).
- `OnMatchStartTrigger._fired` — единственное поле-состояние ВНУТРИ DeepClone-инстанса триггера;
  теряется при пересоздании → выстрелит повторно. Гасить глобальным флагом «матч начат» или в DTO.
- Остальное (контейнеры/таргетинг/VFX/gate) кладёт Ability.Init из ассета — НЕ слать.
- Условия (AbilityConditions) все пересчитываемые; TakeDamageCondition читает MatchCounterComponent →
  безопасно, если тот в снэпшоте.

### A3. Запись игрока (PK = PlayerId)
`GoldComponent{Current,Max}`, `ManaComponent{Current,Max}`, Health аватара (Current+BaseMax+списки),
`HandComponent.CardEntities` → `string[] HandKeys` ПО ПОРЯДКУ (командир = [0]),
`DeckComponent.CardEntities` → `string[] DeckKeys` ПО ПОРЯДКУ ([0] = верх колоды; кладбище порядка не
имеет — только GraveTag), `TurnCounterComponent.Personal`, `MatchCounterComponent` ЦЕЛИКОМ (все словари +
SpellsPlayedLog/CharmsPlayedLog), `LastPlayedSpellComponent`, `LastDamageTakenComponent`,
`CostModifierComponent.Amount`, `ManaFloorComponent.Floor`, `TemporaryManaComponent.RefundAmount`,
`DrawReplacementComponent{LookCount,DestroyChosen}`, маркеры GoldBlock/ReflectDamage,
`TurnResourcesGrantedTag`, `PlayerSideComponent.Side`. НЕ слать: Mulligan (локален), IsLocalPlayer/
Local/Remote/AiPlayer (клиент-относительные, ставит InitPlayerSystem).

### A4. Глобальное
- Номер хода + PlayerId активного + `ActiveState.TimeRemaining`.
- `MatchState.IsOver`.
- `CastMultiplierService`: `_extra` + `_temp` приватны — ДОПИСАТЬ API дампа/restore.
- `MatchTracker` (статик, дублирует MatchCounterComponent) — проверить реальных читателей; если
  некритичны — не сериализовать.

## B. ЛОКАЛЬНОЕ → пересоздаётся после применения
Вьюхи (ViewRef/ViewSpawned/Avatar/Animator/Transform), анимационные гейты (Moving/AttackAnimPending/
Pending*), подсветки/ввод (Select/Target/Playable/Input/LockState), Own/EnemyCardTag (из OwnerId vs
локальный), Local/Remote/AiPlayer, CardTierComponent.Announced, PathMove, AiTargetPreference.

## C. ТРАНЗИЕНТНОЕ → очистить перед применением
Одно-кадровые *Event-компоненты (~26 типов), пайплайн способностей (Cast/Targeting/Queued/Chain/
Pending*/Discover*, Start/EndTurnState), скрэтч-статики (SummonScratch, GeneratedModScratch,
ChainContext, AbilityResolveContext, GeneratedCardChannel, CardPickReplayStore), `ActionQueue` —
ОБЯЗАТЕЛЬНО продренировать (протухшие действия поверх снэпшота = новый десинк).
`CardTierComponent.CurrentTier` — выводится из золота (CardTierSystem пересчитает).

## Применение снэпшота на приёмнике (порядок)
1. Затемнение экрана (общий фейд с реконнектом), блок ввода.
2. Очистка: ActionQueue + скрэтчи + все транзиенты; снести все сущности карт и вьюхи.
3. ⚠️ `BattleState._localKeyMap/_netKeyMap/_netLocalMap` — ОЧИСТИТЬ (сейчас AddEntity с ContainsKey-гардом
   молча НЕ перезаписывает → протухшие EcsPackedEntity; в IGameStateContext нужен ResetEntityMaps()).
   `_localKeyMap` ключуется entity.ToString() — после пересборки id переиспользуются.
4. Пересоздать карты по (exp,id,netKey) через CreateCardEvent с флагом `Resync`: подавить
   CreatureInvokedEvent{Generated} (иначе накрутит счётчики/разбудит ауры) и RegisterInZone
   (порядок зон восстанавливаем ПРЯМЫМ присвоением списков из DTO, не DeckShuffleUtil).
5. Применить состояние поверх Init: статы/списки модификаторов/таймеры/зоны/позиции.
6. Восстановить ability-состояние (ResolveCounter/латчи/Applied/TrackedBuffs через индекс эффекта).
7. Перерегистрировать мапы (PLAYER_ENTITY/OPPONENT_ENTITY + все netKey).
8. Дать кадр системам (CardTierSystem/BuffPerCharm и т.п. осядут), пересчитать контрольную чексумму,
   сверить с активом. Совпала → фейд-ин; нет → лог + повтор/сдача.

## Топ-риски (по убыванию)
1. TrackedBuffs полиморфный IBuffable → схема «ссылка на эффект источника».
2. GeneratedCardCounter.Next → коллизии gen-ключей.
3. Порядок колоды: чексумма хеширует зоны МНОЖЕСТВОМ (порядок не детектится — это осознанно, реплей
   доборов идёт по явным ключам DrawnKeys и порядок клиентов может легитимно дрейфовать); снэпшот
   НАВЯЗЫВАЕТ порядок актива обоим — этим и чинится.
4. Мапы BattleState без очистки → молчаливые промахи TryGetEntity после ресинка.
5. Раздельность мягких/перм модификаторов.
6. BuffPerCharm.Applied* (удвоение).
7. OnMatchStartTrigger._fired (повторный выстрел).
8. Недренированный ActionQueue.
