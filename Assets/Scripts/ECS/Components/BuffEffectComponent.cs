namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Применяется эффектом баффа статов на целевое существо.
    /// </summary>
    public struct BuffEffectComponent
    {
        public int AttackBonus;
        public int HealthBonus;
        public int SpeedBonus;
    }
}
