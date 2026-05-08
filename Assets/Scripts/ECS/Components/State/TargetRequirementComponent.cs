namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Хранит тип требуемой цели на entity КАРТЫ.
    /// Устанавливается при инициализации карты через RequireTargetPlayRequirement.
    /// Читается когда игрок пытается разыграть карту — для создания PendingTargetCardComponent.
    /// </summary>
    public struct TargetRequirementComponent
    {
        public TargetRequirementType RequiredTarget;
    }
}
