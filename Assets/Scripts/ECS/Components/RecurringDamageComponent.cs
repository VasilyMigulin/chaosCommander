namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// «Существо получает Amount урона в конце КАЖДОГО хода» (Напустить саранчу — вешается на всех существ
    /// сразу, без учёта владельца). Тикается RecurringDamageTickSystem на TurnEndedEvent. В отличие от
    /// CreatureTimerComponent не убивает напрямую — обычный TakeDamageEvent, дальше штатная гибель/атрибуция.
    /// </summary>
    public struct RecurringDamageComponent
    {
        public int Amount;
    }
}
