using Game.Core.Shared.Interface;

namespace Game.Core.Ecs.Components
{
    /// <summary>Куда отправить ВЫБРАННУЮ карту (терминал discover). Hand — в руку, Deck — замешать в колоду,
    /// Grave — сбросить. Кому — определяет TakeOwnership. Для FromPool всегда Hand владельца (создаём новую).</summary>
    public enum DiscoverDest { Hand, Deck, Grave }

    // === struct (Component) ===
    /// <summary>
    /// Запрос «раскопки» (discover): NonTarget-эффект «предложить N карт и положить выбранную В РУКУ».
    /// Ставит DiscoverEffect.Apply на ОБОИХ клиентах (резолв способности реплеится у пассива тоже).
    ///   • Своя карта (OwnCardTag) → RunDiscoverSystem показывает окно (PickupWindow), ждёт выбор.
    ///   • Чужая (реплей) → авто-резолв из CardPickReplayStore по NetKey источника.
    /// Корреляция выбора — по SourceCardEntity (эхо в CardPickChosenEvent.CastingCardEntity).
    /// </summary>
    public struct DiscoverRequestComponent
    {
        public int  SourceCardEntity;    // карта-источник (её NetKey — ключ синка)
        public int  OwnerPlayerEntity;   // владелец → его рука назначения
        public int  OwnerId;
        public int  OfferCount;          // сколько показать в окне (discover)
        public bool Offered;             // окно уже показано (актив)
        public int  RequestId;           // токен окна выбора (PickRequestId): по нему — и ТОЛЬКО по нему —
                                         // узнаётся свой CardPickChosenEvent. Корреляция по SourceCardEntity
                                         // была неоднозначной: шину слушают все каналы пика, а у замены
                                         // добора в том же поле лежит entity ИГРОКА из общего id-пространства.
        public int  Seq;                 // порядок среди запросов ОДНОГО источника («Приглашение»: 2 раскопки
                                         // в одном касте → окна СТРОГО по очереди; ставит DiscoverEffect.Apply,
                                         // порядок эффектов при ре-ране одинаков → зеркален)

        // ── терминал: куда и кому уходит выбранная карта ──
        public DiscoverDest Dest;        // Hand / Deck / Grave
        public bool TakeOwnership;       // true → карта становится КАСТЕРА (воровство); false → остаётся у владельца
        public IEffect[] Modifiers;      // target-эффекты к ВЫБРАННОЙ карте («стоит на 2 меньше», бафф/дебафф);
                                         // зона → применяет PlacePicked, пул → едут через GeneratedModScratch

        // ── источник кандидатов ──
        public bool            FromPool; // true → PoolExp/PoolCardId (создаём новую); false → Zone (двигаем существующую)
        public TargetZone      Zone;     // Deck/Hand/Grave (для FromPool=false)
        public ITargetFilter[] Filters;  // фильтры для зоны (для пула игнор — пул сам отфильтрован)
        public string[]        PoolExp;  // пул: идентичности
        public int[]           PoolCardId;

        // ── показанный поднабор (заполняется при offer) — маппинг выбора → идентичность ──
        public int[]    ShownTokens;     // что ушло в OfferedCardEntities (реальные сущности зоны / синтетические id пула)
        public string[] ShownExp;        // идентичность токена (для пула: что создать)
        public int[]    ShownCardId;
    }
}
