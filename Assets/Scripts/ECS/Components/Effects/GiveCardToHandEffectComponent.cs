namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Создаёт карту указанной модели в руку TargetEntity (игрока).
    /// Используется «Водонос» (OnDie → Освежающий напиток в руку владельца).
    /// </summary>
    public struct GiveCardToHandEffectComponent
    {
        public string ExpansionId;
        public int CardId;
        public int Count;
    }
}
