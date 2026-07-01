namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Latch-маркер: TakeDamageReceivedLatch защёлкнулся (Accumulated >= Threshold).
    /// Однажды поставленный — НЕ снимается до конца матча.
    /// Inc-фильтр чекера исключает этот тег, чтобы перестать дёргать защёлкнутые правила.
    /// </summary>
    public struct TakeDamageReceivedLatchedTag { }
}
