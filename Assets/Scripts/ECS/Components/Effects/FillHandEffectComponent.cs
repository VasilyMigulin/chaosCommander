namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Заполняет руку владельца указанной картой/токеном до лимита (MaxNonCommanderCards).
    /// Используется «Пиромант маг» (заполняет руку Поджогами).
    /// </summary>
    public struct FillHandEffectComponent
    {
        public string ExpansionId;
        public int CardId;
    }
}
