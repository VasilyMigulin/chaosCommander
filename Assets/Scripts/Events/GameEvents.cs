using Game.Core.Service;

namespace Game.Core.Events
{
    // ── Turn events ──────────────────────────────────────────────────────────
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

    // ── Card events ──────────────────────────────────────────────────────────
    public struct CardPlayedEvent : IGameEvent
    {
        public string CardName;
        public int CardEntity;
        public int PlayerId;
        public int TargetCell; // board cell index, -1 if none
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

    // ── Hand UI events ───────────────────────────────────────────────────────

    /// <summary>
    /// Изменилась доступность карты для розыгрыша (хватает/не хватает ресурсов).
    /// UI включает/выключает хайлайтер "можно разыграть".
    /// </summary>
    public struct CardAffordableChangedEvent : IGameEvent
    {
        public int  CardEntity;
        public bool IsAffordable;
    }

    /// <summary>
    /// Изменилось состояние способности карты (ReadyTag появился/исчез).
    /// UI включает/выключает хайлайтер "способность активна".
    /// </summary>
    public struct CardAbilityReadyChangedEvent : IGameEvent
    {
        public int  CardEntity;
        public bool IsReady;
    }

    /// Публикуется когда карта входит в руку локального игрока.
    /// UI подписывается чтобы показать карту в CardLayout.
    /// </summary>
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
    }

    /// <summary>
    /// Публикуется когда карта разыграна или убрана из руки.
    /// UI скрывает соответствующий PlayCardView.
    /// </summary>
    public struct CardRemovedFromHandUIEvent : IGameEvent
    {
        public int CardEntity;
    }

    /// <summary>
    /// Командир ушёл на перезарядку (умер на поле).
    /// UI показывает иконку КД на CommanderCardView.
    /// </summary>
    public struct CommanderOnCooldownUIEvent : IGameEvent
    {
        public int CardEntity;
        public int CooldownTurns;
    }

    /// <summary>
    /// КД командира истёк — можно снова разыгрывать.
    /// </summary>
    public struct CommanderCooldownExpiredUIEvent : IGameEvent
    {
        public int CardEntity;
    }

    // ── Creature events ──────────────────────────────────────────────────────
    public struct CreatureMovedEvent : IGameEvent
    {
        public int CreatureEntity;
        public int FromRow;
        public int FromCol;
        public int ToRow;
        public int ToCol;
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

    // ── Ability events ───────────────────────────────────────────────────────
    public struct AbilityActivatedEvent : IGameEvent
    {
        public int SourceEntity;
        public int AbilityIndex;
    }

    public struct AbilityCascadeStartedEvent : IGameEvent { }
    public struct AbilityCascadeEndedEvent : IGameEvent { }

    // ── Resource events ──────────────────────────────────────────────────────
    public struct ResourceChangedEvent : IGameEvent
    {
        public int PlayerId;
        public EnumService.ResourceType Type;
        public int NewValue;
        public int MaxValue;
    }

    // ── Board / selection events (UI) ─────────────────────────────────────────
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

    // ── Mulligan events ───────────────────────────────────────────────────────
    public struct MulliganStartedEvent : IGameEvent
    {
        public int PlayerEntity;
        public int[] OfferedCardEntities;
        public int MaxReplacements;
    }

    public struct MulliganCardReplacedEvent : IGameEvent
    {
        public int PlayerEntity;
        public int OldCardEntity;
        public int NewCardEntity;
    }

    public struct MulliganCompletedEvent : IGameEvent
    {
        public int PlayerEntity;
    }

    public struct AllMulligansCompletedEvent : IGameEvent { }

    // ── Match setup events ────────────────────────────────────────────────────
    public struct PlayerAssignedEvent : IGameEvent
    {
        public int PlayerEntity;
        public int Side;         // 1 или 2
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

    // ── Ability condition events ──────────────────────────────────────────────

    /// <summary>
    /// Публикуется когда все условия способности выполнены и на неё навешивается ReadyTag.
    /// UI подписывается чтобы показать/скрыть индикатор готовности на карте.
    /// </summary>
    public struct AbilityReadyEvent : IGameEvent
    {
        /// <summary>ECS entity способности которая стала готова.</summary>
        public int AbilityEntity;

        /// <summary>ECS entity карты-владельца.</summary>
        public int CardEntity;
    }

    /// <summary>
    /// Публикуется когда условие способности перестало выполняться (ReadyTag снят).
    /// </summary>
    public struct AbilityNotReadyEvent : IGameEvent
    {
        public int AbilityEntity;
        public int CardEntity;
    }

    // ── Card creation events ──────────────────────────────────────────────────

    /// <summary>
    /// Запрос на создание ECS entity карты оппонента из сетевого снэпшота.
    /// Публикуется PhotonRunHandler при получении RPC_SyncDeckSnapshot.
    /// CreateCardSystem читает CardConfig (инжектирован) и вызывает CardModel.InitAndGetEntity.
    /// </summary>
    public struct CreateCardEvent : IGameEvent
    {
        /// <summary>ExpansionConfig.ExpansionId для поиска в CardConfig.</summary>
        public string ExpansionId;

        /// <summary>CardModel.Id внутри экспансии.</summary>
        public int CardId;

        /// <summary>NetworkEntityKey для привязки entity.</summary>
        public string EntityKey;

        /// <summary>PlayerId владельца (оппонент).</summary>
        public int OwnerId;

        /// <summary>Карта принадлежит оппоненту — навесить EnemyCardTag вместо OwnCardTag.</summary>
        public bool IsEnemy;
    }
}
