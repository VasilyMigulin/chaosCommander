namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Замена добора в начале хода (Адовый червь): вместо обычного добора — «посмотри LookCount верхних,
    /// выбери одну». Висит на сущности ИГРОКА до конца матча (персистентно).
    ///
    /// Это ЗАМЕНА САМОЙ МЕХАНИКИ добора начала хода, а не перехват отдельного добора: наличие компонента
    /// проверяет RunTurnStartSystem и вместо DrawCardEvent ставит DrawReplacementDueComponent.
    /// Доборы от эффектов карт идут своим путём и замену не запускают.
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

        /// <summary>Токен окна выбора (PickRequestId) — ключ корреляции CardPickChosenEvent.
        /// Раньше выбор искали по CastingCardEntity, куда этот канал клал entity ИГРОКА, а остальные —
        /// entity КАРТЫ: чужой выбор не находил pending, и он висел вечно, глуша добор до конца матча.</summary>
        public int RequestId;

        /// <summary>Оффер уже опубликован (слот у брокера получен). Фиксация предложения и показ окна
        /// разнесены: слот у брокера может прийти не в том же кадре.</summary>
        public bool Presented;
    }

    /// <summary>
    /// Механика добора начала хода заменена — отработать замену (Адовый червь). Ставит RunTurnStartSystem
    /// ВМЕСТО обычного DrawCardEvent, снимает RunDrawReplacementSystem, забрав предложение. Транзиент.
    ///
    /// Замена выбирается у ИСТОЧНИКА, а не перехватом DrawCardEvent ниже по течению: червь меняет саму
    /// механику «в начале хода берётся карта», а не гасит чей-то добор. Побочно это снимает неразрешимую
    /// задачу — DrawCardEffect суммирует Count в уже существующее событие, так что перехватчик не мог
    /// отличить базовый добор от эффектных, приехавших в том же DrawCardEvent.
    /// </summary>
    public struct DrawReplacementDueComponent
    {
    }
}
