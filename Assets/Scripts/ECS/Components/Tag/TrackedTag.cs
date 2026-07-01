namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Маркер: эта карта уже была зарегистрирована в MatchTracker в текущем такте каста.
    /// Снимается RunFinishCastCardSystem вместе с AbilityCastEvent.
    /// </summary>
    public struct TrackedTag { }
}
