namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Навешивает CreatureTimerComponent на TargetEntity (TurnsRemaining = Turns).
    /// </summary>
    public struct AddCreatureTimerEffectComponent
    {
        public int Turns;
    }
}
