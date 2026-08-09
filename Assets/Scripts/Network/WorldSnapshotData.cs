using MemoryPack;

namespace Game.Core.Network
{
    // === DTO (MemoryPack) ===
    /// <summary>
    /// ПОЛНЫЙ снэпшот зеркалируемого состояния мира для self-heal ресинка (спека: Docs/network/resync-spec.md).
    /// Актив (авторитет) собирает в WorldResyncSystem.Capture, пассив применяет в Apply под затемнением.
    /// v1-границы: карты/зоны/статы/таймеры/счётчики/ресурсы/aura-реверт (AppliedBuffs/BuffPerCharm).
    /// v2 (осознанно отложено): TrackedBuffs(IBuffable), CastMultiplierService, внутренности способностей
    /// (ResolveCounter/латчи/OnMatchStart._fired), цветовые маски, TempControl/OriginalOwner, MatchTracker.
    /// </summary>
    [MemoryPackable]
    public partial struct WorldSnapshotData
    {
        public int   TurnNumber;
        public int   ActivePlayerId;   // ОТПРАВИТЕЛЬ снэпшота (по нему Apply решает, чьи ресурсы авторитетны)
        public bool  MatchOver;

        /// <summary>
        /// Кто СЕЙЧАС ходит (ActiveState у отправителя), -1 — никто. Нужен реконнекту: вернувшийся не знает,
        /// не перешёл ли ход к нему, пока его не было, а ActiveState — единственный маркер «я хожу».
        /// Обычному ресинку безразличен (там ход не терялся).
        /// </summary>
        public int TurnOwnerPlayerId;
        public ulong StateHash;   // контрольная чексумма актива (WorldStateHash) — сверка после применения

        public PlayerSnapshot[] Players;
        public CardSnapshot[]   Cards;
    }

    [MemoryPackable]
    public partial struct PlayerSnapshot
    {
        public int PlayerId;

        // Аватар
        public int   Hp;
        public int   HpBaseMax;
        public int[] HpMods;
        public int[] HpModsPerm;

        // Ресурсы / ход
        public int Gold, GoldMax, Mana, ManaMax;
        public int PersonalTurn;

        // Зоны (ПО ПОРЯДКУ: рука — командир [0]; колода — [0] = верх)
        public string[] HandKeys;
        public string[] DeckKeys;

        // MatchCounterComponent целиком (параллельные массивы вместо словарей)
        public int[]    PlayedModelIds;   public int[] PlayedCounts;
        public int[]    DrawnModelIds;    public int[] DrawnCounts;
        public int[]    GeneratedModelIds;public int[] GeneratedCounts;
        public string[] InvokedArchetypes;public int[] InvokedCounts;
        public int PlayerDamageTaken, PlayerDamageTakenOwnTurn, CreaturesDamageTaken, SpellsPlayed;
        public string[] SpellLogExp;  public int[] SpellLogIds;
        public string[] CharmLogExp;  public int[] CharmLogIds;

        // Персистентные маркеры/модификаторы игрока
        public bool HasCostModifier;    public int CostModifier;
        public bool HasManaFloor;       public int ManaFloor;
        public bool HasTempMana;        public int TempManaRefund;
        public bool GoldBlock;
        public bool ReflectDamage;
        public bool HasDrawReplacement; public int DrawReplacementLook; public bool DrawReplacementDestroy;
        public bool TurnResourcesGranted;
    }

    [MemoryPackable]
    public partial struct CardSnapshot
    {
        public string Key;           // NetworkEntityKey — PK
        public string ExpansionId;
        public int    ModelId;
        public int    OwnerId;
        public bool   IsCommander;

        public byte Zone;            // 0 Hand, 1 Deck, 2 Board, 3 Grave, 4 Limbo
        public int  Row, Col, PosOwnerId;   // валидно при Zone=Board

        public bool  HasAttack;  public int AtkBase;                       public int[] AtkMods;   public int[] AtkModsPerm;
        public bool  HasHp;      public int HpCurrent;  public int HpBaseMax; public int[] HpMods; public int[] HpModsPerm;
        public bool  HasSpeed;   public int SpeedBaseMax; public int SpeedRemaining; public int[] SpeedMods; public int[] SpeedModsPerm;

        public byte  CostType;       // 0 нет, 1 Gold, 2 Mana, 3 Health
        public int   CostBase;       public int[] CostMods;  public int[] CostModsPerm;

        public bool HasAttacksUsed;     public int AttacksUsed;
        public bool HasCharmTimer;      public int CharmTurns;
        public bool HasCreatureTimer;   public int CreatureTurns;
        public bool HasAuraTimer;       public int AuraTurns;
        public bool HasCommanderCd;     public int CommanderCd;
        public bool HasCommanderTracker;public bool CommanderWasInHand;
        public bool HasGenCounter;      public int GenCounterNext;
        public bool HasBuffPerCharm;    public int PerCharmAppliedAtk; public int PerCharmAppliedHp;

        /// <summary>Реверт-лист реактивной ауры этого источника (AppliedBuffsComponent) — цели по ключам.</summary>
        public BuffRecordSnapshot[] AppliedBuffs;
    }

    [MemoryPackable]
    public partial struct BuffRecordSnapshot
    {
        public string TargetKey;
        public int Atk, Hp, Speed;
    }
}
