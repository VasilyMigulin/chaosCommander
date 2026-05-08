using Game.Core.Service;

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

    // в”Ђв”Ђ Card events в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
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

    /// <summary>
    /// РР·РјРµРЅРёР»РѕСЃСЊ СЃРѕСЃС‚РѕСЏРЅРёРµ СЃРїРѕСЃРѕР±РЅРѕСЃС‚Рё РєР°СЂС‚С‹ (ReadyTag РїРѕСЏРІРёР»СЃСЏ/РёСЃС‡РµР·).
    /// UI РІРєР»СЋС‡Р°РµС‚/РІС‹РєР»СЋС‡Р°РµС‚ С…Р°Р№Р»Р°Р№С‚РµСЂ "СЃРїРѕСЃРѕР±РЅРѕСЃС‚СЊ Р°РєС‚РёРІРЅР°".
    /// </summary>
    public struct CardAbilityReadyChangedEvent : IGameEvent
    {
        public int  CardEntity;
        public bool IsReady;
    }

    /// РџСѓР±Р»РёРєСѓРµС‚СЃСЏ РєРѕРіРґР° РєР°СЂС‚Р° РІС…РѕРґРёС‚ РІ СЂСѓРєСѓ Р»РѕРєР°Р»СЊРЅРѕРіРѕ РёРіСЂРѕРєР°.
    /// UI РїРѕРґРїРёСЃС‹РІР°РµС‚СЃСЏ С‡С‚РѕР±С‹ РїРѕРєР°Р·Р°С‚СЊ РєР°СЂС‚Сѓ РІ CardLayout.
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
    /// РџСѓР±Р»РёРєСѓРµС‚СЃСЏ РєРѕРіРґР° РєР°СЂС‚Р° СЂР°Р·С‹РіСЂР°РЅР° РёР»Рё СѓР±СЂР°РЅР° РёР· СЂСѓРєРё.
    /// UI СЃРєСЂС‹РІР°РµС‚ СЃРѕРѕС‚РІРµС‚СЃС‚РІСѓСЋС‰РёР№ PlayCardView.
    /// </summary>
    public struct CardRemovedFromHandUIEvent : IGameEvent
    {
        public int CardEntity;
    }

    /// <summary>
    /// РљРѕРјР°РЅРґРёСЂ СѓС€С‘Р» РЅР° РїРµСЂРµР·Р°СЂСЏРґРєСѓ (СѓРјРµСЂ РЅР° РїРѕР»Рµ).
    /// UI РїРѕРєР°Р·С‹РІР°РµС‚ РёРєРѕРЅРєСѓ РљР” РЅР° CommanderCardView.
    /// </summary>
    public struct CommanderOnCooldownUIEvent : IGameEvent
    {
        public int CardEntity;
        public int CooldownTurns;
    }

    /// <summary>
    /// РљР” РєРѕРјР°РЅРґРёСЂР° РёСЃС‚С‘Рє вЂ” РјРѕР¶РЅРѕ СЃРЅРѕРІР° СЂР°Р·С‹РіСЂС‹РІР°С‚СЊ.
    /// </summary>
    public struct CommanderCooldownExpiredUIEvent : IGameEvent
    {
        public int CardEntity;
    }

    // в”Ђв”Ђ Creature events в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
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

    // в”Ђв”Ђ Ability events в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
    public struct AbilityActivatedEvent : IGameEvent
    {
        public int SourceEntity;
        public int AbilityIndex;
    }

    public struct AbilityCascadeStartedEvent : IGameEvent { }
    public struct AbilityCascadeEndedEvent : IGameEvent { }

    // в”Ђв”Ђ Resource events в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
    public struct ResourceChangedEvent : IGameEvent
    {
        public int PlayerId;
        public EnumService.ResourceType Type;
        public int NewValue;
        public int MaxValue;
    }

    // в”Ђв”Ђ Board / selection events (UI) в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
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

    // в”Ђв”Ђ Mulligan events в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
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

    /// <summary>
    /// UI Р·Р°РїСЂР°С€РёРІР°РµС‚ Р·Р°РјРµРЅСѓ РєРѕРЅРєСЂРµС‚РЅРѕР№ РєР°СЂС‚С‹ РІ РјСѓР»РёРіР°РЅРµ.
    /// Р§РёС‚Р°РµС‚СЃСЏ MulliganSystem.
    /// </summary>
    public struct MulliganReplaceRequestedUIEvent : IGameEvent
    {
        public int PlayerEntity;
        public int CardEntity;
    }

    // в”Ђв”Ђ Turn hint events в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
    /// <summary>РќР°С‡Р°Р»СЃСЏ С…РѕРґ Р»РѕРєР°Р»СЊРЅРѕРіРѕ РёРіСЂРѕРєР°.</summary>
    public struct LocalTurnStartedEvent : IGameEvent
    {
        public int TurnNumber;
        /// <summary>РЎРєРѕР»СЊРєРѕ СЃРµРєСѓРЅРґ РґР°С‘С‚СЃСЏ РЅР° С…РѕРґ.</summary>
        public float TurnDurationSeconds;
    }

    /// <summary>РћРїРїРѕРЅРµРЅС‚ Р·Р°РєРѕРЅС‡РёР» СЃРІРѕР№ С…РѕРґ (РїСЂРёС€Р»Рѕ RPC).</summary>
    public struct OpponentTurnEndedEvent : IGameEvent { }

    // в”Ђв”Ђ Opponent card played events в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
    /// <summary>
    /// РћРїРїРѕРЅРµРЅС‚ СЂР°Р·С‹РіСЂР°Р» РєР°СЂС‚Сѓ вЂ” UI РїРѕРєР°Р·С‹РІР°РµС‚ РІСЃРїР»С‹РІР°СЋС‰СѓСЋ РєР°СЂС‚РѕС‡РєСѓ.
    /// </summary>
    public struct OpponentCardPlayedUIEvent : IGameEvent
    {
        public string CardName;
        public UnityEngine.Sprite Icon;
    }

    // в”Ђв”Ђ Match setup events в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ
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

    // в”Ђв”Ђ Ability condition events в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ

    /// <summary>
    /// РџСѓР±Р»РёРєСѓРµС‚СЃСЏ РєРѕРіРґР° РІСЃРµ СѓСЃР»РѕРІРёСЏ СЃРїРѕСЃРѕР±РЅРѕСЃС‚Рё РІС‹РїРѕР»РЅРµРЅС‹ Рё РЅР° РЅРµС‘ РЅР°РІРµС€РёРІР°РµС‚СЃСЏ ReadyTag.
    /// UI РїРѕРґРїРёСЃС‹РІР°РµС‚СЃСЏ С‡С‚РѕР±С‹ РїРѕРєР°Р·Р°С‚СЊ/СЃРєСЂС‹С‚СЊ РёРЅРґРёРєР°С‚РѕСЂ РіРѕС‚РѕРІРЅРѕСЃС‚Рё РЅР° РєР°СЂС‚Рµ.
    /// </summary>
    public struct AbilityReadyEvent : IGameEvent
    {
        /// <summary>ECS entity СЃРїРѕСЃРѕР±РЅРѕСЃС‚Рё РєРѕС‚РѕСЂР°СЏ СЃС‚Р°Р»Р° РіРѕС‚РѕРІР°.</summary>
        public int AbilityEntity;

        /// <summary>ECS entity РєР°СЂС‚С‹-РІР»Р°РґРµР»СЊС†Р°.</summary>
        public int CardEntity;
    }

    /// <summary>
    /// РџСѓР±Р»РёРєСѓРµС‚СЃСЏ РєРѕРіРґР° СѓСЃР»РѕРІРёРµ СЃРїРѕСЃРѕР±РЅРѕСЃС‚Рё РїРµСЂРµСЃС‚Р°Р»Рѕ РІС‹РїРѕР»РЅСЏС‚СЊСЃСЏ (ReadyTag СЃРЅСЏС‚).
    /// </summary>
    public struct AbilityNotReadyEvent : IGameEvent
    {
        public int AbilityEntity;
        public int CardEntity;
    }

    // в”Ђв”Ђ Card play requirement events в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ

    /// <summary>
    /// РџСѓР±Р»РёРєСѓРµС‚СЃСЏ РєРѕРіРґР° РІСЃРµ С‚СЂРµР±РѕРІР°РЅРёСЏ Рє РїРѕР»СЋ Р±РѕСЏ РґР»СЏ РєР°СЂС‚С‹ РІС‹РїРѕР»РЅРµРЅС‹
    /// (PlayableTag РґРѕР±Р°РІР»РµРЅ). UI РІРєР»СЋС‡Р°РµС‚/РІС‹РєР»СЋС‡Р°РµС‚ РёРЅС‚РµСЂР°РєС‚РёРІРЅРѕСЃС‚СЊ РєР°СЂС‚С‹.
    /// </summary>
    public struct CardPlayableChangedEvent : IGameEvent
    {
        public int  CardEntity;
        public bool IsPlayable;
    }

    // в”Ђв”Ђ Card creation events в”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђв”Ђ

    /// <summary>
    /// Р—Р°РїСЂРѕСЃ РЅР° СЃРѕР·РґР°РЅРёРµ ECS entity РєР°СЂС‚С‹ РѕРїРїРѕРЅРµРЅС‚Р° РёР· СЃРµС‚РµРІРѕРіРѕ СЃРЅСЌРїС€РѕС‚Р°.
    /// РџСѓР±Р»РёРєСѓРµС‚СЃСЏ PhotonRunHandler РїСЂРё РїРѕР»СѓС‡РµРЅРёРё RPC_SyncDeckSnapshot.
    /// CreateCardSystem С‡РёС‚Р°РµС‚ CardConfig (РёРЅР¶РµРєС‚РёСЂРѕРІР°РЅ) Рё РІС‹Р·С‹РІР°РµС‚ CardModel.InitAndGetEntity.
    /// </summary>
    public struct CreateCardEvent : IGameEvent
    {
        /// <summary>ExpansionConfig.ExpansionId РґР»СЏ РїРѕРёСЃРєР° РІ CardConfig.</summary>
        public string ExpansionId;

        /// <summary>CardModel.Id РІРЅСѓС‚СЂРё СЌРєСЃРїР°РЅСЃРёРё.</summary>
        public int CardId;

        /// <summary>NetworkEntityKey РґР»СЏ РїСЂРёРІСЏР·РєРё entity.</summary>
        public string EntityKey;

        /// <summary>PlayerId РІР»Р°РґРµР»СЊС†Р° (РѕРїРїРѕРЅРµРЅС‚).</summary>
        public int OwnerId;

        /// <summary>РљР°СЂС‚Р° РїСЂРёРЅР°РґР»РµР¶РёС‚ РѕРїРїРѕРЅРµРЅС‚Сѓ вЂ” РЅР°РІРµСЃРёС‚СЊ EnemyCardTag РІРјРµСЃС‚Рѕ OwnCardTag.</summary>
        public bool IsEnemy;
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

    // ── Card pick (discover) events ──────────────────────────────────────────

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
        /// <summary>Entity карты которую разыгрывал игрок.</summary>
        public int CastingCardEntity;

        /// <summary>ModelId выбранной карты для репликации.</summary>
        public int ChosenCardModelId;

        /// <summary>Entity выбранной карты (локальное, не для сетки).</summary>
        public int ChosenCardEntity;
    }
}
