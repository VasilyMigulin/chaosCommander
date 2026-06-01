using Game.Core.Service;

namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Хранит маску требуемой цели на entity КАРТЫ.
    /// Устанавливается Ability.Init для режимов Mode = Select / Random
    /// (вместе с RequiresTargetSelectionTag или RequiresRandomTargetTag).
    /// Читается когда игрок пытается разыграть карту — для создания PendingTargetCardComponent
    /// или для авто-выбора случайной цели.
    /// </summary>
    public struct TargetRequirementComponent
    {
        public TargetMask RequiredTarget;
    }
}
