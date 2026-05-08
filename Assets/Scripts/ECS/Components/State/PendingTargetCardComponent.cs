namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Вешается на entity ИГРОКА когда он «держит» карту в руке и ждёт выбора цели.
    /// TargetSelectionSystem читает этот компонент, подсвечивает доступные цели
    /// и при клике по валидной цели создаёт CastEvent с заполненным TargetEntity/TargetCell.
    /// </summary>
    public struct PendingTargetCardComponent
    {
        /// <summary>entity карты которую хотим разыграть.</summary>
        public int CardEntity;

        /// <summary>Тип требуемой цели: Enemy, Ally, Cell, Any.</summary>
        public TargetRequirementType RequiredTarget;
    }

    public enum TargetRequirementType
    {
        EnemyCreature,
        AllyCreature,
        AnyCreature,
        AnyCell,
    }
}
