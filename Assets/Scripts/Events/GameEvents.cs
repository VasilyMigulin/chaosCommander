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

    public struct CardDiedEvent : IGameEvent
    {
        public int CardEntity;
        public int PlayerId;
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

    public struct CreatureDiedEvent : IGameEvent
    {
        public int CreatureEntity;
        public int PlayerId;
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
    }
    public struct PlayerAssignedEvent : IGameEvent
    {
        public int PlayerEntity;
        public int Side;         // 1 РёР»Рё 2
        public bool IsLocalPlayer;
    }

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
         
        public int OwnerId;
         
        public bool IsEnemy;

        public bool IsCommander;

        public bool InHand;
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

