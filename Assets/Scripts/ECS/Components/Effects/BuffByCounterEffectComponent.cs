namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Бафф цели динамическими статами: атака/здоровье = (PerCount × счётчик
    /// сыгранных карт с ModelId у владельца источника).
    /// Используется «Грыз» (+1/1 за каждого Чёрта).
    /// </summary>
    public struct BuffByCounterEffectComponent
    {
        public int CounterModelId;
        public int AttackPerCount;
        public int HealthPerCount;
    }
}
