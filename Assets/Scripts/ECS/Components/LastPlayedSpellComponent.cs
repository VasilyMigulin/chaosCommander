namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Хранит модель последнего разыгранного игроком заклинания (CardType=Spell).
    /// Обновляется LastPlayedSpellTrackerSystem на CardPlayedEvent.
    /// Используется «Попадос» (даёт копию в руку), «Магический гомункул» (проверка наличия в руке).
    /// </summary>
    public struct LastPlayedSpellComponent
    {
        public string ExpansionId;
        public int ModelId;
        public bool HasValue;
    }
}
