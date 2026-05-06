namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Вешается на существо когда нанесён удар во время анимации атаки.
    /// TakeDamageSystem применяет урон при наличии этого события.
    /// </summary>
    public struct AttackHitEvent
    {
        public int TargetEntity;
        public int Amount;
    }
}
