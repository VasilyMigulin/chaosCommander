namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Каст карты отклонён (не хватает ресурсов / нарушены правила).
    /// Поведение: одна из Run*CostCastSystem либо Run*ValidateSystem ставит этот эвент
    /// и удаляет RequestCardCastEvent. UI слушает через bus-translate систему.
    /// </summary>
    public struct DeclineCardCastEvent
    {
        public DeclineReason Reason;
    }

    public enum DeclineReason
    {
        Unknown = 0,
        NotEnoughMana = 1,
        NotEnoughGold = 2,
        NotEnoughHealth = 3,
        CharmLimitReached = 4,
        CommanderOnBoard = 5,
        CommanderOnCooldown = 6,
        NotInHand = 7,
        NotOwnTurn = 8,
        RulesNotMet = 9,
        NoAltCostPayment = 10,   // альтернативная уплата: нечем платить (нет карты для сброса/существа для жертвы/карт в колоде)
        NoAdditionalCostPayment = 11,   // доп. цена ПОВЕРХ обычной (RequiresAdditionalCostComponent): нечем платить
    }
}
