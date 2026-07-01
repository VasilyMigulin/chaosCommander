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

        // ── терминал: куда и кому уходит выбранная карта ──
        public DiscoverDest Dest;        // Hand / Deck / Grave
        public bool TakeOwnership;       // true → карта становится КАСТЕРА (воровство); false → остаётся у владельца

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
