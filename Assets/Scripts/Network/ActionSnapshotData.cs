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

        /// <summary>
        /// NetworkEntityKey карты в руке активного игрока.
        /// Оппонент имеет копию руки и может найти entity по ключу — данные карты не нужны.
        /// </summary>
        public string SourceEntityKey { get; set; }

        /// <summary>NetworkEntityKey цели-entity (null если цель — клетка).</summary>
        public string TargetEntityKey { get; set; }

        /// <summary>Номер целевой клетки (row * cols + col). -1 если цель — entity.</summary>
        public int TargetCell { get; set; }
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

        /// <summary>NetworkEntityKey источника (карта/существо, чья способность срабатывает).</summary>
        public string SourceEntityKey { get; set; }

        /// <summary>Индекс способности в списке способностей карты (0-based).</summary>
        public int AbilityIndex { get; set; }

        /// <summary>NetworkEntityKey выбранной цели (null если цель — клетка или цели нет).</summary>
        public string TargetEntityKey { get; set; }

        /// <summary>Номер целевой клетки. -1 если цель — entity или цели нет.</summary>
        public int TargetCell { get; set; }
    }
}
