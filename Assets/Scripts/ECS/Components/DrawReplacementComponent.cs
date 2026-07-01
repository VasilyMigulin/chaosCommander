namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Замена добора в начале хода (Адовый червь): вместо обычного добора — «посмотри LookCount верхних,
    /// выбери одну». Висит на сущности ИГРОКА до конца матча (персистентно). Перехват — RunDrawReplacementSystem.
    /// </summary>
    public struct DrawReplacementComponent
    {
        /// <summary>Сколько верхних карт показать (Адовый червь = 3).</summary>
        public int LookCount;
        /// <summary>true — выбранную УНИЧТОЖИТЬ, остальные взять в руку; false — наоборот.</summary>
        public bool DestroyChosen;
    }

    /// <summary>
    /// Игрок ждёт выбор замены добора (открыто окно выбора). Хранит предложенные карты.
    /// Ставит/снимает RunDrawReplacementSystem. Транзиент.
    /// </summary>
    public struct PendingDrawReplacementComponent
    {
        public int[] Offered;
    }
}
