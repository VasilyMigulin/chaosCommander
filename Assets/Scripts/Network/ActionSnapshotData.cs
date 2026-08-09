using MemoryPack;

namespace Game.Core.Network
{
    // ── Базовый интерфейс ─────────────────────────────────────────────────────
    //
    // Все конкретные типы действий реализуют IActionData.
    // MemoryPackUnion позволяет сериализовать/десериализовать полиморфно.
    // При добавлении нового типа: зарегистрировать тег (следующий свободный),
    // добавить атрибут [MemoryPackUnion(N, typeof(ActionXxxData))] сюда.

    [MemoryPackable]
    [MemoryPackUnion(1, typeof(ActionCastData))]
    [MemoryPackUnion(2, typeof(ActionMoveData))]
    [MemoryPackUnion(3, typeof(ActionAttackData))]
    [MemoryPackUnion(4, typeof(ActionCardPickedData))]
    [MemoryPackUnion(5, typeof(ActionAbilityData))]
    [MemoryPackUnion(6, typeof(ActionEndTurnData))]
    [MemoryPackUnion(7, typeof(ActionDeathData))]
    [MemoryPackUnion(8, typeof(ActionDrawReplacementData))]
    [MemoryPackUnion(9, typeof(ActionDrawData))]
    [MemoryPackUnion(10, typeof(ActionControlRevertData))]
    public partial interface IActionData
    {
        /// <summary>Глобальный номер хода матча.</summary>
        int TurnNumber { get; }

        /// <summary>Порядковый номер действия в рамках одного хода (0, 1, 2…).</summary>
        int ActionIndex { get; }
    }

    // ── CardCast: карта разыграна с руки ──────────────────────────────────────

    /// <summary>
    /// Карта разыграна с руки активным игроком.
    /// Оппонент имеет синхронизированную копию руки, поэтому передаётся только
    /// NetworkEntityKey карты и выбранная цель.
    /// </summary>
    [MemoryPackable]
    public partial class ActionCastData : IActionData
    {
        public int TurnNumber  { get; set; }
        public int ActionIndex { get; set; }

        /// <summary>NetworkEntityKey карты в руке активного игрока (оппонент находит по копии руки).</summary>
        public string SourceEntityKey { get; set; }

        /// <summary>Клетка для существа (row*5+col); -1 для заклинания/чар (без позиции).
        /// Цели способностей идут отдельно в ActionAbilityData.</summary>
        public int Cell { get; set; }

        /// <summary>АЛЬТЕРНАТИВНАЯ УПЛАТА (семейство «Бесчестный букмекер»): -1 = обычная оплата;
        /// иначе AltCostKind. Пассив зеркалит: тратит заряд маркера и повторяет уплату — урон
        /// AltPaidAmount (DamageSelf) / жертвы по ключам AltPaidKeys (discard/sacrifice/mill).
        /// Ставит CollectActionSystem из AltPaidComponent.</summary>
        public int AltPaidKind { get; set; }
        public int AltPaidAmount { get; set; }
        public string[] AltPaidKeys { get; set; }
    }

    // ── CreatureMove: существо переместилось ──────────────────────────────────

    /// <summary>
    /// Существо активного игрока сделало ход на клетку.
    /// </summary>
    [MemoryPackable]
    public partial class ActionMoveData : IActionData
    {
        public int TurnNumber  { get; set; }
        public int ActionIndex { get; set; }

        /// <summary>NetworkEntityKey существа.</summary>
        public string SourceEntityKey { get; set; }

        /// <summary>Целевая клетка (row * cols + col).</summary>
        public int TargetCell { get; set; }

        /// <summary>Сторона доски назначения (1/2). Нужна для ходов через линию фронта на сторону врага.</summary>
        public int TargetOwnerId { get; set; }
    }

    // ── CreatureAttack: существо атакует ──────────────────────────────────────

    /// <summary>
    /// Существо активного игрока атакует цель.
    /// </summary>
    [MemoryPackable]
    public partial class ActionAttackData : IActionData
    {
        public int TurnNumber  { get; set; }
        public int ActionIndex { get; set; }

        /// <summary>NetworkEntityKey атакующего существа.</summary>
        public string AttackerEntityKey { get; set; }

        /// <summary>NetworkEntityKey цели атаки.</summary>
        public string DefenderEntityKey { get; set; }
    }

    // ── CardPicked: выбор при раскопке (excavate) ────────────────────────────

    /// <summary>
    /// Игрок выбрал карту из предложенных при раскопке.
    /// Оппонент знает тот же набор предложенных карт (детерминирован),
    /// поэтому достаточно передать индекс выбора.
    /// </summary>
    /// <summary>
    /// Игрок выбрал карту при раскопке. Вилка по типу источника:
    ///   • Сессионный источник (колода/рука/кладбище) — выбрана СУЩЕСТВУЮЩАЯ у обоих
    ///     клиентов сущность; передаём её ChosenEntityKey, оппонент находит по ключу.
    ///   • Пул (UniquePool) — карты заданы как данные; создаётся НОВАЯ сущность.
    ///     CreateFromPool=true, передаём ChosenExpansionId+ChosenCardId+новый ChosenEntityKey,
    ///     оппонент создаёт такую же сущность с тем же ключом.
    /// </summary>
    [MemoryPackable]
    public partial class ActionCardPickedData : IActionData
    {
        public int TurnNumber  { get; set; }
        public int ActionIndex { get; set; }

        /// <summary>NetworkEntityKey карты-источника, которая разыгрывается с раскопкой.</summary>
        public string SourceEntityKey { get; set; }

        /// <summary>NetworkEntityKey выбранной карты (существующей или создаваемой из пула).</summary>
        public string ChosenEntityKey { get; set; }

        /// <summary>true — выбор из пула-данных: оппонент должен СОЗДАТЬ сущность.</summary>
        public bool CreateFromPool { get; set; }

        /// <summary>Для CreateFromPool: ExpansionId создаваемой карты.</summary>
        public string ChosenExpansionId { get; set; }

        /// <summary>Для CreateFromPool: CardId (ModelId) создаваемой карты.</summary>
        public int ChosenCardId { get; set; }
    }

    // ── AbilityApplied: способность применена (авто и ручные триггеры) ─────────

    /// <summary>
    /// Способность применена активным игроком — включая авто-триггеры (OnCast, OnDie и т.д.).
    ///
    /// Активный игрок разыгрывает полностью (включая выбор случайных целей),
    /// затем передаёт снапшот оппоненту. Оппонент воспроизводит с той же целью —
    /// детерминированно, не пересчитывая выбор цели.
    ///
    /// Числовые результаты (урон, хил) НЕ передаются — вычисляются детерминированно.
    /// </summary>
    [MemoryPackable]
    public partial class ActionAbilityData : IActionData
    {
        public int TurnNumber  { get; set; }
        public int ActionIndex { get; set; }

        /// <summary>NetworkEntityKey карты-источника способности.</summary>
        public string SourceEntityKey { get; set; }

        /// <summary>Индекс способности в AbilityContainerComponent карты (0-based).</summary>
        public int AbilityIndex { get; set; }

        /// <summary>
        /// Индекс шага цепочки: 0 — основные Effects абилки, 1..N — ChainSteps[i-1].
        /// Для абилок без цепочки всегда 0. Каждый шаг шлётся отдельным снэпшотом
        /// (активный клиент дожидается завершения шага N перед резолвом N+1).
        /// </summary>
        public int StepIndex { get; set; }

        /// <summary>
        /// Ключи разрешённых активным игроком целей (после маски + формы).
        ///   • Карта/существо — NetworkEntityKey сущности.
        ///   • Игрок — псевдо-ключ "PLAYER:{PlayerId}" (у игроков нет NetworkEntityComponent).
        /// Пассивный клиент НЕ ре-резолвит — берёт цели отсюда.
        /// </summary>
        public string[] TargetEntityKeys { get; set; }

        /// <summary>
        /// Клетка, захваченная цепочкой на шаге 0 (BoardPosition первой цели).
        /// Нужна эффектам типа MoveSourceToCell на пассивной стороне, где
        /// ChainStateComponent отсутствует. HasCapturedCell = false если шаг
        /// не работал с on-board целями.
        /// </summary>
        public bool HasCapturedCell { get; set; }
        public int CapturedRow { get; set; }
        public int CapturedCol { get; set; }
        public int CapturedCellOwnerId { get; set; }

        /// <summary>
        /// NetworkEntityKey сущностей, ПРИЗВАННЫХ этим резолвом. Пассив берёт модификаторы призыва
        /// (бафф/таймер) из своей копии способности (ISummonModifierProvider) и применяет к этим
        /// ключам — отложенно, пока сущность не появится (её размещение приходит отдельным ActionCastData).
        /// </summary>
        public string[] SummonedEntityKeys { get; set; }

        /// <summary>Цепочка: накопленный KilledCount к этой стадии (контекст CountSource=ChainKilled).</summary>
        public int KilledCount { get; set; }

        /// <summary>Цепочка: идентичности случайно-сгенерированных карт стадии (GainRandomCardEffect) —
        /// пассив воспроизводит их вместо своего ролла. Параллельные массивы.</summary>
        public string[] GeneratedExpansionIds { get; set; }
        public int[] GeneratedCardIds { get; set; }
    }

    // ── Death: существо погибло по «системной» причине (таймер и т.п.) ─────────

    /// <summary>
    /// Существо погибло на активе по причине, которая НЕ воспроизводится у пассива детерминированно
    /// (таймер «умрёт через N ходов»: пассив не тикает чужой таймер в чужой ход). Боевые смерти
    /// (детерминированный урон) и смерти от эффектов (ре-ран ActionAbilityData) сюда НЕ попадают —
    /// шлёт только CreatureTimerTickSystem. Пассив вешает DeadTag по ключу → DieSystem отрабатывает штатно.
    /// </summary>
    [MemoryPackable]
    public partial class ActionDeathData : IActionData
    {
        public int TurnNumber  { get; set; }
        public int ActionIndex { get; set; }

        /// <summary>NetworkEntityKey погибшего существа.</summary>
        public string EntityKey { get; set; }
    }

    // ── DrawReplacement: замена добора (Адовый червь) ─────────────────────────

    /// <summary>
    /// Активный игрок разрешил замену добора (Адовый червь): из OfferedKeys выбран ChosenKey.
    /// Пассив повторяет: предложенные убирает из колоды оппонента, выбранную → кладбище (DestroyChosen),
    /// остальные → в руку. Игрок адресуется по PlayerId. Карты существуют у обоих (по ключам с мулигана).
    /// </summary>
    [MemoryPackable]
    public partial class ActionDrawReplacementData : IActionData
    {
        public int TurnNumber  { get; set; }
        public int ActionIndex { get; set; }

        public int PlayerId { get; set; }
        public string[] OfferedKeys { get; set; }
        public string ChosenKey { get; set; }
        public bool DestroyChosen { get; set; }
    }

    // ── Draw: активный игрок добрал карты с верха колоды (turn-start) ──────────

    /// <summary>
    /// Активный игрок добрал Count карт с верха своей колоды (добор начала хода). Каскад начала хода идёт
    /// ТОЛЬКО у активного (StartTurnState не зеркалится), поэтому пассив сам этот добор не делает — повторяем
    /// его здесь, чтобы зеркала колоды/руки оппонента не дрейфовали (важно для Свежий зомби/Барабук/Охотник
    /// и счётчика DrawnByModelId — Вонючее облако). Несём КЛЮЧИ добранных карт (DrawnKeys): пассив переносит в
    /// руку именно их по ключу, а НЕ «верхние N» — иначе при любом расхождении порядка колоды зеркала тянут
    /// разные карты → фантомы в руке оппонента (Count оставлен как fallback для старых снапшотов).
    /// ЭФФЕКТНЫЕ доборы (DrawCardEffect) сюда НЕ попадают — они ре-ранятся резолвом на обоих (Sync=false).
    /// </summary>
    [MemoryPackable]
    public partial class ActionDrawData : IActionData
    {
        public int TurnNumber  { get; set; }
        public int ActionIndex { get; set; }

        /// <summary>PlayerId добравшего игрока.</summary>
        public int PlayerId { get; set; }

        /// <summary>Сколько карт фактически добрано (с учётом лимита руки/пустой колоды). Fallback-поле.</summary>
        public int Count { get; set; }

        /// <summary>NetworkEntityKey добранных карт. Пассив переносит в руку ИМЕННО эти карты по ключу
        /// (а не «верхние N») → зеркало руки оппонента не дрейфует при расхождении порядка колоды.</summary>
        public string[] DrawnKeys { get; set; }
    }

    // ── ControlRevert: временный контроль истёк (актив посчитал ходы) ─────────

    /// <summary>
    /// Временный контроль над существом истёк: активный (контролёр) дотикал TempControlledComponent.TurnsRemaining
    /// в конце своего хода и откатил владельца локально. Пассив повторяет откат по ключу (берёт исходного
    /// владельца из своей копии TempControlledComponent). Как ActionDeathData (таймер): считает только актив,
    /// пассив применяет — не тикает сам (иначе рассинхрон на несимметричных границах хода).
    /// </summary>
    [MemoryPackable]
    public partial class ActionControlRevertData : IActionData
    {
        public int TurnNumber  { get; set; }
        public int ActionIndex { get; set; }

        /// <summary>NetworkEntityKey существа, контроль над которым возвращается исходному владельцу.</summary>
        public string EntityKey { get; set; }
    }

    // ── EndTurn: активный игрок завершил ход ──────────────────────────────────

    /// <summary>
    /// Конец хода активного игрока. У пассивного ложится в очередь как обычное действие —
    /// когда реплей доходит до него (после всех предыдущих), ход передаётся локальному игроку.
    /// </summary>
    [MemoryPackable]
    public partial class ActionEndTurnData : IActionData
    {
        public int TurnNumber  { get; set; }
        public int ActionIndex { get; set; }
    }
}
