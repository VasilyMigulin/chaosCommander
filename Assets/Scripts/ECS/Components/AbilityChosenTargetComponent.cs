namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Вешается на entity способности когда игрок явно выбрал конкретную цель.
    /// Приоритет над авто-таргетингом (board-order / random).
    /// Ставится RunAbilityCastSystem из CastEvent.TargetEntity.
    /// </summary>
    public struct AbilityChosenTargetComponent
    {
        public int TargetEntity;
    }
}
