using Game.Core.Shared.Interface;

namespace Game.Core.Ecs.Components
{
    /// <summary>Куда отправить ВЫБРАННУЮ карту (терминал discover). Hand — в руку, Deck — замешать в колоду,
    /// Grave — сбросить. Кому — определяет TakeOwnership. Для FromPool всегда Hand владельца (создаём новую).
    /// None — карту вообще НЕ трогать (осталась там же, где была): для карт, где раскопка — только способ
    /// ВЫБРАТЬ цель для Modifiers (Королевский указ: «посмотрите 3 карты в руке, разыграйте 3 её копии» —
    /// оригинал остаётся в руке нетронутым, без лишнего снятия/установки тегов и повторного CardDrawnEvent).
    /// TakeOwnership с None не сочетается (воровство подразумевает смену владельца — самостоятельная задача).</summary>
    public enum DiscoverDest { Hand, Deck, Grave, None }

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
        public bool            ExcludeAlreadyPicked;   // фильтр применяем ПРИ ПОКАЗЕ (BuildPoolOffer), а не
                                         // здесь при Configure: несколько DiscoverFromPoolEffect на одной
                                         // способности резолвятся синхронно один за другим ДО того, как игрок
                                         // выберет хоть что-то — на момент Configure второго прохода отбор
                                         // первого ещё не записан (DiscoverExclusionComponent пуст) — «Проклятье
                                         // для принцессы» предлагало дубль (2026-08-21).

        // ── показанный поднабор (заполняется при offer) — маппинг выбора → идентичность ──
        public int[]    ShownTokens;     // что ушло в OfferedCardEntities (реальные сущности зоны / синтетические id пула)
        public string[] ShownExp;        // идентичность токена (для пула: что создать)
        public int[]    ShownCardId;
    }
}
