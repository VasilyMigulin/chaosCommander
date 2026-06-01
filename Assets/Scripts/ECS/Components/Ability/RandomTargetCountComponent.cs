namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// При TargetMask.Random — сколько разных случайных целей выбрать (по умолчанию 1).
    /// Используется «Цепная молния» (3 случайных).
    /// </summary>
    public struct RandomTargetCountComponent
    {
        public int Count;
    }
}
