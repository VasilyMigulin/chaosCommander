using Game.Core.Service;
using Game.Core.Shared;

namespace Game.Core.Events
{
    // в”Ђв”Ђ Turn events в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
    public struct TurnStartedEvent : IGameEvent
    {
        public int ActivePlayerId;
        public int TurnNumber;
    }

    public struct TurnEndedEvent : IGameEvent
    {
        public int ActivePlayerId;
    }

    public struct InputBlockedEvent : IGameEvent { }
    public struct InputRestoredEvent : IGameEvent { }

    public struct CardPlayedEvent : IGameEvent
    {
        public string CardName;
        public int CardEntity;
        public int PlayerId;
        public int TargetCell;   // board cell index, -1 if none
        public int TargetEntity; // entity target, -1 if none
    }

    public struct CardDrawnEvent : IGameEvent
    {
        public int CardEntity;
        public int PlayerId;
    }

    /// <summary>Локальный (turn-start) добор Count карт игроком-сущностью PlayerEntity. Шлёт DrawCardSystem
    /// только для Sync-доборов → CollectActionSystem отправляет ActionDrawData оппоненту (синк зеркала колоды/руки).</summary>
    public struct DeckDrawNetEvent : IGameEvent
    {
        public int PlayerEntity;
        public int Count;
        /// <summary>Конкретные добранные сущности (локальные). Коллектор шлёт их ключи в ActionDrawData,
        /// пассив переносит в руку ИМЕННО эти карты по ключу (а не «верхние N» — иначе дрейф зеркала руки).</summary>
        public int[] DrawnEntities;
    }

    public struct CardDiedEvent : IGameEvent
    {
        public int CardEntity;
        public int PlayerId;
    }

    /// <summary>Тех-кнопка: выдать локальному игроку максимум маны и золота. Обрабатывает DebugCheatSystem.</summary>
    public struct DebugFillResourcesEvent : IGameEvent { }

    /// <summary>
    /// Замена добора (Адовый червь) разрешена активным игроком: выбрана карта из предложенных.
    /// CollectActionSystem шлёт это пассиву как ActionDrawReplacementData (offered/chosen — по ключам),
    /// пассив повторяет результат у оппонента. Публикует RunDrawReplacementSystem при резолве.
    /// </summary>
    public struct DrawReplacementResolvedNetEvent : IGameEvent
    {
        public int PlayerEntity;
        public int[] Offered;
        public int Chosen;
        public bool DestroyChosen;
    }

    /// <summary>ЛИПКИЙ хинт выбора цели для UI: Show=true при входе в режим выбора цели по доске (висит,
    /// пока выбираешь), Show=false при выборе/отмене. Публикует RunAbilityTargetSelectionSystem; показывает
    /// TargetHintView. Text — что показать («Выберите цель»).</summary>
    public struct TargetSelectionHintEvent : IGameEvent
    {
        public bool Show;
        public string Text;
    }
    // в”Ђв”Ђ Hand UI events в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ

    /// <summary>
    /// РР·РјРµРЅРёР»Р°СЃСЊ РґРѕСЃС‚СѓРїРЅРѕСЃС‚СЊ РєР°СЂС‚С‹ РґР»СЏ СЂРѕР·С‹РіСЂС‹С€Р° (С…РІР°С‚Р°РµС‚/РЅРµ С…РІР°С‚Р°РµС‚ СЂРµСЃСѓСЂСЃРѕРІ).
    /// UI РІРєР»СЋС‡Р°РµС‚/РІС‹РєР»СЋС‡Р°РµС‚ С…Р°Р№Р»Р°Р№С‚РµСЂ "РјРѕР¶РЅРѕ СЂР°Р·С‹РіСЂР°С‚СЊ".
    /// </summary>
    public struct CardAffordableChangedEvent : IGameEvent
    {
        public int  CardEntity;
        public bool IsAffordable;
    }

    public struct CardAbilityReadyChangedEvent : IGameEvent
    {
        public int  CardEntity;
        public bool IsReady;
    }

    /// <summary>UI: эффективная стоимость карты изменилась (модификатор стоимости — Гиперинфляция).
    /// Шлёт CardAffordabilitySystem; ловит PlayCardView → обновляет число стоимости.</summary>
    public struct CardCostChangedEvent : IGameEvent
    {
        public int CardEntity;
        public int EffectiveCost;
    }

    /// <summary>Глобальный модификатор стоимости карт изменился (AddCostModifierEffect). Сигнал для
    /// CardAffordabilitySystem пересчитать affordability + эффективную стоимость карт руки.</summary>
    public struct CostModifierChangedEvent : IGameEvent { }

    /// <summary>
    /// Публикуется PlayCardView когда карта физически отображена в руке (SetCard вызван).
    /// CardAffordabilitySystem реагирует и публикует актуальное состояние доступности.
    /// </summary>
    public struct CardPlacedInHandViewEvent : IGameEvent
    {
        public int CardEntity;
    }

    public struct CardAddedToHandUIEvent : IGameEvent
    {
        public int    CardEntity;
        public int    PlayerId;
        public string NetworkKey;
        public UnityEngine.Sprite Icon;
        public Game.Core.Service.EnumService.CardType CardType;
        public Game.Core.Service.EnumService.Element  Element;
        public Game.Core.Service.EnumService.Rarity   Rarity;
        public string CardName;
        public bool   IsCommander; 
        public Game.Core.Shared.CardVisualData Visual;
    } 
    public struct CardRemovedFromHandUIEvent : IGameEvent
    {
        public int CardEntity;
    }
     
    public struct CardDiscardFromHandUIEvent : IGameEvent
    {
        public int CardEntity;
        public string CardName;
        public UnityEngine.Sprite Icon;
        public Game.Core.Shared.CardVisualData Visual;
    }

    /// <summary>Токен с OnDraw-форс-кастом (Вонючее облако) авто-разыгран сразу при доборе. Чистая косметика
    /// у владельца: CardLayout показывает пришедшую карту и проигрывает анимацию сброса («пришла → ушла»).
    /// Сама симуляция розыгрыша идёт отдельно (PlayCardUtil.Play). CardLayout берёт на себя удаление слота,
    /// поэтому штатное CardRemovedFromHandUIEvent для этой карты игнорируется.</summary>
    public struct CardForcePlayedFromDrawUIEvent : IGameEvent
    {
        public int CardEntity;
    }

    /// <summary>
    /// Карта верхнего слоя колоды локального игрока ушла в кладбище (Mill).
    /// UI должен на короткое время показать какую карту сожгли.
    /// </summary>
    public struct CardMillFromDeckUIEvent : IGameEvent
    {
        public int CardEntity;
        public string CardName;
        public UnityEngine.Sprite Icon;
        public Game.Core.Shared.CardVisualData Visual;
    }
     
    public struct CommanderOnCooldownUIEvent : IGameEvent
    {
        public int CardEntity;
        public int CooldownTurns;
    }
     
    public struct CommanderCooldownExpiredUIEvent : IGameEvent
    {
        public int CardEntity;
    }
     public struct CreatureMovedEvent : IGameEvent
     {
         public int CreatureEntity;
         public int FromRow;
         public int FromCol;
         public int FromOwnerId;
         public int ToRow;
         public int ToCol;
         public int ToOwnerId;
     }

    public struct CreatureAttackedEvent : IGameEvent
    {
        public int AttackerEntity;
        public int DefenderEntity;
    }

    public struct CreatureDamagedEvent : IGameEvent
    {
        public int CreatureEntity;
        public int Amount;
    }

    public struct AbilityActivatedEvent : IGameEvent
    {
        public int SourceEntity;
        public int AbilityIndex;
    }

    public struct AbilityCascadeStartedEvent : IGameEvent { }
    public struct AbilityCascadeEndedEvent : IGameEvent { }

    public struct ResourceChangedEvent : IGameEvent
    {
        public bool isLocalPlayer;
        public EnumService.ResourceType Type;
        public int NewValue;
        public int MaxValue;
    }
     
    public struct CellSelectedEvent : IGameEvent
    {
        public int Row;
        public int Col;
        public int OwnerId;
    }

    public struct CreatureSelectedEvent : IGameEvent
    {
        public int CreatureEntity;
    }

    public struct CreatureDeselectedEvent : IGameEvent { }

    // ── Драг-розыгрыш существа «под пальцем» (UI ↔ CreatureDragPreviewSystem) ──
    // UI шлёт сырой ввод драга (экранная позиция), система делает всё Mono-осознанное:
    // рейкаст в доску, превью-модель под пальцем, подсветку клетки, коммит/отмену.

    /// <summary>UI → система: палец с картой двигается (каждый кадр драга). Для не-существ система игнорит.</summary>
    public struct CreatureDragMovedEvent : IGameEvent
    {
        public int CardEntity;
        public UnityEngine.Vector2 ScreenPosition;
    }

    /// <summary>UI → система: палец отпущен. Система коммитит размещение (валидная клетка) или шлёт
    /// TargetSelectionCancelledEvent (UI вернёт карту в руку штатным путём).</summary>
    public struct CreatureDragReleasedEvent : IGameEvent
    {
        public int CardEntity;
        public UnityEngine.Vector2 ScreenPosition;
    }

    /// <summary>Система → UI: карта над полем (true → карта растворяется, превью существа видно) /
    /// ушла с поля (false → карта проявляется обратно).</summary>
    public struct CreatureDragOverFieldChangedEvent : IGameEvent
    {
        public int CardEntity;
        public bool OverField;
    }

    /// <summary>Система → UI (один раз на драг): карта — СУЩЕСТВО, размещается только дропом на поле.
    /// UI на отпускании вне поля возвращает её в руку, НЕ уходя в старый клик-путь (OnUse →
    /// PendingSelectCell). Без события (нет системы/борда) UI работает по-старому — graceful fallback.</summary>
    public struct CreatureDragStartedEvent : IGameEvent
    {
        public int CardEntity;
    }

    public struct MulliganStartedEvent : IGameEvent
    {
        public int PlayerEntity;
        public int[] OfferedCardEntities;
        public CardVisualData[] OfferedCardVisuals;
        public int MaxReplacements;
    }

    public struct MulliganCardReplacedEvent : IGameEvent
    { 
        public int[] OldCardEntity;
        public int[] NewCardEntity;
        public CardVisualData[] NewCardVisual;
    }

    public struct MulliganCompletedEvent : IGameEvent
    {
        public int PlayerEntity;
    }

    public struct AllMulligansCompletedEvent : IGameEvent { }

    /// <summary>
    /// Публикуется когда сервер даёт команду инициализировать игровое состояние.
    /// BattleState подписывается на это событие и только тогда инициализирует ECS
    /// и отправляет RPC_NotifyStateReady.
    /// </summary>
    public struct TriggerStateInitEvent : IGameEvent { }

    /// <summary>
    /// Публикуется когда хост начинает PreStart фазу (до начала первого хода).
    /// UI закрывает мулиган и показывает руку с анимацией раздачи карт.
    /// Содержит готовые данные карт руки чтобы UI не лазил в ECS.
    /// </summary>
    public struct PreStartPhaseBeginUIEvent : IGameEvent
    {
        public CardAddedToHandUIEvent[] HandCards;
        public CardAddedToHandUIEvent   CommanderCard;
        public bool HasCommander;
    }

    public struct MulliganReplaceRequestedUIEvent : IGameEvent
    { 
        public int CardEntity;
    }
    
    public struct LocalTurnStartedEvent : IGameEvent
    {
        public int TurnNumber; 
        public float TurnDurationSeconds;
    }
     
    public struct OpponentTurnEndedEvent : IGameEvent { } 
    public struct OpponentCardPlayedUIEvent : IGameEvent
    {
        public string CardName;
        public UnityEngine.Sprite Icon;
        /// <summary>Полные визуальные данные карты — выкат рендерит её как настоящую карту (арт/имя/стоимость/
        /// статы/описание) через CardBaseView. CardName/Icon оставлены как fallback.</summary>
        public Game.Core.Shared.CardVisualData Visual;
    }
    public struct PlayerAssignedEvent : IGameEvent
    {
        public int PlayerEntity;
        public int Side;         // 1 РёР»Рё 2
        public bool IsLocalPlayer;
    }

    /// <summary>Активный игрок завершил ход — коллектор шлёт ActionEndTurnData оппоненту.</summary>
    public struct EndTurnNetEvent : IGameEvent { }

    /// <summary>Итог матча с точки зрения локального игрока.</summary>
    public enum MatchResult { Win, Lose, Draw }

    /// <summary>
    /// Матч завершён: у одного/нескольких игроков HP ≤ 0 после оседания каскада действий.
    /// Публикует GameOverCheckSystem. UI показывает поп-ап результата; турн-системы встают.
    /// </summary>
    public struct MatchEndedEvent : IGameEvent
    {
        public MatchResult LocalResult;
        public bool        IsDraw;
        public int[]       WinnerPlayerIds;
        public int[]       LoserPlayerIds;
    }

    /// <summary>Игрок нажал «выход в меню» в поп-апе результата. Хук для навигации/teardown боя
    /// (корректный дисконнект Photon + смена состояния) — обрабатывается на уровне States.</summary>
    public struct ExitToMenuRequestedEvent : IGameEvent { }

    /// <summary>UI/дев-запрос «завершить ход» — система навесит EndTurnRequestEvent на локального активного.</summary>
    public struct RequestEndTurnUIEvent : IGameEvent { }

    /// <summary>Запрос старта PvE-боя (дев-кнопка оверлея). Обрабатывает MenuState.StartPveBattle:
    /// чисто гасит активный матчмейкинг, ставит PveMode и грузит BattleScene.
    /// EncounterPath — путь энкаунтера в Resources (null/пусто = текущий/последний).</summary>
    public struct PveStartRequestedEvent : IGameEvent { public string EncounterPath; }

    public struct DeckSyncedEvent : IGameEvent
    {
        public int PlayerEntity;
    }

    public struct DeckReadyToSyncEvent : IGameEvent
    {
        public int      PlayerEntity;
        public int      PlayerId;
        public string[] DeckExpansionIds;
        public int[]    DeckCardIds;
        public string[] DeckNetworkKeys;
        public string[] HandExpansionIds;
        public int[]    HandCardIds;
        public string[] HandNetworkKeys;
    }
     
    public struct AbilityReadyEvent : IGameEvent
    { 
        public int AbilityEntity; 
        public int CardEntity;
    }
     
    public struct AbilityNotReadyEvent : IGameEvent
    {
        public int AbilityEntity;
        public int CardEntity;
    }
     
    public struct CardPlayableChangedEvent : IGameEvent
    {
        public int  CardEntity;
        public bool IsPlayable;
    }
     
    public struct CreateCardEvent : IGameEvent
    {
        public string ExpansionId;

        public int CardId;

        public string NetworkEntityKey;

        public int PlayerOwnerEntity;

        public int OwnerId;

        public bool IsEnemy;

        public bool IsCommander;

        public bool InHand;

        /// <summary>Если true — сразу разместить на доске (см. BoardRow/BoardCol/BoardOwnerId).</summary>
        public bool InBoard;
        public int BoardRow;
        public int BoardCol;
        public int BoardOwnerId;

        /// <summary>Если true — сразу отправить на кладбище (для призыва без места).</summary>
        public bool InGrave;

        /// <summary>Если true — после создания добавить сущность в HandComponent/DeckComponent владельца
        /// (PlayerOwnerEntity) и для InHand опубликовать CardDrawnEvent (для локального владельца).
        /// Нужно эффектам-генераторам: обычный CreateCardEvent ставит только зональный тег, но списки
        /// зон не правит. Старые вызовы (мулиган-синк/деки) флаг не ставят → поведение не меняется.</summary>
        public bool RegisterInZoneList;

        /// <summary>Если true — созданную карту разыграть автоматически (Фокус-покус): CreateCardSystem вешает
        /// AutoCastComponent + ForceRandomTargetingComponent, AutoCastSystem форс-кастит её (Free) у активного,
        /// а её таргетинг авто-выбирает цели (Random, как Йогг-Сарон) → без перехвата выбора у игрока.</summary>
        public bool AutoCast;
    }
    // ── Network turn coordination events ────────────────────────────────────

    /// <summary>Активный игрок запрашивает конец хода → летит на хост через RPC.</summary>
    public struct TurnEndRequestedNetEvent : IGameEvent
    {
        public int RequestingPlayerId;
    }

    /// <summary>Этот клиент завершил отработку TurnEnd-способностей → хост ждёт обоих.</summary>
    public struct TurnEndReadyNetEvent : IGameEvent
    {
        public int PlayerId;
    }

    /// <summary>Этот клиент завершил отработку TurnStart-способностей → хост ждёт обоих.</summary>
    public struct TurnStartReadyNetEvent : IGameEvent
    {
        public int PlayerId;
    }

    // ── Target selection events ──────────────────────────────────────────────

    /// <summary>
    /// Публикуется когда игрок отменил выбор цели (кликнул по невалидной клетке).
    /// UI убирает подсветку, карта остаётся в руке.
    /// </summary>
    public struct TargetSelectionCancelledEvent : IGameEvent
    {
        public int CardEntity;
    }

    // ── Card play request (from UI) ──────────────────────────────────────────

    /// <summary>
    /// Публикуется из UI когда игрок кликает по карте в руке.
    /// CardInputSystem читает это событие и либо сразу создаёт CastEvent (карты без цели),
    /// либо добавляет PendingTargetCardComponent на игрока (карты с целью).
    /// </summary>
    public struct CardPlayRequestedEvent : IGameEvent
    {
        public int CardEntity;
    }

    // ── Card play network sync ────────────────────────────────────────────────

    /// <summary>
    /// Публикуется после каста карты для сетевой синхронизации с оппонентом.
    /// </summary>
    public struct CardCastNetSyncEvent : IGameEvent
    {
        public string CardNetworkKey;
        public string TargetNetworkKey; // пусто если цель не entity
        public int    TargetCellIndex;  // -1 если цель не клетка
    }

    /// <summary>
    /// Публикуется на стороне оппонента когда приходит RPC_NotifyCardCast.
    /// RemoteCastSystem берёт этот ивент и создаёт CastEvent по ключам.
    /// </summary>
    public struct RemoteCardCastEvent : IGameEvent
    {
        public string CardEntityKey;
        public string TargetEntityKey; // "" если цели нет
        public int    TargetCell;      // -1 если цель не клетка
    }

    // ── Card pick (discover) events ─────────────────────────────────────────

    /// <summary>
    /// Публикуется когда игроку нужно выбрать карту из предложенных (раскопка).
    /// UI подписывается и показывает панель выбора.
    /// </summary>
    public struct CardPickOfferedEvent : IGameEvent
    {
        /// <summary>Entity карты которую разыгрывает игрок.</summary>
        public int CastingCardEntity;

        /// <summary>Entity игрока-владельца.</summary>
        public int PlayerEntity;

        /// <summary>Entity карт предложенных для выбора.</summary>
        public int[] OfferedCardEntities;

        /// <summary>Визуальные данные предложенных карт (для UI, параллельно OfferedCardEntities).</summary>
        public Game.Core.Shared.CardVisualData[] OfferedCardVisuals;

        /// <summary>Количество предложенных карт.</summary>
        public int OfferedCount;
    }

    /// <summary>
    /// Публикуется UI когда игрок кликнул по одной из предложенных карт.
    /// CardPickSelectionSystem ждёт это событие чтобы зафиксировать выбор.
    /// </summary>
    public struct CardPickChosenEvent : IGameEvent
    {
        /// <summary>Entity карты которую разыгрывает игрок.</summary>
        public int CastingCardEntity;

        /// <summary>Entity выбранной игроком карты.</summary>
        public int ChosenCardEntity;
    }

    /// <summary>
    /// Публикуется когда игрок отменил выбор (закрыл панель без выбора).
    /// CardPickSelectionSystem снимает pending и возвращает карту в руку.
    /// </summary>
    public struct CardPickCancelledEvent : IGameEvent
    {
        public int CastingCardEntity;
    }

    /// <summary>
    /// Публикуется после того как выбор зафиксирован — для сетевой репликации.
    /// Содержит достаточно данных чтобы второй клиент воспроизвёл результат.
    /// </summary>
    public struct CardPickResolvedNetEvent : IGameEvent
    {
        /// <summary>Entity карты которую разыгрывал игрок (локальное).</summary>
        public int CastingCardEntity;

        /// <summary>NetworkEntityKey карты-источника (для сетевой репликации выбора).</summary>
        public string CastingCardNetworkKey;

        /// <summary>Entity выбранной карты (локальное, не для сетки).</summary>
        public int ChosenCardEntity;

        /// <summary>ModelId выбранной карты.</summary>
        public int ChosenCardModelId;

        /// <summary>NetworkEntityKey выбранной карты (существующей или создаваемой из пула).</summary>
        public string ChosenCardNetworkKey;

        /// <summary>true — выбор из пула: оппонент должен создать сущность.</summary>
        public bool CreateFromPool;

        /// <summary>Для CreateFromPool: ExpansionId создаваемой карты.</summary>
        public string ChosenExpansionId;

        /// <summary>Для CreateFromPool: CardId (ModelId) создаваемой карты.</summary>
        public int ChosenCardId;
    }

    /// <summary>
    /// Активный клиент: способность разрешилась (цели выбраны). Публикуется
    /// RunResolveAbilityEffectSystem после ResolveTargets+Shape для СВОИХ карт.
    /// CollectActionSystem ловит и шлёт оппоненту ActionAbilityData со списком целей —
    /// пассивный клиент не ре-резолвит, а применяет эффекты по этим целям.
    /// </summary>
    public struct AbilityResolvedNetEvent : IGameEvent
    {
        /// <summary>Entity карты-источника (локальное).</summary>
        public int SourceCardEntity;

        /// <summary>Индекс способности в AbilityContainerComponent карты.</summary>
        public int AbilityIndex;

        /// <summary>Сущности целей (локальные). CollectActionSystem конвертит в NetworkEntityKey/"PLAYER:{id}".</summary>
        public int[] TargetEntities;

        /// <summary>Сущности, ПРИЗВАННЫЕ этим резолвом (локальные) — чтобы пассив применил к ним
        /// модификаторы призыва (бафф/таймер). CollectActionSystem конвертит в NetworkEntityKey.</summary>
        public int[] SummonedEntities;

        // ── Цепочки (составные способности): шаг + контекст для пассив-реплея стадии ──
        /// <summary>Индекс стадии цепочки (0 для обычных способностей).</summary>
        public int StepIndex;
        /// <summary>Накопленный KilledCount к моменту стадии (контекст для CountSource=ChainKilled).</summary>
        public int KilledCount;
        /// <summary>Идентичности случайно-сгенерированных карт стадии (GainRandomCardEffect) — пассив
        /// воспроизводит ИХ вместо своего ролла. Параллельные массивы exp/cardId.</summary>
        public string[] GeneratedExpansionIds;
        public int[] GeneratedCardIds;
    }

    /// <summary>
    /// Существо погибло от СИСТЕМНОГО таймера («умрёт через N ходов») на активе. Не воспроизводится
    /// детерминированно у пассива (он не тикает чужой таймер в чужой ход) → CollectActionSystem
    /// шлёт это как ActionDeathData, пассив вешает DeadTag. Публикует CreatureTimerTickSystem.
    /// </summary>
    public struct TimerDeathNetEvent : IGameEvent
    {
        public int CreatureEntity;
    }

    /// <summary>
    /// Временный контроль над существом истёк на активе (контролёр дотикал TurnsRemaining в конце своего хода).
    /// Актив откатил владельца локально → CollectActionSystem шлёт ActionControlRevertData, пассив повторяет
    /// откат по ключу. Публикует TempControlRevertSystem. (Аналог TimerDeathNetEvent: считает только актив.)
    /// </summary>
    public struct ControlRevertedNetEvent : IGameEvent
    {
        public int CreatureEntity;
    }

    /// <summary>
    /// Публикуется на стороне оппонента когда приходит RPC_NotifyCreatureMove.
    /// RemoteCreatureMoveSystem добавит MoveRequestEvent на нужную сущность.
    /// </summary>
    public struct RemoteCreatureMoveEvent : IGameEvent
    {
        public string CreatureEntityKey;
        public int    ToRow;
        public int    ToCol;
        public int    ToOwnerId;
    }

    /// <summary>
    /// Публикуется на стороне оппонента когда приходит RPC_NotifyCreatureAttack.
    /// RemoteCreatureAttackSystem добавит AttackRequestEvent на нужную сущность.
    /// </summary>
    public struct RemoteCreatureAttackEvent : IGameEvent
    {
        public string AttackerEntityKey;
        public string DefenderEntityKey;
    }
}

